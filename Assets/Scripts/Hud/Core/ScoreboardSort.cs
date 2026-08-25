using System.Collections.Generic;
using System.Linq;

namespace Game.Hud.Core
{
    /// <summary>
    /// One player's row on the scoreboard panel -- plain data, no Fusion types and no UnityEngine
    /// types, built by ScoreboardPanel from MatchStatsManager.Entries + ScoreFormula.Compute before
    /// being handed here. Every player in the match goes into ONE list: the board is an individual
    /// leaderboard, and team membership rides along on the row (as <see cref="Team"/> plus the
    /// pre-resolved stripe colour) purely so the view can tint a left edge stripe.
    ///
    /// The colour is carried as three plain floats rather than a UnityEngine.Color because
    /// Game.Hud.Core sets noEngineReferences -- ScoreboardPanel resolves TeamData.teamColor and
    /// splats it here, and ScoreboardRowView reassembles it.
    /// </summary>
    public struct ScoreboardRow
    {
        public int PlayerId;
        public int Team;
        public string DisplayName;
        public bool IsDead;
        public bool IsCarryingFlag;
        public int Kills;
        public int Deaths;
        public int Captures;
        public int CoinsDeposited;
        public int FlagCarrySeconds;
        public int FlagReturns;
        public float OverallScore;

        /// <summary>1-based position in the merged sorted list, assigned by the panel after sorting.</summary>
        public int Rank;

        /// <summary>True for the viewing client's own row, so the view can outline it.</summary>
        public bool IsLocalPlayer;

        /// <summary>Team stripe colour, pre-resolved from TeamData.teamColor by the panel.</summary>
        public float TeamColorR;
        public float TeamColorG;
        public float TeamColorB;
    }

    /// <summary>
    /// Pure sort math for the merged scoreboard: highest Overall Score first, with an explicit
    /// tie-break chain rather than a stable-sort accident.
    ///
    /// Why the chain matters: with two teams merged into one list, two opposing players tying on
    /// score is routine, and every peer must render them in the SAME order. Input order is not a
    /// usable tie-break here -- each client builds its list from Runner.ActivePlayers, whose
    /// iteration order is per-peer, so "stable" would still disagree across machines. Kills descending
    /// is the meaningful secondary signal; PlayerId ascending is the final total order, and because
    /// PlayerId is unique per player it guarantees a single deterministic result on every peer.
    ///
    /// Engine-free (Game.Hud.Core sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md, "Scoreboard UI".
    /// </summary>
    public static class ScoreboardSort
    {
        /// <summary>Returns a new sorted list; the input is never reordered.</summary>
        public static List<ScoreboardRow> SortByScoreDescending(IReadOnlyList<ScoreboardRow> rows)
        {
            return rows
                .OrderByDescending(r => r.OverallScore)
                .ThenByDescending(r => r.Kills)
                .ThenBy(r => r.PlayerId)
                .ToList();
        }
    }
}
