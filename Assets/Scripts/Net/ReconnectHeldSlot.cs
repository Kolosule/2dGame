/// <summary>
/// One disconnected player's preserved match state, held server-side for the rest of the match and
/// restored if they rejoin with the same identity token.
///
/// Plain C# with no Unity or Fusion types, so ReconnectRegistry stays engine-free and unit-testable.
/// That is also why the stats counters are loose ints rather than a PlayerStatEntry: that struct is
/// a Fusion INetworkStruct and cannot live in this assembly.
///
/// See docs/superpowers/specs/2026-07-29-reconnection-design.md.
/// </summary>
public class ReconnectHeldSlot
{
    public int Team;
    public string DisplayName;
    public byte[] LoadoutOrder;
    public int TotalDepositedValue;

    // MatchStatsManager row, copied out at capture and back in at restore under the NEW PlayerId.
    public int Kills;
    public int Deaths;
    public int Captures;
    public int CoinsDeposited;
    public int FlagCarrySeconds;
    public int FlagReturns;
}
