using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Game.Hud.Core;

/// <summary>
/// Segmented health bar. Discrete blocks light up whole; the block above the last full one
/// shows a fractional fill. Event-driven — repaints only on PlayerStatsHandler.HealthChanged.
/// </summary>
public class HealthSegmentDisplay : MonoBehaviour, IHudBindable
{
    [Tooltip("One filled Image per segment, left-to-right. Image Type = Filled, Horizontal.")]
    [SerializeField] private Image[] segments;

    [Tooltip("Color of a lit segment.")]
    [SerializeField] private Color litColor = Color.white;

    [Tooltip("Color of an empty segment.")]
    [SerializeField] private Color emptyColor = new Color(1f, 1f, 1f, 0.15f);

    [Tooltip("Optional numeric readout, e.g. 80 / 100.")]
    [SerializeField] private TextMeshProUGUI healthText;

    private PlayerStatsHandler stats;

    public void Bind(HudContext ctx)
    {
        stats = ctx.Stats;
        if (stats == null) return;
        stats.HealthChanged += Repaint;
        Repaint();
    }

    public void Unbind()
    {
        if (stats != null) stats.HealthChanged -= Repaint;
        stats = null;
    }

    private void Repaint()
    {
        if (stats == null || segments == null || segments.Length == 0) return;

        float current = stats.GetCurrentHealth();
        float max = stats.GetMaxHealth();
        int count = segments.Length;

        int filled = HealthSegments.FilledSegments(current, max, count);
        float partial = HealthSegments.PartialFill01(current, max, count);

        for (int i = 0; i < count; i++)
        {
            Image seg = segments[i];
            if (seg == null) continue;

            if (i < filled)
            {
                seg.color = litColor;
                seg.fillAmount = 1f;
            }
            else if (i == filled)
            {
                seg.color = partial > 0f ? litColor : emptyColor;
                seg.fillAmount = partial;
            }
            else
            {
                seg.color = emptyColor;
                seg.fillAmount = 1f;
            }
        }

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
    }

    private void OnDisable() => Unbind();
}
