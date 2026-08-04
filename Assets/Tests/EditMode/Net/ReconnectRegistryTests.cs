using NUnit.Framework;

public class ReconnectRegistryTests
{
    private static ReconnectHeldSlot Slot(int team = 1, int deposited = 250) => new ReconnectHeldSlot
    {
        Team = team,
        DisplayName = "Ada",
        LoadoutOrder = new byte[] { 3, 1, 2 },
        TotalDepositedValue = deposited,
        Kills = 4,
        Deaths = 2,
        Captures = 1,
        CoinsDeposited = 250,
        FlagCarrySeconds = 37,
        FlagReturns = 3
    };

    [Test]
    public void Capture_ThenClaim_ReturnsTheCapturedState()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot());

        Assert.IsTrue(r.TryClaim("aa", out var got));
        Assert.AreEqual(1, got.Team);
        Assert.AreEqual("Ada", got.DisplayName);
        Assert.AreEqual(250, got.TotalDepositedValue);
        Assert.AreEqual(4, got.Kills);
        Assert.AreEqual(37, got.FlagCarrySeconds);
    }

    [Test]
    public void Claim_RemovesTheSlot_SoTwoRacingRejoinsCannotBothRestore()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot());

        Assert.IsTrue(r.TryClaim("aa", out _));
        Assert.IsFalse(r.TryClaim("aa", out var second));
        Assert.IsNull(second);
        Assert.AreEqual(0, r.HeldCount);
    }

    [Test]
    public void Has_TracksCaptureAndClaim()
    {
        var r = new ReconnectRegistry();
        Assert.IsFalse(r.Has("aa"));
        r.Capture("aa", Slot());
        Assert.IsTrue(r.Has("aa"));
        r.TryClaim("aa", out _);
        Assert.IsFalse(r.Has("aa"));
    }

    [Test]
    public void HeldCount_ReflectsDistinctTokens_AndRecaptureReplaces()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot(team: 1, deposited: 10));
        r.Capture("bb", Slot(team: 2, deposited: 20));
        Assert.AreEqual(2, r.HeldCount);

        // Same token twice (dropped, spawned, dropped again) replaces rather than duplicating.
        r.Capture("aa", Slot(team: 1, deposited: 99));
        Assert.AreEqual(2, r.HeldCount);
        Assert.IsTrue(r.TryClaim("aa", out var got));
        Assert.AreEqual(99, got.TotalDepositedValue);
    }

    [Test]
    public void EmptyOrNullToken_IsNeverHeldOrClaimed()
    {
        var r = new ReconnectRegistry();
        r.Capture("", Slot());
        r.Capture(null, Slot());
        Assert.AreEqual(0, r.HeldCount);
        Assert.IsFalse(r.Has(""));
        Assert.IsFalse(r.TryClaim(null, out _));
    }

    [Test]
    public void Clear_ReleasesEverything_TheMatchEnded()
    {
        var r = new ReconnectRegistry();
        r.Capture("aa", Slot());
        r.Capture("bb", Slot());
        r.Clear();
        Assert.AreEqual(0, r.HeldCount);
        Assert.IsFalse(r.Has("aa"));
    }
}
