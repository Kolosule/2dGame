using UnityEngine;
using UnityEngine.UI;
using Game.Hud.Core;

/// <summary>
/// Paints a row of tier pips: pips below the current tier are filled, the rest are empty, and
/// pips past the buff's max tier are hidden entirely. Shared by the individual buff row and the
/// Team Power strip so the two surfaces read identically and the loop exists in one place.
///
/// Lives in Assembly-CSharp rather than beside BuffTierVisual.PipFilled in Game.Hud.Core because
/// it touches UnityEngine.UI.Image and that assembly is noEngineReferences. The decision
/// (which pips are filled) stays pure there; only the painting is here.
/// </summary>
public static class TierPipRow
{
    public static void Paint(Image[] pips, int tier, int maxTier, Color filled, Color empty)
    {
        if (pips == null) return;

        for (int i = 0; i < pips.Length; i++)
        {
            if (pips[i] == null) continue;
            pips[i].gameObject.SetActive(i < maxTier);
            pips[i].color = BuffTierVisual.PipFilled(i, tier) ? filled : empty;
        }
    }
}
