using UnityEngine;
using Fusion;
using Game.Buffs.Core;

/// <summary>
/// Per-player, server-authoritative buff state. Tiers are DERIVED from TotalDepositedValue +
/// LoadoutOrder (both networked) via BuffUnlock — nothing to replay on resimulation. The one
/// active buff (Stealth) is a TickTimer-driven networked flag, activated in Simulate (mirrors dash).
/// </summary>
public class PlayerBuffs : NetworkBehaviour
{
    [SerializeField] private BuffLoadoutConfig config;

    [Networked, Capacity(8)] private NetworkArray<byte> LoadoutOrder { get; }
    [Networked] private int LoadoutLength { get; set; }
    [Networked] public int TotalDepositedValue { get; private set; }
    [Networked] public NetworkBool IsStealthed { get; private set; }
    [Networked] private TickTimer StealthDurationTimer { get; set; }
    [Networked] private TickTimer StealthCooldownTimer { get; set; }

    public int TotalDeposited => TotalDepositedValue;

    public override void Spawned()
    {
        if (HasStateAuthority && LoadoutLength == 0)
            ApplyDefaultLoadout();
    }

    private void ApplyDefaultLoadout()
    {
        if (config == null || config.DefaultOrder == null) return;
        ServerInitLoadout(ToBytes(config.DefaultOrder));
    }

    private static byte[] ToBytes(BuffId[] order)
    {
        var bytes = new byte[order.Length];
        for (int i = 0; i < order.Length; i++) bytes[i] = (byte)order[i];
        return bytes;
    }

    /// <summary>SERVER: set this player's priority order (from the lobby choice or default).</summary>
    public void ServerInitLoadout(byte[] order)
    {
        if (!HasStateAuthority || order == null) return;
        if (order.Length == 0) return; // empty choice: keep the default loadout applied in Spawned
        int n = Mathf.Min(order.Length, 8);
        for (int i = 0; i < n; i++) LoadoutOrder.Set(i, order[i]);
        LoadoutLength = n;
    }

    /// <summary>SERVER: add deposited point value; tiers re-derive automatically from this.</summary>
    public void ServerAddDepositedValue(int points)
    {
        if (!HasStateAuthority || points <= 0) return;
        TotalDepositedValue += points;
    }

    /// <summary>Priority position of a buff in this player's loadout, or -1 if not equipped.</summary>
    private int PositionOf(BuffId id)
    {
        for (int i = 0; i < LoadoutLength; i++)
            if ((BuffId)LoadoutOrder.Get(i) == id) return i;
        return -1;
    }

    /// <summary>Current tier (0 = locked) of the given buff for this player.</summary>
    public int TierOf(BuffId id)
    {
        if (config == null) return 0;
        int pos = PositionOf(id);
        if (pos < 0) return 0;
        int steps = BuffUnlock.UnlockedSteps(config.Thresholds, TotalDepositedValue);
        return BuffUnlock.TierLevel(steps, pos, config.BuffCount, config.MaxTier);
    }

    /// <summary>Sum every equipped buff's passive contribution at its current tier.</summary>
    public void BuildEffectiveStats(ref EffectiveStats stats)
    {
        if (config == null) return;
        for (int i = 0; i < LoadoutLength; i++)
        {
            BuffId id = (BuffId)LoadoutOrder.Get(i);
            BuffDefinition def = config.GetById(id);
            if (def == null) continue;
            def.ContributeStats(ref stats, TierOf(id));
        }
    }

    /// <summary>Called from PlayerController.FixedUpdateNetwork after movement/combat.</summary>
    public void Simulate(NetInput input, NetworkButtons pressed)
    {
        // Stealth expiry first (pure function of the networked timer).
        if (IsStealthed && StealthDurationTimer.ExpiredOrNotRunning(Runner))
        {
            IsStealthed = false;
            StealthCooldownTimer = TickTimer.CreateFromSeconds(Runner, CurrentStealthCooldown());
        }

        // Activation.
        if (pressed.IsSet((int)PlayerButton.Stealth) && CanActivateStealth())
        {
            ActiveBuffParams p = StealthParams();
            IsStealthed = true;
            StealthDurationTimer = TickTimer.CreateFromSeconds(Runner, p.Duration);
        }
    }

    private ActiveBuffParams StealthParams()
    {
        BuffDefinition def = config != null ? config.GetById(BuffId.Stealth) : null;
        return def != null ? def.GetActiveParams(TierOf(BuffId.Stealth)) : default;
    }

    private float CurrentStealthCooldown()
    {
        ActiveBuffParams p = StealthParams();
        return p.Cooldown;
    }

    private bool CanActivateStealth()
    {
        if (IsStealthed) return false;
        if (!StealthCooldownTimer.ExpiredOrNotRunning(Runner)) return false;
        ActiveBuffParams p = StealthParams();
        if (!p.Unlocked) return false;
        bool carrying = CTFGameManager.Instance != null &&
                        CTFGameManager.Instance.IsCarrying(Object.InputAuthority);
        if (carrying && !p.UsableWhileCarryingFlag) return false;
        return true;
    }
}
