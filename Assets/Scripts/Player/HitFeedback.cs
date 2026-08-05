using UnityEngine;

/// <summary>
/// Scene singleton that plays attacker-only hit feedback: an impact particle
/// burst, a floating damage number, and a flash on the target sprite. Holds the
/// prefab references so they are wired once in the scene rather than on every
/// player and projectile prefab. Invoked from the InputAuthority-targeted
/// RPC handlers in PlayerCombat and Projectile.
/// </summary>
public class HitFeedback : MonoBehaviour
{
    public static HitFeedback Instance { get; private set; }

    [SerializeField] private GameObject particleBurstPrefab;
    [SerializeField] private GameObject damageNumberPrefab;
    [SerializeField] private float particleLifetime = 2f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Play the three cosmetic effects. Each is independently null-guarded, so a
    /// missing prefab or a culled/despawned target simply skips that one effect.
    /// </summary>
    public void Play(GameObject target, Vector2 hitPoint, int damage)
    {
        if (particleBurstPrefab != null)
        {
            GameObject fx = Instantiate(particleBurstPrefab, hitPoint, Quaternion.identity);
            Destroy(fx, particleLifetime);
        }

        // Client-local preference: hide the floating numbers only. The particle burst and the
        // target hit-flash below are unaffected -- this is not a "disable hit feedback" toggle.
        if (damageNumberPrefab != null && SettingsStore.ShowDamageNumbers)
        {
            GameObject num = Instantiate(damageNumberPrefab, hitPoint, Quaternion.identity);
            DamageNumber dn = num.GetComponent<DamageNumber>();
            if (dn != null) dn.Init(damage);
        }

        if (target != null)
        {
            HitFlash flash = target.GetComponentInChildren<HitFlash>();
            if (flash != null) flash.PlayFlash();
        }
    }
}
