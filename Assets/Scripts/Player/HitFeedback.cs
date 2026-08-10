using UnityEngine;
using Game.Audio.Core;

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

    [Tooltip("Damage at or above which the heavier hit-confirm cue plays instead. This is a " +
             "loudness tier, NOT a crit system — the crit multiplier was removed in the " +
             "2026-08-05 damage-model change.")]
    [SerializeField] private int heavyHitDamageThreshold = 25;

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
        // This method only ever runs on the ATTACKER's client — both callers are
        // [Rpc(..., RpcTargets.InputAuthority)] (PlayerCombat.RPC_HitFeedback,
        // Projectile.RPC_HitFeedback), so no gating is needed here. Flat and full volume: it is
        // the single most important cue in the game and must never fade with camera drift.
        Audio.Play2D(damage >= heavyHitDamageThreshold
            ? AudioCueId.HitConfirmHeavy
            : AudioCueId.HitConfirm);

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
