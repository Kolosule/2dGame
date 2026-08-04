using System.Collections.Generic;
using System.Linq;

namespace Game.Hud.Core
{
    /// <summary>
    /// One player's row on the scoreboard panel -- plain data, no Fusion types, built by
    /// ScoreboardPanel from MatchStatsManager.Entries + ScoreFormula.Compute before being handed
    /// here. Team separation happens by which list a row is placed into before sorting, not by a
    /// filter in this file.
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
    }

    /// <summary>
    /// Pure group/sort math for the scoreboard: highest Overall Score first, ties stable (input
    /// order preserved) so a repaint with unchanged scores doesn't visibly jitter rows.
    /// Engine-free (Game.Hud.Core sets noEngineReferences) so it is testable outside Unity.
    /// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md, "Scoreboard UI".
    /// </summary>
    public static class ScoreboardSort
    {
        public static List<ScoreboardRow> SortByScoreDescending(IReadOnlyList<ScoreboardRow> rows)
        {
            return rows.OrderByDescending(r => r.OverallScore).ToList();
        }
    }
}
