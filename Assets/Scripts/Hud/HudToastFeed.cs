using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Game.Hud.Core;
using Game.Audio.Core;

/// <summary>
/// One shared transient-notification surface for unlock moments, fed by the individual buff row
/// and the Team Power strip. Messages queue so two tier-ups in the same frame are both seen.
///
/// PURELY VISUAL: it is driven by Time.deltaTime in Update (render path), never by simulation, and
/// it decides nothing. Tier-ups are detected CLIENT-SIDE by the displays that call Show(), because
/// the server-side UnityEvents this replaces fired behind a HasStateAuthority guard — on a
/// dedicated server they fired headless where no client could ever observe them.
/// See docs/superpowers/specs/2026-07-29-coins-buffs-economy-design.md, "Unlock moments".
/// </summary>
public class HudToastFeed : MonoBehaviour
{
    [Tooltip("CanvasGroup faded by this feed. Usually on the same object as the label.")]
    [SerializeField] private CanvasGroup group;

    [SerializeField] private TMP_Text label;

    [Tooltip("Seconds fully opaque before the fade starts.")]
    [SerializeField] private float holdSeconds = 2f;

    [Tooltip("Seconds spent fading out.")]
    [SerializeField] private float fadeSeconds = 0.6f;

    private readonly Queue<string> pending = new Queue<string>();
    private float elapsed;
    private bool showing;

    /// <summary>Queue a message. Safe to call before Awake and from any display.</summary>
    public void Show(string message)
    {
        Audio.PlayUi(AudioCueId.ToastNotification);
        if (string.IsNullOrEmpty(message)) return;
        pending.Enqueue(message);
    }

    private void Awake()
    {
        if (group != null) group.alpha = 0f;
    }

    private void Update()
    {
        if (!showing)
        {
            if (pending.Count == 0) return;
            if (label != null) label.text = pending.Dequeue();
            else pending.Dequeue();
            elapsed = 0f;
            showing = true;
        }

        elapsed += Time.deltaTime;
        float alpha = ToastFade.Alpha01(elapsed, holdSeconds, fadeSeconds);
        if (group != null) group.alpha = alpha;
        if (alpha <= 0f) showing = false;
    }
}
