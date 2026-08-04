using Fusion;
using UnityEngine;
using Game.Stats.Core;

/// <summary>
/// Single central, always-interested source of per-player match stats (kills, deaths, captures,
/// coins deposited, flag carry time, flag returns) plus the small subset of identity/state
/// (team, display name, alive/dead) the scoreboard needs for every player regardless of distance.
///
/// AoI applies per NetworkObject, not per component: a stats component living on each player's
/// own (AoI-culled) avatar would force that whole avatar object always-interested for every
/// viewer, defeating AoI at 20-player scale. This is a scene singleton instead -- mark its
/// GameObject with AlwaysInterestedMarker in the inspector, exactly like TeamScoreManager and
/// MatchManager.
///
/// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md.
/// </summary>
public class MatchStatsManager : NetworkBehaviour
{
    public static MatchStatsManager Instance { get; private set; }

    /// <summary>Matches GameNetworkManager.maxPlayers (20); slots are indexed by PlayerId directly.</summary>
    public const int RosterCapacity = 20;

    [Networked, Capacity(RosterCapacity)]
    public NetworkArray<PlayerStatEntry> Entries { get; }

    [Header("Overall score weights (tunable -- see the design spec's weight table)")]
    [SerializeField] private float killWeight = 10f;
    [SerializeField] private float deathWeight = -10f;
    [SerializeField] private float coinWeight = 0.75f;
    [SerializeField] private float flagCarrySecondWeight = 1f;
    [SerializeField] private float flagReturnWeight = 20f;

    public ScoreWeights Weights => new ScoreWeights
    {
        Kill = killWeight,
        Death = deathWeight,
        Coin = coinWeight,
        FlagCarrySecond = flagCarrySecondWeight,
        FlagReturn = flagReturnWeight
    };

    private void Awake()
    {
        // Never Destroy() a spawned NetworkObject locally (desyncs Fusion's object table on this
        // peer); disable the duplicate and leave it inert, matching TeamScoreManager's guard.
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// SERVER: create/refresh a player's roster entry. Called once at spawn from
    /// NetworkedSpawnManager.TrySpawnPlayer, which already knows the resolved team -- so this sets
    /// Team directly and does not depend on PlayerTeamData.SetTeam's mirror having run first.
    /// </summary>
    public void RegisterPlayer(int playerId, int team, string displayName)
    {
        if (!HasStateAuthority) return;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out int index))
        {
            // Loud on purpose: a silent drop here discards every stat this player ever earns for
            // the rest of the match with nothing in the console. RosterCapacity, GameNetworkManager
            // .maxPlayers, and NetworkProjectConfig's Simulation.PlayerCount are only coupled by
            // comment -- if session size is ever raised without updating all three, this is how
            // you find out.
            Debug.LogError($"❌ MatchStatsManager.RegisterPlayer: playerId {playerId} exceeds " +
                            $"RosterCapacity ({RosterCapacity}); this player's stats will not be tracked.");
            return;
        }

        Entries.Set(index, new PlayerStatEntry
        {
            Active = true,
            Team = (byte)team,
            DisplayName = displayName ?? string.Empty,
            IsDead = false,
            Kills = 0,
            Deaths = 0,
            Captures = 0,
            CoinsDeposited = 0,
            FlagCarrySeconds = 0,
            FlagReturns = 0
        });
    }

    /// <summary>
    /// SERVER: recreate a reconnecting player's roster entry under their NEW PlayerId, carrying the
    /// counters saved when they dropped.
    ///
    /// Slots are indexed by PlayerId and a rejoiner always gets a different one, so the old row
    /// cannot simply be kept: it stays behind, invisible (the scoreboard filters on
    /// Runner.ActivePlayers) and fully overwritten by RegisterPlayer if that id is ever reassigned.
    /// </summary>
    public void RestoreEntry(int playerId, int team, string displayName, ReconnectHeldSlot slot)
    {
        if (!HasStateAuthority || slot == null) return;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out int index))
        {
            Debug.LogError($"❌ MatchStatsManager.RestoreEntry: playerId {playerId} exceeds " +
                            $"RosterCapacity ({RosterCapacity}); this player's restored stats will not be tracked.");
            return;
        }

        Entries.Set(index, new PlayerStatEntry
        {
            Active = true,
            Team = (byte)team,
            DisplayName = displayName ?? string.Empty,
            IsDead = false,
            Kills = slot.Kills,
            Deaths = slot.Deaths,
            Captures = slot.Captures,
            CoinsDeposited = slot.CoinsDeposited,
            FlagCarrySeconds = slot.FlagCarrySeconds,
            FlagReturns = slot.FlagReturns
        });
    }

    /// <summary>SERVER: mirrors a team reassignment after the entry already exists (e.g. a team switch).</summary>
    public void SetTeam(int playerId, int team)
    {
        if (!HasStateAuthority) return;
        if (!TryGetMutable(playerId, out int index, out var entry)) return;
        entry.Team = (byte)team;
        Entries.Set(index, entry);
    }

    public void SetDead(int playerId, bool isDead)
    {
        if (!HasStateAuthority) return;
        if (!TryGetMutable(playerId, out int index, out var entry)) return;
        entry.IsDead = isDead;
        Entries.Set(index, entry);
    }

    public void RecordKill(PlayerRef attacker)
    {
        if (!HasStateAuthority || !attacker.IsRealPlayer) return;
        if (!TryGetMutable(attacker.PlayerId, out int index, out var entry)) return;
        entry.Kills++;
        Entries.Set(index, entry);
    }

    public void RecordDeath(PlayerRef player)
    {
        if (!HasStateAuthority || !player.IsRealPlayer) return;
        if (!TryGetMutable(player.PlayerId, out int index, out var entry)) return;
        entry.Deaths++;
        Entries.Set(index, entry);
    }

    public void RecordCapture(PlayerRef carrier)
    {
        if (!HasStateAuthority || !carrier.IsRealPlayer) return;
        if (!TryGetMutable(carrier.PlayerId, out int index, out var entry)) return;
        entry.Captures++;
        Entries.Set(index, entry);
    }

    public void RecordDeposit(PlayerRef player, int points)
    {
        if (!HasStateAuthority || !player.IsRealPlayer || points <= 0) return;
        if (!TryGetMutable(player.PlayerId, out int index, out var entry)) return;
        entry.CoinsDeposited += points;
        Entries.Set(index, entry);
    }

    public void RecordFlagCarrySeconds(PlayerRef carrier, int seconds)
    {
        if (!HasStateAuthority || !carrier.IsRealPlayer || seconds <= 0) return;
        if (!TryGetMutable(carrier.PlayerId, out int index, out var entry)) return;
        entry.FlagCarrySeconds += seconds;
        Entries.Set(index, entry);
    }

    public void RecordFlagReturn(PlayerRef returner)
    {
        if (!HasStateAuthority || !returner.IsRealPlayer) return;
        if (!TryGetMutable(returner.PlayerId, out int index, out var entry)) return;
        entry.FlagReturns++;
        Entries.Set(index, entry);
    }

    /// <summary>Read accessor for the scoreboard UI. False when the slot is unused or out of range.</summary>
    public bool TryGetEntry(int playerId, out PlayerStatEntry entry)
    {
        entry = default;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out int index)) return false;
        entry = Entries.Get(index);
        return entry.Active;
    }

    private bool TryGetMutable(int playerId, out int index, out PlayerStatEntry entry)
    {
        entry = default;
        if (!RosterIndex.TryResolve(playerId, RosterCapacity, out index)) return false;
        entry = Entries.Get(index);
        return entry.Active; // ignore writes for a player with no registered entry yet
    }
}

/// <summary>
/// One player's replicated match stats, plus the identity/state slice the scoreboard needs
/// regardless of AoI distance. Indexed by PlayerId in MatchStatsManager.Entries.
/// </summary>
public struct PlayerStatEntry : INetworkStruct
{
    public NetworkBool Active;
    public byte Team;
    // _16 matches LobbyProtocol.MaxNicknameChars, the cap SanitizeNickname actually enforces.
    // (MaxNicknameBytes = 64 is the UTF-8 wire budget for the lobby message -- a different unit.)
    public NetworkString<_16> DisplayName;
    public NetworkBool IsDead;
    public int Kills;
    public int Deaths;
    public int Captures;
    public int CoinsDeposited;
    public int FlagCarrySeconds;
    public int FlagReturns;
}
