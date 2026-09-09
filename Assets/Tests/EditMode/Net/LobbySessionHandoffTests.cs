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
    public void TeamOnly_DoesNotInventNicknameOrLoadout()
    {
        handoff.SetTeam(1, 2);

        Assert.IsTrue(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(2, team);
        Assert.IsFalse(handoff.TryGetNickname(1, out string name));
        Assert.IsNull(name);
        Assert.IsFalse(handoff.TryGetLoadout(1, out byte[] order));
        Assert.IsNull(order, "No submission must still allow the configured default loadout.");
    }

    [Test]
    public void NicknameOnly_DoesNotInventTeamOrLoadout()
    {
        handoff.SetNickname(1, "Ada");

        Assert.IsTrue(handoff.TryGetNickname(1, out string name));
        Assert.AreEqual("Ada", name);
        Assert.IsFalse(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(0, team);
        Assert.IsFalse(handoff.TryGetLoadout(1, out byte[] order));
        Assert.IsNull(order);
    }

    [Test]
    public void LoadoutOnly_DoesNotInventTeamOrNickname()
    {
        handoff.SetLoadout(1, new byte[] { 3, 1, 2 });

        Assert.IsTrue(handoff.TryGetLoadout(1, out byte[] order));
        CollectionAssert.AreEqual(new byte[] { 3, 1, 2 }, order);
        Assert.IsFalse(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(0, team);
        Assert.IsFalse(handoff.TryGetNickname(1, out string name));
        Assert.IsNull(name);
    }

    [Test]
    public void LateTeamAndNickname_PreserveAnEarlierLoadout()
    {
        handoff.SetLoadout(1, new byte[] { 3, 1, 2 });
        handoff.SetNickname(1, "Ada");
        handoff.SetTeam(1, 2);

        AssertChoices(handoff, 1, 2, "Ada", new byte[] { 3, 1, 2 });
    }

    [Test]
    public void UpdatingEachChoice_PreservesTheOtherFields()
    {
        SeedChoices(handoff, 1);

        handoff.SetTeam(1, 2);
        AssertChoices(handoff, 1, 2, "Ada", new byte[] { 3, 1, 2 });

        handoff.SetNickname(1, "Grace");
        AssertChoices(handoff, 1, 2, "Grace", new byte[] { 3, 1, 2 });

        handoff.SetLoadout(1, new byte[] { 2, 3, 1 });
        AssertChoices(handoff, 1, 2, "Grace", new byte[] { 2, 3, 1 });
    }

    [Test]
    public void RecordedDefaultValues_AreNotMistakenForMissingFields()
    {
        // Validation belongs to the command paths, not to this projection's presence checks.
        handoff.SetTeam(1, 0);
        handoff.SetNickname(1, null);
        handoff.SetLoadout(1, null);

        Assert.IsTrue(handoff.TryGetTeam(1, out int team));
        Assert.AreEqual(0, team);
        Assert.IsTrue(handoff.TryGetNickname(1, out string name));
        Assert.IsNull(name);
        Assert.IsTrue(handoff.TryGetLoadout(1, out byte[] order));
        Assert.IsNull(order);

        handoff.SetNickname(1, "");
        handoff.SetLoadout(1, new byte[0]);
        AssertChoices(handoff, 1, 0, "", new byte[0]);
    }

    [Test]
    public void IndependentInstances_CannotUpdateRemoveOrResetEachOthersChoices()
    {
        var newerSession = new LobbySessionHandoff();
        SeedChoices(handoff, 1);
        newerSession.SetTeam(1, 2);
        newerSession.SetNickname(1, "Grace");
        newerSession.SetLoadout(1, new byte[] { 2, 3, 1 });

        handoff.SetTeam(1, 2);
        handoff.SetNickname(1, "Alan");
        handoff.SetLoadout(1, new byte[] { 1, 2, 3 });
        AssertChoices(newerSession, 1, 2, "Grace", new byte[] { 2, 3, 1 });

        handoff.RemovePlayer(1);
        AssertChoices(newerSession, 1, 2, "Grace", new byte[] { 2, 3, 1 });

        SeedChoices(handoff, 1);
        handoff.Reset();
        AssertChoices(newerSession, 1, 2, "Grace", new byte[] { 2, 3, 1 });
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
        AssertChoices(handoff, 2, 1, "Ada", new byte[] { 3, 1, 2 });
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
        Assert.IsFalse(handoff.TryGetLoadout(1, out _));
    }

    [Test]
    public void Reset_ClearsFullAndPartialEntriesTogether_AndCanBeRepeated()
    {
        SeedChoices(handoff, 1);
        handoff.SetTeam(2, 2);
        handoff.SetNickname(3, "Grace");
        handoff.SetLoadout(4, new byte[] { 2, 3, 1 });

        handoff.Reset();
        handoff.Reset();

        for (int playerId = 1; playerId <= 4; playerId++)
            AssertMissing(playerId);

        handoff.SetNickname(1, "New session");
        Assert.IsTrue(handoff.TryGetNickname(1, out string name));
        Assert.AreEqual("New session", name);
        Assert.IsFalse(handoff.TryGetTeam(1, out _));
        Assert.IsFalse(handoff.TryGetLoadout(1, out _));
    }

    private static void SeedChoices(LobbySessionHandoff store, int playerId)
    {
        store.SetTeam(playerId, 1);
        store.SetNickname(playerId, "Ada");
        store.SetLoadout(playerId, new byte[] { 3, 1, 2 });
    }

    private static void AssertChoices(LobbySessionHandoff store, int playerId, int team,
                                      string nickname, byte[] order)
    {
        Assert.IsTrue(store.TryGetTeam(playerId, out int actualTeam));
        Assert.AreEqual(team, actualTeam);
        Assert.IsTrue(store.TryGetNickname(playerId, out string actualName));
        Assert.AreEqual(nickname, actualName);
        Assert.IsTrue(store.TryGetLoadout(playerId, out byte[] actualOrder));
        CollectionAssert.AreEqual(order, actualOrder);
    }

    private void AssertMissing(int playerId)
    {
        Assert.IsFalse(handoff.TryGetTeam(playerId, out int team));
        Assert.AreEqual(0, team);
        Assert.IsFalse(handoff.TryGetNickname(playerId, out string name));
        Assert.IsNull(name);
        Assert.IsFalse(handoff.TryGetLoadout(playerId, out byte[] order));
        Assert.IsNull(order);
    }
}
