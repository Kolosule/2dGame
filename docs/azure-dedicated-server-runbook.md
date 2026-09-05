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
| Region | **West US 2** (Washington) | Direct gameplay traffic goes to this VM, so its region affects latency. West US 2 is a good balance for a Seattle + California group and had capacity when West US did not. Photon **`usw`** remains the shared discovery region and relay fallback |
| VM size | **Standard_D8as_v5** (8 vCPU, AMD) | Older F/D families (`F8s_v2`, `D8s_v5`) were all capacity-restricted across West US regions; `D8as_v5` is a newer, abundant family with ample headroom for a 20-player tick loop. Was: `F8s_v2` for peak clock — swap back if it's ever in stock and the profiler asks for it |
| Accelerated Networking | **On** | Free; lower latency + jitter (F-series supports it) |
| OS | **Ubuntu 22.04 LTS** | Light, cheap; matches the Linux Dedicated Server build |
| Scripting backend | **Mono** (used) | IL2CPP is ~faster but needs a Linux cross-compile toolchain on a Windows editor (see Part A); Mono builds clean with ample headroom for 20 players |
| Disk | Premium SSD, **32 GB** | Build is small; you pay for the disk even while deallocated, so keep it small |
| Public IP | **Standard static** (~$3.65/mo) | Stable direct-gameplay endpoint and SSH target across deallocations |
| Pricing model | **Standard, NOT Spot** | Spot can be evicted mid-match and drop every player |
| Idle handling | **Deallocate** Sun night | Compute → $0 while off; you pay only disk + static IP |

**Cost estimate:** ~$17 compute for a ~50-hour weekend + ~$9/mo idle (disk + static IP) ≈
**~$26/month**, versus your $150 credit. Comfortable headroom.

### Deployment values and placeholders

| Thing | Value |
|---|---|
| Resource group | `game-rg` |
| VM | `game-server` — Ubuntu 22.04, `Standard_D8as_v5`, **westus2** |
| Server public IP | **`<AZURE_PUBLIC_IP>`** (static — unchanged across deallocate/start) |
| SSH login | `ssh azureuser@<AZURE_PUBLIC_IP>` (key `%USERPROFILE%\.ssh\id_ed25519`, NSG locked to home IP) |
| Build backend | **Mono** (IL2CPP needs a Linux toolchain on a Windows editor — see Part A) |
| Build folder (PC) | `C:\Repo` |
| Executable | `2dgame-server.x86_64` (+ `2dgame-server_Data`) |
| Deployed to (server) | `/home/azureuser/server/` |
| systemd service | `gameserver` (auto-restart + auto-start on boot) |

Photon is still required. The server registers `PvPvERoom` with Photon and clients discover that
session by name exactly as before; players never type the VM address. After discovery, Fusion first
tries a direct UDP connection to the advertised Azure endpoint. If that cannot be established,
Photon relay remains available automatically. A startup message only says direct connections are
enabled. **Only a runtime `Direct` connection-type message proves that a particular player connected
directly.**

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

## ⚠️ Required first: keep the Photon region pinned

**Do this before building anything if you have players outside US-West (e.g. AU/JP).**

Photon Fusion sessions are **region-scoped**. `PhotonAppSettings.asset` is currently pinned to
**`usw`**, and both the server and every client build must keep the same value. This lets all clients
find `PvPvERoom` through Photon's US-West master, regardless of whether their eventual gameplay
connection is direct or relayed.

1. In Unity: **Fusion → Realtime Settings** (opens `PhotonAppSettings.asset`).
2. Confirm **Fixed Region** is `usw`.
3. If it changes, rebuild and redistribute both the dedicated-server build and player build.

The two region choices have different jobs:

- **Photon `usw`:** session registration, discovery, and the relay path when direct UDP fails.
- **Azure West US 2:** the destination for direct gameplay, so VM location affects direct latency.

`usw` means Photon US West; `us` means US East and will not discover the same session.

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

# Static Standard public IP (survives deallocation → stable direct endpoint and SSH target).
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

# Lock SSH (port 22) to your IP only.
# `az vm create` ALREADY created a `default-allow-ssh` rule (SSH open to *). Do NOT run
# `az vm open-port --port 22 --priority 1000` — it collides with that rule (SecurityRuleConflict).
# Just tighten the source of the existing rule to your IP:
az network nsg rule update \
  -g "$RG" --nsg-name ${VM}NSG -n default-allow-ssh \
  --source-address-prefixes "$MYIP"
```

Open the Fusion gameplay port after replacing every angle-bracket placeholder:

```bash
az network nsg rule create \
 --resource-group <RESOURCE_GROUP> \
 --nsg-name <NSG_NAME> \
 --name AllowFusionDirectUdp \
 --priority 1010 \
 --direction Inbound \
 --access Allow \
 --protocol Udp \
 --source-address-prefixes '*' \
 --source-port-ranges '*' \
 --destination-port-ranges 27015
```

Unlike SSH, UDP 27015 must accept arbitrary player addresses, so its source is `'*'`. Keep SSH 22
restricted to the owner's current public IP. Do not broaden the SSH rule to match the game rule.

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
scp C:\path\to\2dgame-server.tar.gz azureuser@<AZURE_PUBLIC_IP>:~/
```

> Two `scp` gotchas we hit: (1) **run it on the PC, not inside an SSH session** — from the server it
> looks for the file *on the server* and fails with *"No such file or directory."* (2) **Keep the
> `:~/`** on the end — without the colon, `scp` silently makes a *local* file literally named
> `azureuser@<AZURE_PUBLIC_IP>` instead of uploading.

**Then log in and unpack, on the server ☁️:**
```bash
ssh azureuser@<AZURE_PUBLIC_IP>   # from the PC; prompt becomes azureuser@game-server:~$
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

This keeps the server alive, restarts it on crash, and logs to journald. Replace
`<AZURE_PUBLIC_IP>` with the VM's static public IPv4 address, then run this on the VM:

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
ExecStart=/home/azureuser/server/2dgame-server.x86_64 -batchmode -nographics -logFile /home/azureuser/server/server.log -gamePort 27015 -publicIp <AZURE_PUBLIC_IP> -publicPort 27015
Restart=on-failure
RestartSec=3

[Install]
WantedBy=multi-user.target
UNIT

sudo systemctl daemon-reload
sudo systemctl enable gameserver
sudo systemctl restart gameserver
```

The command-line values mean:

- `-gamePort 27015`: listen on UDP 27015 on every IPv4 interface.
- `-publicIp <AZURE_PUBLIC_IP>`: advertise the static Azure IPv4 address through Fusion.
- `-publicPort 27015`: advertise UDP 27015 externally.

You can use environment variables instead. Remove the three endpoint options from `ExecStart`, add
these four `Environment` lines, and keep the Unity options:

```ini
[Service]
Environment=GAME_PORT=27015
Environment=PUBLIC_IP=<AZURE_PUBLIC_IP>
Environment=PUBLIC_PORT=27015
Environment=FUSION_RELAY_ONLY=false
ExecStart=/home/azureuser/server/2dgame-server.x86_64 -batchmode -nographics -logFile /home/azureuser/server/server.log
```

For every setting, a command-line value wins over its environment variable. If neither is present,
the internal port defaults to `27015`, the public port defaults to the internal port, no public IP
is forced, and relay-only defaults to `false`.

After any unit-file edit, reload systemd and restart:

```bash
sudo systemctl daemon-reload
sudo systemctl restart gameserver
systemctl status gameserver --no-pager
journalctl -u gameserver -n 100 --no-pager
tail -f ~/server/server.log
```

With the explicit public endpoint, expect:

```text
[Network] Dedicated server listening on UDP 27015; public endpoint <AZURE_PUBLIC_IP>:27015; direct connections enabled with relay fallback.
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
> `ssh azureuser@<AZURE_PUBLIC_IP> "tail -n 5 ~/server/server.log"`

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

## 7. Part F — Direct UDP, relay fallback, and firewalls

The dedicated server binds to `0.0.0.0:27015` by default. When `PUBLIC_IP`/`-publicIp` is set, it
advertises that address and the public port to Fusion. Photon still registers and discovers
`PvPvERoom`; clients do not manually enter the IP. NAT punchthrough stays enabled, so Fusion can
attempt direct UDP and use Photon relay if direct setup fails.

If no public IP is supplied, startup explains that Fusion may use STUN to discover a public
endpoint. That is useful for testing, but the explicit static Azure address is more predictable.
Neither startup message proves that a player is direct.

The Azure NSG must allow inbound UDP 27015 using the `AllowFusionDirectUdp` rule from Part B.
If Ubuntu's firewall is already enabled, also run:

```bash
sudo ufw allow 27015/udp
sudo ufw status
```

Do **not** enable UFW automatically just for this guide. If UFW is inactive, the Azure NSG rule is
still required. To confirm that the process is bound locally:

```bash
sudo ss -lunp | grep ':27015'
```

Each completed client join produces exactly one server-side transport message:

```text
[Network] Player 4 connected using Direct transport.
[Network] Player 5 connected using Relayed transport.
[Network] Player 6 connected using Unknown transport.
```

`Direct` confirms the optimization for that player. `Relayed` confirms Photon fallback. `Unknown`
means Fusion did not report either active transport at callback time; investigate it rather than
assuming direct connectivity.

Before changing architecture when a player is not direct, check these in order:

1. The Azure NSG has `AllowFusionDirectUdp` for inbound UDP 27015 from arbitrary addresses.
2. `sudo ufw status` is inactive or allows `27015/udp`.
3. `sudo ss -lunp | grep ':27015'` shows the server bound to the configured internal port.
4. The static Azure public IPv4 address and public port match systemd.
5. Startup shows the expected public endpoint. That confirms Fusion received
   `CustomPublicAddress`; it does not prove the client connected directly.
6. Server and client builds both use Photon `FixedRegion = usw`.
7. Both still use session name `PvPvERoom`, so Photon discovery reaches the intended server.
8. The joining player's runtime `ConnectionType` log says `Direct`, `Relayed`, or `Unknown`.

Keep Accelerated Networking enabled on the VM. It does not open the firewall, but it reduces network
overhead and jitter after traffic reaches Azure.

### Test relay fallback

The simplest controlled test is relay-only mode:

1. Add bare `-relayOnly` to `ExecStart`, or set `FUSION_RELAY_ONLY=true`.
2. Run `sudo systemctl daemon-reload && sudo systemctl restart gameserver`.
3. Confirm startup logs `[Network] Dedicated server is using Photon relay-only mode.`
4. Join normally through `PvPvERoom` and confirm the player log says `Relayed`.
5. Remove `-relayOnly` (or restore `FUSION_RELAY_ONLY=false`), then reload and restart again.

To test network fallback rather than the explicit switch, remove the UDP NSG rule, restart or
reconnect a client, and inspect its new connection-type log. A new connection should use relay
instead of losing session discovery. Recreate the NSG rule from Part B immediately afterward.
Do not claim fallback worked unless the runtime message says `Relayed`.

Test the existing reconnect flow once with a `Direct` client and once in relay-only mode: interrupt
that client's network, restore it, and let the reconnect UI rejoin `PvPvERoom`. Treat the reconnect
as verified only when the server prints a fresh per-player transport line.

### Permanent relay-only rollback

First enable relay-only in systemd and confirm relayed joins. Then remove the Azure opening:

```bash
az network nsg rule delete \
 --resource-group <RESOURCE_GROUP> \
 --nsg-name <NSG_NAME> \
 --name AllowFusionDirectUdp
```

If you previously added the UFW rule, remove only that rule:

```bash
sudo ufw delete allow 27015/udp
```

Photon outbound connectivity remains required for registration, discovery, and relay.

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
scp 2dgame-server.tar.gz azureuser@<AZURE_PUBLIC_IP>:~/
```

**3. Swap in the new build (server ☁️)** — `ssh azureuser@<AZURE_PUBLIC_IP>`, then:
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
tail -n 20 ~/server/server.log            # want: the [Network] endpoint startup message
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
| `server.log` never prints a `[Network]` startup line | Not booting as server | Confirm the run command includes `-batchmode` (or `-dedicatedServer`); check `systemctl status gameserver` |
| Binary won't execute (`No such file`/lib error) | Missing base libs or not executable | `chmod +x`; `sudo apt-get install -y libc6 ca-certificates` |
| `az vm create` fails **`SkuNotAvailable` / Capacity Restrictions** (behind an ugly Python traceback) | Region out of that VM size | Newer/abundant family (`Standard_D8as_v5`), different region, or `--zone N`; the real error hides under a `'NoneType'…error` traceback — read the `(SkuNotAvailable)` line (see Part B) |
| `scp` says *No such file*, or a file named `azureuser@<ip>` appears | Ran `scp` from inside the SSH session, or omitted the `:~/` | Run `scp` on the **PC**, not the server; keep the `:~/` destination (see Part C) |
| Server won't start / systemd splits the exe path | **Space** in the executable name | Rename exe + `_Data` to a space-free matching base (`2dgame-server.x86_64`); fix `ExecStart` |
| Linux IL2CPP build fails "No Linux SDK found for x64" | Missing cross-compile toolchain on Windows | Install `com.unity.toolchain.linux-x86_64`, or use **Mono** (see Part A) |
| Clients can't find the match | Wrong Photon App ID, **region mismatch** (Best Region on, no `usw` pin), or server not running | Server and clients must share the same Photon App ID **and the same `FixedRegion`** (see "pin the Photon region" above); check `journalctl -u gameserver` |
| Client says `Failed to start host: OperationCanceled` (or `Failed to connect: OperationCanceled`) | Fusion refuses to reuse a `NetworkRunner`, so this is the **second** connect attempt in one app launch — the first one already failed for the real reason. The client log shows `Failed: NetworkRunner should not be reused.` | Read the **first** failure in the client log; that is the real cause. Builds before the runner-rebuild fix need a full restart of the game between attempts |
| Client says `Failed to start host: ServerInRoom` / `GameIdAlreadyExists` | Player clicked **Host** while the dedicated server already owns `PvPvERoom` | Players use **Join** in a dedicated-server deployment; Host is for running without the VM |
| In **Host** mode the Start Match button appears on a *client* instead of the host | Fusion gives the server player the **last** PlayerRef index, so a lowest-id designation rule picks the first client that joined. Dedicated-server deployments are unaffected — the server is not a player | Fixed: the server now designates its own player as the host-client whenever it is one. Builds without the fix must start the match from whichever player is holding the button |
| Only local (US) players find the match; AU/JP can't | A build does not contain the shared `FixedRegion = usw` setting | Confirm the asset, then rebuild **both** server and client |
| Server exits before registering the session | Invalid endpoint option or environment variable | Read the `[Network] Dedicated server configuration error` in `journalctl`; ports must be 1–65535, the public IP must be dotted IPv4, and relay-only accepts only `true`/`false`/`1`/`0` |
| No local UDP listener | Wrong `-gamePort`, bind failure, or server startup failure | Run `sudo ss -lunp | grep ':27015'`; compare it with systemd and the startup log |
| Every player logs `Relayed` | UDP blocked, wrong public endpoint, or NAT punchthrough disabled | Check the NSG, `sudo ufw status`, bound internal port, static public IP/port, systemd arguments, and that relay-only is false |
| Player logs `Unknown` | Fusion did not report Direct or Relayed at join callback time | Check later reconnects and the other endpoint diagnostics; do not count it as Direct |
| Direct works until the UDP rule is removed | Expected | New/reconnecting clients should use relay; confirm a `Relayed` runtime log before calling fallback successful |
| SSH times out after a weekend | VM deallocated (expected) | `az vm start` first; the static IP is unchanged |
| Bill higher than expected | VM left running / merely "stopped" | `az vm deallocate`; verify status reads "VM deallocated" |
| Need the current IP | — | `az vm show -d -g game-rg -n game-server --query publicIps -o tsv` |
| AU/JP players lag | ~120–170 ms from US-West is physics, not config | Unavoidable with one US server; Fusion prediction keeps it playable |

---

## Notes

- **AU/JP latency** is inherent to a single West US 2 server and the NorCal-majority choice for
  direct traffic; relay routing also depends on Photon `usw`. A second deployment region only makes
  sense if that group grows. Without the shared Photon region, clients cannot discover the session.
- **VM size:** we shipped on `Standard_D8as_v5` (8 vCPU AMD) after `F8s_v2`/`D8s_v5` were capacity-
  locked across West US. For one 20-player match it has far more headroom than the tick loop needs;
  move to a higher-clock F-series only if it's in stock *and* the profiler shows the frame budget
  tightening.
- Keep the OS disk small (32 GB). It's the main thing you pay for while deallocated.
