# Menu & Lobby Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the blind menu/team-selection flow with a lobby that shows a live 20-player roster, auto-assigns balanced teams on join, and lets the first joiner (designated host) start the match at any time.

**Architecture:** Pure-C# protocol + server state (`LobbyProtocol`, `LobbyServerState` in the engine-free `Game.Net` asmdef) over the existing Fusion reliable-data transport; two new MonoBehaviours (`MainMenuUI`, `LobbyScreenUI`) render purely from server-broadcast `LobbyState` snapshots; `GameNetworkManager` slims to boot + transport + server-side lobby glue. Host mode feeds its own UI via a local loopback of the same snapshot.

**Tech Stack:** Unity 6000.3.0f1, Photon Fusion 2 (reliable data, no NetworkObjects in the menu scene), uGUI + TextMeshPro, NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-07-06-menu-revamp-design.md`

## Global Constraints

- Branch: `feat/menu-lobby-revamp` (already created from main; spec committed).
- Session name stays `"PvPvERoom"`; `maxPlayers` stays `20`; `gameplaySceneIndex` stays `1`.
- No NetworkObjects in the MainMenu scene — all lobby traffic is Fusion reliable data.
- `Assets/Scripts/Net/` is asmdef `Game.Net` with `noEngineReferences: true` — nothing in it may reference UnityEngine or Fusion. Player ids are plain `int`s (mapping from `PlayerRef.PlayerId` happens in `GameNetworkManager`).
- Nicknames: max 16 chars (`LobbyProtocol.MaxNicknameChars`), trimmed, control chars stripped, enforced client- and server-side.
- New UI text uses TextMeshPro (`TMP_Text`, `TMP_InputField`), not legacy `UnityEngine.UI.Text`.
- Keep `LobbyTeamChoices` / `LobbyLoadoutChoices` statics and their semantics — `NetworkedSpawnManager` reads them at spawn (`Assets/Scripts/NetworkedSpawnManager.cs:164,228`).
- The Unity editor may hold the project lock (`-batchmode -runTests` fails). All per-task verification uses the out-of-editor tooling below; the in-editor EditMode run + multi-peer check happens once at the end (Task 7).
- `.meta` files for new assets must be created manually (random lowercase 32-hex GUID; editor is locked). Never reuse a GUID.
- Do not commit `.plastic/*` changes.

## Out-of-editor verification tooling

Two PowerShell scripts, created in Task 1, living in the scratchpad dir (call it `$SCRATCH` — use the session scratchpad path). `$ED = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor"`.

1. **`run-logic-tests.ps1`** — compiles the given `Game.Net` sources + NUnit test files + a reflection-based `TestMain.cs` into an exe with Unity's bundled Roslyn, runs it on Unity's bundled .NET runtime. This runs the *real* NUnit test files (no mirrored asserts) by reflecting over `[Test]` methods.
2. **`compile-gate.ps1`** — compiles all of `Assets/Scripts` (Assembly-CSharp surface, asmdef folders excluded) against UnityEngine/Fusion/ScriptAssemblies refs, with a *freshly compiled* `Game.Net` (the `Library/ScriptAssemblies/Game.Net.dll` copy is stale while the editor is locked).

If the net40 `nunit.framework.dll` fails to load on the modern runtime (unlikely — asserts are pure managed code), fall back to the plain-assert-harness pattern from the 2026-07-06 responsiveness work.

---

### Task 1: LobbyHostPolicy start-rule change + verification tooling

**Files:**
- Modify: `Assets/Scripts/Net/LobbyHostPolicy.cs`
- Modify: `Assets/Tests/EditMode/Net/LobbyHostPolicyTests.cs`
- Create: `$SCRATCH\run-logic-tests.ps1`, `$SCRATCH\TestMain.cs`

**Interfaces:**
- Produces: `LobbyHostPolicy.CanStart(int activePlayerCount) -> bool` (replaces the old `CanStart(IReadOnlyList<int>, Func<int,bool>)`). `DesignateHostId(IReadOnlyList<int>) -> int` and `NoHost = -1` unchanged. Task 3 consumes both.

- [ ] **Step 1: Create the test-runner tooling**

`$SCRATCH\TestMain.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;

public static class TestMain
{
    public static int Main()
    {
        int pass = 0, fail = 0;
        foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsClass))
        {
            foreach (var m in t.GetMethods().Where(HasTestAttr))
            {
                try
                {
                    m.Invoke(Activator.CreateInstance(t), null);
                    pass++;
                }
                catch (Exception e)
                {
                    fail++;
                    Console.WriteLine($"FAIL {t.Name}.{m.Name}: {e.InnerException?.Message ?? e.Message}");
                }
            }
        }
        Console.WriteLine($"{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }

    static bool HasTestAttr(MethodInfo m) =>
        m.GetCustomAttributes(false).Any(a => a.GetType().Name == "TestAttribute");
}
```

`$SCRATCH\run-logic-tests.ps1`:

```powershell
param([string[]]$Sources)
$ED = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor"
$proj = "C:\Users\1\Documents\GitHub\2dGame"
$scratch = $PSScriptRoot
$fw = Get-ChildItem "$ED\Data\NetCoreRuntime\shared\Microsoft.NETCore.App" | Select-Object -First 1
$nunit = Get-ChildItem "$proj\Library\PackageCache" -Filter nunit.framework.dll -Recurse |
    Select-Object -First 1 -ExpandProperty FullName
$lines = @("-nologo", "-target:exe", "-nostdlib", "-out:`"$scratch\logictests.exe`"")
foreach ($dll in Get-ChildItem $fw.FullName -Filter *.dll) { $lines += "-r:`"$($dll.FullName)`"" }
$lines += "-r:`"$nunit`""
foreach ($s in $Sources) { $lines += "`"$s`"" }
$lines += "`"$scratch\TestMain.cs`""
Set-Content "$scratch\tests.rsp" $lines -Encoding utf8
& "$ED\Data\NetCoreRuntime\dotnet.exe" exec "$ED\Data\DotNetSdkRoslyn\csc.dll" "@$scratch\tests.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "COMPILE FAILED"; exit 1 }
Copy-Item $nunit "$scratch\nunit.framework.dll" -Force
Set-Content "$scratch\logictests.runtimeconfig.json" `
    ('{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"' + $fw.Name + '"}}}') -Encoding utf8
& "$ED\Data\NetCoreRuntime\dotnet.exe" exec "$scratch\logictests.exe"
exit $LASTEXITCODE
```

- [ ] **Step 2: Update the tests (failing first)**

Replace the three `CanStart_*` tests in `Assets/Tests/EditMode/Net/LobbyHostPolicyTests.cs` (keep the four `DesignateHostId_*` tests and remove the now-unused `using System.Collections.Generic;` only if `HashSet` was its sole use — it was, so remove it):

```csharp
using NUnit.Framework;

public class LobbyHostPolicyTests
{
    [Test]
    public void DesignateHostId_Empty_ReturnsNoHost()
    {
        Assert.AreEqual(LobbyHostPolicy.NoHost, LobbyHostPolicy.DesignateHostId(new int[0]));
    }

    [Test]
    public void DesignateHostId_SinglePlayer_ReturnsThatPlayer()
    {
        Assert.AreEqual(3, LobbyHostPolicy.DesignateHostId(new[] { 3 }));
    }

    [Test]
    public void DesignateHostId_ReturnsLowestId_RegardlessOfOrder()
    {
        Assert.AreEqual(1, LobbyHostPolicy.DesignateHostId(new[] { 4, 1, 7, 2 }));
    }

    [Test]
    public void DesignateHostId_AfterLowestLeaves_ReturnsNextLowest()
    {
        // host (id 1) left; remaining roster re-designates to id 2
        Assert.AreEqual(2, LobbyHostPolicy.DesignateHostId(new[] { 4, 2, 7 }));
    }

    [Test]
    public void CanStart_NoPlayers_False()
    {
        Assert.IsFalse(LobbyHostPolicy.CanStart(0));
    }

    [Test]
    public void CanStart_OnePlayer_True()
    {
        Assert.IsTrue(LobbyHostPolicy.CanStart(1));
    }

    [Test]
    public void CanStart_FullLobby_True()
    {
        Assert.IsTrue(LobbyHostPolicy.CanStart(20));
    }
}
```

- [ ] **Step 3: Run to verify failure**

```powershell
powershell -File "$SCRATCH\run-logic-tests.ps1" -Sources @(
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Net\LobbyHostPolicy.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Tests\EditMode\Net\LobbyHostPolicyTests.cs")
```

Expected: **COMPILE FAILED** — `CanStart(int)` does not exist yet (CS1503/CS7036 on the int argument).

- [ ] **Step 4: Implement the rule change**

Replace `Assets/Scripts/Net/LobbyHostPolicy.cs` entirely:

```csharp
using System.Collections.Generic;

/// <summary>
/// Pure lobby decisions. The "host-client" (the player who gets the Start button) is the lowest-id
/// active player, so designation is deterministic and re-resolves when that player leaves. CanStart
/// is the alpha stress-test gate: any non-empty lobby may start — teams are auto-assigned on join
/// (see LobbyServerState), so nobody can block the match by failing to choose.
/// </summary>
public static class LobbyHostPolicy
{
    public const int NoHost = -1;

    public static int DesignateHostId(IReadOnlyList<int> activePlayerIds)
    {
        int host = NoHost;
        for (int i = 0; i < activePlayerIds.Count; i++)
        {
            int id = activePlayerIds[i];
            if (host == NoHost || id < host) host = id;
        }
        return host;
    }

    public static bool CanStart(int activePlayerCount) => activePlayerCount >= 1;
}
```

Note: `GameNetworkManager` still calls the old signature at this point — that is expected and gets fixed in Task 4. The logic-test compile (which only includes `Game.Net` sources) is green before then; the full compile gate is not run until Task 4.

- [ ] **Step 5: Run to verify pass**

Same command as Step 3. Expected: `7 passed, 0 failed`, exit 0.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Scripts/Net/LobbyHostPolicy.cs Assets/Tests/EditMode/Net/LobbyHostPolicyTests.cs
git commit -m "feat(lobby): CanStart is now >=1 player (teams auto-assigned, nobody blocks start)"
```

---

### Task 2: LobbyProtocol — snapshot + nickname wire format

**Files:**
- Create: `Assets/Scripts/Net/LobbyProtocol.cs` (+ `.meta`)
- Create: `Assets/Tests/EditMode/Net/LobbyProtocolTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `LobbyHostPolicy.NoHost` (Task 1).
- Produces (Tasks 3–5 rely on these exact signatures):
  - `struct LobbyPlayerEntry { int Id; string Name; int Team; ctor(int,string,int) }`
  - `class LobbyStateSnapshot { bool CanStart; int MaxPlayers; int HostId; List<LobbyPlayerEntry> Players }`
  - `LobbyProtocol.MaxNicknameChars = 16`, `MaxNicknameBytes = 64`
  - `string SanitizeNickname(string raw)` — trim, strip control chars, cap 16; null/whitespace → `""`
  - `string PlaceholderName(int playerId)` — `"Player {id}"`
  - `byte[] EncodeNickname(string sanitized)`
  - `bool TryDecodeNickname(byte[] buffer, int offset, int count, out string name)` — false on null/empty/oversize/whitespace-only
  - `byte[] EncodeLobbyState(LobbyStateSnapshot s)`
  - `bool TryDecodeLobbyState(byte[] buffer, int offset, int count, out LobbyStateSnapshot s)` — false on any malformed input, never throws

- [ ] **Step 1: Create `.meta` files for both new files**

Generate a fresh GUID per file: `powershell -Command "[guid]::NewGuid().ToString('N')"`. MonoImporter template (one per file, each with its own GUID):

```yaml
fileFormatVersion: 2
guid: <32-hex-guid>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

- [ ] **Step 2: Write the failing tests**

`Assets/Tests/EditMode/Net/LobbyProtocolTests.cs`:

```csharp
using NUnit.Framework;

public class LobbyProtocolTests
{
    [Test]
    public void SanitizeNickname_TrimsCapsAndStripsControls()
    {
        Assert.AreEqual("Bob", LobbyProtocol.SanitizeNickname("  Bob  "));
        Assert.AreEqual(new string('a', 16), LobbyProtocol.SanitizeNickname(new string('a', 40)));
        Assert.AreEqual("", LobbyProtocol.SanitizeNickname("   "));
        Assert.AreEqual("", LobbyProtocol.SanitizeNickname(null));
        Assert.AreEqual("ab", LobbyProtocol.SanitizeNickname("a\tb"));
    }

    [Test]
    public void Nickname_RoundTrip()
    {
        byte[] buf = LobbyProtocol.EncodeNickname("Bob");
        Assert.IsTrue(LobbyProtocol.TryDecodeNickname(buf, 0, buf.Length, out string name));
        Assert.AreEqual("Bob", name);
    }

    [Test]
    public void Nickname_MultibyteRoundTrip()
    {
        byte[] buf = LobbyProtocol.EncodeNickname("Ünïcøde");
        Assert.IsTrue(LobbyProtocol.TryDecodeNickname(buf, 0, buf.Length, out string name));
        Assert.AreEqual("Ünïcøde", name);
    }

    [Test]
    public void TryDecodeNickname_Malformed_False()
    {
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(null, 0, 3, out _));
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(new byte[0], 0, 0, out _));
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(new byte[100], 0, 100, out _)); // > MaxNicknameBytes
        byte[] spaces = LobbyProtocol.EncodeNickname("   ");
        Assert.IsFalse(LobbyProtocol.TryDecodeNickname(spaces, 0, spaces.Length, out _));
    }

    [Test]
    public void LobbyState_RoundTrip_EmptyRoster()
    {
        var s = new LobbyStateSnapshot { CanStart = false, MaxPlayers = 20, HostId = LobbyHostPolicy.NoHost };
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        Assert.IsTrue(LobbyProtocol.TryDecodeLobbyState(buf, 0, buf.Length, out var d));
        Assert.IsFalse(d.CanStart);
        Assert.AreEqual(20, d.MaxPlayers);
        Assert.AreEqual(LobbyHostPolicy.NoHost, d.HostId);
        Assert.AreEqual(0, d.Players.Count);
    }

    [Test]
    public void LobbyState_RoundTrip_FullRoster()
    {
        var s = new LobbyStateSnapshot { CanStart = true, MaxPlayers = 20, HostId = 1 };
        for (int i = 1; i <= 20; i++)
            s.Players.Add(new LobbyPlayerEntry(i, "Player" + i, (i % 2) + 1));
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        Assert.IsTrue(LobbyProtocol.TryDecodeLobbyState(buf, 0, buf.Length, out var d));
        Assert.IsTrue(d.CanStart);
        Assert.AreEqual(1, d.HostId);
        Assert.AreEqual(20, d.Players.Count);
        Assert.AreEqual("Player7", d.Players[6].Name);
        Assert.AreEqual(2, d.Players[6].Team);
        Assert.AreEqual(7, d.Players[6].Id);
    }

    [Test]
    public void LobbyState_TruncatedBuffers_AllRejected()
    {
        var s = new LobbyStateSnapshot { CanStart = false, MaxPlayers = 20, HostId = 3 };
        s.Players.Add(new LobbyPlayerEntry(3, "Ann", 1));
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        for (int len = 0; len < buf.Length; len++)
            Assert.IsFalse(LobbyProtocol.TryDecodeLobbyState(buf, 0, len, out _), $"len={len} should fail");
    }

    [Test]
    public void LobbyState_BadTeamByte_Rejected()
    {
        var s = new LobbyStateSnapshot { CanStart = true, MaxPlayers = 20, HostId = 5 };
        s.Players.Add(new LobbyPlayerEntry(5, "Ann", 1));
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        buf[11] = 9; // header is 7 bytes, player id is 4 -> team byte sits at index 11
        Assert.IsFalse(LobbyProtocol.TryDecodeLobbyState(buf, 0, buf.Length, out _));
    }

    [Test]
    public void LobbyState_TrailingBytes_Rejected()
    {
        var s = new LobbyStateSnapshot { CanStart = true, MaxPlayers = 20, HostId = LobbyHostPolicy.NoHost };
        byte[] buf = LobbyProtocol.EncodeLobbyState(s);
        byte[] longer = new byte[buf.Length + 1];
        System.Array.Copy(buf, longer, buf.Length);
        Assert.IsFalse(LobbyProtocol.TryDecodeLobbyState(longer, 0, longer.Length, out _));
    }
}
```

- [ ] **Step 3: Run to verify failure**

```powershell
powershell -File "$SCRATCH\run-logic-tests.ps1" -Sources @(
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Net\LobbyHostPolicy.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Tests\EditMode\Net\LobbyHostPolicyTests.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Tests\EditMode\Net\LobbyProtocolTests.cs")
```

Expected: **COMPILE FAILED** — `LobbyProtocol` type not found (CS0246).

- [ ] **Step 4: Implement**

`Assets/Scripts/Net/LobbyProtocol.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>One player's row in the lobby roster snapshot.</summary>
public struct LobbyPlayerEntry
{
    public int Id;
    public string Name;
    public int Team; // 1 or 2

    public LobbyPlayerEntry(int id, string name, int team)
    {
        Id = id;
        Name = name;
        Team = team;
    }
}

/// <summary>Full lobby state, broadcast by the server after every lobby change.</summary>
public class LobbyStateSnapshot
{
    public bool CanStart;
    public int MaxPlayers;
    public int HostId = LobbyHostPolicy.NoHost;
    public List<LobbyPlayerEntry> Players = new List<LobbyPlayerEntry>();
}

/// <summary>
/// Byte-level encoding for lobby messages sent over Fusion reliable data. Pure C# (no UnityEngine,
/// no Fusion) so it is unit-testable. Every decoder is length-checked and returns false on
/// malformed input rather than throwing — a bad packet must never take down the lobby.
/// Snapshot wire format (little-endian ints):
///   [canStart:1][maxPlayers:1][hostId:int32][playerCount:1]
///   then per player: [id:int32][team:1][nameLen:1][name: nameLen UTF-8 bytes]
/// </summary>
public static class LobbyProtocol
{
    public const int MaxNicknameChars = 16;
    // 16 chars at up to 4 UTF-8 bytes each; nameLen is a single byte on the wire.
    public const int MaxNicknameBytes = 64;

    /// <summary>Trim, strip control chars, cap at MaxNicknameChars. Null/whitespace -> "".</summary>
    public static string SanitizeNickname(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder(MaxNicknameChars);
        foreach (char c in raw.Trim())
        {
            if (char.IsControl(c)) continue;
            sb.Append(c);
            if (sb.Length == MaxNicknameChars) break;
        }
        return sb.ToString();
    }

    /// <summary>Roster name shown until the player's nickname message arrives.</summary>
    public static string PlaceholderName(int playerId) => "Player " + playerId;

    public static byte[] EncodeNickname(string sanitized) =>
        Encoding.UTF8.GetBytes(sanitized ?? "");

    public static bool TryDecodeNickname(byte[] buffer, int offset, int count, out string name)
    {
        name = "";
        if (buffer == null || count <= 0 || count > MaxNicknameBytes) return false;
        if (offset < 0 || offset + count > buffer.Length) return false;
        string decoded;
        try { decoded = Encoding.UTF8.GetString(buffer, offset, count); }
        catch (ArgumentException) { return false; }
        name = SanitizeNickname(decoded);
        return name.Length > 0;
    }

    public static byte[] EncodeLobbyState(LobbyStateSnapshot s)
    {
        var bytes = new List<byte>(8 + s.Players.Count * 24);
        bytes.Add((byte)(s.CanStart ? 1 : 0));
        bytes.Add((byte)s.MaxPlayers);
        WriteInt(bytes, s.HostId);
        bytes.Add((byte)s.Players.Count);
        foreach (var p in s.Players)
        {
            WriteInt(bytes, p.Id);
            bytes.Add((byte)p.Team);
            byte[] name = Encoding.UTF8.GetBytes(SanitizeNickname(p.Name));
            bytes.Add((byte)name.Length);
            bytes.AddRange(name);
        }
        return bytes.ToArray();
    }

    public static bool TryDecodeLobbyState(byte[] buffer, int offset, int count, out LobbyStateSnapshot s)
    {
        s = null;
        // header = canStart(1) + maxPlayers(1) + hostId(4) + playerCount(1) = 7 bytes minimum
        if (buffer == null || offset < 0 || count < 7 || offset + count > buffer.Length) return false;

        int pos = offset;
        int end = offset + count;
        var result = new LobbyStateSnapshot();
        result.CanStart = buffer[pos++] == 1;
        result.MaxPlayers = buffer[pos++];
        if (!TryReadInt(buffer, ref pos, end, out int hostId)) return false;
        result.HostId = hostId;
        int playerCount = buffer[pos++];

        for (int i = 0; i < playerCount; i++)
        {
            if (!TryReadInt(buffer, ref pos, end, out int id)) return false;
            if (pos + 2 > end) return false;
            int team = buffer[pos++];
            int nameLen = buffer[pos++];
            if (team != 1 && team != 2) return false;
            if (nameLen > MaxNicknameBytes || pos + nameLen > end) return false;
            string name;
            try { name = Encoding.UTF8.GetString(buffer, pos, nameLen); }
            catch (ArgumentException) { return false; }
            pos += nameLen;
            result.Players.Add(new LobbyPlayerEntry(id, name, team));
        }

        if (pos != end) return false; // trailing bytes = malformed
        s = result;
        return true;
    }

    private static void WriteInt(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private static bool TryReadInt(byte[] buffer, ref int pos, int end, out int value)
    {
        value = 0;
        if (pos + 4 > end) return false;
        value = buffer[pos] | (buffer[pos + 1] << 8) | (buffer[pos + 2] << 16) | (buffer[pos + 3] << 24);
        pos += 4;
        return true;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Same command as Step 3. Expected: `16 passed, 0 failed` (7 policy + 9 protocol), exit 0.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Scripts/Net/LobbyProtocol.cs Assets/Scripts/Net/LobbyProtocol.cs.meta Assets/Tests/EditMode/Net/LobbyProtocolTests.cs Assets/Tests/EditMode/Net/LobbyProtocolTests.cs.meta
git commit -m "feat(lobby): LobbyProtocol wire format (roster snapshot + nickname), length-checked decoders"
```

---

### Task 3: LobbyServerState — roster, auto-assign, host designation

**Files:**
- Create: `Assets/Scripts/Net/LobbyServerState.cs` (+ `.meta`, same MonoImporter template/GUID procedure as Task 2 Step 1)
- Create: `Assets/Tests/EditMode/Net/LobbyServerStateTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: `LobbyHostPolicy` (Task 1), `LobbyProtocol.PlaceholderName/SanitizeNickname`, `LobbyStateSnapshot`, `LobbyPlayerEntry` (Task 2).
- Produces (Task 4 relies on these exact signatures):
  - `int PlayerCount { get; }`
  - `int PlayerJoined(int id)` — adds with placeholder name on the smaller team (tie → 1), returns assigned team; re-join returns existing team
  - `void PlayerLeft(int id)`
  - `bool SetNickname(int id, string raw)` — true if the stored name changed
  - `bool SwitchTeam(int id, int team)` — true if valid and actually changed
  - `int CurrentHostId()`
  - `LobbyStateSnapshot BuildSnapshot(int maxPlayers)` — players ordered by ascending id

- [ ] **Step 1: Write the failing tests**

`Assets/Tests/EditMode/Net/LobbyServerStateTests.cs`:

```csharp
using NUnit.Framework;

public class LobbyServerStateTests
{
    [Test]
    public void PlayerJoined_BalancesTeams_TieGoesToTeam1()
    {
        var s = new LobbyServerState();
        Assert.AreEqual(1, s.PlayerJoined(1)); // 0-0 tie -> team 1
        Assert.AreEqual(2, s.PlayerJoined(2)); // 1-0 -> team 2
        Assert.AreEqual(1, s.PlayerJoined(3)); // 1-1 tie -> team 1
        Assert.AreEqual(2, s.PlayerJoined(4));
    }

    [Test]
    public void PlayerJoined_Rejoin_KeepsExistingTeam()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1);            // team 1
        s.PlayerJoined(2);            // team 2
        Assert.AreEqual(1, s.PlayerJoined(1));
        Assert.AreEqual(2, s.PlayerCount);
    }

    [Test]
    public void PlayerLeft_RemovesAndRebalancesFutureJoins()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1);            // team 1
        s.PlayerJoined(2);            // team 2
        s.PlayerLeft(1);
        Assert.AreEqual(1, s.PlayerCount);
        Assert.AreEqual(1, s.PlayerJoined(3)); // team 1 is now smaller
    }

    [Test]
    public void SwitchTeam_Validates()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1); // team 1
        Assert.IsTrue(s.SwitchTeam(1, 2));
        Assert.IsFalse(s.SwitchTeam(1, 2)); // already on team 2
        Assert.IsFalse(s.SwitchTeam(1, 3)); // invalid team
        Assert.IsFalse(s.SwitchTeam(99, 1)); // unknown player
    }

    [Test]
    public void SetNickname_SanitizesAndKeepsPlaceholderOnEmpty()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(7);
        Assert.IsFalse(s.SetNickname(7, "   "));   // empty after sanitize -> keep placeholder
        Assert.IsTrue(s.SetNickname(7, "  Ann  "));
        Assert.IsFalse(s.SetNickname(7, "Ann"));   // unchanged
        Assert.IsFalse(s.SetNickname(99, "Bob"));  // unknown player
        var snap = s.BuildSnapshot(20);
        Assert.AreEqual("Ann", snap.Players[0].Name);
    }

    [Test]
    public void CurrentHostId_LowestId_ReresolvesOnLeave()
    {
        var s = new LobbyServerState();
        Assert.AreEqual(LobbyHostPolicy.NoHost, s.CurrentHostId());
        s.PlayerJoined(4);
        s.PlayerJoined(2);
        s.PlayerJoined(7);
        Assert.AreEqual(2, s.CurrentHostId());
        s.PlayerLeft(2);
        Assert.AreEqual(4, s.CurrentHostId());
    }

    [Test]
    public void BuildSnapshot_ContentsAndGate()
    {
        var s = new LobbyServerState();
        var empty = s.BuildSnapshot(20);
        Assert.IsFalse(empty.CanStart);
        Assert.AreEqual(20, empty.MaxPlayers);

        s.PlayerJoined(5);
        s.PlayerJoined(3);
        var snap = s.BuildSnapshot(20);
        Assert.IsTrue(snap.CanStart);
        Assert.AreEqual(3, snap.HostId);
        Assert.AreEqual(2, snap.Players.Count);
        Assert.AreEqual(3, snap.Players[0].Id); // ascending id order
        Assert.AreEqual(5, snap.Players[1].Id);
        Assert.AreEqual("Player 5", snap.Players[1].Name);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```powershell
powershell -File "$SCRATCH\run-logic-tests.ps1" -Sources @(
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Net\LobbyHostPolicy.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Scripts\Net\LobbyProtocol.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Tests\EditMode\Net\LobbyHostPolicyTests.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Tests\EditMode\Net\LobbyProtocolTests.cs",
  "C:\Users\1\Documents\GitHub\2dGame\Assets\Tests\EditMode\Net\LobbyServerStateTests.cs")
```

Expected: **COMPILE FAILED** — `LobbyServerState` type not found (CS0246).

- [ ] **Step 3: Implement**

`Assets/Scripts/Net/LobbyServerState.cs`:

```csharp
using System.Collections.Generic;

/// <summary>
/// Server-side lobby roster + rules. Pure C# — player ids are plain ints (mapping from
/// Fusion's PlayerRef.PlayerId is GameNetworkManager's job) so this is unit-testable.
/// Owns: balanced team auto-assign on join (smaller team wins, tie -> team 1), optional
/// team switching, nickname storage, host designation (LobbyHostPolicy: lowest id) and
/// the start gate (>= 1 player).
/// </summary>
public class LobbyServerState
{
    private class Entry
    {
        public string Name;
        public int Team;
    }

    // Sorted so BuildSnapshot emits a stable ascending-id roster.
    private readonly SortedDictionary<int, Entry> players = new SortedDictionary<int, Entry>();

    public int PlayerCount => players.Count;

    public bool HasPlayer(int id) => players.ContainsKey(id);

    /// <summary>
    /// Adds the player with a placeholder name on the smaller team (tie -> team 1) and returns
    /// the assigned team. A re-join returns the existing team without changing anything.
    /// </summary>
    public int PlayerJoined(int id)
    {
        if (players.TryGetValue(id, out var existing)) return existing.Team;
        int team = CountTeam(1) <= CountTeam(2) ? 1 : 2;
        players[id] = new Entry { Name = LobbyProtocol.PlaceholderName(id), Team = team };
        return team;
    }

    public void PlayerLeft(int id) => players.Remove(id);

    /// <summary>Sanitizes and stores; an empty sanitize result keeps the current name. True if changed.</summary>
    public bool SetNickname(int id, string raw)
    {
        if (!players.TryGetValue(id, out var e)) return false;
        string clean = LobbyProtocol.SanitizeNickname(raw);
        if (clean.Length == 0 || clean == e.Name) return false;
        e.Name = clean;
        return true;
    }

    /// <summary>True only if the player exists, team is 1|2, and it differs from their current team.</summary>
    public bool SwitchTeam(int id, int team)
    {
        if (team != 1 && team != 2) return false;
        if (!players.TryGetValue(id, out var e) || e.Team == team) return false;
        e.Team = team;
        return true;
    }

    public int TeamOf(int id) => players.TryGetValue(id, out var e) ? e.Team : 0;

    public int CurrentHostId()
    {
        var ids = new List<int>(players.Keys);
        return LobbyHostPolicy.DesignateHostId(ids);
    }

    public LobbyStateSnapshot BuildSnapshot(int maxPlayers)
    {
        var s = new LobbyStateSnapshot
        {
            CanStart = LobbyHostPolicy.CanStart(players.Count),
            MaxPlayers = maxPlayers,
            HostId = CurrentHostId(),
        };
        foreach (var kv in players)
            s.Players.Add(new LobbyPlayerEntry(kv.Key, kv.Value.Name, kv.Value.Team));
        return s;
    }

    private int CountTeam(int team)
    {
        int n = 0;
        foreach (var kv in players)
            if (kv.Value.Team == team) n++;
        return n;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Same command as Step 2. Expected: `23 passed, 0 failed`, exit 0.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scripts/Net/LobbyServerState.cs Assets/Scripts/Net/LobbyServerState.cs.meta Assets/Tests/EditMode/Net/LobbyServerStateTests.cs Assets/Tests/EditMode/Net/LobbyServerStateTests.cs.meta
git commit -m "feat(lobby): LobbyServerState roster with balanced auto-assign + host designation"
```

---

### Task 4: UI components + GameNetworkManager rewire

One task because the three files reference each other and only compile together (single Assembly-CSharp). Deliverable: the whole client/server lobby flow compiles against the new protocol.

**Files:**
- Create: `Assets/Scripts/UI/MainMenuUI.cs` (+ `.meta`; also create the `Assets/Scripts/UI/` folder + folder `.meta`)
- Create: `Assets/Scripts/UI/LobbyScreenUI.cs` (+ `.meta`)
- Modify: `Assets/Scripts/GameNetworkManager.cs` (full rewrite below)
- Delete: `Assets/Scripts/Player/Teamselectionui.cs` (+ `.meta`)
- Create: `$SCRATCH\compile-gate.ps1`

Folder `.meta` template (fresh GUID):

```yaml
fileFormatVersion: 2
guid: <32-hex-guid>
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

**Interfaces:**
- Consumes: everything Tasks 1–3 produce.
- Produces (Task 5's scene wiring targets these exact members):
  - `MainMenuUI`: serialized fields `menuPanel (GameObject)`, `nicknameInput (TMP_InputField)`, `joinButton (Button)`, `hostButton (Button)`, `statusText (TMP_Text)`, `networkManager (GameNetworkManager)`; public `string Nickname { get; }`, `void Show()`, `void Hide()`, `void ShowStatus(string)`, `void SetBusy(bool)`
  - `LobbyScreenUI`: serialized fields `lobbyPanel (GameObject)`, `playersHeader (TMP_Text)`, `statusText (TMP_Text)`, `team1ListParent (Transform)`, `team2ListParent (Transform)`, `nameRowPrefab (GameObject)`, `switchToTeam1Button (Button)`, `switchToTeam2Button (Button)`, `startButton (Button)`, `loadoutToggleButton (Button)`, `loadoutPanel (GameObject)`, `buffConfig (BuffLoadoutConfig)`, `slotLabels (TMP_Text[])`, `slotUpButtons (Button[])`, `slotDownButtons (Button[])`, `networkManager (GameNetworkManager)`; public `void Show()`, `void Hide()`, `void ApplyLobbyState(LobbyStateSnapshot, int localPlayerId)`
  - `GameNetworkManager`: public `void StartHost()`, `void StartClient()`, `void RequestTeamSwitch(int team)`, `void RequestStartMatch()`, `void SubmitLocalLoadoutChoice(byte[] order)` (unchanged); serialized fields `menuUI (MainMenuUI)`, `lobbyUI (LobbyScreenUI)`

- [ ] **Step 1: Write `MainMenuUI`**

`Assets/Scripts/UI/MainMenuUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// MainMenu entry screen: nickname + Join/Host + a status line for connect progress and errors.
/// Purely presentational — GameNetworkManager owns the runner and calls back into Show/ShowStatus
/// on failure or shutdown. The nickname persists in PlayerPrefs across sessions.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button hostButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Wiring")]
    [SerializeField] private GameNetworkManager networkManager;

    private const string NicknamePref = "lobby.nickname";

    /// <summary>Sanitized nickname from the input field ("" when empty — server keeps the placeholder).</summary>
    public string Nickname =>
        LobbyProtocol.SanitizeNickname(nicknameInput != null ? nicknameInput.text : "");

    private void Start()
    {
        if (nicknameInput != null)
        {
            nicknameInput.characterLimit = LobbyProtocol.MaxNicknameChars;
            nicknameInput.text = PlayerPrefs.GetString(NicknamePref, "");
        }

        if (joinButton != null) joinButton.onClick.AddListener(() => Connect(asHost: false));
        else Debug.LogError("❌ MainMenuUI: Join button not assigned!");

        if (hostButton != null) hostButton.onClick.AddListener(() => Connect(asHost: true));
        else Debug.LogError("❌ MainMenuUI: Host button not assigned!");

        ShowStatus("");
    }

    private void Connect(bool asHost)
    {
        if (networkManager == null)
        {
            Debug.LogError("❌ MainMenuUI: networkManager not assigned!");
            return;
        }

        PlayerPrefs.SetString(NicknamePref, Nickname);
        PlayerPrefs.Save();
        SetBusy(true);
        ShowStatus(asHost ? "Starting host..." : "Connecting...");
        if (asHost) networkManager.StartHost();
        else networkManager.StartClient();
    }

    public void Show()
    {
        if (menuPanel != null) menuPanel.SetActive(true);
        SetBusy(false);
    }

    public void Hide()
    {
        if (menuPanel != null) menuPanel.SetActive(false);
    }

    public void ShowStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    public void SetBusy(bool busy)
    {
        if (joinButton != null) joinButton.interactable = !busy;
        if (hostButton != null) hostButton.interactable = !busy;
    }
}
```

- [ ] **Step 2: Write `LobbyScreenUI`**

`Assets/Scripts/UI/LobbyScreenUI.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Lobby screen: "Players: X/20" header, two team columns of name rows, team-switch buttons, a
/// collapsible loadout picker, and a Start button shown only to the designated host. Renders
/// purely from the server's LobbyStateSnapshot (ApplyLobbyState) — it holds no authoritative
/// state, so host-mode and dedicated-server mode share one rendering path.
/// </summary>
public class LobbyScreenUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private TMP_Text playersHeader;
    [SerializeField] private TMP_Text statusText;

    [Header("Team Columns")]
    [SerializeField] private Transform team1ListParent;
    [SerializeField] private Transform team2ListParent;
    [Tooltip("Prefab with a TMP_Text on it (or a child). One instance per roster row, pooled.")]
    [SerializeField] private GameObject nameRowPrefab;
    [SerializeField] private Button switchToTeam1Button;
    [SerializeField] private Button switchToTeam2Button;

    [Header("Start (designated host only)")]
    [SerializeField] private Button startButton;

    [Header("Loadout (collapsible)")]
    [SerializeField] private Button loadoutToggleButton;
    [SerializeField] private GameObject loadoutPanel;
    [Tooltip("The buff loadout config (same asset used by the player prefab).")]
    [SerializeField] private BuffLoadoutConfig buffConfig;
    [Tooltip("One row per loadout slot, top = highest priority.")]
    [SerializeField] private TMP_Text[] slotLabels;
    [SerializeField] private Button[] slotUpButtons;
    [SerializeField] private Button[] slotDownButtons;

    [Header("Wiring")]
    [SerializeField] private GameNetworkManager networkManager;

    private readonly List<GameObject> rowPool = new List<GameObject>();
    private List<Game.Buffs.Core.BuffId> loadoutOrder;

    private void Start()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (loadoutPanel != null) loadoutPanel.SetActive(false);

        if (switchToTeam1Button != null)
            switchToTeam1Button.onClick.AddListener(() => networkManager.RequestTeamSwitch(1));
        if (switchToTeam2Button != null)
            switchToTeam2Button.onClick.AddListener(() => networkManager.RequestTeamSwitch(2));

        if (startButton != null)
        {
            startButton.onClick.AddListener(() => networkManager.RequestStartMatch());
            startButton.gameObject.SetActive(false);
        }

        if (loadoutToggleButton != null)
            loadoutToggleButton.onClick.AddListener(ToggleLoadoutPanel);

        InitLoadoutOrder();
        WireLoadoutButtons();
        RefreshLoadoutLabels();
    }

    public void Show()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        SetStatus("Waiting for lobby state...");
    }

    public void Hide()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    /// <summary>
    /// Renders the server's snapshot: header count, roster rows per team column, switch-button
    /// enabling, and the Start button (visible only when the local player is the designated host).
    /// </summary>
    public void ApplyLobbyState(LobbyStateSnapshot s, int localPlayerId)
    {
        if (s == null) return;

        if (playersHeader != null)
            playersHeader.text = $"Players: {s.Players.Count}/{s.MaxPlayers}";

        int rowIndex = 0;
        int localTeam = 0;
        foreach (var p in s.Players)
        {
            if (p.Id == localPlayerId) localTeam = p.Team;

            var row = GetRow(rowIndex++);
            row.transform.SetParent(p.Team == 1 ? team1ListParent : team2ListParent, false);
            var label = row.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                string host = p.Id == s.HostId ? "★ " : "";
                string you = p.Id == localPlayerId ? " (you)" : "";
                label.text = $"{host}{p.Name}{you}";
            }
            row.SetActive(true);
        }
        for (int i = rowIndex; i < rowPool.Count; i++)
            rowPool[i].SetActive(false);

        if (switchToTeam1Button != null) switchToTeam1Button.interactable = localTeam == 2;
        if (switchToTeam2Button != null) switchToTeam2Button.interactable = localTeam == 1;

        bool isHost = s.HostId != LobbyHostPolicy.NoHost && localPlayerId == s.HostId;
        if (startButton != null)
        {
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = isHost && s.CanStart;
        }

        SetStatus(isHost
            ? "You are the lobby host — press Start when ready."
            : "Waiting for the host to start the match...");
    }

    private GameObject GetRow(int index)
    {
        while (rowPool.Count <= index)
        {
            var row = Instantiate(nameRowPrefab);
            row.SetActive(false);
            rowPool.Add(row);
        }
        return rowPool[index];
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }

    // ============================
    // Loadout picker (moved from the old TeamSelectionUI, labels now TMP)
    // ============================

    private void ToggleLoadoutPanel()
    {
        if (loadoutPanel != null) loadoutPanel.SetActive(!loadoutPanel.activeSelf);
    }

    private void InitLoadoutOrder()
    {
        loadoutOrder = new List<Game.Buffs.Core.BuffId>();
        if (buffConfig != null && buffConfig.DefaultOrder != null)
            foreach (var id in buffConfig.DefaultOrder) loadoutOrder.Add(id);
    }

    private void WireLoadoutButtons()
    {
        if (slotUpButtons != null)
            for (int i = 0; i < slotUpButtons.Length; i++)
            {
                int idx = i;
                if (slotUpButtons[i] != null) slotUpButtons[i].onClick.AddListener(() => MoveSlot(idx, -1));
            }
        if (slotDownButtons != null)
            for (int i = 0; i < slotDownButtons.Length; i++)
            {
                int idx = i;
                if (slotDownButtons[i] != null) slotDownButtons[i].onClick.AddListener(() => MoveSlot(idx, +1));
            }
    }

    private void MoveSlot(int index, int delta)
    {
        if (loadoutOrder == null) return;
        int target = index + delta;
        if (index < 0 || index >= loadoutOrder.Count || target < 0 || target >= loadoutOrder.Count) return;
        (loadoutOrder[index], loadoutOrder[target]) = (loadoutOrder[target], loadoutOrder[index]);
        RefreshLoadoutLabels();

        // Every reorder is submitted immediately (tiny payload) — no separate confirm step.
        if (networkManager != null)
            networkManager.SubmitLocalLoadoutChoice(LoadoutAsBytes());
    }

    private void RefreshLoadoutLabels()
    {
        if (slotLabels == null || loadoutOrder == null || buffConfig == null) return;
        for (int i = 0; i < slotLabels.Length; i++)
        {
            if (slotLabels[i] == null) continue;
            if (i < loadoutOrder.Count)
            {
                var def = buffConfig.GetById(loadoutOrder[i]);
                slotLabels[i].text = $"{i + 1}. {(def != null ? def.DisplayName : loadoutOrder[i].ToString())}";
            }
            else slotLabels[i].text = "";
        }
    }

    private byte[] LoadoutAsBytes()
    {
        if (loadoutOrder == null) return null;
        var bytes = new byte[loadoutOrder.Count];
        for (int i = 0; i < loadoutOrder.Count; i++) bytes[i] = (byte)loadoutOrder[i];
        return bytes;
    }
}
```

- [ ] **Step 3: Rewrite `GameNetworkManager`**

Replace `Assets/Scripts/GameNetworkManager.cs` entirely (the `LobbyTeamChoices`/`LobbyLoadoutChoices` statics at the bottom are unchanged from the current file):

```csharp
using UnityEngine;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using System;
using System.Collections.Generic;

/// <summary>
/// Boot + transport + server-side lobby glue. Players land in a single 20-player lobby
/// (session "PvPvERoom"); the server auto-assigns each joiner to the smaller team, tracks
/// nicknames and team switches in LobbyServerState, and broadcasts a full LobbyState snapshot
/// (LobbyProtocol) to every player after each lobby change. The designated host-client
/// (lowest PlayerId — the first joiner) gets the Start button and may start whenever at least
/// one player is connected. In host mode the host's own UI is fed the same snapshot through a
/// local loopback, so both modes share one rendering path (LobbyScreenUI.ApplyLobbyState).
/// Team/loadout results land in LobbyTeamChoices/LobbyLoadoutChoices for NetworkedSpawnManager.
/// </summary>
public class GameNetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("UI References")]
    public MainMenuUI menuUI;
    public LobbyScreenUI lobbyUI;

    [Header("Network Settings")]
    public string sessionName = "PvPvERoom";
    public int gameplaySceneIndex = 1;

    [Tooltip("Session player cap. Fusion refuses connections beyond this count.")]
    public int maxPlayers = 20;

    // Reliable-data channel tags. TEAM is a team-SWITCH request (teams are auto-assigned on join).
    private static readonly ReliableKey TeamChoiceKey = ReliableKey.FromInts(0x54454100, 0x4D, 0, 0); // "TEAM"
    private static readonly ReliableKey LoadoutKey = ReliableKey.FromInts(0x4C4F4144, 0x55, 0, 0);    // "LOAD"
    private static readonly ReliableKey NameKey = ReliableKey.FromInts(0x4E414D45, 0, 0, 0);          // "NAME"
    private static readonly ReliableKey RosterKey = ReliableKey.FromInts(0x524F5354, 0, 0, 0);        // "ROST"
    private static readonly ReliableKey StartMatchKey = ReliableKey.FromInts(0x53545254, 0, 0, 0);    // "STRT"

    private NetworkRunner runner;
    private NetworkSceneManagerDefault sceneManager;
    private PooledNetworkObjectProvider objectProvider;
    private LobbyServerState serverLobby = new LobbyServerState();
    private bool gameStarting = false;

    void Start()
    {
        DontDestroyOnLoad(gameObject);
        runner = gameObject.AddComponent<NetworkRunner>();

        // Fusion steps Physics2D inside the network tick (required for NetworkRigidbody2D prediction).
        // ClientPhysicsSimulation defaults to Disabled, which means CLIENTS never call
        // Physics.Simulate() and so never integrate their own rigidbody forward — SimulateForward
        // enables client-side prediction of the local player's position.
        var simulatePhysics = gameObject.AddComponent<RunnerSimulatePhysics2D>();
        simulatePhysics.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateForward;

        // Pool high-churn networked prefabs (projectiles) instead of Instantiate/Destroy each shot.
        objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();

        // One scene manager for the lifetime of the runner. Created here (not per StartGame call)
        // so a failed connect + retry doesn't stack duplicate components on this GameObject.
        sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

        // Register the single input source.
        var inputProvider = gameObject.AddComponent<NetworkInputProvider>();
        runner.AddCallbacks(inputProvider);

        // Receive lobby callbacks (player join/leave, reliable lobby data).
        runner.AddCallbacks(this);

        LobbyTeamChoices.Clear();
        LobbyLoadoutChoices.Clear();
        serverLobby = new LobbyServerState();
        gameStarting = false;

        var boot = NetworkBootMode.Resolve(
            Application.isBatchMode,
            System.Environment.GetCommandLineArgs());

        if (boot == NetworkBootKind.DedicatedServer)
        {
            StartServer();
            return; // headless server: no menu UI
        }

        if (menuUI == null) Debug.LogError("❌ MainMenuUI not assigned!");
        if (lobbyUI == null) Debug.LogError("❌ LobbyScreenUI not assigned!");
    }

    // ============================
    // Connection entry points (menuUI buttons call these)
    // ============================

    public async void StartHost()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Host, // AutoHostOrClient creates separate sessions — never use it here
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            EnterLobbyUI();
        }
        else
        {
            Debug.LogError($"❌ Failed to start host: {result.ShutdownReason}");
            if (menuUI != null)
            {
                menuUI.ShowStatus($"Failed to start host: {result.ShutdownReason}");
                menuUI.SetBusy(false);
            }
        }
    }

    public async void StartClient()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            EnterLobbyUI();
        }
        else
        {
            Debug.LogError($"❌ Failed to connect: {result.ShutdownReason}");
            if (menuUI != null)
            {
                menuUI.ShowStatus($"Failed to connect: {result.ShutdownReason}");
                menuUI.SetBusy(false);
            }
        }
    }

    async void StartServer()
    {
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider
        };

        var result = await runner.StartGame(args);

        if (result.Ok)
            Debug.Log("✅ Dedicated server started — waiting for players.");
        else
            Debug.LogError($"❌ Server failed to start: {result.ShutdownReason}");
    }

    private void EnterLobbyUI()
    {
        if (menuUI != null) menuUI.Hide();
        if (lobbyUI != null) lobbyUI.Show();
        SendLocalNickname();
    }

    /// <summary>
    /// Pushes the local player's nickname to the lobby. Host mode records directly and
    /// re-broadcasts; clients send over reliable data. An empty nickname keeps the server's
    /// "Player N" placeholder (nothing is sent — the decoder rejects empty payloads anyway).
    /// </summary>
    private void SendLocalNickname()
    {
        if (runner == null || !runner.IsRunning) return;
        string nick = menuUI != null ? menuUI.Nickname : "";

        if (runner.IsServer)
        {
            if (runner.LocalPlayer != PlayerRef.None)
            {
                serverLobby.SetNickname(runner.LocalPlayer.PlayerId, nick);
                BroadcastLobby(); // refresh even if unchanged so the just-shown UI gets a snapshot
            }
        }
        else if (nick.Length > 0)
        {
            runner.SendReliableDataToServer(NameKey, LobbyProtocol.EncodeNickname(nick));
        }
    }

    // ============================
    // Lobby actions (lobbyUI buttons call these)
    // ============================

    /// <summary>Ask to move to the given team (1|2). Server-side it must actually change something.</summary>
    public void RequestTeamSwitch(int teamNumber)
    {
        if (teamNumber != 1 && teamNumber != 2) return;
        if (runner == null || !runner.IsRunning || gameStarting) return;

        if (runner.IsServer)
        {
            if (runner.LocalPlayer == PlayerRef.None) return; // dedicated server is not a player
            if (serverLobby.SwitchTeam(runner.LocalPlayer.PlayerId, teamNumber))
            {
                LobbyTeamChoices.Set(runner.LocalPlayer, teamNumber);
                BroadcastLobby();
            }
        }
        else
        {
            runner.SendReliableDataToServer(TeamChoiceKey, new byte[] { (byte)teamNumber });
        }
    }

    /// <summary>
    /// Called by LobbyScreenUI when the local player reorders their buffs. Records on the host
    /// (directly if we are the host, else over reliable-data).
    /// </summary>
    public void SubmitLocalLoadoutChoice(byte[] order)
    {
        if (runner == null || !runner.IsRunning) return;

        // A zero-length reliable payload trips a Fusion assert on the real socket path
        // (only reproduces on remote clients). Treat null/empty as "no custom loadout".
        if (order == null || order.Length == 0) return;

        if (runner.IsServer)
            LobbyLoadoutChoices.Set(runner.LocalPlayer, order);
        else
            runner.SendReliableDataToServer(LoadoutKey, order);
    }

    /// <summary>
    /// Called when the local player clicks Start. Host mode starts directly; on a dedicated
    /// server the designated host-client asks the server, which re-validates.
    /// </summary>
    public void RequestStartMatch()
    {
        if (runner == null || !runner.IsRunning || gameStarting) return;

        if (runner.IsServer)
        {
            if (!LobbyHostPolicy.CanStart(serverLobby.PlayerCount)) return;
            gameStarting = true;
            LoadGameplayScene();
        }
        else
        {
            runner.SendReliableDataToServer(StartMatchKey, new byte[] { 1 });
        }
    }

    // ============================
    // Server-side lobby state + broadcast
    // ============================

    /// <summary>
    /// Server-only: add the player to the lobby roster with a balanced auto-assigned team and
    /// mirror it into LobbyTeamChoices (read by NetworkedSpawnManager). Runs for mid-match
    /// late joiners too, so they spawn on a balanced team without a lobby round-trip.
    /// </summary>
    private void ServerHandleJoin(PlayerRef player)
    {
        int team = serverLobby.PlayerJoined(player.PlayerId);
        LobbyTeamChoices.Set(player, team);
        if (!gameStarting) BroadcastLobby();
    }

    /// <summary>
    /// Server-only: encode one snapshot and send it to every remote player; a host-as-player
    /// applies it to its own LobbyScreenUI directly (same snapshot, no wire trip).
    /// </summary>
    private void BroadcastLobby()
    {
        if (runner == null || !runner.IsServer) return;

        var snap = serverLobby.BuildSnapshot(maxPlayers);
        byte[] payload = LobbyProtocol.EncodeLobbyState(snap);

        foreach (var p in runner.ActivePlayers)
        {
            if (p == runner.LocalPlayer) continue; // local loopback below instead
            runner.SendReliableDataToPlayer(p, RosterKey, payload);
        }

        if (runner.LocalPlayer != PlayerRef.None && lobbyUI != null)
            lobbyUI.ApplyLobbyState(snap, runner.LocalPlayer.PlayerId);
    }

    private async void LoadGameplayScene()
    {
        if (lobbyUI != null) lobbyUI.Hide();
        await runner.LoadScene(SceneRef.FromIndex(gameplaySceneIndex));
    }

    void OnDestroy()
    {
        if (runner != null) runner.Shutdown();
    }

    void OnApplicationQuit()
    {
        if (runner != null) runner.Shutdown();
    }

    // ============================
    // Fusion callbacks
    // ============================

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // DO NOT SPAWN PLAYER HERE — NetworkedSpawnManager in the Gameplay scene handles it.
        if (runner.IsServer)
            ServerHandleJoin(player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            serverLobby.PlayerLeft(player.PlayerId);
            LobbyTeamChoices.Remove(player);
            LobbyLoadoutChoices.Remove(player);
            if (!gameStarting) BroadcastLobby();
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        // Pooled instances belong to the session that just died — drop them so a
        // restarted session doesn't reuse destroyed objects.
        if (objectProvider != null)
            objectProvider.ClearPools();

        // The live-coin registry is server-only static state; clear it so a restarted session
        // doesn't inherit stale (destroyed) coin references or a bogus live count.
        CoinRegistry.Clear();

        if (lobbyUI != null) lobbyUI.Hide();
        if (menuUI != null)
        {
            menuUI.Show();
            menuUI.ShowStatus($"Disconnected: {shutdownReason}");
        }

        LobbyTeamChoices.Clear();
        LobbyLoadoutChoices.Clear();
        serverLobby = new LobbyServerState();
        gameStarting = false;
    }

    public void OnConnectedToServer(NetworkRunner runner) { }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // The ONLY place that accepts/refuses connections (future ban list / lockout goes here).
        // The player cap itself is enforced by Fusion via StartGameArgs.PlayerCount.
        request.Accept();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"❌ Connection failed: {reason}");
        if (menuUI != null)
        {
            menuUI.ShowStatus($"Connection failed: {reason}");
            menuUI.SetBusy(false);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        if (runner.IsServer)
        {
            if (key == TeamChoiceKey)
            {
                if (gameStarting || data.Count < 1 || data.Array == null) return;
                int team = data.Array[data.Offset];
                if (serverLobby.SwitchTeam(player.PlayerId, team))
                {
                    LobbyTeamChoices.Set(player, team);
                    BroadcastLobby();
                }
                return;
            }

            if (key == NameKey)
            {
                if (data.Array == null) return;
                if (!LobbyProtocol.TryDecodeNickname(data.Array, data.Offset, data.Count, out string name)) return;
                if (serverLobby.SetNickname(player.PlayerId, name) && !gameStarting)
                    BroadcastLobby();
                return;
            }

            if (key == LoadoutKey)
            {
                if (data.Count < 1 || data.Array == null) return;
                var order = new byte[data.Count];
                Array.Copy(data.Array, data.Offset, order, 0, data.Count);
                LobbyLoadoutChoices.Set(player, order);
                return;
            }

            if (key == StartMatchKey)
            {
                // Only the designated host-client may start, and only when the gate allows it.
                if (!gameStarting
                    && player.PlayerId == serverLobby.CurrentHostId()
                    && LobbyHostPolicy.CanStart(serverLobby.PlayerCount))
                {
                    gameStarting = true;
                    LoadGameplayScene();
                }
                return;
            }

            return;
        }

        // ---- Client ----
        if (key == RosterKey && data.Array != null)
        {
            if (LobbyProtocol.TryDecodeLobbyState(data.Array, data.Offset, data.Count, out var snap)
                && lobbyUI != null)
            {
                lobbyUI.ApplyLobbyState(snap, runner.LocalPlayer.PlayerId);
            }
        }
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // A real dedicated server (no local player) should not render or play audio. -nographics
        // already suppresses rendering; disabling cameras/listeners avoids per-frame work and
        // AudioListener warnings on the headless build.
        if (runner.IsServer && runner.LocalPlayer == PlayerRef.None)
        {
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                cam.enabled = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                listener.enabled = false;
        }
    }

    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
}

/// <summary>
/// Per-player team assignments collected during the lobby (auto-assigned on join, updated on
/// switch), keyed by PlayerRef. Lives on the host/server only and survives the menu -> gameplay
/// scene load. NetworkedSpawnManager reads this to spawn each player on the right team.
/// </summary>
public static class LobbyTeamChoices
{
    private static readonly Dictionary<PlayerRef, int> choices = new Dictionary<PlayerRef, int>();

    public static void Set(PlayerRef player, int team) => choices[player] = team;
    public static bool Has(PlayerRef player) => choices.ContainsKey(player);
    public static bool TryGet(PlayerRef player, out int team) => choices.TryGetValue(player, out team);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
    public static int Count => choices.Count;
}

/// <summary>
/// Per-player buff loadout (priority order as BuffId bytes) collected during the lobby, parallel
/// to LobbyTeamChoices. Read by NetworkedSpawnManager on the host to initialise each player's
/// PlayerBuffs. A missing entry falls back to the BuffLoadoutConfig default order.
/// </summary>
public static class LobbyLoadoutChoices
{
    private static readonly Dictionary<PlayerRef, byte[]> choices = new Dictionary<PlayerRef, byte[]>();

    public static void Set(PlayerRef player, byte[] order) => choices[player] = order;
    public static bool TryGet(PlayerRef player, out byte[] order) => choices.TryGetValue(player, out order);
    public static void Remove(PlayerRef player) => choices.Remove(player);
    public static void Clear() => choices.Clear();
}
```

- [ ] **Step 4: Delete the old TeamSelectionUI**

```powershell
git rm Assets/Scripts/Player/Teamselectionui.cs Assets/Scripts/Player/Teamselectionui.cs.meta
```

(The MainMenu scene still references its GUID until Task 5 rewires it — that produces a harmless "missing script" in the scene, fixed by Task 5. The important part is nothing in code references `TeamSelectionUI` anymore; verify with a grep.)

Run: `grep -rn "TeamSelectionUI" Assets/Scripts` — expected: no matches.

- [ ] **Step 5: Create and run the full compile gate**

`$SCRATCH\compile-gate.ps1`:

```powershell
$ED = "C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor"
$proj = "C:\Users\1\Documents\GitHub\2dGame"
$scratch = $PSScriptRoot

# 1. Fresh Game.Net (the ScriptAssemblies copy is stale while the editor holds the lock).
$netSources = Get-ChildItem "$proj\Assets\Scripts\Net" -Filter *.cs | Select-Object -ExpandProperty FullName
$lines = @("-nologo", "-target:library", "-out:`"$scratch\Game.Net.gate.dll`"")
$lines += "-r:`"$ED\Data\NetStandard\ref\2.1.0\netstandard.dll`""
foreach ($s in $netSources) { $lines += "`"$s`"" }
Set-Content "$scratch\gamenet.rsp" $lines -Encoding utf8
& "$ED\Data\NetCoreRuntime\dotnet.exe" exec "$ED\Data\DotNetSdkRoslyn\csc.dll" "@$scratch\gamenet.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "Game.Net COMPILE FAILED"; exit 1 }

# 2. Assembly-CSharp surface (asmdef-owned folders excluded; note trailing backslashes).
$excluded = @(
    "$proj\Assets\Scripts\Buffs\Core\",
    "$proj\Assets\Scripts\Enemy\AI\",
    "$proj\Assets\Scripts\Net\",
    "$proj\Assets\Scripts\Combat\Core\",
    "$proj\Assets\Scripts\Player\Animation\Core\")
$sources = Get-ChildItem "$proj\Assets\Scripts" -Recurse -Filter *.cs |
    Where-Object { $f = $_.FullName; -not ($excluded | Where-Object { $f.StartsWith($_) }) } |
    Select-Object -ExpandProperty FullName

$lines = @("-nologo", "-target:library", "-nowarn:0169,0649,1998", "-out:`"$scratch\compilegate.dll`"")
$lines += "-r:`"$ED\Data\NetStandard\ref\2.1.0\netstandard.dll`""
foreach ($d in Get-ChildItem "$ED\Data\Managed\UnityEngine" -Filter *.dll) { $lines += "-r:`"$($d.FullName)`"" }
$lines += "-r:`"$scratch\Game.Net.gate.dll`""
foreach ($n in "Fusion.Unity", "Fusion.Addons.Physics", "Game.Buffs.Core", "Game.EnemyAI",
              "Game.Combat.Core", "Game.PlayerAnimation.Core", "Unity.InputSystem",
              "Unity.TextMeshPro", "UnityEngine.UI") {
    $p = "$proj\Library\ScriptAssemblies\$n.dll"
    if (Test-Path $p) { $lines += "-r:`"$p`"" }
}
foreach ($d in Get-ChildItem "$proj\Assets\Photon\Fusion\Assemblies" -Filter *.dll -ErrorAction SilentlyContinue) {
    $lines += "-r:`"$($d.FullName)`""
}
foreach ($s in $sources) { $lines += "`"$s`"" }
Set-Content "$scratch\compilegate.rsp" $lines -Encoding utf8
& "$ED\Data\NetCoreRuntime\dotnet.exe" exec "$ED\Data\DotNetSdkRoslyn\csc.dll" "@$scratch\compilegate.rsp"
if ($LASTEXITCODE -ne 0) { Write-Host "Assembly-CSharp COMPILE FAILED"; exit 1 }
Write-Host "COMPILE GATE PASSED"
```

Run: `powershell -File "$SCRATCH\compile-gate.ps1"`
Expected: `COMPILE GATE PASSED`, exit 0. Fix any reported errors before committing (duplicate-reference or missing-ref noise: prefer adjusting the ref list over touching game code).

- [ ] **Step 6: Re-run logic tests (regression)**

Task 3 Step 2's command. Expected: `23 passed, 0 failed`.

- [ ] **Step 7: Commit**

```powershell
git add Assets/Scripts/UI Assets/Scripts/UI.meta Assets/Scripts/GameNetworkManager.cs
git commit -m "feat(lobby): snapshot-driven MainMenuUI + LobbyScreenUI, GameNetworkManager rewired to LobbyServerState (removes TeamSelectionUI)"
```

(The `git rm` from Step 4 is already staged and lands in this commit.)

---

### Task 5: MainMenu scene rebuild + name-row prefab

Scene wiring is YAML editing (established pattern in this repo — see the hit-landed feedback commits). No automated test; verification is structural (grep for wired GUIDs) plus the Task 7 in-editor pass.

**Files:**
- Create: `Assets/Prefabs/UI/LobbyNameRow.prefab` (+ `.meta`; create `Assets/Prefabs/UI/` folder + `.meta` if missing)
- Modify: `Assets/Scenes/MainMenu.unity`

**Interfaces:**
- Consumes: the exact serialized field names from Task 4 (`menuPanel`, `nicknameInput`, ..., `networkManager`; `lobbyPanel`, `playersHeader`, ..., `slotDownButtons`).
- Produces: a scene where `GameNetworkManager.menuUI/lobbyUI` and every `MainMenuUI`/`LobbyScreenUI` field is wired.

- [ ] **Step 1: Create `LobbyNameRow.prefab`**

Root GameObject `LobbyNameRow`: `RectTransform` (height 28, anchors stretch-horizontal), `CanvasRenderer`, `TextMeshProUGUI` (font size 20, alignment Left/Midline, raycastTarget off, text "Player"). TextMeshProUGUI script GUID is `f4688fdb7df04437aeb418b961361dc5` (fileID 11500000). Use existing TMP objects in `MainMenu.unity` as the serialization reference for a minimal `MonoBehaviour` block; Unity fills remaining defaults on import. Fresh GUID in the prefab `.meta` (DefaultImporter/PrefabImporter template):

```yaml
fileFormatVersion: 2
guid: <32-hex-guid>
PrefabImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

- [ ] **Step 2: Rework the scene hierarchy**

Read the `.meta` GUIDs first: `MainMenuUI.cs.meta`, `LobbyScreenUI.cs.meta` (created in Task 4); `GameNetworkManager.cs.meta` (existing — the scene's `NetworkManager` object already references it; keep that object and its component, only its `menuUI`/`lobbyUI` fields change).

Target hierarchy under the existing `MenuCanvas`:

```
MenuCanvas (existing Canvas + CanvasScaler + GraphicRaycaster — keep)
├─ MenuPanel                     ← repurpose the existing menuPanel if present, else new
│   ├─ TitleText                 (existing TMP title — keep/move here)
│   ├─ NicknameInput             (TMP_InputField: bg Image, Text Area/Placeholder "Enter nickname"/Text)
│   ├─ JoinButton                (Button + TMP label "Join")
│   ├─ HostButton                (Button + TMP label "Host")
│   └─ MenuStatusText            (TMP_Text, initially empty)
└─ LobbyPanel                    (full-screen, inactive is fine — LobbyScreenUI.Start hides it)
    ├─ PlayersHeader             (TMP_Text "Players: 0/20", top-center)
    ├─ Team1Column
    │   ├─ Team1Header           (TMP_Text "Team 1", blue #3366FF)
    │   ├─ SwitchToTeam1Button   (Button + TMP label "Join Team 1")
    │   └─ Team1List             (empty RectTransform + VerticalLayoutGroup, childControlHeight off)
    ├─ Team2Column
    │   ├─ Team2Header           (TMP_Text "Team 2", red #FF3333)
    │   ├─ SwitchToTeam2Button   (Button + TMP label "Join Team 2")
    │   └─ Team2List             (empty RectTransform + VerticalLayoutGroup, childControlHeight off)
    ├─ LoadoutToggleButton       (Button + TMP label "Loadout")
    ├─ LoadoutPanel              (inactive; 3 rows — BuffLoadoutConfig.DefaultOrder has 3 entries:
    │   │                         ExtraJump, Stealth, QuickerDash)
    │   ├─ Slot0: SlotLabel0 (TMP_Text) + Slot0Up (Button "▲") + Slot0Down (Button "▼")
    │   ├─ Slot1: (same shape)
    │   └─ Slot2: (same shape)
    ├─ StartButton               (Button + TMP label "Start Match", inactive)
    └─ LobbyStatusText           (TMP_Text, bottom-center)
```

Delete from the scene: the old `TeamSelectionPanel` subtree, old `Team1Button`/`Team2Button`/`Team1CountText`/`Team2CountText`, old `HostButton`/`JoinButton`/`startButton` (replaced by the new ones above), the `TeamSelectionManager` object (its `TeamSelectionUI` component is now a missing script), and any legacy `UnityEngine.UI.Text` objects in the menu flow.

Add a `UIManager` GameObject (root level) with both `MainMenuUI` and `LobbyScreenUI` components.

- [ ] **Step 3: Wire every serialized field**

| Component (on UIManager) | Field | Target |
|---|---|---|
| MainMenuUI | menuPanel | MenuPanel |
| MainMenuUI | nicknameInput | NicknameInput (TMP_InputField) |
| MainMenuUI | joinButton / hostButton | JoinButton / HostButton |
| MainMenuUI | statusText | MenuStatusText |
| MainMenuUI | networkManager | NetworkManager (GameNetworkManager) |
| LobbyScreenUI | lobbyPanel | LobbyPanel |
| LobbyScreenUI | playersHeader / statusText | PlayersHeader / LobbyStatusText |
| LobbyScreenUI | team1ListParent / team2ListParent | Team1List / Team2List (RectTransform) |
| LobbyScreenUI | nameRowPrefab | LobbyNameRow.prefab (by prefab GUID) |
| LobbyScreenUI | switchToTeam1Button / switchToTeam2Button | the two switch buttons |
| LobbyScreenUI | startButton | StartButton |
| LobbyScreenUI | loadoutToggleButton / loadoutPanel | LoadoutToggleButton / LoadoutPanel |
| LobbyScreenUI | buffConfig | same BuffLoadoutConfig asset GUID the old TeamSelectionUI referenced (read it from the scene before deleting) |
| LobbyScreenUI | slotLabels[0..2] / slotUpButtons[0..2] / slotDownButtons[0..2] | the three slot rows |
| GameNetworkManager (on NetworkManager) | menuUI / lobbyUI | UIManager's components |

Wiring rules: MonoBehaviour scene refs use `{fileID: <component fileID>}` of the *component*, not the GameObject; the prefab ref uses `{fileID: <root fileID in prefab>, guid: <prefab guid>, type: 3}`.

- [ ] **Step 4: Structural verification**

```powershell
# New scripts referenced in the scene:
grep -c "<MainMenuUI meta guid>" Assets/Scenes/MainMenu.unity     # expected: 1
grep -c "<LobbyScreenUI meta guid>" Assets/Scenes/MainMenu.unity  # expected: 1
# Old script gone:
grep -c "<old Teamselectionui meta guid>" Assets/Scenes/MainMenu.unity  # expected: 0
# No dangling zero-refs in the two new components' serialized fields:
# (inspect the UIManager MonoBehaviour blocks manually — every field non-{fileID: 0})
```

Also re-run `compile-gate.ps1` (unchanged code, but cheap) — expected: `COMPILE GATE PASSED`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Scenes/MainMenu.unity Assets/Prefabs/UI
git commit -m "feat(lobby): rebuild MainMenu scene — nickname entry, roster columns, collapsible loadout, host-only start"
```

---

### Task 6: Update project docs/memory breadcrumbs

**Files:**
- Modify: `docs/superpowers/guides/2026-06-25-dedicated-server-testing-guide.md` — the lobby-flow steps ("every player must choose a team before Start enables") are now wrong; update to: teams auto-assigned, first joiner is host, Start live from 1 player, nickname field.

- [ ] **Step 1: Update the guide's lobby section** to describe the new flow (auto-assign, roster, host star, Start gate ≥1).
- [ ] **Step 2: Commit**

```powershell
git add docs/superpowers/guides/2026-06-25-dedicated-server-testing-guide.md
git commit -m "docs: dedicated-server testing guide reflects revamped lobby flow"
```

---

### Task 7: Final verification

- [ ] **Step 1: Full logic-test run** — Task 3 Step 2's command. Expected: `23 passed, 0 failed`.
- [ ] **Step 2: Full compile gate** — `powershell -File "$SCRATCH\compile-gate.ps1"`. Expected: `COMPILE GATE PASSED`.
- [ ] **Step 3: Diff review** — `git log --oneline main..HEAD` shows the spec + 5 implementation commits; `git diff main..HEAD --stat` touches only the files this plan names (plus `.meta`s).
- [ ] **Step 4: Hand off the in-editor checklist to the user** (cannot be automated while the editor holds the lock):
  1. Open the project — confirm zero console errors after script reload, and the EditMode test suite passes in the Test Runner (`LobbyHostPolicyTests`, `LobbyProtocolTests`, `LobbyServerStateTests`).
  2. Multipeer/ParrelSync or two builds: Host + 1 client — roster shows both names within a second of joining, "Players: 2/20", host has ★ and Start, client does not.
  3. Client switches team → both screens update; client's switch button flips sides.
  4. Nickname persists across a restart (PlayerPrefs).
  5. Host clicks Start alone in lobby (1 player) → Gameplay loads.
  6. Dedicated server (`-batchmode -nographics -dedicatedServer`) + 2 clients: first client gets ★/Start, second doesn't; first client leaves → Start migrates to the second; Start loads Gameplay for everyone; a third client joining mid-match spawns on the smaller team.
  7. Kill the server while a client sits in the lobby → client returns to menu with "Disconnected: ..." status.

## Known limitations (accepted, matches current behavior)

- After a mid-*match* shutdown, the menu UI objects were destroyed by the scene switch, so the "back to menu" path only fully works from the lobby (MainMenu scene). Same as today; fixing it means reloading the MainMenu scene on shutdown — out of scope.
- `LobbyScreenUI` team counts per column are implicit (row counts); no numeric per-team counter for the alpha.
- Host mode's Start is gated only by `CanStart` (the host is by definition the session owner); the lowest-id designation only matters on the dedicated server.
