# Flag HUD Rebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the CTF flag HUD with screen-edge directional arrows (one per team flag, shown only when that flag is away from home and off-screen) plus rebuilt pickup feedback (center-screen notification + carrier head icon).

**Architecture:** Flag networked state is the unchanged source of truth. A new local-only `FlagDirectionHud` MonoBehaviour on the Screen-Space HUD canvas reads both flags from `CTFGameManager.Instance` each `LateUpdate` and drives two child arrow `Image`s via `Camera.main` viewport math. `CTFGameManager` is trimmed of its old on-flag world-space indicators and status labels; the notification RPC and carrier head icon (`FlagCarrierMarker`) are kept/rebuilt.

**Tech Stack:** Unity (C#), Photon Fusion (networking, untouched here), Unity UI (uGUI) for the HUD.

## Global Constraints

- This is **presentation only** — add no `[Networked]` state and no RPCs beyond the existing `RPC_ShowNotification`. Flag position/state/carrier are already replicated.
- No automated test harness exists for runtime MonoBehaviour code in this repo; verification is **Unity compile (no errors in the Console) + manual in-editor host+client play**. Each task's "test" step is a compile/play check.
- Team color comes from `TeamManager.Instance.GetTeamData(team).teamColor`; team identity from the `Team` enum and `TeamUtil`.
- Follow existing file conventions: scripts live under `Assets/Scripts/CTF Flag/`, one class per file, filename == class name.
- Arrow sprites are authored pointing **right (+X)**; rotation `0` = pointing east. The math assumes this.
- Commit after each task. Branch note: start this work on a branch off `main` (see Task 0) — current branch `fix/predict-local-player-animation` has unrelated uncommitted changes.

---

### Task 0: Branch setup

**Files:** none (git only)

- [ ] **Step 1: Create a clean branch off main**

The repo has unrelated uncommitted changes on `fix/predict-local-player-animation`
(`GameNetworkManager.cs`, `ProjectSettings`, `.plastic`). Leave those alone; branch
from the current state so this feature is isolated.

```bash
git checkout -b feature/flag-hud-rebuild
```

- [ ] **Step 2: Confirm the spec is present**

Run: `ls docs/superpowers/specs/2026-06-24-flag-hud-rebuild-design.md`
Expected: the file path prints (it was committed during brainstorming).

---

### Task 1: Expose owning-team enum on `Flag`; decouple status-label callback

**Files:**
- Modify: `Assets/Scripts/CTF Flag/Flag.cs`

**Interfaces:**
- Produces: `public Team Flag.OwningTeamEnum { get; }` — canonical `Team` for this flag, consumed by `FlagDirectionHud` (Task 4).

- [ ] **Step 1: Add the owning-team enum getter**

In `Flag.cs`, next to the existing `public string OwningTeam => owningTeam;` (around line 67), add:

```csharp
    /// <summary>Canonical Team for this flag (HUD/color lookups). Derived from the authored string.</summary>
    public Team OwningTeamEnum => TeamUtil.Normalize(owningTeam);
```

- [ ] **Step 2: Remove the status-label coupling**

`CTFGameManager.OnFlagStateChanged` only fed the old status label + world-space
indicator, both of which are being deleted (Task 2). Remove its call from
`Flag.OnStateChanged`. Change the method (around line 314) from:

```csharp
    private void OnStateChanged()
    {
        UpdateVisuals();

        // Drive CTF HUD off this networked state change instead of a per-frame UI rebuild.
        if (CTFGameManager.Instance != null)
            CTFGameManager.Instance.OnFlagStateChanged(this);
    }
```

to:

```csharp
    private void OnStateChanged()
    {
        UpdateVisuals();
    }
```

- [ ] **Step 3: Compile check**

In Unity, let it recompile. Expected: Console shows **no** errors. (`Flag.cs`
no longer references `OnFlagStateChanged`; that method is removed in Task 2, so
order matters — do Task 1 then Task 2 before returning to the editor if you want a
single clean compile. If compiling between tasks, expect a transient
"CTFGameManager does not contain OnFlagStateChanged" only if Task 2 ran first;
doing Task 1 first avoids it.)

- [ ] **Step 4: Commit**

```bash
git add "Assets/Scripts/CTF Flag/Flag.cs"
git commit -m "feat(ctf): expose Flag.OwningTeamEnum; drop status-label callback"
```

---

### Task 2: Trim `CTFGameManager` of old indicators + status labels

**Files:**
- Modify: `Assets/Scripts/CTF Flag/CTFGameManager.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `CTFGameManager.Instance`, `public Flag Team1Flag { get; }`, `public Flag Team2Flag { get; }` (already exist) — consumed by `FlagDirectionHud` (Task 4). `RPC_ShowNotification(string)` unchanged.

- [ ] **Step 1: Remove the status-label and indicator serialized fields**

Delete these four `[SerializeField]` blocks from the `[Header("UI References")]`
and `[Header("Flag Indicators")]` sections (lines ~33-44):

```csharp
    [Tooltip("Text for Team1/Blue flag status")]
    [SerializeField] private TextMeshProUGUI team1FlagStatusText;

    [Tooltip("Text for Team2/Red flag status")]
    [SerializeField] private TextMeshProUGUI team2FlagStatusText;

    [Header("Flag Indicators")]
    [Tooltip("Transform showing Team1 flag location")]
    [SerializeField] private GameObject team1FlagIndicator;

    [Tooltip("Transform showing Team2 flag location")]
    [SerializeField] private GameObject team2FlagIndicator;
```

Keep `notificationText`, `gameOverPanel`, `winnerText`, and `notificationDuration`.

- [ ] **Step 2: Remove the per-frame `Update()` indicator follow**

Delete the entire `Update()` method (lines ~101-113).

- [ ] **Step 3: Remove the status-label / indicator UI methods**

Delete `OnFlagStateChanged(Flag)` (~240-246), `RefreshAllFlagUI()` (~248-252),
and `RefreshFlagUI(Flag, TextMeshProUGUI, GameObject)` (~254-288).

- [ ] **Step 4: Drop the `RefreshAllFlagUI()` call in `Spawned()`**

In `Spawned()` (~77-90) remove the `RefreshAllFlagUI();` line. The remaining body:

```csharp
    public override void Spawned()
    {
        base.Spawned();

        // Find flags if not assigned
        if (team1Flag == null || team2Flag == null)
        {
            FindFlags();
        }

        OnGameOverChanged();
    }
```

- [ ] **Step 5: Compile check**

In Unity, recompile. Expected: Console shows **no** errors. `TMPro` is still used
by `notificationText`/`winnerText`, so leave the `using TMPro;` import.

- [ ] **Step 6: Commit**

```bash
git add "Assets/Scripts/CTF Flag/CTFGameManager.cs"
git commit -m "refactor(ctf): remove on-flag indicators and status labels from manager"
```

---

### Task 3: Rebuild `FlagCarrierMarker`

**Files:**
- Modify: `Assets/Scripts/CTF Flag/FlagCarrierMarker.cs`

**Interfaces:**
- Produces: `public void SetCarryingFlag(bool)` (idempotent), `public bool IsCarryingFlag()` — both already called by `Flag.cs`; signatures unchanged.

- [ ] **Step 1: Replace the file body**

Rewrite `FlagCarrierMarker.cs` so `SetCarryingFlag` is idempotent (safe to call
every frame from `Flag.Update()`'s reconciliation), drops the no-op
`playerMovement.enabled` toggle, and cleans up on destroy:

```csharp
using UnityEngine;

/// <summary>
/// Attach to player prefabs. Shows a floating icon above the player's head while
/// they carry a flag, on every peer. Dash suppression is handled elsewhere via
/// IsCarryingFlag(); this component is purely the head-icon visual.
/// </summary>
public class FlagCarrierMarker : MonoBehaviour
{
    [Header("Visual Indicator")]
    [Tooltip("Icon to show above the player's head when carrying a flag")]
    [SerializeField] private GameObject flagIconPrefab;

    [Tooltip("Height above the player to show the icon")]
    [SerializeField] private float iconHeight = 2f;

    private GameObject flagIcon;
    private bool isCarryingFlag;

    /// <summary>Idempotent: repeated calls with the same value do nothing.</summary>
    public void SetCarryingFlag(bool carrying)
    {
        if (carrying == isCarryingFlag) return;
        isCarryingFlag = carrying;

        if (carrying)
        {
            if (flagIcon == null && flagIconPrefab != null)
            {
                flagIcon = Instantiate(flagIconPrefab, transform);
                flagIcon.transform.localPosition = Vector3.up * iconHeight;
            }
        }
        else if (flagIcon != null)
        {
            Destroy(flagIcon);
            flagIcon = null;
        }
    }

    public bool IsCarryingFlag() => isCarryingFlag;

    private void OnDestroy()
    {
        if (flagIcon != null) Destroy(flagIcon);
    }
}
```

- [ ] **Step 2: Compile check**

In Unity, recompile. Expected: Console shows **no** errors. (`Flag.cs` calls
`SetCarryingFlag(bool)` and `IsCarryingFlag()`; both still exist.)

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/CTF Flag/FlagCarrierMarker.cs"
git commit -m "refactor(ctf): make FlagCarrierMarker idempotent, drop dash hack"
```

---

### Task 4: Create `FlagDirectionHud`

**Files:**
- Create: `Assets/Scripts/CTF Flag/FlagDirectionHud.cs`

**Interfaces:**
- Consumes: `CTFGameManager.Instance.Team1Flag` / `.Team2Flag` (Task 2), `Flag.State`, `Flag.OwningTeamEnum` (Task 1), `Flag.transform`, `flag.Object.IsValid`, `TeamManager.Instance.GetTeamData(Team).teamColor`.
- Produces: a component to attach to the HUD canvas and wire in Task 5.

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/CTF Flag/FlagDirectionHud.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local-only HUD. For each team flag, shows a screen-edge arrow that points toward
/// the flag, but only while that flag is away from home AND off-screen. Each arrow is
/// recolored to its flag's owning-team color. Reads replicated flag state from
/// CTFGameManager.Instance; adds no networking of its own.
///
/// Attach to the Screen-Space HUD canvas. Wire one arrow RectTransform+Image per team.
/// Author arrow sprites pointing RIGHT (+X); rotation 0 = pointing east.
/// Arrow RectTransforms must be anchored AND pivoted at center (0.5, 0.5).
/// </summary>
public class FlagDirectionHud : MonoBehaviour
{
    [System.Serializable]
    public class FlagArrow
    {
        [Tooltip("Arrow UI element. Anchor + pivot must both be center (0.5, 0.5).")]
        public RectTransform arrow;

        [Tooltip("The arrow's Image, recolored to the flag's owning-team color.")]
        public Image arrowImage;
    }

    [Header("Arrows")]
    [Tooltip("Arrow for the Team1 (Blue) flag.")]
    [SerializeField] private FlagArrow team1Arrow;

    [Tooltip("Arrow for the Team2 (Red) flag.")]
    [SerializeField] private FlagArrow team2Arrow;

    [Header("Layout")]
    [Tooltip("Camera for world->screen. Defaults to Camera.main when left empty.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Pixels the arrow is inset from the screen edge.")]
    [SerializeField] private float edgeMargin = 60f;

    private RectTransform canvasRect;

    private void Awake()
    {
        canvasRect = transform as RectTransform;
        SetActive(team1Arrow, false);
        SetActive(team2Arrow, false);
    }

    private void LateUpdate()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || canvasRect == null || CTFGameManager.Instance == null)
            return;

        UpdateArrow(team1Arrow, CTFGameManager.Instance.Team1Flag);
        UpdateArrow(team2Arrow, CTFGameManager.Instance.Team2Flag);
    }

    private void UpdateArrow(FlagArrow a, Flag flag)
    {
        if (a == null || a.arrow == null) return;

        // Hide unless there's a valid flag that is out of its base.
        if (flag == null || flag.Object == null || !flag.Object.IsValid ||
            flag.State == Flag.FlagState.AtHome)
        {
            SetActive(a, false);
            return;
        }

        Vector3 vp = viewCamera.WorldToViewportPoint(flag.transform.position);
        bool behind = vp.z < 0f;
        bool onScreen = !behind && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

        // Flag is visible on screen -> no arrow needed.
        if (onScreen)
        {
            SetActive(a, false);
            return;
        }

        SetActive(a, true);
        ApplyColor(a, flag);

        // Direction from screen center to the flag, in centered viewport space.
        Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
        if (behind) dir = -dir;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
        dir.Normalize();

        // Clamp onto the inset canvas rectangle (box clamp from center).
        Vector2 half = canvasRect.rect.size * 0.5f - Vector2.one * edgeMargin;
        half = Vector2.Max(half, Vector2.zero);
        float scale = Mathf.Min(
            half.x / Mathf.Max(Mathf.Abs(dir.x), 1e-6f),
            half.y / Mathf.Max(Mathf.Abs(dir.y), 1e-6f));

        a.arrow.anchoredPosition = dir * scale;
        a.arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    private static void ApplyColor(FlagArrow a, Flag flag)
    {
        if (a.arrowImage == null || TeamManager.Instance == null) return;
        TeamData data = TeamManager.Instance.GetTeamData(flag.OwningTeamEnum);
        if (data != null) a.arrowImage.color = data.teamColor;
    }

    private static void SetActive(FlagArrow a, bool active)
    {
        if (a == null || a.arrow == null) return;
        if (a.arrow.gameObject.activeSelf != active)
            a.arrow.gameObject.SetActive(active);
    }
}
```

- [ ] **Step 2: Compile check**

In Unity, recompile. Expected: Console shows **no** errors, and `FlagDirectionHud`
appears as an addable component.

- [ ] **Step 3: Commit**

```bash
git add "Assets/Scripts/CTF Flag/FlagDirectionHud.cs" "Assets/Scripts/CTF Flag/FlagDirectionHud.cs.meta"
git commit -m "feat(ctf): add FlagDirectionHud screen-edge flag arrows"
```

---

### Task 5: Scene wiring (Unity editor)

**Files:**
- Modify: `Assets/Scenes/Gameplay.unity` (via the editor, not by hand)

This task is performed in the Unity editor because it creates UI GameObjects and
wires component references — hand-editing scene YAML for new `Image` objects is
error-prone (see the project's NetworkObject/fileID memory notes).

- [ ] **Step 1: Create the two arrow UI objects**

In the Gameplay scene Hierarchy, under the existing `Canvas` (the Screen-Space HUD
holding `NotificationText`):
- Create `UI > Image`, name it `Team1FlagArrow`.
- Set its RectTransform **anchor preset to middle-center** and **pivot to (0.5, 0.5)**.
- Assign an arrow sprite that points **right**; size it (e.g. 64x64).
- Duplicate it, name the copy `Team2FlagArrow`.
- Disable both (uncheck the GameObject active box) — `FlagDirectionHud` toggles them.

- [ ] **Step 2: Add and wire `FlagDirectionHud`**

- Select the `Canvas` GameObject and `Add Component > FlagDirectionHud`.
- `Team1Arrow`: drag `Team1FlagArrow` into `arrow`, and its `Image` into `arrowImage`.
- `Team2Arrow`: drag `Team2FlagArrow` into `arrow`, and its `Image` into `arrowImage`.
- Leave `viewCamera` empty (defaults to `Camera.main`) unless your gameplay camera
  is not tagged MainCamera; if so, drag it in.
- Leave `edgeMargin` at 60 (tune later).

- [ ] **Step 3: Remove the obsolete objects**

- Delete the world-space `Team1FlagIndicator` and `Team2FlagIndicator` GameObjects.
- If the scene had dedicated `team1FlagStatusText` / `team2FlagStatusText` labels
  that are no longer wanted, delete them too (the manager no longer references them;
  the removed serialized fields leave only harmless orphan YAML keys on the
  `CTFGameManager` component, which Unity drops on next save).

- [ ] **Step 4: Confirm the carrier head-icon prefab is wired**

- Select the player prefab; on its `FlagCarrierMarker`, confirm `flagIconPrefab`
  is assigned and `iconHeight` is set (e.g. 2). (Unchanged from before; just verify
  the rebuild kept the reference.)

- [ ] **Step 5: Save the scene**

File > Save (Ctrl+S). Expected: no console errors; `Gameplay.unity` shows as
modified in `git status`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scenes/Gameplay.unity
git commit -m "feat(ctf): wire FlagDirectionHud arrows, remove old flag indicators"
```

---

### Task 6: Manual verification (host + client)

**Files:** none

- [ ] **Step 1: Play as host + client**

Enter Play mode as host and connect a second client (or a second editor/build).
Assign players to opposite teams.

- [ ] **Step 2: Verify pickup feedback**

- A player steals a flag → the center-screen notification flashes on **both** peers.
- While carried → the flag icon floats above the carrier's head on **both** peers.
- On drop/return → the head icon disappears on both peers.

- [ ] **Step 3: Verify the arrows**

For each flag:
- At home → no arrow.
- Carried/dropped but **visible on your screen** → no arrow.
- Carried/dropped and **off your screen** → arrow appears pinned to the screen edge,
  pointing toward the flag, and tracks correctly as you move.
- Arrow color matches the flag's owning team (Team1=blue, Team2=red per `TeamData`).

- [ ] **Step 4: Note any tuning**

Adjust `edgeMargin` and arrow sprite size to taste. No code change expected.
```
