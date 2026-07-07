# Player HUD — Canvas Wiring Guide (Task 12, Step 3)

Detailed, click-by-click companion for rebuilding the in-match HUD Canvas in
`Assets/Scenes/Gameplay.unity`. Field names below match the components exactly
(all are `[SerializeField] private`, so they show in the Inspector).

Buff ids (`Game.Buffs.Core.BuffId`): `ExtraJump`, `Stealth`, `QuickerDash`.

---

## 0. Prep

1. Open `Assets/Scenes/Gameplay.unity`.
2. If Unity prompts on first TextMeshPro use, click **Window ▸ TextMeshPro ▸ Import TMP Essential Resources**.
3. In the Hierarchy, find the existing HUD **Canvas** (the one that held the old `UIManager`).
   - Select it, and in the Inspector remove the now-missing `UIManager` script (the
     component shows as "Missing (Mono Script)" once `UIManager.cs` is deleted — click the
     3-dot menu ▸ **Remove Component**).
   - Confirm the Canvas has **Canvas Scaler** set to **Scale With Screen Size** (Reference
     Resolution e.g. 1920×1080) so the HUD scales across resolutions.
4. Delete the old HUD child objects that the `UIManager` drove (old health slider, coin texts,
   dash radial, the 4 team-buff icons). Leave the flag HUD objects (directional arrow, carrier
   icon, notification text) untouched.

**Anchoring tip:** with a RectTransform selected, click the anchor-preset box (top-left of the
RectTransform inspector) and **Alt+click** a preset to set both anchors and position at once.

---

## 1. PlayerHud root

1. Right-click the **Canvas** ▸ **Create Empty**. Rename it `PlayerHud`.
2. Set its RectTransform to stretch-fill the Canvas: Alt+click the **bottom-right stretch** preset
   (the one that stretches both axes), then set Left/Right/Top/Bottom = 0.
3. **Add Component ▸ Player Hud**. Leave the `Displays` array empty for now — you fill it in Step 8.

Everything below is created as a child of `PlayerHud`.

---

## 2. Bottom cluster container

1. Right-click `PlayerHud` ▸ **Create Empty**, rename `BottomCluster`.
2. RectTransform: Alt+click the **bottom-stretch** preset (anchors bottom-left→bottom-right).
   Set Height ≈ 90, Pos Y ≈ 50 (lifts it off the screen edge), Left/Right ≈ 40.
3. **Add Component ▸ Horizontal Layout Group**:
   - Spacing ≈ 24, Child Alignment = **Middle Left**.
   - Uncheck **Control Child Size Width/Height** (each sub-panel sizes itself).
4. *(Optional dark panel)* **Add Component ▸ Image**, color black at alpha ≈ 100/255, and
   **Add Component ▸ Content Size Fitter** or leave a fixed rect. This gives the "dark translucent
   panel" behind the cluster. Skip if you prefer floating elements.

You'll add three children to `BottomCluster`: Health, Coins, Buff row.

---

## 3. Health (segmented bar)

1. Right-click `BottomCluster` ▸ **Create Empty**, rename `Health`.
2. Add a **Horizontal Layout Group** (Spacing ≈ 4, Child Alignment = Middle Left, Control Child
   Size = on for both, so segments size evenly).
3. Create the first segment: right-click `Health` ▸ **UI ▸ Image**, rename `Seg`.
   - Set **Source Image** to any solid/rounded UI sprite (Unity's built-in `Background` works;
     or leave `None` for a plain filled rect).
   - **Image Type = Filled**, **Fill Method = Horizontal**, **Fill Origin = Left**, **Fill Amount = 1**.
   - RectTransform size ≈ 26×18 (Layout Group will space them).
4. Duplicate `Seg` (Ctrl+D) until you have **10** segments (`Seg (1)` … `Seg (9)`).
5. Select `Health`, **Add Component ▸ Health Segment Display**, and wire it:
   - **Segments**: set Size = 10, then drag `Seg`, `Seg (1)` … `Seg (9)` into elements **in
     left-to-right order** (order matters — element 0 = leftmost).
   - **Lit Color**: a health color, e.g. red `#E5484D` (or white if you prefer).
   - **Empty Color**: dim, e.g. white at alpha ≈ 40/255 (default is fine).
   - **Health Text** *(optional)*: create a **UI ▸ Text - TextMeshPro** child (e.g. `HpText`),
     drag it here for an "80 / 100" readout. Leave empty to omit.

> The component lights whole segments and shows a fractional fill on the one partial block; empty
> blocks render in Empty Color. If you want a visible "track" behind partial blocks, put a second
> dim Image behind each `Seg` — optional polish, not required.

---

## 4. Coins

1. Right-click `BottomCluster` ▸ **Create Empty**, rename `Coins`. Add a Horizontal Layout Group
   (Spacing ≈ 6, Middle Left).
2. *(Optional)* **UI ▸ Image** child `CoinIcon` with your coin sprite, tinted gold `#F5C518`,
   size ≈ 24×24.
3. **UI ▸ Text - TextMeshPro** child `CoinCount`. Set font size ≈ 28, color gold `#F5C518`,
   alignment Middle Left, text `0`.
4. *(Optional)* another TMP child `CoinValue` for total value.
5. Select `Coins`, **Add Component ▸ Coin Display**:
   - **Coin Count Text** → drag `CoinCount`.
   - **Coin Value Text** *(optional)* → drag `CoinValue`, or leave empty.

---

## 5. Buff row (3 icons)

Create one reusable icon, then duplicate. Each buff = its own `BuffIconDisplay`.

1. Right-click `BottomCluster` ▸ **Create Empty**, rename `Buffs`. Add a Horizontal Layout Group
   (Spacing ≈ 12, Middle Left).
2. Build one icon: right-click `Buffs` ▸ **Create Empty**, rename `BuffDash`, size ≈ 48×48.
   - Child **UI ▸ Image** `Icon` — your dash sprite; stretch to fill the 48×48 parent.
   - Child **UI ▸ Image** `Cooldown` — same sprite or a ring sprite; **Image Type = Filled**,
     **Fill Method = Radial 360**, **Fill Origin = Top**, **Clockwise** as you prefer,
     color a semi-transparent dark overlay. Stretch to fill.
   - Select `BuffDash`, **Add Component ▸ Buff Icon Display**:
     - **Buff Id = QuickerDash**
     - **Max Tier = 3**
     - **Icon** → drag the `Icon` child.
     - **Locked Color**: dim (default white α≈0.25 is fine).
     - **Accent Color**: dash accent, e.g. cyan `#3DD6D0`.
     - **Cooldown Radial** → drag the `Cooldown` child.
3. Duplicate `BuffDash` → rename `BuffJump`:
   - **Buff Id = ExtraJump**, **Accent Color** e.g. green `#59C36A`.
   - **Cooldown Radial → None** (jump has no cooldown). Delete its `Cooldown` child, or just clear
     the field.
4. Duplicate `BuffDash` again → rename `BuffStealth`:
   - **Buff Id = Stealth**, **Accent Color** e.g. purple `#A66CFF`, keep its `Cooldown` child wired.
5. Order in the row: `BuffDash`, `BuffJump`, `BuffStealth` (left→right, any order you like).

> Tier shows as icon brightness: locked (tier 0) ≈ Locked Color, tier 3 = full Accent Color.
> The radial fills to full (=1) when the ability is ready, sweeps down to 0 the instant it's used.

---

## 6. Team score + team-buff badge (top-right)

1. Right-click `PlayerHud` ▸ **Create Empty**, rename `TeamScore`. RectTransform: Alt+click the
   **top-right** preset; nudge Pos X ≈ -40, Pos Y ≈ -30.
2. Add a Horizontal Layout Group (Spacing ≈ 10, Upper Right) or just place texts manually.
3. **UI ▸ Text - TextMeshPro** child `Team1Score` (font ≈ 26, text `0`).
4. **UI ▸ Text - TextMeshPro** child `Team2Score` (font ≈ 26, text `0`).
5. **UI ▸ Image** child `TeamBuffBadge` — a small emblem sprite. **Disable it** (uncheck the top-left
   checkbox in the Inspector) so it starts hidden; the component shows it only when your team's buff
   is active.
6. Select `TeamScore`, **Add Component ▸ Team Score Display**:
   - **Team 1 Score Text** → `Team1Score`.
   - **Team 2 Score Text** → `Team2Score`.
   - **Team Buff Badge** → drag the `TeamBuffBadge` **GameObject** (not the Image).
   - (There is no team field to set — the component reads your team from the local player at bind.)

---

## 7. TeamScoreManager reminder

`TeamScoreDisplay` finds `TeamScoreManager.Instance` at runtime, so no scene reference is needed —
**but** confirm the Gameplay scene still has its `TeamScoreManager` GameObject (unchanged by this
work). If it was on the same object as the old `UIManager`, make sure you only removed the
`UIManager` component, not the whole object.

---

## 8. Wire PlayerHud.displays

1. Select the `PlayerHud` object.
2. On the **Player Hud** component, set **Displays** Size = **6**.
3. Drag these six components (drag the *GameObject*; Unity picks the `IHudBindable` component) into
   the elements, in any order:
   - `Health` (HealthSegmentDisplay)
   - `Coins` (CoinDisplay)
   - `BuffDash` (BuffIconDisplay)
   - `BuffJump` (BuffIconDisplay)
   - `BuffStealth` (BuffIconDisplay)
   - `TeamScore` (TeamScoreDisplay)

> If an object has multiple components, drag it onto the slot and Unity assigns the matching one.
> If it grabs the wrong type, click the slot's picker and choose the specific component.

---

## 9. Sanity checks before Play

- Each `Seg` Image has **Image Type = Filled / Horizontal**; each buff `Cooldown` is **Filled /
  Radial 360**.
- `HealthSegmentDisplay.Segments` has exactly 10 entries, left-to-right.
- `BuffIconDisplay` buff ids are set (Dash=QuickerDash, Jump=ExtraJump, Stealth=Stealth); jump's
  Cooldown Radial is empty.
- `TeamBuffBadge` GameObject starts **disabled**.
- `PlayerHud.Displays` has all 6 entries.
- No console errors referencing a missing `UIManager` script.

Then proceed to Step 4 (run EditMode tests) and Steps 5–6 (Play-mode + multi-peer verify) in the
main plan.
