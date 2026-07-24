# Azure Dedicated Server — Weekend Hosting Runbook

Hosting the headless Fusion dedicated server on Azure for a **one-weekend-a-month** LAN-style
session, optimized for latency + per-core clock, with idle cost driven to near-zero by
deallocating between weekends.

This complements [the dedicated-server testing guide](superpowers/guides/2026-06-25-dedicated-server-testing-guide.md).
That guide covers verifying the server topology; this one covers **running it on Azure**.

---

## 0. The plan at a glance

| Decision | Choice | Rationale |
|---|---|---|
| Region | **West US** (California) | Closest to the NorCal majority; Seattle ~20 ms; AU/JP unavoidably ~120–170 ms |
| VM size | **Standard_F8s_v2** (8 vCPU, ~3.7 GHz) | High per-core clock; big headroom so the tick loop never hitches. `Standard_FX4mds` if you want the absolute max clock |
| Accelerated Networking | **On** | Free; lower latency + jitter (F-series supports it) |
| OS | **Ubuntu 22.04 LTS** | Light, cheap; matches the Linux Dedicated Server build |
| Scripting backend | **IL2CPP** (optional) | ~faster than Mono for the sim; worth it since you want max perf |
| Disk | Premium SSD, **32 GB** | Build is small; you pay for the disk even while deallocated, so keep it small |
| Public IP | **Standard static** (~$3.65/mo) | Stable SSH target across deallocations; game traffic doesn't use it (Photon relays) |
| Pricing model | **Standard, NOT Spot** | Spot can be evicted mid-match and drop every player |
| Idle handling | **Deallocate** Sun night | Compute → $0 while off; you pay only disk + static IP |

**Cost estimate:** ~$17 compute for a ~50-hour weekend + ~$9/mo idle (disk + static IP) ≈
**~$26/month**, versus your $150 credit. Comfortable headroom.

---

## 1. Prerequisites (one-time, local machine)

1. **Unity module:** install **Linux Dedicated Server Build Support** via Unity Hub →
   *Installs → your editor → Add Modules*. (This gives the "Dedicated Server / Linux" build
   platform, which is graphics-stripped and headless by design.)

2. **Azure CLI:** install and sign in.
   ```bash
   az login
   az account show --query name -o tsv   # confirm you're on the right subscription
   ```
   If you have more than one subscription, pin it:
   ```bash
   az account set --subscription "<your-subscription-name-or-id>"
   ```

3. **An SSH key** (used to reach the VM). If you don't have one:
   ```bash
   ssh-keygen -t ed25519 -C "azure-gameserver"
   ```

---

## 2. Part A — Build the Linux Dedicated Server (Unity, editor-side)

I can't run the Unity build from here, so do this in the editor:

1. **File → Build Profiles** (Unity 6) or **File → Build Settings** (older). Select/add the
   **Dedicated Server** platform, set **Target Platform = Linux**, and **Switch Platform**.
2. Confirm **Scene list**: `MainMenu` at index **0**, `Gameplay` at index **1**
   (`GameNetworkManager.gameplaySceneIndex` defaults to 1). The dedicated-server boot skips the
   menu automatically, but the scene indices still need to be correct.
3. *(Optional, for max perf)* **Player Settings → Other Settings → Scripting Backend = IL2CPP**.
4. **Build** into an empty folder, and name the output **without spaces** — e.g. `2dgame-server`.
   You'll get:
   ```
   2dgame-server            <- the executable
   2dgame-server_Data/      <- data folder
   UnityPlayer.so, etc.
   ```
5. From that build folder, make a tarball to upload:
   ```bash
   cd /path/to/build
   tar czf 2dgame-server.tar.gz *
   ```

> **Why batch mode still matters:** a Dedicated Server build does *not* auto-set
> `Application.isBatchMode`. The run command below passes `-batchmode`, which is what
> [`NetworkBootMode.Resolve`](../Assets/Scripts/Net/NetworkBootMode.cs) keys on to boot as
> `GameMode.Server`. (`-dedicatedServer` also works if you prefer.)

---

## 3. Part B — Provision the VM (one-time, `az` CLI)

Run these once. Adjust the two variables at the top.

```bash
# --- edit these ---
RG=game-rg
VM=game-server
MYIP=$(curl -s https://api.ipify.org)   # your current public IP, for locking down SSH
# ------------------

# Resource group in West US
az group create -n "$RG" -l westus

# Static Standard public IP (survives deallocation → stable SSH target)
az network public-ip create -g "$RG" -n ${VM}-ip --sku Standard --allocation-method Static -l westus

# Create the VM: F8s v2, Ubuntu 22.04, 32 GB Premium SSD, accelerated networking on
az vm create \
  -g "$RG" -n "$VM" -l westus \
  --image Ubuntu2204 \
  --size Standard_F8s_v2 \
  --public-ip-address ${VM}-ip \
  --os-disk-size-gb 32 \
  --storage-sku Premium_LRS \
  --accelerated-networking true \
  --admin-username azureuser \
  --ssh-key-values ~/.ssh/id_ed25519.pub

# Lock SSH (port 22) to your IP only — game traffic does NOT need any inbound rule (see Part F)
az vm open-port -g "$RG" -n "$VM" --port 22 --priority 1000
az network nsg rule update \
  -g "$RG" --nsg-name ${VM}NSG -n open-port-22 \
  --source-address-prefixes "$MYIP"
```

Grab the IP for later:
```bash
az vm show -d -g "$RG" -n "$VM" --query publicIps -o tsv
```

> If `Standard_F8s_v2` isn't available in West US on your subscription, try `Standard_FX4mds`
> (higher clock) or `Standard_F8s_v2` in **West US 3**. Check with:
> `az vm list-skus -l westus --size Standard_F --all -o table`

---

## 4. Part C — Deploy the build

From the folder containing `2dgame-server.tar.gz`:
```bash
IP=$(az vm show -d -g game-rg -n game-server --query publicIps -o tsv)

scp 2dgame-server.tar.gz azureuser@$IP:~/
ssh azureuser@$IP '
  mkdir -p ~/server &&
  tar xzf ~/2dgame-server.tar.gz -C ~/server &&
  chmod +x ~/server/2dgame-server &&
  echo "Deployed:" && ls ~/server
'
```

> If the binary fails to launch complaining about a missing library, install the common runtime
> deps once: `sudo apt-get update && sudo apt-get install -y libc6 ca-certificates`. A
> graphics-stripped Dedicated Server build normally needs nothing beyond a base Ubuntu.

---

## 5. Part D — Run it under systemd

This keeps the server alive, restarts it on crash, and logs to journald. On the VM:

```bash
sudo tee /etc/systemd/system/gameserver.service >/dev/null <<'UNIT'
[Unit]
Description=2D Multiplayer Platformer dedicated server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=azureuser
WorkingDirectory=/home/azureuser/server
ExecStart=/home/azureuser/server/2dgame-server -batchmode -nographics -logFile /home/azureuser/server/server.log
Restart=on-failure
RestartSec=3

[Install]
WantedBy=multi-user.target
UNIT

sudo systemctl daemon-reload
sudo systemctl enable --now gameserver
```

Check it's up:
```bash
systemctl status gameserver --no-pager
tail -f ~/server/server.log     # expect: "✅ Dedicated server started — waiting for players."
```

Because it's `enable`d, the server **auto-starts whenever the VM boots** — so your weekend
start/stop (Part E) is all you touch from then on.

Clients connect exactly as before: launch the normal player build, enter a nickname, **Join**.
They find the session by name (`PvPvERoom`) through Photon — no IP needed on their end.

---

## 6. Part E — Weekend start / stop (the cost lever)

Deallocating stops all compute billing. Two tiny commands from your laptop:

**Friday — bring it up:**
```bash
az vm start -g game-rg -n game-server
# systemd auto-starts the server on boot; give it ~30s, then:
ssh azureuser@$(az vm show -d -g game-rg -n game-server --query publicIps -o tsv) \
  'tail -n 5 ~/server/server.log'
```

**Sunday — shut it down (stops billing):**
```bash
az vm deallocate -g game-rg -n game-server
```

> **Use `deallocate`, not just `stop`/shutdown.** A VM that's merely "stopped" from inside the OS
> still bills for compute. `az vm deallocate` releases the compute — that's what makes idle cost
> ~$0. Confirm with `az vm get-instance-view -g game-rg -n game-server --query 'instanceView.statuses[1].displayStatus'`
> → should read **"VM deallocated"**.

Optional belt-and-suspenders: an **auto-shutdown** schedule so you can't forget on Sunday:
```bash
az vm auto-shutdown -g game-rg -n game-server --time 0700 --timezone "Pacific Standard Time"
```
(Auto-shutdown deallocates on schedule; it does not auto-start, so you still run `az vm start`
on Friday.)

---

## 7. Part F — Ports / firewall (important, and reassuring)

With the standard **Photon Cloud** setup this project uses, the dedicated server **connects
outbound** to Photon's name server and relay; clients reach the session **through Photon**, not
by connecting directly to your VM. That means:

- **You do not need to open any inbound game port** on the NSG. Only **SSH (22)**, locked to your
  IP (done in Part B).
- Outbound is open by default on Azure, so the server can reach Photon with no extra rules.

> Only if you later switch Fusion to a **direct / public-IP server mode** (not the default here)
> would you open the server's UDP port inbound on the NSG and point clients at the VM's IP. For
> the current relay-based setup, leave inbound closed except SSH.

---

## 8. Part G — Guardrail: budget alert

So a forgotten VM can never eat the whole credit, set a budget alert (portal is easiest:
*Cost Management → Budgets → Add*, e.g. $50/mo with an 80% email alert). CLI equivalent:
```bash
az consumption budget create \
  --budget-name weekend-server --amount 50 --time-grain Monthly \
  --category Cost --resource-group game-rg \
  --start-date $(date +%Y-%m-01) --end-date 2027-01-01
```

---

## 9. Teardown (if you ever stop hosting)

```bash
az group delete -n game-rg --yes --no-wait
```
Deletes the VM, disk, IP, and NSG in one shot. (Deallocating is enough between weekends; only do
this if you're done for good.)

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `server.log` never prints the ✅ line | Not booting as server | Confirm the run command includes `-batchmode` (or `-dedicatedServer`); check `systemctl status gameserver` |
| Binary won't execute (`No such file`/lib error) | Missing base libs or not executable | `chmod +x`; `sudo apt-get install -y libc6 ca-certificates` |
| Clients can't find the match | Wrong Photon App ID / region mismatch, or server not running | Server and clients must share the same Photon App ID; check `journalctl -u gameserver` |
| SSH times out after a weekend | VM deallocated (expected) | `az vm start` first; the static IP is unchanged |
| Bill higher than expected | VM left running / merely "stopped" | `az vm deallocate`; verify status reads "VM deallocated" |
| Need the current IP | — | `az vm show -d -g game-rg -n game-server --query publicIps -o tsv` |
| AU/JP players lag | ~120–170 ms from US-West is physics, not config | Unavoidable with one US server; Fusion prediction keeps it playable |

---

## Notes

- **AU/JP latency** is inherent to a single US-West server and the NorCal-majority choice — not a
  misconfiguration. A second region only makes sense if that group grows.
- **FX-series vs F8s v2:** FX has the higher clock, but for one 20-player match F8s v2 already has
  far more headroom than the tick loop needs. Upgrade only if the profiler ever shows the server
  frame budget tightening.
- Keep the OS disk small (32 GB). It's the main thing you pay for while deallocated.
