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
| Region | **West US 2** (Washington) | West US (California) was capacity-locked for compute at provision time; West US 2 came up clean. Latency is ~a wash regardless — game traffic routes through the Photon **`usw` (San José)** relay, not the VM (Part F). Fine balance for a Seattle + California group |
| VM size | **Standard_D8as_v5** (8 vCPU, AMD) | Older F/D families (`F8s_v2`, `D8s_v5`) were all capacity-restricted across West US regions; `D8as_v5` is a newer, abundant family with ample headroom for a 20-player tick loop. Was: `F8s_v2` for peak clock — swap back if it's ever in stock and the profiler asks for it |
| Accelerated Networking | **On** | Free; lower latency + jitter (F-series supports it) |
| OS | **Ubuntu 22.04 LTS** | Light, cheap; matches the Linux Dedicated Server build |
| Scripting backend | **Mono** (used) | IL2CPP is ~faster but needs a Linux cross-compile toolchain on a Windows editor (see Part A); Mono builds clean with ample headroom for 20 players |
| Disk | Premium SSD, **32 GB** | Build is small; you pay for the disk even while deallocated, so keep it small |
| Public IP | **Standard static** (~$3.65/mo) | Stable SSH target across deallocations; game traffic doesn't use it (Photon relays) |
| Pricing model | **Standard, NOT Spot** | Spot can be evicted mid-match and drop every player |
| Idle handling | **Deallocate** Sun night | Compute → $0 while off; you pay only disk + static IP |

**Cost estimate:** ~$17 compute for a ~50-hour weekend + ~$9/mo idle (disk + static IP) ≈
**~$26/month**, versus your $150 credit. Comfortable headroom.

### This deployment's actual values (built & running 2026-07-28)

| Thing | Value |
|---|---|
| Resource group | `game-rg` |
| VM | `game-server` — Ubuntu 22.04, `Standard_D8as_v5`, **westus2** |
| Server public IP | **`20.59.20.112`** (static — unchanged across deallocate/start) |
| SSH login | `ssh azureuser@20.59.20.112` (key `%USERPROFILE%\.ssh\id_ed25519`, NSG locked to home IP) |
| Build backend | **Mono** (IL2CPP needs a Linux toolchain on a Windows editor — see Part A) |
| Build folder (PC) | `C:\Repo` |
| Executable | `2dgame-server.x86_64` (+ `2dgame-server_Data`) |
| Deployed to (server) | `/home/azureuser/server/` |
| systemd service | `gameserver` (auto-restart + auto-start on boot) |

> Pushing a new build later → **Part H — Updating the game**.

---

## 1. Prerequisites (one-time, local machine)

1. **Unity module:** install **Linux Dedicated Server Build Support** via Unity Hub →
   *Installs → your editor → Add Modules*. (This gives the "Dedicated Server / Linux" build
   platform, which is graphics-stripped and headless by design.)

2. **Azure CLI:** install and sign in.

   **Windows (this project's dev machine):** install via
   `winget install --exact --id Microsoft.AzureCLI`, then **close and reopen the terminal** so `az`
   lands on PATH. Verify with `az version`.

   ```bash
   az login
   az account show --query name -o tsv   # confirm you're on the right subscription
   ```
   If you have more than one subscription, pin it:
   ```bash
   az account set --subscription "<your-subscription-name-or-id>"
   ```

   > **First-run provider registration:** the first networking/compute command on a fresh
   > subscription may print *"Resource provider 'Microsoft.Network' … is not registered. We are
   > registering for you."* — that's a one-time, ~1–3 min auto-enable, not an error. Wait until
   > `az provider show -n Microsoft.Network --query registrationState -o tsv` reads `Registered`,
   > then re-run the command that triggered it.

3. **An SSH key** (used to reach the VM). If you don't have one:
   ```bash
   ssh-keygen -t ed25519 -C "azure-gameserver"
   ```
   > `ssh-keygen` ships with Windows 11. Press **Enter** through both prompts to accept the default
   > path (`%USERPROFILE%\.ssh\id_ed25519`, e.g. `C:\Users\<you>\.ssh\`) and an empty passphrase. The
   > `az vm create` below points `--ssh-key-values` at the **`.pub`** (public) half of this pair; the
   > private half (no extension) never leaves your machine.

---

## ⚠️ Required first: pin the Photon region (cross-region players)

**Do this before building anything if you have players outside US-West (e.g. AU/JP).**

Photon Fusion sessions are **region-scoped**. `PhotonAppSettings.asset` currently has
`FixedRegion:` **blank**, which means every peer runs **Best Region** selection and joins its own
lowest-ping regional master. With a single US-hosted server that silently breaks discovery:

- The Azure **West US** server registers `PvPvERoom` in Photon's **US-West (`usw`)** master.
- NorCal / Seattle clients also ping-select `usw` → they find the match. ✅
- **AU / JP clients ping-select their *own* local region → they never see the session at all.** ❌

To make one US server discoverable by everyone, pin **both the server build and the client build**
to the same region. For **any** US-West Azure VM (West US / West US 2 / West US 3) that region is
**`usw`** (US West, San José) — the Azure region and the Photon region are independent knobs, and
`usw` stays the right pin for all of them:

1. In Unity: **Fusion → Realtime Settings** (opens `PhotonAppSettings.asset`).
2. Set **Fixed Region** to `usw`.
3. Rebuild **and redistribute both** the dedicated-server build *and* the player build — a region
   pin only takes effect in builds made after the change.

> Region tokens: `usw` = US West (San José, matches Azure West US), `us` = US **East** (don't use
> for a West US VM). AU/JP players then connect to `usw` at the ~120–170 ms noted at the bottom —
> that latency is only reachable *because* they're now pinned to the same master.

---

## 2. Part A — Build the Linux Dedicated Server (Unity, editor-side)

I can't run the Unity build from here, so do this in the editor:

1. **File → Build Profiles** (Unity 6) or **File → Build Settings** (older). Select/add the
   **Dedicated Server** platform, set **Target Platform = Linux**, and **Switch Platform**.
2. Confirm **Scene list**: `MainMenu` at index **0**, `Gameplay` at index **1**
   (`GameNetworkManager.gameplaySceneIndex` defaults to 1). The dedicated-server boot skips the
   menu automatically, but the scene indices still need to be correct.
3. *(Optional, for max perf)* **Player Settings → Other Settings → Scripting Backend = IL2CPP**.
   > **Windows caveat (we hit this):** building a Linux IL2CPP server from a Windows editor needs the
   > Linux cross-compiler toolchain (`com.unity.toolchain.linux-x86_64`). Without it the build fails
   > with *"No Linux SDK found for x64 … Could not set up a toolchain for Architecture x64."* Either
   > install that package (**Window → Package Manager → + → Install package by name**) **or** just set
   > **Scripting Backend = Mono** — Mono needs no toolchain and has ample perf for a 20-player match.
   > **This project shipped its first server on Mono.**
4. **Build** into an **empty** folder, and in the save dialog name the output **`2dgame-server`**
   — **no spaces** (a space breaks the systemd `ExecStart` later; the default `Server Build` name
   bit us here). A Linux build produces:
   ```
   2dgame-server.x86_64     <- the executable (Linux builds get a .x86_64 extension)
   2dgame-server_Data/      <- data folder — its base name MUST match the exe's
   UnityPlayer.so, libdecor-*.so, etc.
   *_BurstDebugInformation_DoNotShip/   <- debug symbols; do NOT ship (excluded when packaging)
   ```
   > If you forget and build with a space (`Server Build.x86_64` + `Server Build_Data`), just rename
   > **both** to a matching space-free base (`2dgame-server.x86_64` + `2dgame-server_Data`) — they
   > must always share the same base name or the server can't find its data.
5. Package the build into one tarball. Archive the folder's **contents from outside it** (so files
   sit at the tarball's top level) and **exclude the debug folder**:
   ```bash
   # Windows PowerShell (tar ships with Win 11). Tarball lands in your current folder.
   tar -czf 2dgame-server.tar.gz -C "C:\path\to\build" --exclude="*DoNotShip*" .
   ```
   ```bash
   # macOS/Linux equivalent
   tar czf 2dgame-server.tar.gz -C /path/to/build --exclude='*DoNotShip*' .
   ```
   > Don't create the tarball *inside* the build folder while archiving `*` — it can sweep a
   > half-written copy of itself (and stray files) into the archive and ship them too.

> **Why batch mode still matters:** a Dedicated Server build does *not* auto-set
> `Application.isBatchMode`. The run command below passes `-batchmode`, which is what
> [`NetworkBootMode.Resolve`](../Assets/Scripts/Net/NetworkBootMode.cs) keys on to boot as
> `GameMode.Server`. (`-dedicatedServer` also works if you prefer.)

---

## 3. Part B — Provision the VM (one-time, `az` CLI)

Run these once. Adjust the variables at the top.

> **Windows / PowerShell note:** the blocks below are **bash**. In PowerShell the `RG=…` / `$(…)`
> variable syntax won't work — either run them in **Git Bash / WSL**, or substitute literal values
> (`game-rg`, `game-server`, `westus2`, your IP) directly into each `az` command and use
> **`curl.exe -s`** (not bare `curl`, which is a different alias in Windows PowerShell 5.1) to fetch
> your IP. The `az` commands themselves are identical across shells.

```bash
# --- edit these ---
RG=game-rg
VM=game-server
MYIP=$(curl -s https://api.ipify.org)   # your current public IP, for locking down SSH
LOC=westus2                             # capacity fell over in westus; westus2 came up clean
SIZE=Standard_D8as_v5                   # F8s_v2 / D8s_v5 were capacity-restricted (see note below)
# ------------------

# Resource group
az group create -n "$RG" -l "$LOC"

# Static Standard public IP (survives deallocation → stable SSH target).
# NOTE: a public IP is REGIONAL — if you change $LOC later you must delete + recreate it there.
az network public-ip create -g "$RG" -n ${VM}-ip --sku Standard --allocation-method Static -l "$LOC"

# Create the VM: 8 vCPU, Ubuntu 22.04, 32 GB Premium SSD, accelerated networking on
az vm create \
  -g "$RG" -n "$VM" -l "$LOC" \
  --image Ubuntu2204 \
  --size "$SIZE" \
  --public-ip-address ${VM}-ip \
  --os-disk-size-gb 32 \
  --storage-sku Premium_LRS \
  --accelerated-networking true \
  --admin-username azureuser \
  --ssh-key-values ~/.ssh/id_ed25519.pub

# Lock SSH (port 22) to your IP only — game traffic does NOT need any inbound rule (see Part F).
# `az vm create` ALREADY created a `default-allow-ssh` rule (SSH open to *). Do NOT run
# `az vm open-port --port 22 --priority 1000` — it collides with that rule (SecurityRuleConflict).
# Just tighten the source of the existing rule to your IP:
az network nsg rule update \
  -g "$RG" --nsg-name ${VM}NSG -n default-allow-ssh \
  --source-address-prefixes "$MYIP"
```

Grab the IP for later:
```bash
az vm show -d -g "$RG" -n "$VM" --query publicIps -o tsv
```

> **Capacity restrictions are common — we hit them repeatedly.** `F8s_v2`, then `D8s_v5`, came back
> `SkuNotAvailable` across **West US** *and* **West US 2**. The CLI buries this behind an ugly Python
> traceback (*"content … already consumed"* / `'NoneType' object has no attribute 'error'`) — that's
> a cosmetic CLI bug; the line that actually matters is **`(SkuNotAvailable) … Capacity Restrictions`**.
> Fixes, least-disruptive first: (1) switch to a newer, abundant family — **`Standard_D8as_v5` worked**;
> (2) different region (`westus2`/`westus3` — the static IP is regional, so delete + recreate it there
> first); (3) a specific `--zone N`. What this project actually shipped: **`Standard_D8as_v5` in
> `westus2`**. (`az vm list-skus -l westus2 --size Standard_D --all -o table` lists candidates, but
> note it reflects subscription offers, not live capacity — the only sure test is `az vm create`.)

---

## 4. Part C — Deploy the build

Two contexts — keep them straight: **💻 your PC** (PowerShell) holds the build; **☁️ the server** is
reached via `ssh`. **`scp` and `ssh` both run from the PC.**

**On your PC 💻 — upload the tarball** (full path so the current folder doesn't matter; `scp` uses
your SSH key, no password):
```bash
scp C:\path\to\2dgame-server.tar.gz azureuser@20.59.20.112:~/
```

> Two `scp` gotchas we hit: (1) **run it on the PC, not inside an SSH session** — from the server it
> looks for the file *on the server* and fails with *"No such file or directory."* (2) **Keep the
> `:~/`** on the end — without the colon, `scp` silently makes a *local* file literally named
> `azureuser@20.59.20.112` instead of uploading.

**Then log in and unpack, on the server ☁️:**
```bash
ssh azureuser@20.59.20.112        # from the PC; prompt becomes azureuser@game-server:~$
```
```bash
mkdir -p ~/server
tar xzf ~/2dgame-server.tar.gz -C ~/server
chmod +x ~/server/2dgame-server.x86_64      # note the .x86_64 — Linux builds carry it
ls -lh ~/server                              # expect: the exe, _Data, and .so files
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
ExecStart=/home/azureuser/server/2dgame-server.x86_64 -batchmode -nographics -logFile /home/azureuser/server/server.log
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
> **PowerShell:** the `$(…)` sub-shell won't expand — just use the literal IP:
> `ssh azureuser@20.59.20.112 "tail -n 5 ~/server/server.log"`

**Sunday — shut it down (stops billing):**
```bash
az vm deallocate -g game-rg -n game-server
```

> **Use `deallocate`, not just `stop`/shutdown.** A VM that's merely "stopped" from inside the OS
> still bills for compute. `az vm deallocate` releases the compute — that's what makes idle cost
> ~$0. Confirm with `az vm get-instance-view -g game-rg -n game-server --query 'instanceView.statuses[1].displayStatus'`
> → should read **"VM deallocated"**.

Optional belt-and-suspenders: an **auto-shutdown** schedule so you can't forget on Sunday.

> **`--timezone` is not supported** by `az vm auto-shutdown` in current CLI versions
> (`unrecognized arguments: --timezone`). Two working paths:
> - **Portal (handles DST):** VM → *Operations → Auto-shutdown* → On, pick **7:00 AM** and
>   **(UTC-08:00) Pacific Time** → Save.
> - **CLI (time is UTC):** give the time in UTC and drop the timezone flag. 7 AM Pacific ≈ **1400 UTC**
>   in summer (PDT); it drifts to 6 AM Pacific in winter (PST) — fine for a safety net:
>   ```bash
>   az vm auto-shutdown -g game-rg -n game-server --time 1400
>   ```

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

## Part H — Updating the game (push a new build)

When you change the game, you rebuild, re-upload, and restart the service. The VM must be **running**
(if it's deallocated, run `az vm start -g game-rg -n game-server` first and give it ~30 s).

> **Pick a quiet moment — restarting the service drops everyone connected.** And if the change touches
> gameplay or networking, **rebuild and redistribute the *player* build too**: Fusion clients and the
> server must be on matching builds, and any Photon region/config change only lands in builds made
> *after* it.

**1. Rebuild (Unity, PC 💻)** — same as Part A: **Dedicated Server / Linux**, **Mono** backend. Name
it **`2dgame-server`** in the save dialog (space-free → `2dgame-server.x86_64` + `2dgame-server_Data`,
no renaming needed). Build into an **empty** folder (or clear the old one first) so stale files don't
linger.

**2. Package + upload (PC 💻):**
```bash
tar -czf 2dgame-server.tar.gz -C "C:\Repo" --exclude="*DoNotShip*" .
scp 2dgame-server.tar.gz azureuser@20.59.20.112:~/
```

**3. Swap in the new build (server ☁️)** — `ssh azureuser@20.59.20.112`, then:
```bash
sudo systemctl stop gameserver            # stop the old version (drops players)
rm -rf ~/server && mkdir -p ~/server      # wipe old files so nothing stale survives
tar xzf ~/2dgame-server.tar.gz -C ~/server
chmod +x ~/server/2dgame-server.x86_64
sudo systemctl start gameserver           # start the new version
```
> `rm -rf ~/server` removes only the build folder (fully rebuildable from the tarball) — but
> double-check the path reads exactly `~/server` before running it. The wipe matters: extracting
> *over* an old build leaves behind any files you deleted in the new version.

**4. Verify (server ☁️):**
```bash
systemctl status gameserver --no-pager    # want: active (running)
tail -n 20 ~/server/server.log            # want: ✅ Dedicated server started — waiting for players.
```

No systemd changes are ever needed — the service already points at `~/server` and auto-starts on
boot, so every future update is just these four steps. When you're done playing, `az vm deallocate`
as usual (Part E).

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
| `az vm create` fails **`SkuNotAvailable` / Capacity Restrictions** (behind an ugly Python traceback) | Region out of that VM size | Newer/abundant family (`Standard_D8as_v5`), different region, or `--zone N`; the real error hides under a `'NoneType'…error` traceback — read the `(SkuNotAvailable)` line (see Part B) |
| `scp` says *No such file*, or a file named `azureuser@<ip>` appears | Ran `scp` from inside the SSH session, or omitted the `:~/` | Run `scp` on the **PC**, not the server; keep the `:~/` destination (see Part C) |
| Server won't start / systemd splits the exe path | **Space** in the executable name | Rename exe + `_Data` to a space-free matching base (`2dgame-server.x86_64`); fix `ExecStart` |
| Linux IL2CPP build fails "No Linux SDK found for x64" | Missing cross-compile toolchain on Windows | Install `com.unity.toolchain.linux-x86_64`, or use **Mono** (see Part A) |
| Clients can't find the match | Wrong Photon App ID, **region mismatch** (Best Region on, no `usw` pin), or server not running | Server and clients must share the same Photon App ID **and the same `FixedRegion`** (see "pin the Photon region" above); check `journalctl -u gameserver` |
| Only local (US) players find the match; AU/JP can't | `FixedRegion` blank → Best Region routes each peer to a different master | Pin `FixedRegion = usw` and rebuild **both** server and client |
| SSH times out after a weekend | VM deallocated (expected) | `az vm start` first; the static IP is unchanged |
| Bill higher than expected | VM left running / merely "stopped" | `az vm deallocate`; verify status reads "VM deallocated" |
| Need the current IP | — | `az vm show -d -g game-rg -n game-server --query publicIps -o tsv` |
| AU/JP players lag | ~120–170 ms from US-West is physics, not config | Unavoidable with one US server; Fusion prediction keeps it playable |

---

## Notes

- **AU/JP latency** is inherent to a single US-West server and the NorCal-majority choice — not a
  misconfiguration. A second region only makes sense if that group grows. Note this ~120–170 ms is
  only *reachable* once AU/JP clients are pinned to `usw` (see "pin the Photon region"); without the
  pin they land on a different master and can't join at all.
- **VM size:** we shipped on `Standard_D8as_v5` (8 vCPU AMD) after `F8s_v2`/`D8s_v5` were capacity-
  locked across West US. For one 20-player match it has far more headroom than the tick loop needs;
  move to a higher-clock F-series only if it's in stock *and* the profiler shows the frame budget
  tightening.
- Keep the OS disk small (32 GB). It's the main thing you pay for while deallocated.
