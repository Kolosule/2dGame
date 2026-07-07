using TMPro;
using UnityEngine;
using Game.Combat.Core;

/// <summary>
/// World-space floating damage number. Spawned locally on the attacker's client
/// by HitFeedback. Drifts upward and fades out, then destroys itself. The rise
/// and fade math is the pure DamageNumberMotion helper (unit-tested).
/// </summary>
public class DamageNumber : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private float lifetime = 0.7f;
    [SerializeField] private float riseSpeed = 1f;

    private Vector3 startPos;
    private float elapsed;
    private Color baseColor;

    public void Init(int amount)
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = amount.ToString();
            baseColor = label.color;
        }
        startPos = transform.position;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        transform.position = startPos + Vector3.up * DamageNumberMotion.YOffset(elapsed, riseSpeed);

        if (label != null)
        {
            Color c = baseColor;
            c.a = DamageNumberMotion.Alpha(elapsed, lifetime);
            label.color = c;
        }

        if (elapsed >= lifetime) Destroy(gameObject);
    }
}
