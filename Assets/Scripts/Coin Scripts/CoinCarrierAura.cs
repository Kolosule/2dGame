using UnityEngine;
using Game.Hud.Core;

/// <summary>
/// World-space carrier aura: a soft glow behind the body sprite whose intensity tiers up
/// with the networked TotalCoinValue (spec Part 3). PURELY VISUAL — same role split as
/// FlagCarrierMarker. Hidden while stealthed, on every viewer (a stealthed coin-runner goes
/// dark). Death needs no special case: coins drop on death -> value 0 -> tier 0 -> aura off.
/// The aura renderer is a separate child sprite sorted BEHIND the body, so it cannot fight
/// the hit-flash, death-dim, or stealth transparency (which all touch the body sprite).
/// </summary>
public class CoinCarrierAura : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Child glow SpriteRenderer, sorted behind the body sprite. See docs/coin-carrier-aura-wiring.md")]
    [SerializeField] private SpriteRenderer auraRenderer;

    [Header("Tiers (TotalCoinValue thresholds, ascending)")]
    [SerializeField] private int[] tierThresholds = { 5, 15, 30 };
    [Tooltip("Aura alpha per tier (index 0 = tier 1)")]
    [SerializeField] private float[] tierAlphas = { 0.25f, 0.45f, 0.7f };
    [Tooltip("Aura local scale per tier (index 0 = tier 1)")]
    [SerializeField] private float[] tierScales = { 1.2f, 1.5f, 1.9f };

    [Header("Pulse")]
    [SerializeField] private float pulseHz = 0.8f;
    [Tooltip("Pulse speed at the top tier")]
    [SerializeField] private float topTierPulseHz = 2f;
    [SerializeField, Range(0f, 1f)] private float pulseFraction = 0.2f;

    private NetworkedPlayerInventory inventory;
    private PlayerBuffs buffs;
    private int tier;

    private void Awake()
    {
        if (DedicatedServerPresentation.IsHeadless)
        {
            enabled = false;
            return;
        }

        inventory = GetComponent<NetworkedPlayerInventory>();
        buffs = GetComponent<PlayerBuffs>();
    }

    private void OnEnable()
    {
        if (DedicatedServerPresentation.IsHeadless) return;
        if (inventory != null) inventory.CoinsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.CoinsChanged -= Refresh;
    }

    private void Refresh()
    {
        tier = inventory != null ? AuraTiers.Resolve(inventory.TotalCoinValue, tierThresholds) : 0;
        if (auraRenderer != null && tier > 0)
        {
            int idx = Mathf.Clamp(tier - 1, 0, tierScales.Length - 1);
            auraRenderer.transform.localScale = Vector3.one * tierScales[idx];
        }
    }

    private void Update()
    {
        if (auraRenderer == null) return;

        bool stealthed = buffs != null && buffs.IsStealthed;
        bool visible = tier > 0 && !stealthed;
        if (auraRenderer.enabled != visible) auraRenderer.enabled = visible;
        if (!visible) return;

        int idx = Mathf.Clamp(tier - 1, 0, tierAlphas.Length - 1);
        float hz = tier >= tierThresholds.Length ? topTierPulseHz : pulseHz;
        float pulse = 1f - pulseFraction * (0.5f + 0.5f * Mathf.Sin(Time.time * hz * 2f * Mathf.PI));
        Color c = auraRenderer.color;
        c.a = tierAlphas[idx] * pulse;
        auraRenderer.color = c;
    }
}
