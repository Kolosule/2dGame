using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;
using Game.Buffs.Core;

/// <summary>
/// One buff icon. Tier drives colour/glow AND an exact pip row; a progress fill shows how close
/// the next tier is, so a player can see what a deposit run is worth before making it. Active
/// abilities (dash, stealth) keep their per-frame radial cooldown sweep.
///
/// Everything repaints off PlayerBuffs.BuffsChanged (which Scope 1 also raises on phase changes),
/// never by polling. Tier-ups are detected client-side here, inside that repaint.
/// </summary>
public class BuffIconDisplay : MonoBehaviour, IHudBindable
{
    [Header("Identity")]
    [SerializeField] private BuffId buffId;

    [Tooltip("Fallback max tier used only if the loadout config is unavailable.")]
    [SerializeField] private int maxTier = 3;

    [Tooltip("Name used in the unlock toast, e.g. \"Flag Runner\".")]
    [SerializeField] private string displayName = "Buff";

    [Header("Icon color/glow")]
    [Tooltip("Main icon image whose color is lerped by tier.")]
    [SerializeField] private Image icon;
    [SerializeField] private Color lockedColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color accentColor = Color.yellow;

    [Header("Tier pips (index 0 = tier 1). Exact tier, not inferred from colour.")]
    [SerializeField] private Image[] pips;
    [SerializeField] private Color pipFilledColor = Color.white;
    [SerializeField] private Color pipEmptyColor = new Color(1f, 1f, 1f, 0.18f);

    [Header("Next-unlock progress")]
    [Tooltip("Image Type = Filled. fillAmount tracks progress toward this buff's next tier.")]
    [SerializeField] private Image nextUnlockFill;

    [Header("Cooldown radial (dash / stealth only)")]
    [Tooltip("Image Type = Filled, Radial. fillAmount 1 = ready. Leave null for passive buffs.")]
    [SerializeField] private Image cooldownRadial;

    [Header("Unlock toast")]
    [SerializeField] private HudToastFeed toastFeed;

    private PlayerBuffs buffs;
    private PlayerMovement movement;
    private TierUpEdge tierEdge;

    public void Bind(HudContext ctx)
    {
        buffs = ctx.Buffs;
        movement = ctx.Inventory != null ? ctx.Inventory.GetComponent<PlayerMovement>() : null;
        if (buffs != null)
        {
            buffs.BuffsChanged += RepaintTier;
            buffs.StealthStateChanged += RepaintTier;
        }
        // The first RepaintTier primes the edge detector, so binding (and a late joiner arriving
        // already at tier 3) never toasts.
        RepaintTier();
    }

    public void Unbind()
    {
        if (buffs != null)
        {
            buffs.BuffsChanged -= RepaintTier;
            buffs.StealthStateChanged -= RepaintTier;
        }
        buffs = null;
        movement = null;
        tierEdge.Reset();
    }

    private void RepaintTier()
    {
        if (buffs == null) return;

        int tier = buffs.TierOf(buffId);
        int max = buffs.MaxTier > 0 ? buffs.MaxTier : maxTier;

        if (icon != null)
            icon.color = Color.Lerp(lockedColor, accentColor, BuffTierVisual.Intensity01(tier, max));

        TierPipRow.Paint(pips, tier, max, pipFilledColor, pipEmptyColor);

        if (nextUnlockFill != null)
            nextUnlockFill.fillAmount = buffs.NextUnlockProgress01(buffId);

        // Sudden Death maxes every tier at once; the banner announces that, so a burst of four
        // toasts would be noise rather than information.
        bool suddenDeath = MatchManager.Instance != null && MatchManager.Instance.AllBuffsMaxed;
        if (tierEdge.Observe(tier) && !suddenDeath && toastFeed != null)
            toastFeed.Show($"{displayName}  T{tier}");
    }

    private void Update()
    {
        if (buffs == null || cooldownRadial == null) return;

        if (buffId == BuffId.QuickerDash && movement != null)
            cooldownRadial.fillAmount = movement.GetDashCooldownPercent();
        else if (buffId == BuffId.Stealth)
            cooldownRadial.fillAmount = buffs.StealthCooldownFill01();
    }

    private void OnDisable() => Unbind();
}
