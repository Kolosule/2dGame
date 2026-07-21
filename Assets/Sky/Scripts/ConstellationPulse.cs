using UnityEngine;
using Game.Sky.Core;

/// <summary>
/// Softly pulses a constellation's brightness and scale with a sine wave. Local cosmetic only —
/// no networking. Modulates every child SpriteRenderer's alpha around its authored value and
/// this transform's local scale. Attach to a constellation root that has node/line SpriteRenderers
/// as children.
/// </summary>
public class ConstellationPulse : MonoBehaviour
{
    [Tooltip("Raw radian pulse rate; ~0.9 is a slow, calm shimmer.")]
    [SerializeField] private float frequency = 0.9f;
    [Tooltip("Alpha swing as a fraction of the authored alpha (0.12 = +/-12%).")]
    [SerializeField, Range(0f, 0.5f)] private float alphaAmplitude = 0.12f;
    [Tooltip("Scale swing as a fraction of the authored scale.")]
    [SerializeField, Range(0f, 0.5f)] private float scaleAmplitude = 0.06f;
    [Tooltip("Phase offset. Left at 0, a random phase is chosen so identical constellations " +
             "don't pulse in lockstep.")]
    [SerializeField] private float phase = 0f;

    private SpriteRenderer[] renderers;
    private float[] baseAlphas;
    private Vector3 baseScale;

    private void Awake()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseAlphas = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseAlphas[i] = renderers[i].color.a;
        baseScale = transform.localScale;
        if (Mathf.Approximately(phase, 0f)) phase = Random.value * 2f * Mathf.PI;
    }

    private void Update()
    {
        float am = PulseMath.Multiplier(Time.time, frequency, alphaAmplitude, phase);
        for (int i = 0; i < renderers.Length; i++)
        {
            Color c = renderers[i].color;
            c.a = Mathf.Clamp01(baseAlphas[i] * am);
            renderers[i].color = c;
        }
        float sm = PulseMath.Multiplier(Time.time, frequency, scaleAmplitude, phase);
        transform.localScale = baseScale * sm;
    }
}
