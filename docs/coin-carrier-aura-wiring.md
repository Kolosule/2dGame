# Coin Carrier Aura — Editor Wiring

One-time prefab wiring for the carrier glow (code: `Assets/Scripts/Coin Scripts/CoinCarrierAura.cs`).

1. Open the PlayerPrefab.
2. Add a child GameObject named `CoinAura` under the prefab root.
   - Add a `SpriteRenderer`.
   - Sprite: `Knob` (built-in soft radial circle; search "Knob" with the type filter set to
     Sprite and "Search: All"). Any soft radial glow sprite works if art provides one later.
   - Color: warm gold `#FFC64B`, alpha irrelevant (script drives it).
   - Sorting Layer: SAME as the visible body sprite; Order in Layer: body's order MINUS 1
     (the glow must render behind the body).
   - Local position (0, 0, 0). Leave scale at 1 — the script drives it per tier.
3. Add the `CoinCarrierAura` component to the prefab ROOT (next to FlagCarrierMarker).
   - Drag the `CoinAura` SpriteRenderer into its `auraRenderer` field.
4. Defaults: thresholds 5/15/30 (TotalCoinValue), alphas 0.25/0.45/0.7, scales 1.2/1.5/1.9.
   Retune in playtests.

Verify in a two-peer session: pick up coins past a threshold -> glow appears on BOTH peers;
activate Stealth -> glow disappears for everyone; die -> coins drop and glow clears.
