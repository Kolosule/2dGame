# Reconnection / Disconnection Handling — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a mid-match disconnect survivable — the server holds a dropped player's team, earned progression, and stats for the rest of the match keyed by a client-persisted identity token, and the client automatically retries its way back in and is restored to the same team with the same progression.

**Architecture:** A client-minted GUID lives in `PlayerPrefs` and rides every connect as Fusion's `StartGameArgs.ConnectionToken`. Server-side, `GameNetworkManager` gains a `ReconnectRegistry` (token → held state) that is captured in `OnPlayerLeft` and claimed in `OnPlayerJoined`; the claim writes the **existing** `LobbyTeamChoices` / `LobbyNicknameChoices` / `LobbyLoadoutChoices` handoff dictionaries so `NetworkedSpawnManager`'s spawn path needs no new branch, and parks the progression on a pending-restore map that the spawn callback consumes **before the avatar is replicated**. All pure rules (the hold map, the admission rule, the retry schedule, the token codec, the phase gate) live in engine-free assemblies (`Game.Net`, `Game.Match.Core`) and are unit-tested outside Unity. Client-side, a new `ReconnectController` MonoBehaviour owns drop detection, the backoff loop, and the runner rebuild that a shut-down `NetworkRunner` requires.

**Tech Stack:** Unity 6.3 (6000.3.0f1), Photon Fusion 2.0.9 (Host/Client + dedicated server), C#, NUnit EditMode tests, TextMeshPro, uGUI.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-29-reconnection-design.md` — read it first; this plan implements every decision in it verbatim.
- **The hold lasts the REST OF THE MATCH. Do not add a grace `TickTimer`.** The hold has exactly one expiry event: `GameNetworkManager.BeginReturnToLobby()` (already the single server-only return-to-lobby chokepoint) calling `Clear()`.
- **A held slot RESERVES its seat**, enforced in `OnConnectRequest`. **Do not change `StartGameArgs.PlayerCount`** — it stays `maxPlayers` (20) and remains Fusion's backstop.
- **The carried flag is always dropped immediately** on a leave, before anything else. This is already the first action in `NetworkedSpawnManager.OnPlayerLeft` — keep it first.
- **The avatar is despawned on disconnect, never frozen.** Do not add a "frozen zombie" mode.
- **Rejoin is a respawn:** team spawn, full health, empty hands. Do not preserve position, velocity, health, or coins in hand.
- **The token is an identity hint, not a credential.** No authentication, no server-side secret, no ban list, no `OnCustomAuthenticationResponse`.
- **Host migration stays out of scope.** `OnHostMigration` remains an empty stub. Server death ends the match.
- **All server-side writes happen only under `runner.IsServer` / `HasStateAuthority`**, matching every existing call site.
- **Engine-free assemblies stay engine-free.** `Game.Net` and `Game.Match.Core` both have `noEngineReferences: true` and `references: []`. No `UnityEngine`, no `Fusion`, no `Mathf` in any file under `Assets/Scripts/Net/` or `Assets/Scripts/Match/Core/`. This is why `PlayerIdentity` (needs `PlayerPrefs`) and `ReconnectController` (a `MonoBehaviour`) live at `Assets/Scripts/` root in the default assembly next to `GameNetworkManager`, **not** in `Assets/Scripts/Net/` where they conceptually belong. An asmdef covers its whole folder subtree — putting them under `Net/` will not compile.
- **`Game.Net` has no namespace** (`rootNamespace: ""`, and `LobbyServerState` is in the global namespace). New `Game.Net` types are global too. `Game.Match.Core` types are in the `Game.Match.Core` namespace.
- **Out of scope — do not build:** session discovery / server browser, accounts, cross-match persistence, spectating while disconnected, a disconnected "ghost row" on the scoreboard, any change to `MatchStatsManager`'s AoI setup, any change to the scoreboard UI.

### Numbers, verbatim from the spec

| Thing | Value |
|---|---|
| Identity token | `Guid.NewGuid().ToString("N")` → 32 lowercase hex chars → **16 raw bytes** on the wire |
| `PlayerPrefs` key | `reconnect.identity.v1` (plus salts — see Task 2) |
| Hold duration | Rest of the match (no timer) |
| Seat reservation | `active + held <= maxPlayers` (20); known token always admitted |
| Retry attempts | **5** |
| Retry backoff | **1 / 2 / 4 / 8 / 8** seconds before attempts 1–5 (~23 s total) |
| Phases that preserve state | `Warmup`, `Countdown`, `Live`, `SuddenDeath` |
| Phases that release fully | `PostMatch`, `Intermission` (and the lobby, where `MatchManager.Instance` is null) |

### Fusion API facts, verified against `Assets/Photon/Fusion/Assemblies/Fusion.Runtime.xml` in this project (2.0.9)

- `Fusion.StartGameArgs.ConnectionToken` — `byte[]`, "Connection token sent by client to server. Not used in shared mode." Default `null`.
- `Fusion.NetworkRunner.GetPlayerConnectionToken(Fusion.PlayerRef)` — "Returns a copy of the Connection Token used by a Player when connecting to this Server. **Only available on Server. It will return null if running on a Client or the Connection token is missing.**" This is what bridges a token to a `PlayerRef`; `OnConnectRequest` fires before any `PlayerRef` exists, so it cannot do the association itself.
- A `NetworkRunner` that has shut down **cannot be restarted**. Every reconnect attempt rebuilds the component stack.

### The callback-ordering invariant (load-bearing — read before Task 3)

`GameNetworkManager` calls `runner.AddCallbacks(this)` in `Start()` on the persistent `DontDestroyOnLoad` object; `NetworkedSpawnManager` calls `Runner.AddCallbacks(this)` in `Spawned()`, which happens on a later scene load. Fusion invokes callbacks in registration order, so **`GameNetworkManager`'s `OnPlayerJoined`/`OnPlayerLeft` always run before `NetworkedSpawnManager`'s on the same runner.**

This is not a new assumption. The existing join path already depends on it: `ServerHandleJoin` must fill `LobbyTeamChoices` before `TrySpawnPlayer` reads it, and it does. Task 3 depends on the same invariant in the other direction (capture must read the avatar before the spawn manager despawns it), and adds a `Debug.LogError` tripwire that fires loudly if the order ever changes.

### How to run tests

**Environment truth — read this before writing any "tests pass" claim:**

- **NUnit does not run outside the Unity editor here** (no reachable `nunit.framework.dll`). The committed NUnit `[Test]` files are the **user's** Test Runner gate. They are required deliverables — write them exactly as specified — but you cannot execute them.
- **Your execution evidence is a plain-`Main` harness.** Compile the engine-free sources (`Assets/Scripts/Net/*.cs`, `Assets/Scripts/Match/Core/*.cs`) plus a hand-written `class H { static int Main() }` assert harness against `netstandard 2.1` using
  `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\DotNetSdkRoslyn\csc.dll`,
  write a `net6.0` `runtimeconfig.json` beside the exe, and run it on
  `C:\Program Files\Unity\Hub\Editor\6000.3.0f1\Editor\Data\NetCoreRuntime\dotnet.exe` (it carries `Microsoft.NETCore.App 6.0.21`). Mirror every NUnit case as a harness assertion so your reported numbers correspond 1:1 to the committed tests.
- **Whole-surface compile gate.** Build a `@response.rsp` for `csc.dll` referencing the netstandard 2.1 ref, `Editor\Data\Managed\UnityEngine\*.dll`, `Assets\Photon\Fusion\Assemblies\*.dll`, and `Library\ScriptAssemblies\*.dll` (skip `*Editor*`, `*CodeGen*`, `*Tests*`, and **always exclude `Assembly-CSharp.dll`** — a stale copy produces bogus `CS1503`/`CS0117` against freshly compiled sources). Compile every `Assets/Scripts/**/*.cs` **except** the asmdef-owned folders: `Buffs/Core`, `Combat/Core`, `Enemy/AI`, `Hud/Core`, `Match/Core`, `Net`, `Player/Animation/Core`, `Player/Movement/Core`, `Stats/Core`. Exclude those with a **trailing `\`** on the prefix — a plain `StartsWith` on `...\Scripts\Net` also eats `NetworkedSpawnManager.cs`.
- **This plan changes `Game.Net` (Tasks 1, 5) and `Game.Match.Core` (Task 1).** Their `Library\ScriptAssemblies\Game.Net.dll` / `Game.Match.Core.dll` go stale the moment Task 1 lands: **drop both from the reference list and compile those folders' `.cs` inline** for the rest of the plan.
- Quote every path inside the `.rsp` ("Program Files" has a space). Reference paths must be Windows-format (`cygpath -w`); source paths work as relative.
- **A clean compile is not verification.** Report "Harness: N/N assertions pass" and "Compile gate: exit 0" as two separate claims. Never write "tests pass" meaning only that it compiled.

EditMode tests run in Unity for the **user**: Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All. Note this as pending in your report; do not claim it.

### What you CANNOT do (and must not claim)

You have no Unity Editor and no Play mode. Every step labeled **"Manual"** is the **user's** work:

- Do not create GameObjects, add components, or wire serialized fields in any `.unity` scene.
- Do not `git add` `Assets/Scenes/*.unity` (they are the user's working files; staging them sweeps up unrelated local edits).
- Do not enter Play mode, and never report a Play-mode or multi-peer result.

Write the code, run the harness and the compile gate, commit **code only**, and list every manual step you skipped under "Pending user verification" in your report.

---

## File Structure

**Created:**
- `Assets/Scripts/Net/ReconnectHeldSlot.cs` (+ `.meta`) — the preserved-state record. Plain fields, engine-free.
- `Assets/Scripts/Net/ReconnectRegistry.cs` (+ `.meta`) — token → held slot map. Capture / has / claim / clear.
- `Assets/Scripts/Net/ReconnectPolicy.cs` (+ `.meta`) — the seat-reservation admission rule.
- `Assets/Scripts/Net/ReconnectBackoff.cs` (+ `.meta`) — the client retry schedule.
- `Assets/Scripts/Net/IdentityTokenCodec.cs` (+ `.meta`) — hex ↔ 16 bytes.
- `Assets/Scripts/PlayerIdentity.cs` (+ `.meta`) — `PlayerPrefs`-backed GUID (default assembly; needs `UnityEngine`).
- `Assets/Scripts/ReconnectController.cs` (+ `.meta`) — client retry loop (default assembly; `MonoBehaviour`).
- `Assets/Tests/EditMode/Net/ReconnectRegistryTests.cs` (+ `.meta`)
- `Assets/Tests/EditMode/Net/ReconnectPolicyTests.cs` (+ `.meta`)
- `Assets/Tests/EditMode/Net/ReconnectBackoffTests.cs` (+ `.meta`)
- `Assets/Tests/EditMode/Net/IdentityTokenCodecTests.cs` (+ `.meta`)
- `docs/superpowers/plans/2026-08-03-reconnection-testing-guide.md` — the user's manual verification rubric (Task 8).

**Modified:**
- `Assets/Scripts/Match/Core/MatchRules.cs` — add `PreservesDisconnectState(MatchPhase)`.
- `Assets/Tests/EditMode/Match/MatchRulesTests.cs` — cases for the above.
- `Assets/Scripts/Net/LobbyServerState.cs` — add `PlayerJoinedOnTeam(int id, int team)`.
- `Assets/Tests/EditMode/Net/LobbyServerStateTests.cs` — cases for the above.
- `Assets/Scripts/GameNetworkManager.cs` — connection token on connect; the registry + pending-restore map; capture in `OnPlayerLeft`; claim in `OnPlayerJoined`; the `OnConnectRequest` admission gate; runner teardown/rebuild; intentional-quit latch; drop detection; reconnect UI passthroughs; the mid-match `lobbyUI` hide fix.
- `Assets/Scripts/NetworkedSpawnManager.cs` — scatter carried coins on leave; consume the pending restore at spawn.
- `Assets/Scripts/Buffs/PlayerBuffs.cs` — `ServerRestoreDeposited(int)`.
- `Assets/Scripts/Stats/MatchStatsManager.cs` — `RestoreEntry(int, int, string, ReconnectHeldSlot)`.
- `Assets/Scripts/UI/MainMenuUI.cs` — reconnecting state + optional Cancel button.

**Modified by the USER, not by any implementer:** `Assets/Scenes/MainMenu.unity` — optionally gains a reconnect panel + Cancel button wired into `MainMenuUI` (Task 7 is written so the feature works with both fields left unassigned). No other scene changes; no prefab changes.

---

## Task 1: Pure reconnect rules (`Game.Net` + `Game.Match.Core`)

Everything in this task is engine-free and fully executable in the harness. Nothing here references Unity or Fusion, so this task is the whole feature's testable core.

**Files:**
- Create: `Assets/Scripts/Net/ReconnectHeldSlot.cs` (+ `.meta`)
- Create: `Assets/Scripts/Net/ReconnectRegistry.cs` (+ `.meta`)
- Create: `Assets/Scripts/Net/ReconnectPolicy.cs` (+ `.meta`)
- Create: `Assets/Scripts/Net/ReconnectBackoff.cs` (+ `.meta`)
- Create: `Assets/Scripts/Net/IdentityTokenCodec.cs` (+ `.meta`)
- Modify: `Assets/Scripts/Match/Core/MatchRules.cs`
- Test: `Assets/Tests/EditMode/Net/ReconnectRegistryTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Net/ReconnectPolicyTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Net/ReconnectBackoffTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Net/IdentityTokenCodecTests.cs` (+ `.meta`)
- Test: `Assets/Tests/EditMode/Match/MatchRulesTests.cs` (append)

Both test folders already exist with their asmdefs (`Game.Net.Tests` references `Game.Net`; `Game.Match.Core.Tests` references `Game.Match.Core`). No new asmdef is needed.

**Interfaces:**
- Consumes: nothing (engine-free leaves).
- Produces:
  - `class ReconnectHeldSlot` — public fields `int Team; string DisplayName; byte[] LoadoutOrder; int TotalDepositedValue; int Kills; int Deaths; int Captures; int CoinsDeposited; int FlagCarrySeconds; int FlagReturns;`
  - `class ReconnectRegistry` — `int HeldCount { get; }`, `void Capture(string token, ReconnectHeldSlot slot)`, `bool Has(string token)`, `bool TryClaim(string token, out ReconnectHeldSlot slot)`, `void Clear()`
  - `static bool ReconnectPolicy.CanAdmit(bool knownToken, int activeCount, int heldCount, int maxPlayers)`
  - `static class ReconnectBackoff` — `const int MaxAttempts = 5`, `static float DelaySecondsForAttempt(int attempt)`
  - `static class IdentityTokenCodec` — `const int TokenBytes = 16`, `static byte[] ToBytes(string hex)`, `static string ToHex(byte[] token)`
  - `static bool Game.Match.Core.MatchRules.PreservesDisconnectState(MatchPhase phase)`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/EditMode/Net/ReconnectRegistryTests.cs`:

```csharp
using NUnit.Framework;

public class ReconnectRegistryTests
{
    private static ReconnectHeldSlot Slot(int team = 1, int deposited = 250) => new ReconnectHeldSlot
    {
        Team = team,
        DisplayName = "Ada",
        LoadoutOrder = new byte[] { 3, 1, 2 },
        TotalDepositedValue = deposited,
        Kills = 4,
        Deaths = 2,
        Captures = 1,
        CoinsDeposited = 250,
        FlagCarrySeconds = 37,
        FlagReturns = 3
    };

    [Test]
    public void Capture_ThenClaim_ReturnsTheCapturedState()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot());

        Assert.IsTrue(r.TryClaim("aa", out var got));
        Assert.AreEqual(1, got.Team);
        Assert.AreEqual("Ada", got.DisplayName);
        Assert.AreEqual(250, got.TotalDepositedValue);
        Assert.AreEqual(4, got.Kills);
        Assert.AreEqual(37, got.FlagCarrySeconds);
    }

    [Test]
    public void Claim_RemovesTheSlot_SoTwoRacingRejoinsCannotBothRestore()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot());

        Assert.IsTrue(r.TryClaim("aa", out _));
        Assert.IsFalse(r.TryClaim("aa", out var second));
        Assert.IsNull(second);
        Assert.AreEqual(0, r.HeldCount);
    }

    [Test]
    public void Has_TracksCaptureAndClaim()
    {
        var r = new ReconnectRegistry();
        Assert.IsFalse(r.Has("aa"));
        r.Capture("aa", Slot());
        Assert.IsTrue(r.Has("aa"));
        r.TryClaim("aa", out _);
        Assert.IsFalse(r.Has("aa"));
    }

    [Test]
    public void HeldCount_ReflectsDistinctTokens_AndRecaptureReplaces()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot(team: 1, deposited: 10));
        r.Capture("bb", Slot(team: 2, deposited: 20));
        Assert.AreEqual(2, r.HeldCount);

        // Same token twice (dropped, spawned, dropped again) replaces rather than duplicating.
        r.Capture("aa", Slot(team: 1, deposited: 99));
        Assert.AreEqual(2, r.HeldCount);
        Assert.IsTrue(r.TryClaim("aa", out var got));
        Assert.AreEqual(99, got.TotalDepositedValue);
    }

    [Test]
    public void EmptyOrNullToken_IsNeverHeldOrClaimed()
    {
        var r = new ReconnectRegistry();
        r.Capture("", Slot());
        r.Capture(null, Slot());
        Assert.AreEqual(0, r.HeldCount);
        Assert.IsFalse(r.Has(""));
        Assert.IsFalse(r.TryClaim(null, out _));
    }

    [Test]
    public void Clear_ReleasesEverything_TheMatchEnded()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot());
        r.Capture("bb", Slot());
        r.Clear();
        Assert.AreEqual(0, r.HeldCount);
        Assert.IsFalse(r.Has("aa"));
    }
}
```

Create `Assets/Tests/EditMode/Net/ReconnectPolicyTests.cs`:

```csharp
using NUnit.Framework;

public class ReconnectPolicyTests
{
    [Test]
    public void KnownToken_IsAlwaysAdmitted_ItIsReclaimingItsOwnReservedSeat()
    {
        // Session completely full on both counts: the holder still gets back in.
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: true, activeCount: 19, heldCount: 1, maxPlayers: 20));
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: true, activeCount: 20, heldCount: 0, maxPlayers: 20));
    }

    [Test]
    public void UnknownToken_IsRefusedWhenHeldSlotsFillTheCap()
    {
        // 19 playing + 1 holding a reserved seat = full, even though Fusion freed its own slot.
        Assert.IsFalse(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 19, heldCount: 1, maxPlayers: 20));
    }

    [Test]
    public void UnknownToken_IsAdmittedWhileThereIsRoom()
    {
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 18, heldCount: 1, maxPlayers: 20));
        Assert.IsTrue(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 0, heldCount: 0, maxPlayers: 20));
    }

    [Test]
    public void UnknownToken_IsRefusedWhenActivePlayersAloneFillTheCap()
    {
        Assert.IsFalse(ReconnectPolicy.CanAdmit(knownToken: false, activeCount: 20, heldCount: 0, maxPlayers: 20));
    }
}
```

Create `Assets/Tests/EditMode/Net/ReconnectBackoffTests.cs`:

```csharp
using NUnit.Framework;

public class ReconnectBackoffTests
{
    [Test]
    public void MaxAttempts_IsFive()
    {
        Assert.AreEqual(5, ReconnectBackoff.MaxAttempts);
    }

    [TestCase(1, 1f)]
    [TestCase(2, 2f)]
    [TestCase(3, 4f)]
    [TestCase(4, 8f)]
    [TestCase(5, 8f)]
    public void DelaySecondsForAttempt_FollowsTheSpecSchedule(int attempt, float expected)
    {
        Assert.AreEqual(expected, ReconnectBackoff.DelaySecondsForAttempt(attempt), 1e-4f);
    }

    [TestCase(0)]
    [TestCase(6)]
    [TestCase(-1)]
    public void DelaySecondsForAttempt_OutOfRange_IsZero(int attempt)
    {
        Assert.AreEqual(0f, ReconnectBackoff.DelaySecondsForAttempt(attempt), 1e-4f);
    }

    [Test]
    public void TotalScheduledWait_IsAboutTwentyThreeSeconds()
    {
        float total = 0f;
        for (int i = 1; i <= ReconnectBackoff.MaxAttempts; i++)
            total += ReconnectBackoff.DelaySecondsForAttempt(i);
        Assert.AreEqual(23f, total, 1e-4f);
    }
}
```

Create `Assets/Tests/EditMode/Net/IdentityTokenCodecTests.cs`:

```csharp
using NUnit.Framework;

public class IdentityTokenCodecTests
{
    private const string Hex32 = "0123456789abcdef0123456789abcdef";

    [Test]
    public void RoundTrip_HexToBytesToHex_IsLossless()
    {
        byte[] bytes = IdentityTokenCodec.ToBytes(Hex32);
        Assert.IsNotNull(bytes);
        Assert.AreEqual(16, bytes.Length);
        Assert.AreEqual(Hex32, IdentityTokenCodec.ToHex(bytes));
    }

    [Test]
    public void ToBytes_ParsesEachBytePairCorrectly()
    {
        byte[] bytes = IdentityTokenCodec.ToBytes(Hex32);
        Assert.AreEqual(0x01, bytes[0]);
        Assert.AreEqual(0x23, bytes[1]);
        Assert.AreEqual(0xef, bytes[7]);
    }

    [Test]
    public void ToBytes_AcceptsUppercase()
    {
        byte[] bytes = IdentityTokenCodec.ToBytes("0123456789ABCDEF0123456789ABCDEF");
        Assert.IsNotNull(bytes);
        Assert.AreEqual(0xab, bytes[5]);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0123")]                                    // too short
    [TestCase("0123456789abcdef0123456789abcdef00")]      // too long
    [TestCase("0123456789abcdef0123456789abcdeg")]        // 'g' is not hex
    public void ToBytes_RejectsAnythingThatIsNot32HexChars(string hex)
    {
        Assert.IsNull(IdentityTokenCodec.ToBytes(hex));
    }

    [Test]
    public void ToHex_RejectsNullOrWrongLength_ReturningEmpty()
    {
        // The server path feeds this GetPlayerConnectionToken's result, which is null on a client
        // or when the token is missing. Empty string means "no identity", never a bogus key.
        Assert.AreEqual("", IdentityTokenCodec.ToHex(null));
        Assert.AreEqual("", IdentityTokenCodec.ToHex(new byte[0]));
        Assert.AreEqual("", IdentityTokenCodec.ToHex(new byte[15]));
        Assert.AreEqual("", IdentityTokenCodec.ToHex(new byte[17]));
    }
}
```

Append to `Assets/Tests/EditMode/Match/MatchRulesTests.cs`, inside the existing `MatchRulesTests` class (before its closing brace):

```csharp
    // A drop preserves state only while the match is actually being played. Once it is decided
    // (PostMatch/Intermission) the scene reload is about to reset everything anyway, so holding
    // state that is seconds from deletion buys nothing.
    [TestCase(MatchPhase.Warmup, true)]
    [TestCase(MatchPhase.Countdown, true)]
    [TestCase(MatchPhase.Live, true)]
    [TestCase(MatchPhase.SuddenDeath, true)]
    [TestCase(MatchPhase.PostMatch, false)]
    [TestCase(MatchPhase.Intermission, false)]
    public void PreservesDisconnectState_FalseOnceTheMatchIsDecided(MatchPhase phase, bool expected)
    {
        Assert.AreEqual(expected, MatchRules.PreservesDisconnectState(phase));
    }
```

- [ ] **Step 2: Run the harness to verify the tests fail**

Build the harness described in "How to run tests" with the five new source files absent. Expected: **compile failure**, `CS0246: The type or namespace name 'ReconnectRegistry' could not be found` (and the same for `ReconnectPolicy`, `ReconnectBackoff`, `IdentityTokenCodec`, `ReconnectHeldSlot`), plus `CS0117: 'MatchRules' does not contain a definition for 'PreservesDisconnectState'`.

- [ ] **Step 3: Write `ReconnectHeldSlot`**

Create `Assets/Scripts/Net/ReconnectHeldSlot.cs`:

```csharp
/// <summary>
/// One disconnected player's preserved match state, held server-side for the rest of the match and
/// restored if they rejoin with the same identity token.
///
/// Plain C# with no Unity or Fusion types, so ReconnectRegistry stays engine-free and unit-testable.
/// That is also why the stats counters are loose ints rather than a PlayerStatEntry: that struct is
/// a Fusion INetworkStruct and cannot live in this assembly.
///
/// See docs/superpowers/specs/2026-07-29-reconnection-design.md.
/// </summary>
public class ReconnectHeldSlot
{
    public int Team;
    public string DisplayName;
    public byte[] LoadoutOrder;
    public int TotalDepositedValue;

    // MatchStatsManager row, copied out at capture and back in at restore under the NEW PlayerId.
    public int Kills;
    public int Deaths;
    public int Captures;
    public int CoinsDeposited;
    public int FlagCarrySeconds;
    public int FlagReturns;
}
```

Create `Assets/Scripts/Net/ReconnectHeldSlot.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 133b7bd5045e4fd389d07803d9fa4c0f
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

- [ ] **Step 4: Write `ReconnectRegistry`**

Create `Assets/Scripts/Net/ReconnectRegistry.cs`:

```csharp
using System.Collections.Generic;

/// <summary>
/// Server-side map of identity token -> the state of a player who dropped mid-match.
///
/// The hold lasts the REST OF THE MATCH — there is deliberately no timer here. The single expiry
/// event is the match ending, where GameNetworkManager.BeginReturnToLobby calls Clear().
///
/// Pure C#: GameNetworkManager owns the instance and does all the Fusion-facing work, so these
/// rules are unit-testable exactly like LobbyServerState.
/// </summary>
public class ReconnectRegistry
{
    private readonly Dictionary<string, ReconnectHeldSlot> held = new Dictionary<string, ReconnectHeldSlot>();

    /// <summary>Held (disconnected) slots. Counts against the player cap — see ReconnectPolicy.</summary>
    public int HeldCount => held.Count;

    /// <summary>
    /// Store this token's state, replacing any earlier hold for it (a player who dropped, rejoined,
    /// and dropped again re-holds rather than stacking). A null/empty token is ignored: a client
    /// with no identity simply cannot be held.
    /// </summary>
    public void Capture(string token, ReconnectHeldSlot slot)
    {
        if (string.IsNullOrEmpty(token) || slot == null) return;
        held[token] = slot;
    }

    public bool Has(string token) => !string.IsNullOrEmpty(token) && held.ContainsKey(token);

    /// <summary>
    /// Take this token's state AND remove it, so two rejoins racing on one token cannot both
    /// restore it — the first claim wins and the second is seated as a new player.
    /// </summary>
    public bool TryClaim(string token, out ReconnectHeldSlot slot)
    {
        slot = null;
        if (string.IsNullOrEmpty(token)) return false;
        if (!held.TryGetValue(token, out slot)) return false;
        held.Remove(token);
        return true;
    }

    /// <summary>Release every hold. Called when the match ends and on runner shutdown.</summary>
    public void Clear() => held.Clear();
}
```

Create `Assets/Scripts/Net/ReconnectRegistry.cs.meta` with the same `MonoImporter` template as Step 3 and `guid: 55db59add9c3459da816d68f089d8928`.

- [ ] **Step 5: Write `ReconnectPolicy` and `ReconnectBackoff`**

Create `Assets/Scripts/Net/ReconnectPolicy.cs`:

```csharp
/// <summary>
/// The server's admission rule, applied in GameNetworkManager.OnConnectRequest.
///
/// A held (disconnected) slot RESERVES its seat, which Fusion's own PlayerCount cannot express —
/// Fusion frees its slot the moment a player disconnects. So the real cap is enforced one level up,
/// here, while StartGameArgs.PlayerCount stays at maxPlayers as a backstop.
/// </summary>
public static class ReconnectPolicy
{
    /// <summary>
    /// A known token is always admitted: it is reclaiming a seat already reserved for it. An unknown
    /// token is admitted only while active + held is below the cap, which keeps the invariant
    /// active + held &lt;= maxPlayers — every held slot was previously an active one, so no headroom
    /// above Fusion's PlayerCount is ever needed.
    /// </summary>
    public static bool CanAdmit(bool knownToken, int activeCount, int heldCount, int maxPlayers)
    {
        if (knownToken) return true;
        return activeCount + heldCount < maxPlayers;
    }
}
```

Create `Assets/Scripts/Net/ReconnectBackoff.cs`:

```csharp
/// <summary>
/// The client's retry schedule: 5 attempts at 1 / 2 / 4 / 8 / 8 seconds (~23 s), then fall back to
/// the main menu. A fast first retry catches a momentary blip; the backoff avoids hammering a
/// server that is genuinely down.
///
/// Giving up is not final: the hold lasts the rest of the match, so the player can still reconnect
/// manually from the menu minutes later and get their state back.
/// </summary>
public static class ReconnectBackoff
{
    public const int MaxAttempts = 5;

    private static readonly float[] Delays = { 1f, 2f, 4f, 8f, 8f };

    /// <summary>Seconds to wait BEFORE attempt number `attempt` (1-based). Out of range -> 0.</summary>
    public static float DelaySecondsForAttempt(int attempt)
    {
        if (attempt < 1 || attempt > MaxAttempts) return 0f;
        return Delays[attempt - 1];
    }
}
```

Create both `.meta` files with the Step 3 template: `ReconnectPolicy.cs.meta` → `guid: 6a56d15044e74590b71676cf09b1c3f6`; `ReconnectBackoff.cs.meta` → `guid: 7455c4705e2448ae834727967b160fa6`.

- [ ] **Step 6: Write `IdentityTokenCodec`**

Create `Assets/Scripts/Net/IdentityTokenCodec.cs`:

```csharp
/// <summary>
/// Converts between the 32-char hex identity string kept in PlayerPrefs and the 16 raw bytes sent
/// as Fusion's StartGameArgs.ConnectionToken. Engine-free so the round trip is unit-testable, and
/// deliberately strict: anything that is not exactly 16 bytes / 32 hex chars is "no identity"
/// rather than a partially-parsed key that could collide with a real one.
/// </summary>
public static class IdentityTokenCodec
{
    public const int TokenBytes = 16;

    /// <summary>32 hex chars -> 16 bytes. Null for anything else (wrong length or a non-hex char).</summary>
    public static byte[] ToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != TokenBytes * 2) return null;

        var bytes = new byte[TokenBytes];
        for (int i = 0; i < TokenBytes; i++)
        {
            int hi = HexValue(hex[i * 2]);
            int lo = HexValue(hex[i * 2 + 1]);
            if (hi < 0 || lo < 0) return null;
            bytes[i] = (byte)((hi << 4) | lo);
        }
        return bytes;
    }

    /// <summary>
    /// 16 bytes -> 32 lowercase hex chars. Empty string for anything else — including null, which
    /// is what NetworkRunner.GetPlayerConnectionToken returns on a client or when a client sent no
    /// token at all.
    /// </summary>
    public static string ToHex(byte[] token)
    {
        if (token == null || token.Length != TokenBytes) return string.Empty;

        var chars = new char[TokenBytes * 2];
        for (int i = 0; i < TokenBytes; i++)
        {
            chars[i * 2] = HexDigit(token[i] >> 4);
            chars[i * 2 + 1] = HexDigit(token[i] & 0xF);
        }
        return new string(chars);
    }

    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'a' + (value - 10));

    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }
}
```

Create `Assets/Scripts/Net/IdentityTokenCodec.cs.meta` with the Step 3 template and `guid: 4c0439368e694395bc003c9d45fd568b`.

- [ ] **Step 7: Add `MatchRules.PreservesDisconnectState`**

In `Assets/Scripts/Match/Core/MatchRules.cs`, add this method after `AllBuffsMaxed`:

```csharp
        /// <summary>
        /// A disconnect in this phase preserves the player's state for a rejoin. True while the
        /// match is being played (Warmup, Countdown, Live, SuddenDeath); false once it is decided
        /// (PostMatch, Intermission), where the return-to-lobby scene reload is about to reset
        /// everything anyway.
        ///
        /// The lobby case — no match at all — is "MatchManager.Instance == null" and is handled by
        /// the caller, since this assembly cannot see the manager.
        /// </summary>
        public static bool PreservesDisconnectState(MatchPhase phase) =>
            phase != MatchPhase.PostMatch && phase != MatchPhase.Intermission;
```

Create the four test `.meta` files with the Step 3 template and these guids: `ReconnectRegistryTests.cs.meta` → `2273b103dbe647f5b22ffd179c342133`; `ReconnectPolicyTests.cs.meta` → `e66e340b41814b8c8c7f7e22c9bfc735`; `ReconnectBackoffTests.cs.meta` → `a958fe0c129a4bab8c17dc5013a51e61`; `IdentityTokenCodecTests.cs.meta` → `8459b660c1804f5d9bdd28e4a8d9d71a`.

- [ ] **Step 8: Run the harness to verify the tests pass**

Rebuild and run the harness. Expected: every assertion passes. Count the NUnit cases yourself — counting each `[TestCase]` row individually, the five files hold **35 cases** (`ReconnectRegistryTests` 6, `ReconnectPolicyTests` 4, `ReconnectBackoffTests` 10, `IdentityTokenCodecTests` 9, the appended `MatchRulesTests` block 6). Your harness must mirror every one. Report your own counted totals for both the cases and the assertions; if your count disagrees with 35, trust your count and say so.

- [ ] **Step 9: Commit**

```bash
git add "Assets/Scripts/Net/ReconnectHeldSlot.cs" "Assets/Scripts/Net/ReconnectHeldSlot.cs.meta" "Assets/Scripts/Net/ReconnectRegistry.cs" "Assets/Scripts/Net/ReconnectRegistry.cs.meta" "Assets/Scripts/Net/ReconnectPolicy.cs" "Assets/Scripts/Net/ReconnectPolicy.cs.meta" "Assets/Scripts/Net/ReconnectBackoff.cs" "Assets/Scripts/Net/ReconnectBackoff.cs.meta" "Assets/Scripts/Net/IdentityTokenCodec.cs" "Assets/Scripts/Net/IdentityTokenCodec.cs.meta" "Assets/Scripts/Match/Core/MatchRules.cs" "Assets/Tests/EditMode/Net/" "Assets/Tests/EditMode/Match/MatchRulesTests.cs"
git commit -m "feat(net): pure reconnect rules — hold registry, admission, backoff, token codec"
```

---

## Task 2: `PlayerIdentity` + the connection token on every connect

**Files:**
- Create: `Assets/Scripts/PlayerIdentity.cs` (+ `.meta`)
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`StartHost`, `StartClient`)

**Interfaces:**
- Consumes: `IdentityTokenCodec.ToBytes`, `IdentityTokenCodec.TokenBytes` (Task 1).
- Produces: `static string PlayerIdentity.Hex { get; }`, `static byte[] PlayerIdentity.TokenBytes { get; }`.

**Why there is no unit test here:** `PlayerIdentity` is a thin `PlayerPrefs` wrapper — the only logic worth testing (the hex ↔ bytes round trip and the length validation that decides whether a stored value is reused or re-minted) is `IdentityTokenCodec`, already covered in Task 1. Do not add a test that writes real `PlayerPrefs`; EditMode tests share the editor's prefs store and would pollute the user's actual identity.

- [ ] **Step 1: Write `PlayerIdentity`**

Create `Assets/Scripts/PlayerIdentity.cs`:

```csharp
using UnityEngine;

/// <summary>
/// The local player's stable identity: a GUID minted once, kept in PlayerPrefs, and sent as Fusion's
/// StartGameArgs.ConnectionToken on every connect so the server can recognise a rejoining player
/// whose PlayerRef has changed.
///
/// It is an identity HINT, not a credential: it only ever unlocks state the holder already earned in
/// the current match. See docs/superpowers/specs/2026-07-29-reconnection-design.md.
///
/// NOTE: this lives in the default assembly rather than Assets/Scripts/Net/ because Game.Net is
/// declared noEngineReferences and PlayerPrefs is a UnityEngine type.
/// </summary>
public static class PlayerIdentity
{
    private const string PrefKeyBase = "reconnect.identity.v1";

    /// <summary>Command-line override: `-identitySuffix bravo` -> key "reconnect.identity.v1.bravo".</summary>
    private const string SuffixArg = "-identitySuffix";

    private static string cachedHex;
    private static byte[] cachedBytes;

    /// <summary>The 32-char lowercase hex identity, minted and persisted on first access.</summary>
    public static string Hex
    {
        get
        {
            if (!string.IsNullOrEmpty(cachedHex)) return cachedHex;

            string key = PrefKey();
            string stored = PlayerPrefs.GetString(key, "");

            // Re-mint anything that is not a well-formed token (first run, cleared prefs, a value
            // written by an older build). ToBytes is the single definition of "well-formed".
            if (IdentityTokenCodec.ToBytes(stored) == null)
            {
                stored = System.Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(key, stored);
                PlayerPrefs.Save();
            }

            cachedHex = stored;
            return cachedHex;
        }
    }

    /// <summary>The same identity as the 16 raw bytes Fusion sends as the connection token.</summary>
    public static byte[] TokenBytes
    {
        get
        {
            if (cachedBytes == null) cachedBytes = IdentityTokenCodec.ToBytes(Hex);
            return cachedBytes;
        }
    }

    /// <summary>
    /// PlayerPrefs is per-PRODUCT, not per-process: on Windows it is one registry key derived from
    /// company + product name. Two clients on one machine — two standalone builds, or Multiplayer
    /// Play Mode virtual players — therefore share an identity by default, which makes every local
    /// peer look like a duplicate token and makes reconnection untestable locally.
    ///
    /// Two salts fix that: the editor always gets its own key (so editor + build are distinct), and
    /// `-identitySuffix &lt;value&gt;` gives each standalone build its own. The suffix is stable across
    /// relaunches of the same peer, which matters — a per-process salt would mint a new identity on
    /// every restart and break exactly the reconnect-after-relaunch case worth testing.
    /// </summary>
    private static string PrefKey()
    {
        string key = PrefKeyBase;

        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == SuffixArg && !string.IsNullOrEmpty(args[i + 1]))
            {
                key = key + "." + args[i + 1];
                break;
            }
        }

#if UNITY_EDITOR
        key = key + ".editor";
#endif
        return key;
    }
}
```

Create `Assets/Scripts/PlayerIdentity.cs.meta` with the Task 1 Step 3 `MonoImporter` template and `guid: ab75e90e25fa448f9211e289c3dedbf1`.

- [ ] **Step 2: Send the token on every connect**

In `Assets/Scripts/GameNetworkManager.cs`, add `ConnectionToken` to the `StartGameArgs` in **`StartHost`** and **`StartClient`** (not `StartServer` — a dedicated server is not a player and sends no token):

```csharp
        var args = new StartGameArgs()
        {
            GameMode = GameMode.Host, // AutoHostOrClient creates separate sessions — never use it here
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider,
            // Stable per-install identity, so a rejoining player is recognisable across a new
            // PlayerRef. Sent on every connect — there is no separate "reconnect mode" on the wire.
            ConnectionToken = PlayerIdentity.TokenBytes
        };
```

and the same `ConnectionToken` line in `StartClient`'s args block (which uses `GameMode.Client`).

- [ ] **Step 3: Record the session actually joined**

Still in `GameNetworkManager`, add the field next to `gameStarting`:

```csharp
    // The session name we are actually connected to. Reconnect retries against THIS rather than
    // re-reading the sessionName field, so a future server browser cannot silently send a
    // reconnecting player to the wrong server. See the spec's session-identity section.
    private string connectedSessionName;
```

and set it in both `StartHost` and `StartClient` inside the `if (result.Ok)` branch, before `EnterLobbyUI()`:

```csharp
        if (result.Ok)
        {
            connectedSessionName = args.SessionName;
            EnterLobbyUI();
        }
```

- [ ] **Step 4: Run the compile gate**

Run the whole-surface compile gate. Expected: exit 0, warning count unchanged from the pre-task baseline (record the baseline number before the change and state both).

- [ ] **Step 5: Commit**

```bash
git add "Assets/Scripts/PlayerIdentity.cs" "Assets/Scripts/PlayerIdentity.cs.meta" "Assets/Scripts/GameNetworkManager.cs"
git commit -m "feat(net): persistent player identity sent as the Fusion connection token"
```

---

## Task 3: Server-side capture on disconnect

The registry gets filled here. Nothing consumes it yet (Task 5 does), so this task is verified by the compile gate plus the user's later manual check that a drop no longer deletes coins.

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (registry field, `OnPlayerLeft`, `BeginReturnToLobby`, `OnShutdown`)
- Modify: `Assets/Scripts/NetworkedSpawnManager.cs` (`OnPlayerLeft` — scatter coins)

**Interfaces:**
- Consumes: `ReconnectRegistry`, `ReconnectHeldSlot`, `IdentityTokenCodec.ToHex` (Task 1); `MatchRules.PreservesDisconnectState` (Task 1); `MatchStatsManager.TryGetEntry` and `PlayerBuffs.TotalDeposited` (both already exist).
- Produces: a populated `reconnectRegistry` on the server, and `pendingRestores` (used by Task 5).

- [ ] **Step 1: Add the registry and pending-restore map**

In `Assets/Scripts/GameNetworkManager.cs`, add to the `using` block:

```csharp
using System.Linq;
using Game.Match.Core;
```

and add these fields next to `serverLobby`:

```csharp
    // Server-only. Token -> state preserved for players who dropped mid-match, held for the REST OF
    // THE MATCH (no timer) and released in BeginReturnToLobby / OnShutdown.
    private ReconnectRegistry reconnectRegistry = new ReconnectRegistry();

    // Server-only. A claimed hold, parked between OnPlayerJoined (which reclaims it) and the spawn
    // that consumes it — the two can be a whole scene load apart for a mid-match rejoin.
    private readonly Dictionary<PlayerRef, ReconnectHeldSlot> pendingRestores =
        new Dictionary<PlayerRef, ReconnectHeldSlot>();
```

- [ ] **Step 2: Capture on leave**

Replace `GameNetworkManager.OnPlayerLeft` with:

```csharp
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            // FIRST: preserve their state while the lobby records and the avatar still exist.
            ServerCaptureForReconnect(runner, player);

            serverLobby.PlayerLeft(player.PlayerId);
            LobbyTeamChoices.Remove(player);
            LobbyNicknameChoices.Remove(player);
            LobbyLoadoutChoices.Remove(player);
            pendingRestores.Remove(player);
            if (!gameStarting) BroadcastLobby();
        }
    }
```

and add this method directly below it:

```csharp
    /// <summary>
    /// Server-only. Preserves a mid-match leaver's earned state, keyed by their connection token, so
    /// a rejoin can restore it (docs/superpowers/specs/2026-07-29-reconnection-design.md).
    ///
    /// ORDERING INVARIANT: this must run while the leaver's avatar still exists, because the
    /// deposited value is read off it. GameNetworkManager registers its callbacks in Start() on the
    /// persistent object, long before NetworkedSpawnManager.Spawned() registers the callback that
    /// despawns the avatar, so this always runs first on the same runner. The existing JOIN path
    /// already depends on the same invariant (ServerHandleJoin must fill LobbyTeamChoices before
    /// TrySpawnPlayer reads it), so it is load-bearing in both directions — the LogError below is
    /// the tripwire if it ever changes.
    /// </summary>
    private void ServerCaptureForReconnect(NetworkRunner runner, PlayerRef player)
    {
        // Only while a match is actually being played. A lobby drop (no MatchManager at all) and a
        // PostMatch/Intermission drop release fully, exactly as before this feature: there is
        // nothing worth preserving, and reserving seats in a lobby would lock out real joiners.
        if (MatchManager.Instance == null) return;
        if (!MatchRules.PreservesDisconnectState(MatchManager.Instance.Phase)) return;

        // GetPlayerConnectionToken is server-only and returns null when the client sent no token;
        // ToHex turns anything malformed into "", which the registry refuses to key on.
        string token = IdentityTokenCodec.ToHex(runner.GetPlayerConnectionToken(player));
        if (string.IsNullOrEmpty(token)) return;

        // Dropped again before their avatar ever spawned (e.g. during the gameplay scene load):
        // re-hold the still-unconsumed restore instead of reading an avatar that never existed.
        if (pendingRestores.TryGetValue(player, out ReconnectHeldSlot pending))
        {
            reconnectRegistry.Capture(token, pending);
            return;
        }

        var slot = new ReconnectHeldSlot
        {
            Team = LobbyTeamChoices.TryGet(player, out int team) ? team : serverLobby.TeamOf(player.PlayerId),
            DisplayName = LobbyNicknameChoices.TryGet(player, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : LobbyProtocol.PlaceholderName(player.PlayerId),
            // Without this the rejoiner silently reverts to BuffLoadoutConfig's default priority
            // order, because LobbyLoadoutChoices.Remove runs moments from now.
            LoadoutOrder = LobbyLoadoutChoices.TryGet(player, out byte[] order) ? order : null
        };

        if (runner.TryGetPlayerObject(player, out NetworkObject avatar) && avatar != null)
        {
            PlayerBuffs buffs = avatar.GetComponent<PlayerBuffs>();
            if (buffs != null) slot.TotalDepositedValue = buffs.TotalDeposited;
        }
        else
        {
            Debug.LogError($"❌ ServerCaptureForReconnect: no avatar for Player {player.PlayerId} — " +
                           "callback order changed; their deposited value will NOT be restored.");
        }

        if (MatchStatsManager.Instance != null &&
            MatchStatsManager.Instance.TryGetEntry(player.PlayerId, out PlayerStatEntry entry))
        {
            slot.Kills = entry.Kills;
            slot.Deaths = entry.Deaths;
            slot.Captures = entry.Captures;
            slot.CoinsDeposited = entry.CoinsDeposited;
            slot.FlagCarrySeconds = entry.FlagCarrySeconds;
            slot.FlagReturns = entry.FlagReturns;
        }

        reconnectRegistry.Capture(token, slot);
    }
```

- [ ] **Step 3: Release the hold when the match ends**

In `BeginReturnToLobby`, add the clear (this is the hold's only expiry event):

```csharp
    public void BeginReturnToLobby()
    {
        if (runner == null || !runner.IsServer) return;

        // The match is over: every hold expires here. This is the whole reason the reconnect design
        // needs no grace TickTimer — the match boundary is the timer.
        reconnectRegistry.Clear();
        pendingRestores.Clear();

        gameStarting = false;
        _ = runner.LoadScene(SceneRef.FromIndex(menuSceneIndex));
    }
```

In `OnShutdown`, add the same two lines immediately after `CoinRegistry.Clear();`:

```csharp
        reconnectRegistry.Clear();
        pendingRestores.Clear();
```

- [ ] **Step 4: Scatter carried coins on leave**

In `Assets/Scripts/NetworkedSpawnManager.cs`, replace the flag-drop/despawn block in `OnPlayerLeft` with:

```csharp
        // Fusion does NOT clean up a leaver's objects in Host/Server mode - that is our job.
        // Drop any flag they were carrying FIRST (while the avatar still exists, so the flag
        // lands at their last position and the carrier-marker cleanup can still run), then
        // despawn the avatar so it doesn't linger as a frozen zombie.
        foreach (var flag in FindObjectsByType<Flag>(FindObjectsSortMode.None))
        {
            if (flag.IsCarriedBy(player))
                flag.DropFlag();
        }

        if (runner.TryGetPlayerObject(player, out NetworkObject playerObject))
        {
            // A disconnect costs exactly what a death costs: scatter carried coins back into the
            // world at the last position, mirroring PlayerStatsHandler.Die. Without this a leaving
            // carrier deletes their coins from the economy entirely.
            NetworkedPlayerInventory inventory = playerObject.GetComponent<NetworkedPlayerInventory>();
            if (inventory != null)
                inventory.OnPlayerDeath(playerObject.transform.position);

            runner.Despawn(playerObject);
        }
```

- [ ] **Step 5: Run the compile gate**

Expected: exit 0. If `Library\ScriptAssemblies\Game.Net.dll` / `Game.Match.Core.dll` are stale from Task 1, drop them from the references and compile `Assets/Scripts/Net/*.cs` and `Assets/Scripts/Match/Core/*.cs` inline (see "How to run tests").

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/GameNetworkManager.cs" "Assets/Scripts/NetworkedSpawnManager.cs"
git commit -m "feat(net): hold a mid-match leaver's state and scatter their coins like a death"
```

---

## Task 4: The seat-reservation admission gate

**Files:**
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`OnConnectRequest`)

**Interfaces:**
- Consumes: `ReconnectPolicy.CanAdmit`, `ReconnectRegistry.Has`, `IdentityTokenCodec.ToHex` (Task 1); `reconnectRegistry` (Task 3).
- Produces: nothing new — this changes behavior only.

- [ ] **Step 1: Replace `OnConnectRequest`**

```csharp
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        // The ONLY place that accepts/refuses connections (future ban list / lockout goes here).
        //
        // A held (disconnected) slot RESERVES its seat, which Fusion's PlayerCount cannot express —
        // Fusion frees its own slot the instant a player drops. So the real cap is enforced here,
        // while StartGameArgs.PlayerCount stays at maxPlayers as a backstop. Since every held slot
        // was previously an active one, active + held <= maxPlayers holds by construction.
        bool known = reconnectRegistry.Has(IdentityTokenCodec.ToHex(token));

        if (ReconnectPolicy.CanAdmit(known, runner.ActivePlayers.Count(), reconnectRegistry.HeldCount, maxPlayers))
        {
            request.Accept();
        }
        else
        {
            Debug.Log($"🚪 Refusing connection: session full " +
                      $"({runner.ActivePlayers.Count()} active + {reconnectRegistry.HeldCount} held / {maxPlayers}).");
            request.Refuse();
        }
    }
```

`runner.ActivePlayers` is an `IEnumerable<PlayerRef>`, so `.Count()` needs the `using System.Linq;` added in Task 3 Step 1.

- [ ] **Step 2: Run the compile gate**

Expected: exit 0.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/GameNetworkManager.cs"
git commit -m "feat(net): reserve a held slot's seat against the player cap"
```

---

## Task 5: Rejoin restoration

The other half of Task 3. After this task a reconnecting player is fully restored server-side.

**Files:**
- Modify: `Assets/Scripts/Net/LobbyServerState.cs` (`PlayerJoinedOnTeam`)
- Modify: `Assets/Tests/EditMode/Net/LobbyServerStateTests.cs` (append cases)
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`OnPlayerJoined` / `ServerHandleJoin`, `TryConsumeRestore`, `ReturnRestore`)
- Modify: `Assets/Scripts/NetworkedSpawnManager.cs` (`TrySpawnPlayer`, `SpawnPlayer`, `OnPlayerSpawned`)
- Modify: `Assets/Scripts/Buffs/PlayerBuffs.cs` (`ServerRestoreDeposited`)
- Modify: `Assets/Scripts/Stats/MatchStatsManager.cs` (`RestoreEntry`)

**Interfaces:**
- Consumes: `ReconnectRegistry.TryClaim`, `ReconnectHeldSlot` (Task 1); `pendingRestores` (Task 3).
- Produces:
  - `int LobbyServerState.PlayerJoinedOnTeam(int id, int team)`
  - `bool GameNetworkManager.TryConsumeRestore(PlayerRef player, out ReconnectHeldSlot slot)`
  - `void GameNetworkManager.ReturnRestore(PlayerRef player, ReconnectHeldSlot slot)`
  - `void PlayerBuffs.ServerRestoreDeposited(int total)`
  - `void MatchStatsManager.RestoreEntry(int playerId, int team, string displayName, ReconnectHeldSlot slot)`

- [ ] **Step 1: Write the failing test for `PlayerJoinedOnTeam`**

Append to `Assets/Tests/EditMode/Net/LobbyServerStateTests.cs`, inside the existing class:

```csharp
    [Test]
    public void PlayerJoinedOnTeam_SeatsAReconnectorOnTheirHeldTeam_BypassingAutoBalance()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1); // team 1
        s.PlayerJoined(2); // team 2
        s.PlayerJoined(3); // team 1  -> auto-balance would put id 4 on team 2

        Assert.AreEqual(1, s.PlayerJoinedOnTeam(4, 1));
        Assert.AreEqual(1, s.TeamOf(4));
        Assert.AreEqual(4, s.PlayerCount);
    }

    [Test]
    public void PlayerJoinedOnTeam_InvalidTeam_FallsBackToBalancedAutoAssign()
    {
        var s = new LobbyServerState();
        Assert.AreEqual(1, s.PlayerJoinedOnTeam(1, 0));  // 0-0 tie -> team 1
        Assert.AreEqual(2, s.PlayerJoinedOnTeam(2, 7));  // 1-0 -> team 2
    }

    [Test]
    public void PlayerJoinedOnTeam_ExistingPlayer_IsMovedToTheHeldTeam()
    {
        var s = new LobbyServerState();
        s.PlayerJoined(1);                 // team 1
        Assert.AreEqual(2, s.PlayerJoinedOnTeam(1, 2));
        Assert.AreEqual(2, s.TeamOf(1));
        Assert.AreEqual(1, s.PlayerCount); // not duplicated
    }
```

- [ ] **Step 2: Run the harness to verify it fails**

Expected: `CS1061: 'LobbyServerState' does not contain a definition for 'PlayerJoinedOnTeam'`.

- [ ] **Step 3: Implement `PlayerJoinedOnTeam`**

In `Assets/Scripts/Net/LobbyServerState.cs`, add after `PlayerJoined`:

```csharp
    /// <summary>
    /// Adds (or re-seats) the player on a SPECIFIC team — a reconnecting player reclaiming the team
    /// they held when they dropped, bypassing balanced auto-assign. An invalid team falls back to
    /// PlayerJoined. Returns the seated team.
    /// </summary>
    public int PlayerJoinedOnTeam(int id, int team)
    {
        if (team != 1 && team != 2) return PlayerJoined(id);

        if (players.TryGetValue(id, out var existing))
        {
            existing.Team = team;
            return team;
        }

        players[id] = new Entry { Name = LobbyProtocol.PlaceholderName(id), Team = team };
        return team;
    }
```

- [ ] **Step 4: Run the harness to verify it passes**

Expected: all `LobbyServerStateTests` assertions pass, including the three new ones.

- [ ] **Step 5: Claim the hold on join**

In `Assets/Scripts/GameNetworkManager.cs`, change the `OnPlayerJoined` callback to pass the runner:

```csharp
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // DO NOT SPAWN PLAYER HERE — NetworkedSpawnManager in the Gameplay scene handles it.
        if (runner.IsServer)
            ServerHandleJoin(runner, player);
    }
```

and replace `ServerHandleJoin` with:

```csharp
    /// <summary>
    /// Server-only: seat the player in the lobby roster and mirror the result into the handoff
    /// dictionaries NetworkedSpawnManager reads. Runs for mid-match late joiners too.
    ///
    /// A reconnecting player (a token matching a held slot) reclaims their held team, name, and
    /// loadout instead of being auto-assigned, and their progression is parked in pendingRestores
    /// for the spawn to consume. Restoration deliberately flows through the SAME three dictionaries
    /// as a normal join, so the spawn path needs no reconnect-specific branch and the lobby
    /// team-pick is skipped implicitly rather than by a special case.
    /// </summary>
    private void ServerHandleJoin(NetworkRunner runner, PlayerRef player)
    {
        string token = IdentityTokenCodec.ToHex(runner.GetPlayerConnectionToken(player));

        if (reconnectRegistry.TryClaim(token, out ReconnectHeldSlot held))
        {
            int heldTeam = serverLobby.PlayerJoinedOnTeam(player.PlayerId, held.Team);
            serverLobby.SetNickname(player.PlayerId, held.DisplayName);

            LobbyTeamChoices.Set(player, heldTeam);
            LobbyNicknameChoices.Set(player, held.DisplayName);
            if (held.LoadoutOrder != null && held.LoadoutOrder.Length > 0)
                LobbyLoadoutChoices.Set(player, held.LoadoutOrder);

            pendingRestores[player] = held;

            Debug.Log($"🔄 Player {player.PlayerId} reconnected — restored to team {heldTeam} " +
                      $"with {held.TotalDepositedValue} deposited.");

            if (!gameStarting) BroadcastLobby();
            return;
        }

        int team = serverLobby.PlayerJoined(player.PlayerId);
        LobbyTeamChoices.Set(player, team);
        LobbyNicknameChoices.Set(player, LobbyProtocol.PlaceholderName(player.PlayerId));
        if (!gameStarting) BroadcastLobby();
    }

    /// <summary>
    /// Server-only. Hands the spawn path a reconnecting player's parked state exactly once. Returns
    /// false (and null) for a normal joiner.
    /// </summary>
    public bool TryConsumeRestore(PlayerRef player, out ReconnectHeldSlot slot)
    {
        if (!pendingRestores.TryGetValue(player, out slot)) return false;
        pendingRestores.Remove(player);
        return true;
    }

    /// <summary>
    /// Server-only. Puts a consumed restore back when the spawn it was consumed for failed, so the
    /// spawn manager's existing retry path still restores the player.
    /// </summary>
    public void ReturnRestore(PlayerRef player, ReconnectHeldSlot slot)
    {
        if (slot != null) pendingRestores[player] = slot;
    }
```

- [ ] **Step 6: Restore the deposited value on `PlayerBuffs`**

In `Assets/Scripts/Buffs/PlayerBuffs.cs`, add after `ServerAddDepositedValue`:

```csharp
    /// <summary>
    /// SERVER: set the deposited total outright when restoring a reconnecting player. Separate from
    /// ServerAddDepositedValue because restore is an assignment, not an accumulation, and 0 is a
    /// legal restored value. Called from the Runner.Spawn callback, before replication, so the
    /// rejoiner's first snapshot already carries their tiers.
    /// </summary>
    public void ServerRestoreDeposited(int total)
    {
        if (!HasStateAuthority || total < 0) return;
        TotalDepositedValue = total;
    }
```

- [ ] **Step 7: Restore the stats row on `MatchStatsManager`**

In `Assets/Scripts/Stats/MatchStatsManager.cs`, add directly after `RegisterPlayer`:

```csharp
    /// <summary>
    /// SERVER: recreate a reconnecting player's roster entry under their NEW PlayerId, carrying the
    /// counters saved when they dropped.
    ///
    /// Slots are indexed by PlayerId and a rejoiner always gets a different one, so the old row
    /// cannot simply be kept: it stays behind, invisible (the scoreboard filters on
    /// Runner.ActivePlayers) and fully overwritten by RegisterPlayer if that id is ever reassigned.
    /// </summary>
    public void RestoreEntry(int playerId, int team, string displayName, ReconnectHeldSlot slot)
    {
        if (!HasStateAuthority || slot == null) return;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out int index))
        {
            Debug.LogError($"❌ MatchStatsManager.RestoreEntry: playerId {playerId} exceeds " +
                            $"RosterCapacity ({RosterCapacity}); this player's restored stats will not be tracked.");
            return;
        }

        Entries.Set(index, new PlayerStatEntry
        {
            Active = true,
            Team = (byte)team,
            DisplayName = displayName ?? string.Empty,
            IsDead = false,
            Kills = slot.Kills,
            Deaths = slot.Deaths,
            Captures = slot.Captures,
            CoinsDeposited = slot.CoinsDeposited,
            FlagCarrySeconds = slot.FlagCarrySeconds,
            FlagReturns = slot.FlagReturns
        });
    }
```

- [ ] **Step 8: Consume the restore at spawn**

In `Assets/Scripts/NetworkedSpawnManager.cs`, replace the tail of `TrySpawnPlayer` (from `spawnedPlayers.Add(player);` onward) with:

```csharp
        spawnedPlayers.Add(player);

        // A reconnecting player's held state is parked on GameNetworkManager until their avatar
        // exists (join and spawn can be a whole scene load apart). Consume it once, here, so both
        // the spawn callback and the stats registration below can use it.
        ReconnectHeldSlot restore = null;
        if (GameNetworkManager.Instance != null)
            GameNetworkManager.Instance.TryConsumeRestore(player, out restore);

        int team = AssignTeam(player, choice);

        Vector3 spawnPosition = GetSpawnPosition(team);
        SpawnPlayer(Runner, player, spawnPosition, team, restore);

        if (MatchStatsManager.Instance != null)
        {
            if (!LobbyNicknameChoices.TryGet(player, out string name) || string.IsNullOrEmpty(name))
                name = LobbyProtocol.PlaceholderName(player.PlayerId);

            if (restore != null)
                MatchStatsManager.Instance.RestoreEntry(player.PlayerId, team, name, restore);
            else
                MatchStatsManager.Instance.RegisterPlayer(player.PlayerId, team, name);
        }
```

Replace `SpawnPlayer` with:

```csharp
    private void SpawnPlayer(NetworkRunner runner, PlayerRef player, Vector3 spawnPosition, int team,
                             ReconnectHeldSlot restore)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("❌ Player prefab not assigned!");
            return;
        }


        NetworkObject spawnedObject = Runner.Spawn(
            playerPrefab,
            spawnPosition,
            Quaternion.identity,
            player,
            (runner, obj) => OnPlayerSpawned(runner, obj, team, restore)
        );

        if (spawnedObject == null)
        {
            Debug.LogError($"❌ Failed to spawn player {player.PlayerId}!");
            // Roll the bookkeeping back so a later trigger can retry the spawn cleanly.
            spawnedPlayers.Remove(player);
            if (playerTeams.Remove(player))
            {
                if (team == 1) team1Count--;
                else if (team == 2) team2Count--;
            }
            // Park the restore again so the retry still restores them.
            if (restore != null && GameNetworkManager.Instance != null)
                GameNetworkManager.Instance.ReturnRestore(player, restore);
        }
    }
```

Replace `OnPlayerSpawned` with:

```csharp
    private void OnPlayerSpawned(NetworkRunner runner, NetworkObject obj, int team, ReconnectHeldSlot restore)
    {
        // Register this object as the player's canonical player-object. Fusion replicates the
        // association to every peer, so Runner.TryGetPlayerObject(playerRef) resolves on clients
        // (not just the host). CTF flag carrier resolution (Flag.cs) depends on this; without it
        // clients can't find the carrier GameObject, so head markers and the carried-flag arrow
        // never track the carrier.
        runner.SetPlayerObject(obj.InputAuthority, obj);

        PlayerTeamData teamData = obj.GetComponent<PlayerTeamData>();

        if (teamData != null)
        {
            teamData.SetTeam(TeamUtil.FromNumber(team));
        }
        // Position is set by Runner.Spawn and synced by NetworkRigidbody2D.

        // Initialise the player's buff loadout from their lobby choice (host-authoritative).
        PlayerBuffs buffs = obj.GetComponent<PlayerBuffs>();
        if (buffs != null)
        {
            if (LobbyLoadoutChoices.TryGet(obj.InputAuthority, out byte[] order))
                buffs.ServerInitLoadout(order);
            // If no lobby choice, PlayerBuffs.Spawned applies the config default order.

            // Reconnect: restore earned progression HERE, in the pre-replication spawn callback, so
            // the rejoiner's very first snapshot already carries their buff tiers. There is no frame
            // in which they are visible and interactive at tier 0, and no RPC ordering to reason about.
            if (restore != null)
                buffs.ServerRestoreDeposited(restore.TotalDepositedValue);
        }
    }
```

- [ ] **Step 9: Run the harness and the compile gate**

Harness: all `LobbyServerStateTests` cases pass (existing plus the three new). Compile gate: exit 0. Report both separately.

- [ ] **Step 10: Commit**

```bash
git add "Assets/Scripts/Net/LobbyServerState.cs" "Assets/Tests/EditMode/Net/LobbyServerStateTests.cs" "Assets/Scripts/GameNetworkManager.cs" "Assets/Scripts/NetworkedSpawnManager.cs" "Assets/Scripts/Buffs/PlayerBuffs.cs" "Assets/Scripts/Stats/MatchStatsManager.cs"
git commit -m "feat(net): restore a reconnecting player's team, progression and stats"
```

---

## Task 6: Client reconnection loop

**Files:**
- Create: `Assets/Scripts/ReconnectController.cs` (+ `.meta`)
- Modify: `Assets/Scripts/GameNetworkManager.cs` (runner build/teardown, intentional-quit latch, drop detection, reconnect passthroughs)

**Interfaces:**
- Consumes: `ReconnectBackoff.MaxAttempts`, `ReconnectBackoff.DelaySecondsForAttempt` (Task 1); `PlayerIdentity.TokenBytes` (Task 2).
- Produces:
  - `void GameNetworkManager.TeardownRunner()`, `void GameNetworkManager.BuildRunner()`
  - `Task<bool> GameNetworkManager.TryReconnectAsync()`
  - `void GameNetworkManager.ReacquireMenuUI()`
  - `void GameNetworkManager.ShowReconnectingUI(string)`, `void GameNetworkManager.HideReconnectingUI(string)`, `void GameNetworkManager.OnReconnectSucceeded()`
  - `int GameNetworkManager.MenuSceneIndex { get; }`
  - `void GameNetworkManager.CancelReconnect()`
  - `class ReconnectController` — `bool IsReconnecting { get; }`, `void BeginReconnect(string reason)`, `void Cancel()`

- [ ] **Step 1: Extract the runner build so it can be rebuilt**

In `Assets/Scripts/GameNetworkManager.cs`, replace the body of `Start()` from `runner = gameObject.AddComponent<NetworkRunner>();` through `runner.AddCallbacks(this);` with a call to a new method, and add the method plus its teardown twin:

```csharp
    void Start()
    {
        if (Instance != this) return;
        DontDestroyOnLoad(gameObject);

        BuildRunner();

        LobbyTeamChoices.Clear();
        LobbyNicknameChoices.Clear();
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

    /// <summary>
    /// Creates the runner and every component bound to it. Called once at Start, and again per
    /// reconnect attempt: a NetworkRunner that has shut down CANNOT be restarted, so a reconnect
    /// must rebuild the whole stack rather than calling StartGame on the dead one.
    /// </summary>
    public void BuildRunner()
    {
        runner = gameObject.AddComponent<NetworkRunner>();

        // Fusion steps Physics2D inside the network tick (required for NetworkRigidbody2D prediction).
        // ClientPhysicsSimulation defaults to Disabled, which means CLIENTS never call
        // Physics.Simulate() and so never integrate their own rigidbody forward — SimulateForward
        // enables client-side prediction of the local player's position.
        simulatePhysics = gameObject.AddComponent<RunnerSimulatePhysics2D>();
        simulatePhysics.ClientPhysicsSimulation = ClientPhysicsSimulation.SimulateForward;

        // Pool high-churn networked prefabs (projectiles) instead of Instantiate/Destroy each shot.
        objectProvider = gameObject.AddComponent<PooledNetworkObjectProvider>();

        // One scene manager for the lifetime of the runner.
        sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();

        // Register the single input source.
        inputProvider = gameObject.AddComponent<NetworkInputProvider>();
        runner.AddCallbacks(inputProvider);

        // Receive lobby callbacks (player join/leave, reliable lobby data). Registered HERE, before
        // any gameplay-scene component can register, which is the ordering invariant the join and
        // leave paths both depend on (see ServerCaptureForReconnect).
        runner.AddCallbacks(this);
    }

    /// <summary>
    /// Destroys the runner and its bound components. Unity defers Destroy to end of frame, so a
    /// caller MUST wait one frame before calling BuildRunner or it will stack duplicates.
    /// </summary>
    public void TeardownRunner()
    {
        if (runner != null) { runner.Shutdown(); Destroy(runner); runner = null; }
        if (simulatePhysics != null) { Destroy(simulatePhysics); simulatePhysics = null; }
        if (objectProvider != null) { Destroy(objectProvider); objectProvider = null; }
        if (sceneManager != null) { Destroy(sceneManager); sceneManager = null; }
        if (inputProvider != null) { Destroy(inputProvider); inputProvider = null; }
    }
```

Add the two new fields alongside the existing `sceneManager` / `objectProvider` declarations:

```csharp
    private RunnerSimulatePhysics2D simulatePhysics;
    private NetworkInputProvider inputProvider;
```

- [ ] **Step 2: Add the drop-detection state and the reconnect entry points**

Add these fields next to `connectedSessionName`:

```csharp
    // Drop detection. A shutdown we caused (quit, app close) must NOT trigger the retry loop, and
    // neither must a first connect that never succeeded — that keeps today's "Connection failed"
    // behavior on the menu.
    private bool intentionalDisconnect = false;
    private bool hasBeenConnected = false;
    private bool startedAsClient = false;
    private ReconnectController reconnectController;
```

Add `startedAsClient = true;` as the first line of `StartClient()`, and `startedAsClient = false;` as the first line of both `StartHost()` and `StartServer()`.

In `Awake()`, after `Instance = this;`, resolve the controller:

```csharp
        reconnectController = GetComponent<ReconnectController>();
```

Replace `OnConnectedToServer`, `OnDisconnectedFromServer`, and the tail of `OnShutdown`, and mark the deliberate shutdowns:

```csharp
    public void OnConnectedToServer(NetworkRunner runner)
    {
        hasBeenConnected = true;
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"⚠️ Disconnected from server: {reason}");
        TryBeginReconnect(reason.ToString());
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

        reconnectRegistry.Clear();
        pendingRestores.Clear();

        LobbyTeamChoices.Clear();
        LobbyNicknameChoices.Clear();
        LobbyLoadoutChoices.Clear();
        serverLobby = new LobbyServerState();
        gameStarting = false;

        // An unexpected client drop hands off to the retry loop, which owns the UI from here.
        if (TryBeginReconnect(shutdownReason.ToString())) return;

        if (lobbyUI != null) lobbyUI.Hide();
        if (menuUI != null)
        {
            menuUI.Show();
            menuUI.ShowStatus($"Disconnected: {shutdownReason}");
        }
    }

    /// <summary>
    /// True when the retry loop took over. Only for a CLIENT that actually got connected and did not
    /// quit on purpose: a dedicated server, a host, and a failed first connect all keep today's
    /// straight-to-the-menu behavior.
    ///
    /// Fusion may raise OnDisconnectedFromServer, OnShutdown, or both for one drop, so this is
    /// called from both and BeginReconnect is idempotent.
    /// </summary>
    private bool TryBeginReconnect(string reason)
    {
        if (intentionalDisconnect || !hasBeenConnected || !startedAsClient) return false;
        if (reconnectController == null) return false;

        reconnectController.BeginReconnect(reason);
        return true;
    }

    /// <summary>Marks the next shutdown as ours, so it goes to the menu instead of the retry loop.</summary>
    public void MarkIntentionalDisconnect() => intentionalDisconnect = true;

    public void CancelReconnect()
    {
        if (reconnectController != null) reconnectController.Cancel();
    }
```

Update the two deliberate shutdowns to set the latch first:

```csharp
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        intentionalDisconnect = true;
        if (runner != null) runner.Shutdown();
    }

    void OnApplicationQuit()
    {
        intentionalDisconnect = true;
        if (runner != null) runner.Shutdown();
    }
```

- [ ] **Step 3: Add the reconnect attempt and the UI passthroughs**

Add these to `GameNetworkManager`, after `StartServer`:

```csharp
    public int MenuSceneIndex => menuSceneIndex;

    /// <summary>
    /// One reconnect attempt against the session we were actually in, with the same identity token.
    /// Requires a freshly built runner (see TeardownRunner/BuildRunner). Does not touch the menu UI —
    /// ReconnectController owns the overlay while the loop runs.
    /// </summary>
    public async System.Threading.Tasks.Task<bool> TryReconnectAsync()
    {
        if (runner == null) return false;

        startedAsClient = true;
        intentionalDisconnect = false;

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = string.IsNullOrEmpty(connectedSessionName) ? sessionName : connectedSessionName,
            PlayerCount = maxPlayers,
            SceneManager = sceneManager,
            ObjectProvider = objectProvider,
            ConnectionToken = PlayerIdentity.TokenBytes
        };

        var result = await runner.StartGame(args);
        return result.Ok;
    }

    /// <summary>
    /// Re-point at the menu scene's UI after a LOCAL (non-networked) scene load, which raises no
    /// Fusion OnSceneLoadDone. Same body as the return-to-lobby re-acquire, factored out so the two
    /// paths cannot drift.
    /// </summary>
    public void ReacquireMenuUI()
    {
        menuUI = FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        lobbyUI = FindFirstObjectByType<LobbyScreenUI>(FindObjectsInactive.Include);

        if (menuUI != null) menuUI.SetNetworkManager(this);
        if (lobbyUI != null) lobbyUI.SetNetworkManager(this);
    }

    public void ShowReconnectingUI(string message)
    {
        if (lobbyUI != null) lobbyUI.Hide();
        if (menuUI != null)
        {
            menuUI.Show();
            menuUI.ShowReconnecting(message);
        }
    }

    public void HideReconnectingUI(string message)
    {
        if (menuUI != null)
        {
            menuUI.Show();
            menuUI.HideReconnecting();
            menuUI.ShowStatus(message);
        }
    }

    /// <summary>A reconnect attempt succeeded: drop the overlay and re-enter the normal lobby flow.</summary>
    public void OnReconnectSucceeded()
    {
        connectedSessionName = string.IsNullOrEmpty(connectedSessionName) ? sessionName : connectedSessionName;
        if (menuUI != null) menuUI.HideReconnecting();
        EnterLobbyUI();
    }
```

In `OnSceneLoadDone`, replace the existing re-acquire block with a call to the shared method:

```csharp
        // The persistent GameNetworkManager's serialized menu/lobby refs died with the previous
        // menu scene instance; re-acquire the new ones.
        ReacquireMenuUI();
```

(Delete the four lines it replaces — the two `FindFirstObjectByType` assignments and the two `SetNetworkManager` calls.)

- [ ] **Step 4: Write `ReconnectController`**

Create `Assets/Scripts/ReconnectController.cs`:

```csharp
using System.Collections;
using UnityEngine;

/// <summary>
/// Client-side reconnection loop. GameNetworkManager decides that a drop was unexpected and calls
/// BeginReconnect; from there this owns the UI until it either reconnects or gives up to the menu.
///
/// Lives on the persistent GameNetworkManager GameObject. A shut-down NetworkRunner cannot be
/// restarted, so every attempt tears the runner component stack down and rebuilds it — which also
/// means every attempt must wait a frame between the two (Unity defers Destroy to end of frame).
///
/// See docs/superpowers/specs/2026-07-29-reconnection-design.md.
/// </summary>
[RequireComponent(typeof(GameNetworkManager))]
public class ReconnectController : MonoBehaviour
{
    private GameNetworkManager net;
    private Coroutine loop;

    public bool IsReconnecting => loop != null;

    private void Awake()
    {
        net = GetComponent<GameNetworkManager>();
    }

    /// <summary>
    /// Idempotent: Fusion may raise OnDisconnectedFromServer and OnShutdown for a single drop, and
    /// both call in here.
    /// </summary>
    public void BeginReconnect(string reason)
    {
        if (loop != null) return;
        loop = StartCoroutine(ReconnectLoop(reason));
    }

    /// <summary>
    /// User pressed Cancel. Stops the loop and returns to the idle menu. An in-flight StartGame task
    /// is not itself cancellable, so FallBackToMenu tears the runner down, which ends it.
    /// </summary>
    public void Cancel()
    {
        if (loop == null) return;
        StopCoroutine(loop);
        loop = null;
        StartCoroutine(FallBackToMenu("Reconnect cancelled."));
    }

    private IEnumerator ReconnectLoop(string reason)
    {
        // Once the runner dies the gameplay scene is full of despawned husks. Get back to the menu
        // scene LOCALLY (a plain scene load, not runner.LoadScene) so every attempt starts clean;
        // Fusion's scene sync drives the load back into gameplay when an attempt succeeds.
        UnityEngine.SceneManagement.SceneManager.LoadScene(net.MenuSceneIndex);
        yield return null;              // let the load settle before touching the new scene's UI
        net.ReacquireMenuUI();
        net.ShowReconnectingUI($"Connection lost ({reason}) — reconnecting…");

        for (int attempt = 1; attempt <= ReconnectBackoff.MaxAttempts; attempt++)
        {
            net.ShowReconnectingUI(
                $"Connection lost — reconnecting… (attempt {attempt} of {ReconnectBackoff.MaxAttempts})");
            yield return new WaitForSeconds(ReconnectBackoff.DelaySecondsForAttempt(attempt));

            net.TeardownRunner();
            yield return null;          // Destroy is deferred to end of frame; rebuild next frame
            net.BuildRunner();

            var task = net.TryReconnectAsync();
            while (!task.IsCompleted) yield return null;

            bool ok = !task.IsFaulted && !task.IsCanceled && task.Result;
            if (task.IsFaulted)
                Debug.LogWarning($"⚠️ Reconnect attempt {attempt} threw: {task.Exception}");

            if (ok)
            {
                loop = null;
                net.OnReconnectSucceeded();
                yield break;
            }
        }

        loop = null;
        yield return FallBackToMenu("Could not reconnect. Returning to the menu.");
    }

    private IEnumerator FallBackToMenu(string message)
    {
        // Leave a live, unused runner behind and the menu's Join button would call StartGame on a
        // dead one. Rebuild so a manual Join still works.
        net.TeardownRunner();
        yield return null;
        net.BuildRunner();
        net.HideReconnectingUI(message);
    }
}
```

Create `Assets/Scripts/ReconnectController.cs.meta` with the Task 1 Step 3 `MonoImporter` template and `guid: 8f6af85375f24254a2d698f706c05f19`.

- [ ] **Step 5: Run the compile gate**

Expected: exit 0. `MainMenuUI.ShowReconnecting` / `HideReconnecting` do not exist yet, so **this step will fail with `CS1061` until Task 7 Step 1 lands**. Do Task 7 Step 1 first if you are running the compile gate between tasks; otherwise commit this task and Task 7 together after Task 7 Step 1.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/ReconnectController.cs" "Assets/Scripts/ReconnectController.cs.meta" "Assets/Scripts/GameNetworkManager.cs"
git commit -m "feat(net): client auto-reconnect loop with backoff and runner rebuild"
```

---

## Task 7: Reconnecting UI + the mid-match lobby-panel fix

**Files:**
- Modify: `Assets/Scripts/UI/MainMenuUI.cs`
- Modify: `Assets/Scripts/GameNetworkManager.cs` (`OnSceneLoadDone`)

**Interfaces:**
- Consumes: `GameNetworkManager.CancelReconnect` (Task 6).
- Produces: `void MainMenuUI.ShowReconnecting(string message)`, `void MainMenuUI.HideReconnecting()`.

Both new serialized fields are optional (null-guarded, matching this file's existing style), so the feature works before the user wires anything — the status line alone carries the message.

- [ ] **Step 1: Add the reconnecting state to `MainMenuUI`**

Add the fields after `statusText`:

```csharp
    [Header("Reconnect (optional — the status line alone works without these)")]
    [SerializeField] private GameObject reconnectPanel;
    [SerializeField] private Button cancelReconnectButton;
```

Add the Cancel wiring at the end of `Start()`, after the host-button block:

```csharp
        if (cancelReconnectButton != null)
        {
            cancelReconnectButton.onClick.AddListener(() =>
            {
                if (networkManager != null) networkManager.CancelReconnect();
            });
        }

        if (reconnectPanel != null) reconnectPanel.SetActive(false);
```

Add the two methods after `SetBusy`:

```csharp
    /// <summary>
    /// Reconnecting state: the retry loop's message on the status line, Join/Host disabled, and the
    /// optional reconnect panel (with its Cancel button) shown. Call Show() first — Show resets busy.
    /// </summary>
    public void ShowReconnecting(string message)
    {
        if (reconnectPanel != null) reconnectPanel.SetActive(true);
        SetBusy(true);
        ShowStatus(message);
    }

    public void HideReconnecting()
    {
        if (reconnectPanel != null) reconnectPanel.SetActive(false);
        SetBusy(false);
    }
```

- [ ] **Step 2: Hide the lobby panel on a gameplay scene load**

In `GameNetworkManager.OnSceneLoadDone`, replace the early return:

```csharp
        // Only care about arriving back in the menu scene (the return-to-lobby path). The gameplay
        // load has a different build index and is handled by the gameplay-side managers.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != menuSceneIndex)
        {
            // A player pulled into a RUNNING match — a mid-match late joiner, or a reconnecting
            // player — never went through LoadGameplayScene's lobbyUI.Hide(), so without this the
            // lobby panel stays drawn on top of gameplay. Pre-existing, but reconnection turns a
            // rare path into the common one.
            if (lobbyUI != null) lobbyUI.Hide();
            if (menuUI != null) menuUI.Hide();
            return;
        }
```

- [ ] **Step 3: Run the compile gate**

Expected: exit 0, including everything from Task 6. Record the warning count against the baseline; the two new `[SerializeField]` fields legitimately add two `CS0649`s (`reconnectPanel`, `cancelReconnectButton`) — attribute the increase to exactly those, do not wave a larger increase through.

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/UI/MainMenuUI.cs" "Assets/Scripts/GameNetworkManager.cs"
git commit -m "feat(ui): reconnecting overlay state, and hide the lobby panel on a gameplay load"
```

- [ ] **Step 5: Manual (USER) — optional scene wiring**

Not the implementer's work. In `Assets/Scenes/MainMenu.unity`, on the GameObject carrying `MainMenuUI`:
1. Optionally create a small panel under the menu canvas with a "Cancel" `Button`, and assign it to `Reconnect Panel` / `Cancel Reconnect Button`.
2. Leave both empty to ship without it — the status line still shows "Connection lost — reconnecting… (attempt N of 5)" and Join/Host stay disabled while the loop runs.
3. Add the `ReconnectController` component to the persistent `GameNetworkManager` GameObject. **This one is required** — without it `TryBeginReconnect` finds no controller and a drop falls back to today's straight-to-the-menu behavior. `[RequireComponent(typeof(GameNetworkManager))]` means adding it to that object is all that is needed; it has no serialized fields.

---

## Task 8: Manual verification guide

**Files:**
- Create: `docs/superpowers/plans/2026-08-03-reconnection-testing-guide.md`

This is a document, not code. It exists because every meaningful check for this feature is a multi-peer Play-mode check that no implementer can run.

- [ ] **Step 1: Write the guide**

Create `docs/superpowers/plans/2026-08-03-reconnection-testing-guide.md` with these sections, written out in full:

**Setup**
- Add `ReconnectController` to the persistent `GameNetworkManager` GameObject (required).
- Optionally wire `Reconnect Panel` / `Cancel Reconnect Button` on `MainMenuUI`.
- **Identity collision warning:** `PlayerPrefs` is per-product, so two clients on one machine share an identity unless salted. The editor is salted automatically (`.editor`). For two standalone builds, launch each with a different `-identitySuffix`, e.g. `Game.exe -identitySuffix alpha` and `Game.exe -identitySuffix bravo`. If two peers do share an identity, the second is seated as a brand-new player and **nothing restores** — which looks exactly like a broken feature but is the designed duplicate-token behavior.

**Core restore (1 server + 2 clients, mid-match)**
1. Client B drops while carrying a flag and coins → flag drops at their last position; **coins scatter there** (this is new — verify explicitly); avatar despawns; B's scoreboard row disappears.
2. B reconnects → same team, same nickname, same deposited value and buff tiers, same loadout order, scoreboard row restored with its counters, spawned at the **team spawn** at **full health** with **no coins**. No lobby team-pick appears. No lobby panel drawn over gameplay.
3. Kill B's process entirely and relaunch → same result (proves the identity survives a process restart via `PlayerPrefs`, not just an in-memory field).

**Retry loop**
4. Disable B's network adapter → the overlay appears with the attempt counter. Re-enable mid-backoff → the loop succeeds and restores.
5. Let it exhaust all 5 attempts → main menu with a reason. Then Join manually → still restored (the hold lasts the rest of the match).
6. Cancel mid-loop → clean return to the idle menu, and a subsequent manual Join works (proves the runner rebuild is sound).
7. Quit B deliberately (window close / Alt-F4) → **no** overlay, no retry loop.

**Phase behavior**
8. Drop in the lobby → seat released immediately; rejoin is a plain fresh join with auto-assigned team.
9. Drop during PostMatch → released; the rejoiner rides the return-to-lobby scene load in with everyone else.
10. Play a full match to return-to-lobby with a player still held → confirm the hold is gone (they rejoin as new).

**Seat reservation (testable without 20 peers)**
11. Temporarily set `GameNetworkManager.maxPlayers = 2`. Fill the session, drop one player, confirm a third peer is **refused** while the slot is held, and that the original can still get back in. Restore `maxPlayers = 20` afterwards.

**Server death**
12. Stop the dedicated server process → every client runs its retry loop, fails, and lands on the menu with the shutdown reason. Nothing migrates; the match is over.

**EditMode suite**
13. Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All — all green, including the new `ReconnectRegistryTests`, `ReconnectPolicyTests`, `ReconnectBackoffTests`, `IdentityTokenCodecTests`, and the added `MatchRulesTests` / `LobbyServerStateTests` cases.

- [ ] **Step 2: Commit**

```bash
git add "docs/superpowers/plans/2026-08-03-reconnection-testing-guide.md"
git commit -m "docs(reconnection): manual verification guide"
```

---

## Self-Review Notes

**Spec coverage:**

| Spec section | Task |
|---|---|
| Identity token (`PlayerIdentity`, codec, `ConnectionToken`) | 1, 2 |
| `ReconnectRegistry` + hold duration (rest of match, `BeginReturnToLobby` clears) | 1, 3 |
| Disconnect ordering: flag → coins → capture → despawn | 3 |
| Rejoin handshake (claim → handoff dictionaries → spawn → pre-replication restore) | 5 |
| `MatchStatsManager.RestoreEntry` under the new `PlayerId` | 5 |
| Preserved-vs-reset contract | 3 (capture) + 5 (restore); reset is the existing spawn path, deliberately untouched |
| Client retry loop, backoff, runner rebuild, local menu load | 1, 6 |
| Reconnecting UX on `MainMenuUI` + Cancel | 7 |
| Adjacent fix: `lobbyUI` drawn over gameplay | 7 |
| Match-phase behavior (`PreservesDisconnectState` + null `MatchManager`) | 1, 3 |
| Seat reservation without touching `PlayerCount` | 1, 4 |
| Edge cases: released hold, duplicate token, missing token, server death, racing claims, drop-before-spawn | 1 (registry/codec), 3 (pending re-hold), 5 (`TryClaim` removes), 6 (server death = loop exhausts) |
| Session identity forward-compat (`connectedSessionName`) | 2, 6 |
| Non-goals | Global Constraints; no task builds any of them |

**Known ordering note:** Task 6's compile gate fails until Task 7 Step 1 adds `MainMenuUI.ShowReconnecting`/`HideReconnecting`. This is called out inline in Task 6 Step 5 rather than silently reordered, because Task 6 and Task 7 are otherwise independent reviews.

**Type consistency:** `ReconnectHeldSlot` field names are used identically in Tasks 1, 3, and 5. `TryConsumeRestore` / `ReturnRestore` / `RestoreEntry` / `ServerRestoreDeposited` signatures in Task 5's Interfaces block match every call site. `ReconnectBackoff.MaxAttempts` and `DelaySecondsForAttempt` match between Task 1's implementation and Task 6's loop. `MenuSceneIndex` (Task 6) is the property; `menuSceneIndex` remains the public field it wraps.
