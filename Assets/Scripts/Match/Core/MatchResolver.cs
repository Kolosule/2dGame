namespace Game.Match.Core
{
    /// <summary>
    /// Pure match-outcome logic, engine-free so it is unit-testable. The single place that
    /// decides a timer-expiry winner and formats the results banner. Winner codes: 0 = draw,
    /// 1 = Team1, 2 = Team2 (matches TeamUtil.ToNumber).
    /// </summary>
    public static class MatchResolver
    {
        /// <summary>Timer expired with no capture: higher coin score wins, exactly equal is a draw.</summary>
        public static int ResolveTimerWinner(int team1Score, int team2Score)
        {
            if (team1Score > team2Score) return 1;
            if (team2Score > team1Score) return 2;
            return 0;
        }

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


