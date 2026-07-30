# Coins → Buffs Economy — Scope Prompts

Ready-to-paste prompts for four independently-landable scopes from
[the design spec](specs/2026-07-29-coins-buffs-economy-design.md). Each is
self-contained: a fresh session needs nothing from the brainstorming conversation.

**Run them in order.** Scope 1 is independent. Scope 2 is independent of 1 but must
land before Scope 4's team-side HUD. Scope 3 is independent of 1 and 2. Scope 4
depends on 2 and 3 for the state it displays.

Each prompt already tells the model to write a plan first via
`superpowers:writing-plans`, per house workflow, so paste and go.

---

## Scope 1 — Win-condition boundary + Sudden Death

Smallest change, highest value: it makes the game's stated win condition actually
true. Start here.

```
Implement Scope 1 of docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md
in the 2dGame repo: the win-condition boundary and the new Sudden Death phase.

Read the spec first — specifically the "Win-condition boundary" section and decision
rows 10 and 11. Then follow the house workflow: invoke superpowers:writing-plans to
produce an implementation plan, get my approval on it, then implement.

SCOPE — exactly these changes:

1. Remove the coin-score tiebreak. Coins must not decide or tiebreak a match.
   - Delete MatchManager.ResolveByTimer() (Assets/Scripts/Match/MatchManager.cs:161-168).
   - Delete MatchResolver.ResolveTimerWinner (Assets/Scripts/Match/Core/MatchResolver.cs)
     and its EditMode test case ResolveTimerWinner_HigherScoreWins_EqualIsDraw in
     Assets/Tests/EditMode/Match/MatchResolverTests.cs.
   - Keep MatchResolver.WinnerLabel — it still formats the results banner.

2. Delete GameSettingsManager.scoreLimit
   (Assets/Scripts/ScriptableObjects/Game Settings Manager.cs:49). It has zero
   references. Do not repurpose it.

3. Add MatchPhase.SuddenDeath as a sixth phase to the enum at
   Assets/Scripts/Match/MatchManager.cs:7.
   - Live timer expiry transitions to SuddenDeath instead of resolving a winner.
   - SuddenDeath arms NO timer (TickTimer.None), except via the optional hard cap below.
   - A capture during SuddenDeath ends the match exactly as a Live capture does:
     ReportCapture sets Winner and enters PostMatch. Widen ReportCapture's phase guard
     from "Phase != Live" to accept Live OR SuddenDeath.

4. In SuddenDeath, every player has every individual buff at max tier and both teams
   have every team buff at max tier. Implement this as a READ-TIME OVERRIDE on tier
   resolution — PlayerBuffs.TierOf (Assets/Scripts/Buffs/PlayerBuffs.cs:90) returns
   MaxTier when MatchManager.Phase == SuddenDeath. Do NOT add per-player networked
   state, and do NOT mutate TotalDepositedValue. Tiers in this project are derived,
   never stored, so there is nothing to reset and nothing to replay on resimulation.
   Preserve that property.

5. Add a suddenDeathHardCap setting, DEFAULT 0 = off. When an operator sets it and it
   elapses, resolve as a draw (Winner = 0) and enter PostMatch, so a headless
   dedicated server cannot wedge on an unwinnable match. This is an ops safety valve;
   draws must be unreachable in default play.

OUT OF SCOPE — do not touch: the territorial damage system, TeamScoreManager, the
individual buff catalog, coin drop rates, or any HUD work beyond what's needed to not
break the existing results panel. A SuddenDeath HUD banner is Scope 4.

PROJECT CONVENTIONS (non-negotiable):
- Photon Fusion 2 Host/Client + dedicated server. All phase transitions decided ONLY
  under HasStateAuthority, inside FixedUpdateNetwork.
- Simulation-path timing via TickTimer only. No Time.time in simulation.
- Replicated state is [Networked] with OnChangedRender for render reactions. Clients
  and late joiners must derive the correct phase from networked state, never from a
  missed RPC.
- Runner.Spawn/Despawn only under HasStateAuthority.
- MatchManager must stay always-interested under interest management, or the phase
  and results vanish for distant players in a 20-player match.

VERIFICATION: add EditMode tests (pure C#, runnable outside Unity via the bundled-
Roslyn workaround if the editor holds the project lock) covering the Sudden Death tier
override returning MaxTier regardless of deposited value, and the phase guard
accepting captures in Live and SuddenDeath but not in Countdown or PostMatch. Then
tell me exactly what you verified and what still needs manual in-editor play. Do not
claim the work is verified on the basis of a clean compile.
```

---

## Scope 2 — Territory rework + Vanguard team buff

The design's biggest behavioural change. Replaces a 9× invisible damage swing with a
3× two-state debuff, and replaces the two hollow team booleans with one derived
tiered buff.

```
Implement Scope 2 of docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md
in the 2dGame repo: the territorial combat rework and the Vanguard team buff.

Read the spec first — the "Territorial combat", "Team buff catalog", "Team thresholds
are per-player-average" and "Team curve" sections, plus audit findings 2, 3 and 4.
Then follow the house workflow: invoke superpowers:writing-plans, get my approval on
the plan, then implement.

SCOPE — exactly these changes:

1. Replace the lerped two-sided territorial model with ONE debuff on damage dealt.
   Currently the modifiers compound: at Team1's base, Team1 attacking Team2 resolves
   to 1.5 x 1.5 = 2.25x while Team2 attacking Team1 resolves to 0.5 x 0.5 = 0.25x.
   That 9x swing is invisible to players and was never tuned.
   - Reuse TeamManager.GetTerritorialAdvantage (Assets/Scripts/Teams/TeamManager.cs:76-94)
     unchanged as the INPUT. Only quantize its output.
   - Two zones: advantage < -0.33 is the enemy third and takes the debuff; anything
     >= -0.33 is clear at x1.00. Two discrete states, not a gradient — the whole point
     is that it can be shown as an icon.
   - Full debuff is x0.33 damage dealt. Total swing 3x.
   - DELETE the received-side entirely: TeamManager.GetDamageReceivedModifier, and the
     receivedModifier parameter threaded through CombatConfig.CalculateFinalDamage and
     ResolveDamage (Assets/Scripts/ScriptableObjects/CombatConfig.cs:76-128). One
     debuff, one side, one direction.
   - Note while you work: today the modifiers are only applied at all inside
     "if (territorialAdvantageEnabled)" at CombatConfig.cs:80-83, which is why the old
     team buffs were literally no-ops with that flag false. Keep the flag if useful,
     but the new debuff must not silently vanish without it being obvious.

2. Replace the two team booleans with ONE derived, tiered team buff called Vanguard.
   - Delete Team1DamageBuff / Team2DamageBuff / Team1DefenseBuff / Team2DefenseBuff,
     damageBuffThreshold, defenseBuffThreshold, and CheckMilestones from
     Assets/Scripts/Coin Scripts/TeamScoreManager.cs:20-157.
   - Also delete the onDamageBuffUnlocked / onDefenseBuffUnlocked UnityEvents
     (TeamScoreManager.cs:32-33). They fire inside a HasStateAuthority guard, so on a
     dedicated server they fire headless where no client can ever observe them, and
     nothing subscribes to them. Client-visible feedback goes through OnChangedRender.
   - Vanguard has TWO tiers: T1 removes 50% of the debuff (x0.67), T2 removes 100%
     (x1.00). Resolve as: dealt = 1 - 0.67 * (1 - 0.5 * tier), giving exactly
     0.33 / 0.67 / 1.00 at tiers 0 / 1 / 2.
   - Derive the team tier through the SAME pure helper the individual layer uses,
     Assets/Scripts/Buffs/Core/BuffUnlock.cs, with buffCount == 1 and maxTier == 2.
     Do not write a parallel tier mechanism.

3. Team thresholds are PER-PLAYER-AVERAGE deposited value: {75, 150}, compared against
   teamScore / TeamRosterSize. They are NOT absolute team scores — against absolute
   score a 10-player team would cross both in the opening minute, which is the exact
   failure mode of the old 50/100 numbers.
   - Capture TeamRosterSize ONCE as [Networked] state on entering MatchPhase.Live and
     use it as the divisor for the rest of the match. This keeps tier derivation pure:
     roster churn cannot retroactively unlock or revoke a tier, so no monotonic latch
     and no stored tier state is needed.

Be aware of the intended consequence, and do not "fix" it: at target pacing a TYPICAL
team never unlocks Vanguard and fights the full x0.33 debuff in the enemy third all
match. T1 needs a strong team, T2 a dominant one. That steepness is deliberate.

OUT OF SCOPE — do not touch: the individual buff catalog, coin drop rates, any HUD
work (Scope 4), match phases (Scope 1), and ANY respawn timing. In particular do NOT
collapse the dead GameSettingsManager.GetRespawnTime / respawnTimeMultiplier /
TeamData.respawnDelay path — it is a real dead path but nothing here touches respawns,
so cleaning it up would be unrelated refactoring.

PROJECT CONVENTIONS (non-negotiable):
- CombatConfig.ResolveDamage is THE single entry point for all combat damage. Both
  PlayerCombat and Enemy route through it. Keep it that way.
- [Networked] + OnChangedRender for replicated state; TickTimer for simulation timing;
  Runner.Spawn/Despawn and all authoritative writes only under HasStateAuthority.
- TeamScoreManager must stay always-interested under interest management.
- Note the pre-existing cheat surface: RPC_AddPoints is RpcSources.All, so any client
  can inflate team score. Do NOT fix it here — it wants its own security pass — but do
  not make it worse.

VERIFICATION: add EditMode tests (pure C#, runnable outside Unity) covering the
Vanguard formula producing exactly 0.33 / 0.67 / 1.00 at tiers 0/1/2; zone
classification at the -0.33 boundary from both sides; and team threshold
normalisation against {75, 150} including rosterSize of 1, an empty team, and exact
boundary values of 75 and 150. Report what you actually ran. A clean compile is not
verification.
```

---

## Scope 3 — Individual layer: explicit MaxTier, 12-step curve, Flag Runner

```
Implement Scope 3 of docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md
in the 2dGame repo: the individual buff layer — a fourth buff, an explicit MaxTier,
and the new 12-step progression curve.

Read the spec first — "Individual buff catalog", "Loadout UX", "MaxTier becomes
explicit", and the "Individual curve" section. The earlier
docs/superpowers/specs/2026-06-24-deposit-earned-buffs-design.md describes the
existing three buffs and is still accurate for them. Then follow the house workflow:
invoke superpowers:writing-plans, get my approval, then implement.

SCOPE — exactly these changes:

1. Fix the MaxTier footgun. BuffLoadoutConfig.MaxTier is currently
   "thresholds.Length / BuffCount" (Assets/Scripts/Buffs/BuffLoadoutConfig.cs:24), so
   adding a fourth buff without three more thresholds silently drops EVERY buff from
   tier 3 to tier 2. Make MaxTier an authored serialized field and validate
   thresholds.Length == maxTier * buffCount, failing loudly.

2. Add a fourth individual buff, Flag Runner (BuffId.FlagRunner), passive, following
   the existing BuffDefinition subclass pattern in Assets/Scripts/Buffs/
   (JumpBuffDefinition / DashBuffDefinition / StealthBuffDefinition):
   - T1: +10% move speed while carrying the enemy flag
   - T2: +20% move speed while carrying
   - T3: +20% AND dash permitted while carrying the flag
   Two hooks, both of which already exist — do not invent new mechanics:
   - A new EffectiveStats.CarrySpeedMultiplier field
     (Assets/Scripts/Buffs/Core/EffectiveStats.cs), surfaced through
     PlayerStatModifiers.cs and consumed in PlayerMovement's speed calculation.
   - T3 lifts the EXISTING carry-blocks-dash gate at
     Assets/Scripts/Player/PlayerMovement.cs:126-135. Note that gate deliberately
     reads carrying state from networked flag state via CTFGameManager.IsCarrying,
     NOT from the render-path FlagCarrierMarker bool, because it runs in simulation.
     Preserve that.
   Flag Runner's T3 intentionally mirrors Stealth's T3 (UsableWhileCarryingFlag) so
   "top tier lifts the flag restriction" is consistent language across the catalog.

3. Replace the threshold curve at BuffLoadoutConfig.cs:15 with the 12-step curve
   (4 buffs x 3 tiers):
       5, 10, 16, 24,  34, 46, 62, 80,  110, 150, 200, 260
   Expected outcomes under the round-robin, which you should assert in tests:
       55 banked  ->  6 steps  ->  #1,#2 at T2; #3,#4 at T1
      120 banked  ->  9 steps  ->  #1 at T3; #2,#3,#4 at T2
      220 banked  -> 11 steps  ->  #1,#2,#3 at T3; #4 at T2
      260 banked  -> 12 steps  ->  all T3

4. Extend the lobby loadout picker from 3 entries to 4. It is a REORDER picker, not a
   subset picker — all buffs are always equipped, only the priority order is chosen.
   Default order [ExtraJump, Stealth, QuickerDash, FlagRunner]. Loadout stays optional
   at the start gate; a missing choice takes the default. The existing plumbing is
   LobbyLoadoutChoices -> NetworkedSpawnManager -> PlayerBuffs.ServerInitLoadout.

DESIGN CONSTRAINTS — do not add buffs outside these:
- Movement and utility ONLY. No attack damage, no max health, no base move speed. A
  player who out-farms you should get harder to catch, not able to two-shot you; this
  caps the snowball at mobility.
- Stealth remains the ONLY active ability. Everything else is passive. Do not add
  input bindings or new active-ability timers.

OUT OF SCOPE: territory and team buffs (Scope 2), match phases (Scope 1), HUD work
(Scope 4), coin drop rates (Scope 4).

PROJECT CONVENTIONS (non-negotiable):
- Tiers are DERIVED on query from [Networked] TotalDepositedValue + LoadoutOrder via
  BuffUnlock — never stored as independent networked state. This is what makes the
  system resimulation-safe. Preserve it: add no per-buff networked tier fields.
- PlayerStatModifiers must NEVER mutate the shared PlayerStats ScriptableObject.
- Gameplay in Simulate(), visuals in Render(). TickTimer for simulation timing.
- New Input System only; no Keyboard.current reads in simulation paths.
- The player prefab has TWO Animators — GetComponentInChildren<Animator>() returns the
  weapon one, not the visible body. Wire body renderers/animators explicitly if you
  touch visuals.

VERIFICATION: add EditMode tests (pure C#, runnable outside Unity) for BuffUnlock step
and tier math against the 12-step curve including the 4-buff round-robin, the four
expected outcomes listed above, the MaxTier validation rule failing loudly, and
loadout byte round-tripping with 4 entries. Report what you ran; a clean compile is
not verification.
```

---

## Scope 4 — Feedback surfaces + deterministic coin supply

```
Implement Scope 4 of docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md
in the 2dGame repo: the two HUD feedback surfaces and deterministic enemy coin drops.

Read the spec first — "Feedback surfaces" and "Deterministic coin supply", plus audit
finding 6. Scopes 2 and 3 must already be landed, since this displays their state.
Then follow the house workflow: invoke superpowers:writing-plans, get my approval,
then implement.

SCOPE — exactly these changes:

1. Make enemy coin drops deterministic so pacing is computable. Enemy.SpawnCoins
   currently rolls Random.Range(coinsToDropMin, coinsToDropMax + 1) with 1-3
   (Assets/Scripts/Enemy/Base/Enemy.cs:25-26 and :332).
   - Replace the min/max pair with a single authored "coinsToDrop" int per enemy
     archetype prefab. No randomness. Stronger archetypes drop more.
   - Add an integer "coinDropBonus" to RingTier in
     Assets/Scripts/Enemy/AI/DifficultyRingConfig.cs (it currently carries only
     healthMult / damageMult / speedMult), so enemies nearer the hazardous centre pay
     out more. Keep it an int so total supply stays exactly computable.
   - Starting values to tune in play: base 2 for the weakest archetype rising to 4 for
     the strongest; ring bonus +0 outer / +1 middle / +2 inner. Coin point value stays 1.
   - Total deposited value for a player then equals kills x (coinsToDrop + ringBonus),
     minus losses to death and the 30s coin lifetime.

2. Build the merged Team Power strip — territory and team buffs on ONE surface,
   because they are one subject: how strong is my team's position right now.
   - Team scores with a next-milestone tick on the bar.
   - A zone indicator (two states: clear / enemy third) whose DISPLAYED state already
     folds in the team's unlocked Vanguard tier. This folding is the entire point: a
     player pushing into enemy turf watches the indicator stop reading as penalised
     once the team unlocks Vanguard, and learns the buff from the thing it changes.
   - Replace TeamScoreDisplay's single undifferentiated buff badge
     (Assets/Scripts/Hud/TeamScoreDisplay.cs:62), which today cannot even distinguish
     damage from defense.
   - Evaluate the zone for the LOCAL PLAYER ONLY and fire on band CHANGE, not per
     frame. With two discrete states changes are rare, so this must cost nothing at
     20 players.

3. Extend the individual buff row — its own separate surface. Keep
   BuffIconDisplay's existing tier colour lerp and its radial cooldown sweep for
   Stealth and Dash (Assets/Scripts/Hud/BuffIconDisplay.cs), and add:
   - Tier pips (e.g. filled/empty dots) so the current tier is readable exactly rather
     than inferred from a colour.
   - A next-unlock progress fill driven by TotalDepositedValue against the next
     threshold, so a player can see what a deposit run is worth before making it.
   Both repaint off the existing PlayerBuffs.BuffsChanged event.

4. Unlock toasts. Tier-ups are discrete EVENTS and need a transient notification, for
   both the individual row and the team strip. Detect them CLIENT-SIDE inside
   OnChangedRender by comparing previous to new tier. Do not reintroduce server-side
   UnityEvents for this — the ones Scope 2 deleted fired behind a HasStateAuthority
   guard, so on a dedicated server they fired headless where no client could see them.

5. A Sudden Death banner: "SUDDEN DEATH - all buffs unlocked - next capture wins". The
   two surfaces above need no special case for it — the buff row shows every pip
   filled and the team strip shows the debuff lifted, both derived from
   MatchManager.Phase like everything else.

OUT OF SCOPE: tuning the debuff magnitude or the threshold curves (they are set in
Scopes 2 and 3), match phase logic (Scope 1), and the per-player stats scoreboard
(K/D, captures, coins) which is a separate item.

PROJECT CONVENTIONS (non-negotiable):
- HUD is EVENT-DRIVEN under Assets/Scripts/Hud/, never polling. Subscribe to
  PlayerBuffs.BuffsChanged, TeamScoreManager.ScoresChanged, MatchManager.PhaseChanged.
  Follow the existing IHudBindable Bind/Unbind pattern and unsubscribe in Unbind.
- Runtime-singleton managers may not exist yet when a HUD element binds; TeamScoreDisplay
  already shows the correct lazy-subscribe pattern for that.
- Visuals in Render(); never drive gameplay from HUD code.
- Managers, flags and the carrier must be always-interested under interest management,
  or the HUD vanishes for distant players in a 20-player match.

VERIFICATION: EditMode tests for whatever pure logic you extract (zone-to-display
mapping including the Vanguard fold, next-threshold progress math, tier-up edge
detection). The surfaces themselves need manual in-editor play: confirm the zone
indicator visibly changes meaning when Vanguard unlocks, that pips and progress track
deposits, and that toasts fire once per tier-up rather than on every deposit. Tell me
which of these you actually observed versus inferred.
```
