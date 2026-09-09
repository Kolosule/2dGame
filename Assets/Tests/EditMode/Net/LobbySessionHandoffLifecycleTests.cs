using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Exercises the owner's state boundaries without starting a Fusion connection or loading scenes.
/// Like PlayerIdentityTests, reflection bridges the test asmdef to Assembly-CSharp.
/// </summary>
public class LobbySessionHandoffLifecycleTests
{
    private const int PlayerId = 5;
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly List<Component> owners = new List<Component>();
    private Type managerType;
    private Type reconnectType;
    private Type runnerType;
    private Type playerType;
    private PropertyInfo instanceProperty;
    private object originalInstance;
    private GameObject root;
    private Component owner;

    [OneTimeSetUp]
    public void FindOwnerMembers()
    {
        managerType = Type.GetType("GameNetworkManager, Assembly-CSharp", true);
        reconnectType = Type.GetType("ReconnectController, Assembly-CSharp", true);
        runnerType = managerType.GetField("runner", InstanceMembers).FieldType;
        playerType = managerType.GetMethod("TryGetLobbyTeam").GetParameters()[0].ParameterType;
        instanceProperty = managerType.GetProperty("Instance");
    }

    [SetUp]
    public void CreateSessionOwner()
    {
        originalInstance = instanceProperty.GetValue(null);
        root = new GameObject(nameof(LobbySessionHandoffLifecycleTests));
        // Do not run Awake/Start: batchmode Start would boot a real dedicated server.
        root.SetActive(false);
        owner = CreateOwner("Session owner");
        instanceProperty.SetValue(null, owner);
    }

    [TearDown]
    public void DestroySessionOwners()
    {
        foreach (Component component in owners)
            SetField(component, "runner", null);
        UnityEngine.Object.DestroyImmediate(root);
        owners.Clear();
        instanceProperty.SetValue(null, originalInstance);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FreshSession_ResetsAllChoices_EvenOnAnUnusedRunner(bool asClient)
    {
        SeedChoices(owner);
        LobbySessionHandoff handoff = Handoff(owner);
        SetField(owner, "hasBeenConnected", true);
        SetField(owner, "intentionalDisconnect", true);
        SetField(owner, "connectedSessionName", "Previous room");

        Invoke(owner, "BeginSession", asClient);

        Assert.AreSame(handoff, Handoff(owner));
        AssertMissing(owner);
        Assert.AreEqual(asClient, GetField<bool>(owner, "startedAsClient"));
        Assert.IsFalse(GetField<bool>(owner, "hasBeenConnected"));
        Assert.IsFalse(GetField<bool>(owner, "intentionalDisconnect"));
        Assert.IsNull(GetField<string>(owner, "connectedSessionName"));
    }

    [Test]
    public void BuildingAReplacementRunner_DoesNotResetHandoff()
    {
        SeedChoices(owner);
        Component previousRunner = CreateRunner("Previous runner");
        SetField(owner, "runner", previousRunner);

        Invoke(owner, "BuildRunner");

        Component currentRunner = GetField<Component>(owner, "runner");
        Assert.AreNotSame(previousRunner, currentRunner);
        Assert.IsTrue((bool)Invoke(owner, "OwnsRunner", currentRunner));
        Assert.IsFalse((bool)Invoke(owner, "OwnsRunner", previousRunner));
        AssertChoices(owner);
    }

    [Test]
    public void RetryCleanup_PreservesHandoff_ButKeepsOtherStoresRunnerScoped()
    {
        SeedChoices(owner);
        SetField(owner, "startedAsClient", true);
        SetField(owner, "hasBeenConnected", true);
        ReconnectRegistry holds = GetField<ReconnectRegistry>(owner, "reconnectRegistry");
        LobbyNicknameBook nicknames = GetField<LobbyNicknameBook>(owner, "nicknames");
        LobbyServerState lobby = GetField<LobbyServerState>(owner, "serverLobby");
        holds.Capture("token", new ReconnectHeldSlot { Team = 2 });
        nicknames.RememberLocal("Ada");
        lobby.PlayerJoined(PlayerId);

        for (int attempt = 0; attempt < ReconnectBackoff.MaxAttempts; attempt++)
        {
            Invoke(owner, "ShutdownCleanup");
            AssertChoices(owner);
        }

        Assert.AreEqual(0, holds.HeldCount);
        Assert.AreEqual("", nicknames.LocalNickname);
        Assert.AreEqual(0, GetField<LobbyServerState>(owner, "serverLobby").PlayerCount);
    }

    [TestCase(false, true, false, true)]
    [TestCase(true, false, false, true)]
    [TestCase(true, true, true, true)]
    [TestCase(true, true, false, false)]
    public void TerminalRunnerCleanup_ResetsAllChoices(
        bool asClient, bool wasConnected, bool intentional, bool hasReconnectController)
    {
        SeedChoices(owner);
        SetField(owner, "startedAsClient", asClient);
        SetField(owner, "hasBeenConnected", wasConnected);
        SetField(owner, "intentionalDisconnect", intentional);
        if (!hasReconnectController)
            SetField(owner, "reconnectController", null);

        Invoke(owner, "ShutdownCleanup");
        Invoke(owner, "ShutdownCleanup");

        AssertMissing(owner);
    }

    [Test]
    public void CancelOrExhaustedRetries_ResetEvenWhenTheRunnerIsAlreadyGone()
    {
        SeedChoices(owner);
        SetField(owner, "startedAsClient", true);
        SetField(owner, "hasBeenConnected", true);
        Assert.IsNull(GetField<Component>(owner, "runner"));

        // FallBackToMenu uses this before teardown for both cancellation and exhausted retries.
        Invoke(owner, "MarkIntentionalDisconnect");
        Invoke(owner, "ShutdownCleanup");

        AssertMissing(owner);
        Assert.IsTrue(GetField<bool>(owner, "intentionalDisconnect"));
    }

    [Test]
    public void GameplaySceneLoadCallbacks_KeepTheSessionChoices()
    {
        SeedChoices(owner);
        Component runner = CreateRunner("Scene runner");
        SetField(owner, "runner", runner);
        SetField(owner, "menuSceneIndex", int.MinValue);

        Invoke(owner, "OnSceneLoadStart", runner);
        Invoke(owner, "OnSceneLoadDone", runner);

        AssertChoices(owner);
    }

    [Test]
    public void ReturningToLobby_ExpiresMatchHolds_ButKeepsChoicesRosterAndIdentity()
    {
        SeedChoices(owner);
        SetField(owner, "gameStarting", true);
        LobbyServerState lobby = GetField<LobbyServerState>(owner, "serverLobby");
        lobby.PlayerJoinedOnTeam(PlayerId, 2);
        LobbyNicknameBook nicknames = GetField<LobbyNicknameBook>(owner, "nicknames");
        nicknames.RememberLocal("Ada");
        var held = new ReconnectHeldSlot { Team = 2, DisplayName = "Ada" };
        ReconnectRegistry holds = GetField<ReconnectRegistry>(owner, "reconnectRegistry");
        holds.Capture("token", held);
        IDictionary pending = GetField<IDictionary>(owner, "pendingRestores");
        IDictionary tokens = GetField<IDictionary>(owner, "tokensByPlayer");
        object player = CreatePlayer();
        pending.Add(player, held);
        tokens.Add(player, "token");

        Invoke(owner, "PrepareReturnToLobby");

        AssertChoices(owner);
        Assert.AreEqual(0, holds.HeldCount);
        Assert.AreEqual(0, pending.Count);
        Assert.AreEqual("token", tokens[player]);
        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(2, lobby.TeamOf(PlayerId));
        Assert.AreEqual("Ada", nicknames.LocalNickname);
        Assert.IsFalse(GetField<bool>(owner, "gameStarting"));
    }

    [TestCase("OnShutdown")]
    [TestCase("OnDisconnectedFromServer")]
    public void StaleRunnerCallback_CannotResetANewerSession(string callback)
    {
        Component previousRunner = CreateRunner("Previous runner");
        SetField(owner, "runner", previousRunner);
        Invoke(owner, "BeginSession", true);
        SetField(owner, "runner", CreateRunner("Current runner"));
        SeedChoices(owner);

        RunnerEndCallback(callback, previousRunner);

        AssertChoices(owner);
    }

    [Test]
    public void CurrentRunnerShutdown_ResetsAnEndedSession_Idempotently()
    {
        Component runner = CreateRunner("Current runner");
        SetField(owner, "runner", runner);
        SeedChoices(owner);

        RunnerEndCallback("OnShutdown", runner);
        RunnerEndCallback("OnShutdown", runner);

        AssertMissing(owner);
    }

    [Test]
    public void TerminalDisconnectWithoutShutdown_ResetsTheHandoff()
    {
        Component runner = CreateRunner("Current runner");
        SetField(owner, "runner", runner);
        SeedChoices(owner);
        Type reasonType = managerType.GetMethod("OnDisconnectedFromServer").GetParameters()[1].ParameterType;
        object reason = Enum.ToObject(reasonType, 0);
        LogAssert.Expect(LogType.Warning, $"Disconnected from server: {reason}");

        Invoke(owner, "OnDisconnectedFromServer", runner, reason);

        AssertMissing(owner);
    }

    [TestCase("OnDestroy")]
    [TestCase("OnApplicationQuit")]
    public void OwnerEnds_ResetsItsHandoffWithoutARunner(string callback)
    {
        SeedChoices(owner);

        Invoke(owner, callback);

        AssertMissing(owner);
    }

    [Test]
    public void DuplicateMenuManager_StartAndDestroyDoNotTouchPersistentOwner()
    {
        SeedChoices(owner);
        Component duplicate = CreateOwner("Duplicate menu manager");
        SeedChoices(duplicate);

        Invoke(duplicate, "Start");
        Invoke(duplicate, "OnDestroy");

        Assert.AreSame(owner, instanceProperty.GetValue(null));
        AssertChoices(owner);
        AssertMissing(duplicate);
    }

    [Test]
    public void OldOwnerDestruction_CannotResetTheNewActiveOwnersHandoff()
    {
        SeedChoices(owner);
        Component newOwner = CreateOwner("New session owner");
        SeedChoices(newOwner);
        instanceProperty.SetValue(null, newOwner);

        Invoke(owner, "OnDestroy");

        Assert.AreSame(newOwner, instanceProperty.GetValue(null));
        AssertMissing(owner);
        AssertChoices(newOwner);
    }

    [Test]
    public void SpawnFacade_ConvertsPlayerRefToPrimitiveId_AndPreservesMissingFields()
    {
        object player = CreatePlayer();
        Handoff(owner).SetTeam(PlayerId, 2);
        object[] teamArgs = { player, 0 };
        object[] nicknameArgs = { player, null };
        object[] loadoutArgs = { player, null };

        Assert.IsTrue((bool)Invoke(owner, "TryGetLobbyTeam", teamArgs));
        Assert.AreEqual(2, teamArgs[1]);
        Assert.IsFalse((bool)Invoke(owner, "TryGetLobbyNickname", nicknameArgs));
        Assert.IsNull(nicknameArgs[1]);
        Assert.IsFalse((bool)Invoke(owner, "TryGetLobbyLoadout", loadoutArgs));
        Assert.IsNull(loadoutArgs[1]);

        SeedChoices(owner);
        Assert.IsTrue((bool)Invoke(owner, "TryGetLobbyNickname", nicknameArgs));
        Assert.AreEqual("Ada", nicknameArgs[1]);
        Assert.IsTrue((bool)Invoke(owner, "TryGetLobbyLoadout", loadoutArgs));
        CollectionAssert.AreEqual(new byte[] { 3, 1, 2 }, (byte[])loadoutArgs[1]);
    }

    private Component CreateOwner(string name)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(root.transform, false);
        Component component = gameObject.AddComponent(managerType);
        Component reconnect = gameObject.AddComponent(reconnectType);
        SetField(component, "reconnectController", reconnect);
        owners.Add(component);
        return component;
    }

    private Component CreateRunner(string name)
    {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(root.transform, false);
        return gameObject.AddComponent(runnerType);
    }

    private object CreatePlayer() =>
        playerType.GetMethod("FromIndex").Invoke(null, new object[] { PlayerId });

    private void RunnerEndCallback(string callback, Component runner)
    {
        Type reasonType = managerType.GetMethod(callback).GetParameters()[1].ParameterType;
        Invoke(owner, callback, runner, Enum.ToObject(reasonType, 0));
    }

    private LobbySessionHandoff Handoff(Component component) =>
        GetField<LobbySessionHandoff>(component, "lobbyHandoff");

    private void SeedChoices(Component component)
    {
        LobbySessionHandoff handoff = Handoff(component);
        handoff.SetTeam(PlayerId, 2);
        handoff.SetNickname(PlayerId, "Ada");
        handoff.SetLoadout(PlayerId, new byte[] { 3, 1, 2 });
    }

    private void AssertChoices(Component component)
    {
        LobbySessionHandoff handoff = Handoff(component);
        Assert.IsTrue(handoff.TryGetTeam(PlayerId, out int team));
        Assert.AreEqual(2, team);
        Assert.IsTrue(handoff.TryGetNickname(PlayerId, out string name));
        Assert.AreEqual("Ada", name);
        Assert.IsTrue(handoff.TryGetLoadout(PlayerId, out byte[] order));
        CollectionAssert.AreEqual(new byte[] { 3, 1, 2 }, order);
    }

    private void AssertMissing(Component component)
    {
        LobbySessionHandoff handoff = Handoff(component);
        Assert.IsFalse(handoff.TryGetTeam(PlayerId, out _));
        Assert.IsFalse(handoff.TryGetNickname(PlayerId, out _));
        Assert.IsFalse(handoff.TryGetLoadout(PlayerId, out _));
    }

    private object Invoke(Component component, string method, params object[] args) =>
        managerType.GetMethod(method, InstanceMembers).Invoke(component, args);

    private T GetField<T>(Component component, string name) =>
        (T)managerType.GetField(name, InstanceMembers).GetValue(component);

    private void SetField(Component component, string name, object value) =>
        managerType.GetField(name, InstanceMembers).SetValue(component, value);
}
