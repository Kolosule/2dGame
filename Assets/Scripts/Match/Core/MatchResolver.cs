namespace Game.Match.Core
{
    /// <summary>
    /// Pure match-outcome logic, engine-free so it is unit-testable. Formats the results banner
    /// from a winner code: 0 = draw, 1 = Team1, 2 = Team2 (matches TeamUtil.ToNumber). Capture is
    /// the only win condition — coins neither decide nor tiebreak a match — so there is no
    /// score-comparison resolver here by design.
    /// </summary>
    public static class MatchResolver
    {
        /// <summary>Results-banner text for a winner code. Anything other than 1/2 reads as a draw.</summary>
        public static string WinnerLabel(int winner)
        {
            switch (winner)
            {
                case 1: return "Team 1 Wins!";
                case 2: return "Team 2 Wins!";
                default: return "It's a Draw!";
            }
        }
    }
}


