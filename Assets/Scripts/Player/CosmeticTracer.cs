using UnityEngine;

/// <summary>
/// Self-animating, NON-networked muzzle tracer for client-side shot prediction. Draws a brief line
/// from the muzzle along the aim direction, fades out, then destroys itself. Purely cosmetic — no
/// gameplay effect. Spawned by PlayerCombat on the firing client; the real networked projectile
/// governs actual travel and damage. Code-generated so it needs no art asset.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class CosmeticTracer : MonoBehaviour
{
    private LineRenderer line;
    private float duration;
    private float elapsed;
    private Color startColor;

    /// <summary>Spawn a one-shot tracer. Safe to call every shot; it cleans itself up.</summary>
    public static void Spawn(Vector3 origin, Vector2 dir, float length, float width, Color color, float duration)
    {
        var go = new GameObject("CosmeticTracer");
        var tracer = go.AddComponent<CosmeticTracer>(); // RequireComponent adds the LineRenderer
        tracer.Init(origin, dir, length, width, color, duration);
    }

    private void Init(Vector3 origin, Vector2 dir, float length, float width, Color color, float duration)
    {
        this.duration = Mathf.Max(0.01f, duration);
        startColor = color;

        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.positionCount = 2;
        line.SetPosition(0, origin);
        line.SetPosition(1, origin + (Vector3)(dir.normalized * length));
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = 100;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        Color c = startColor;
        c.a = startColor.a * (1f - t); // fade out
        line.startColor = c;
        line.endColor = c;
    }
}
