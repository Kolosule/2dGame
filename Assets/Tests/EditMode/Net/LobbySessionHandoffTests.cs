using NUnit.Framework;

public class LobbySessionHandoffTests
{
    private LobbySessionHandoff handoff;

    [SetUp]
    public void SetUp()
    {
        handoff = new LobbySessionHandoff();
    }

    [Test]
    public void MissingPlayer_HasNoChoices()
    {
        AssertMissing(1);
    }

    [Test]
    public void TeamOnly_DoesNotInventNickname()
    {
        handoff.SetTeam(1, 2);

        Assert.IsTrue(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(2, team);
        Assert.IsFalse(handoff.TryGetNickname(1, out string name));
        Assert.IsNull(name);
    }

    [Test]
    public void NicknameOnly_DoesNotInventTeam()
    {
        handoff.SetNickname(1, "Ada");

        Assert.IsTrue(handoff.TryGetNickname(1, out string name));
        Assert.AreEqual("Ada", name);
        Assert.IsFalse(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(0, team);
    }

    [Test]
    public void LateTeam_PreservesAnEarlierNickname()
    {
        handoff.SetNickname(1, "Ada");
        handoff.SetTeam(1, 2);

        AssertChoices(handoff, 1, 2, "Ada");
    }

    [Test]
    public void UpdatingEachChoice_PreservesTheOtherFields()
    {
        SeedChoices(handoff, 1);

        handoff.SetTeam(1, 2);
        AssertChoices(handoff, 1, 2, "Ada");

        handoff.SetNickname(1, "Grace");
        AssertChoices(handoff, 1, 2, "Grace");
    }

    [Test]
    public void RecordedDefaultValues_AreNotMistakenForMissingFields()
    {
        // Validation belongs to the command paths, not to this projection's presence checks.
        handoff.SetTeam(1, 0);
        handoff.SetNickname(1, null);

        Assert.IsTrue(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(0, team);
        Assert.IsTrue(handoff.TryGetNickname(1, out string name));
        Assert.IsNull(name);

        handoff.SetNickname(1, "");
        AssertChoices(handoff, 1, 0, "");
    }

    [Test]
    public void IndependentInstances_CannotUpdateRemoveOrResetEachOthersChoices()
    {
        var newerSession = new LobbySessionHandoff();
        SeedChoices(handoff, 1);
        newerSession.SetTeam(1, 2);
        newerSession.SetNickname(1, "Grace");

        handoff.SetTeam(1, 2);
        handoff.SetNickname(1, "Alan");
        AssertChoices(newerSession, 1, 2, "Grace");

        handoff.RemovePlayer(1);
        AssertChoices(newerSession, 1, 2, "Grace");

        SeedChoices(handoff, 1);
        handoff.Reset();
        AssertChoices(newerSession, 1, 2, "Grace");
    }

    [Test]
    public void RemovePlayer_ClearsEveryChoice_WithoutAffectingOtherPlayers()
    {
        SeedChoices(handoff, 1);
        SeedChoices(handoff, 2);

        handoff.RemovePlayer(1);
        handoff.RemovePlayer(1);
        handoff.RemovePlayer(99);

        AssertMissing(1);
        AssertChoices(handoff, 2, 1, "Ada");
    }

    [Test]
    public void RemovedPlayerId_CanBeReusedWithoutInheritingOtherFields()
    {
        SeedChoices(handoff, 1);
        handoff.RemovePlayer(1);
        handoff.SetTeam(1, 2);

        Assert.IsTrue(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(2, team);
        Assert.IsFalse(handoff.TryGetNickname(1, out _));
    }

    [Test]
    public void Reset_ClearsFullAndPartialEntriesTogether_AndCanBeRepeated()
    {
        SeedChoices(handoff, 1);
        handoff.SetTeam(2, 2);
        handoff.SetNickname(3, "Grace");

        handoff.Reset();
        handoff.Reset();

        for (int playerId = 1; playerId <= 3; playerId++)
            AssertMissing(playerId);

        handoff.SetNickname(1, "New session");
        Assert.IsTrue(handoff.TryGetNickname(1, out string name));
        Assert.AreEqual("New session", name);
        Assert.IsFalse(handoff.TryGetTeam(1, out _));
    }

    private static void SeedChoices(LobbySessionHandoff store, int playerId)
    {
        store.SetTeam(playerId, 1);
        store.SetNickname(playerId, "Ada");
    }

    private static void AssertChoices(LobbySessionHandoff store, int playerId, int team,
                                      string nickname)
    {
        Assert.IsTrue(store.TryGetTeam(playerId, out int actualTeam));
        Assert.AreEqual(team, actualTeam);
        Assert.IsTrue(store.TryGetNickname(playerId, out string actualName));
        Assert.AreEqual(nickname, actualName);
    }

    private void AssertMissing(int playerId)
    {
        Assert.IsFalse(handoff.TryGetTeam(playerId, out int team));
        Assert.AreEqual(0, team);
        Assert.IsFalse(handoff.TryGetNickname(playerId, out string name));
        Assert.IsNull(name);
    }
}
