using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;
using Game.Buffs.Core;

/// <summary>
/// One buff icon. Tier drives color/glow (event-driven via PlayerBuffs.BuffsChanged); active
/// abilities (dash, stealth) also show a per-frame radial cooldown sweep. maxTier is read from
/// the loadout config, defaulting to 3.
/// </summary>
public class BuffIconDisplay : MonoBehaviour, IHudBindable
{
    [Header("Identity")]
    [SerializeField] private BuffId buffId;
    [SerializeField] private int maxTier = 3;

    [Header("Icon color/glow")]
    [Tooltip("Main icon image whose color is lerped by tier.")]
    [SerializeField] private Image icon;
    [SerializeField] private Color lockedColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color accentColor = Color.yellow;

    [Header("Cooldown radial (dash / stealth only)")]
    [Tooltip("Image Type = Filled, Radial. fillAmount 1 = ready. Leave null for passive buffs.")]
    [SerializeField] private Image cooldownRadial;

    private PlayerBuffs buffs;
    private PlayerMovement movement;

    public void Bind(HudContext ctx)
    {
        buffs = ctx.Buffs;
        movement = ctx.Inventory != null ? ctx.Inventory.GetComponent<PlayerMovement>() : null;
        if (buffs != null)
        {
            buffs.BuffsChanged += RepaintTier;
            buffs.StealthStateChanged += RepaintTier;
        }
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
    }

    private void RepaintTier()
    {
        if (buffs == null || icon == null) return;
        int tier = buffs.TierOf(buffId);
        float intensity = BuffTierVisual.Intensity01(tier, maxTier);
        icon.color = Color.Lerp(lockedColor, accentColor, intensity);
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
