using System.Collections;
using UnityEngine;
using Game.Combat.Core;

/// <summary>
/// Brief white flash on a target's SpriteRenderer when it is hit. Cosmetic and
/// local — spawned by HitFeedback on the attacker's client only. The coroutine
/// lives on the target so rapid repeated hits simply restart it, and the base
/// color is captured once so a hit mid-flash still restores correctly.
/// </summary>
public class HitFlash : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float flashDuration = 0.1f;

    private Color baseColor;
    private bool baseColorCaptured;
    private Coroutine running;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
            baseColorCaptured = true;
        }
    }

    public void PlayFlash()
    {
        if (spriteRenderer == null || !baseColorCaptured) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            float t = FlashCurve.Intensity(elapsed, flashDuration);
            spriteRenderer.color = Color.Lerp(baseColor, Color.white, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        spriteRenderer.color = baseColor;
        running = null;
    }
}
