using UnityEngine;

/// <summary>References to the local player's networked sources, handed to each HUD display on bind.</summary>
public readonly struct HudContext
{
    public readonly NetworkedPlayerInventory Inventory;
    public readonly PlayerStatsHandler Stats;
    public readonly PlayerBuffs Buffs;
    public readonly PlayerTeamData Team;

    public HudContext(NetworkedPlayerInventory inventory, PlayerStatsHandler stats,
                      PlayerBuffs buffs, PlayerTeamData team)
    {
        Inventory = inventory;
        Stats = stats;
        Buffs = buffs;
        Team = team;
    }
}

/// <summary>A HUD display that binds to the local player's sources and updates event-driven.</summary>
public interface IHudBindable
{
    void Bind(HudContext ctx);
    void Unbind();
}

/// <summary>
/// In-match HUD root. Discovers the local (input-authority) player ONCE — a bounded,
/// self-terminating search, not per-frame value polling — then binds every child display.
/// Replaces the old polling UIManager.
/// </summary>
public class PlayerHud : MonoBehaviour
{
    [Tooltip("Every child display implementing IHudBindable. Populated in the inspector.")]
    [SerializeField] private MonoBehaviour[] displays;

    private bool bound;

    private void Update()
    {
        if (bound) return;
        TryBind();
    }

    private void TryBind()
    {
        NetworkedPlayerInventory[] players =
            FindObjectsByType<NetworkedPlayerInventory>(FindObjectsSortMode.None);

        foreach (var inv in players)
        {
            if (!inv.HasInputAuthority) continue;

            var ctx = new HudContext(
                inv,
                inv.GetComponent<PlayerStatsHandler>(),
                inv.GetComponent<PlayerBuffs>(),
                inv.GetComponent<PlayerTeamData>());

            foreach (var display in displays)
            {
                if (display is IHudBindable bindable)
                    bindable.Bind(ctx);
            }

            bound = true;
            return;
        }
    }

    private void OnDisable()
    {
        if (!bound) return;
        foreach (var display in displays)
        {
            if (display is IHudBindable bindable)
                bindable.Unbind();
        }
        bound = false;
    }
}
