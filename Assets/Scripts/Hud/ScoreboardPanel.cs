using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Game.Hud.Core;
using Game.Stats.Core;

/// <summary>
/// Renders every active player's stats, grouped by team and sorted by Overall Score. Shown
/// on-demand (hold Tab, wired by ScoreboardInputReader) and auto-shown during MatchPhase.PostMatch
/// (wired by MatchPhaseHud). Reads MatchStatsManager.Entries directly on the render path while
/// visible -- no per-tick simulation work, no polling while hidden.
/// See docs/superpowers/specs/2026-07-29-scoreboard-killfeed-design.md.
/// </summary>
public class ScoreboardPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform team1RowContainer;
    [SerializeField] private Transform team2RowContainer;
    [SerializeField] private ScoreboardRowView rowTemplate;

    private readonly List<ScoreboardRowView> team1Pool = new List<ScoreboardRowView>();
    private readonly List<ScoreboardRowView> team2Pool = new List<ScoreboardRowView>();

    private bool forcedVisible; // PostMatch auto-show
    private bool heldVisible;   // Tab held

    private void Awake()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
        SetVisible(false);
    }

    /// <summary>Local input reader calls this on the Scoreboard action's performed/canceled.</summary>
    public void SetHeld(bool held)
    {
        heldVisible = held;
        Repaint();
    }

    /// <summary>MatchPhaseHud calls this to force the board open for the whole PostMatch phase.</summary>
    public void SetForcedVisible(bool visible)
    {
        forcedVisible = visible;
        Repaint();
    }

    private void Repaint()
    {
        bool visible = heldVisible || forcedVisible;
        SetVisible(visible);
        if (visible) PaintRows();
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null) panelRoot.SetActive(visible);
    }

    private void PaintRows()
    {
        MatchStatsManager manager = MatchStatsManager.Instance;
        if (manager == null || manager.Runner == null) return;

        var team1Rows = new List<ScoreboardRow>();
        var team2Rows = new List<ScoreboardRow>();

        foreach (PlayerRef player in manager.Runner.ActivePlayers)
        {
            if (!manager.TryGetEntry(player.PlayerId, out PlayerStatEntry entry)) continue;

            var row = new ScoreboardRow
            {
                PlayerId = player.PlayerId,
                Team = entry.Team,
                DisplayName = entry.DisplayName.Value,
                IsDead = entry.IsDead,
                IsCarryingFlag = CTFGameManager.Instance != null && CTFGameManager.Instance.IsCarrying(player),
                Kills = entry.Kills,
                Deaths = entry.Deaths,
                Captures = entry.Captures,
                CoinsDeposited = entry.CoinsDeposited,
                FlagCarrySeconds = entry.FlagCarrySeconds,
                FlagReturns = entry.FlagReturns,
                OverallScore = ScoreFormula.Compute(entry.Kills, entry.Deaths, entry.CoinsDeposited,
                    entry.FlagCarrySeconds, entry.FlagReturns, manager.Weights)
            };

            if (entry.Team == (byte)Team.Team1) team1Rows.Add(row);
            else if (entry.Team == (byte)Team.Team2) team2Rows.Add(row);
        }

        PaintTeam(ScoreboardSort.SortByScoreDescending(team1Rows), team1Pool, team1RowContainer);
        PaintTeam(ScoreboardSort.SortByScoreDescending(team2Rows), team2Pool, team2RowContainer);
    }

    private void PaintTeam(List<ScoreboardRow> rows, List<ScoreboardRowView> pool, Transform container)
    {
        if (rowTemplate == null || container == null) return;

        while (pool.Count < rows.Count)
        {
            ScoreboardRowView view = Instantiate(rowTemplate, container);
            view.gameObject.SetActive(true);
            pool.Add(view);
        }

        for (int i = 0; i < pool.Count; i++)
        {
            bool active = i < rows.Count;
            pool[i].gameObject.SetActive(active);
            if (active) pool[i].Paint(rows[i]);
        }
    }
}
