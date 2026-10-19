using System;
using System.Collections;
using System.Reflection;
using Fusion;
using Fusion.Addons.Physics;
using Game.Combat.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

public class DirectionalMeleeSimulationTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private NetworkRunner runner;
    private GameObject settingsObject;
    private GameObject floor;
    private ScriptableObject config;
    private Type inputType;

    private sealed class Player
    {
        public NetworkObject Object;
        public Component Combat;
        public Component Movement;
        public Component Stats;
        public Component Animator;
        public Rigidbody2D Body;
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode();
        inputType = Type.GetType("NetInput, Assembly-CSharp", true);
        config = ScriptableObject.CreateInstance(Type.GetType("CombatConfig, Assembly-CSharp", true));
        settingsObject = new GameObject("Directional combat settings");
        settingsObject.SetActive(false);
        Component settings = settingsObject.AddComponent(Type.GetType("GameSettingsManager, Assembly-CSharp", true));
        SetField(settings, "combatConfig", config);
        settingsObject.SetActive(true);

        floor = new GameObject("Directional combat floor");
        floor.layer = LayerMask.NameToLayer("Ground");
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.AddComponent<BoxCollider2D>().size = new Vector2(100f, 1f);

        var runnerObject = new GameObject("Directional combat runner");
        runner = runnerObject.AddComponent<NetworkRunner>();
        runnerObject.AddComponent<RunnerSimulatePhysics2D>();
        var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
        var start = runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Single,
            SceneManager = sceneManager,
            Config = NetworkProjectConfig.Global
        });
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!start.IsCompleted && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.IsTrue(start.IsCompleted,
            $"Offline Fusion startup timed out (playing={Application.isPlaying}, paused={EditorApplication.isPaused}, running={runner.IsRunning}).");
        Assert.IsFalse(start.IsFaulted, start.Exception?.ToString());
        Assert.IsTrue(start.Result.Ok, start.Result.ToString());
        Step(8);
        Assert.Greater((int)runner.Tick, 3);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (runner != null)
        {
            var shutdown = runner.Shutdown();
            while (!shutdown.IsCompleted) yield return null;
        }
        if (settingsObject != null) Object.DestroyImmediate(settingsObject);
        if (config != null) Object.DestroyImmediate(config);
        if (floor != null) Object.DestroyImmediate(floor);
        yield return new ExitPlayMode();
    }

    [Test]
    public void GroundedDownDoesNotCommitAndUpCanStartImmediately()
    {
        Player player = SpawnPlayer(new Vector2(0f, 1.05f), "Team1", inputAuthority: true);
        Assert.IsTrue((bool)Invoke(player.Movement, "IsGrounded"));
        player.Body.linearVelocity = new Vector2(3f, 0f);
        Swing(player, -1, pressed: true);

        Assert.AreEqual(0, Get<int>(player.Combat, "AttackStartTick"));
        Assert.IsTrue(Get<TickTimer>(player.Combat, "AttackCooldownTimer").ExpiredOrNotRunning(runner));
        Assert.IsTrue(Get<TickTimer>(player.Animator, "ActionTimer").ExpiredOrNotRunning(runner));
        Assert.AreEqual(new Vector2(3f, 0f), player.Body.linearVelocity);

        Swing(player, 1, pressed: true);
        Invoke(player.Animator, "Simulate");
        Assert.AreEqual((int)runner.Tick, Get<int>(player.Combat, "AttackStartTick"));
        Assert.AreEqual(1, Get<int>(player.Combat, "AttackAim"));
        Assert.AreEqual("AttackUp", Get<object>(player.Animator, "OverrideState").ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LandingCancelsDownPermanentlyWithoutResettingCommitment(bool active)
    {
        Player attacker = SpawnPlayer(new Vector2(0f, 10f), "Team1", inputAuthority: true);
        Player target = SpawnPlayer(new Vector2(0f, 8.63f), "Team2");
        Swing(attacker, -1, pressed: true);
        if (active) ActivateSwing(attacker);
        int startTick = Get<int>(attacker.Combat, "AttackStartTick");
        TickTimer cooldown = Get<TickTimer>(attacker.Combat, "AttackCooldownTimer");

        Place(attacker.Object, new Vector2(0f, 1.05f));
        Swing(attacker, -1);
        Assert.IsTrue((bool)Get<NetworkBool>(attacker.Combat, "DownAttackCancelled"));
        Assert.AreEqual(startTick, Get<int>(attacker.Combat, "AttackStartTick"));
        Assert.AreEqual(cooldown, Get<TickTimer>(attacker.Combat, "AttackCooldownTimer"));
        Assert.IsTrue(Get<bool>(attacker.Combat, "IsSwingCommitted"));
        Assert.IsTrue(Get<TickTimer>(attacker.Animator, "ActionTimer").ExpiredOrNotRunning(runner));

        Place(attacker.Object, new Vector2(0f, 10f));
        ActivateSwing(attacker);
        Swing(attacker, 1);
        Assert.AreEqual(100f, Get<float>(target.Stats, "CurrentHealth"));
        Assert.AreEqual(-1, Get<int>(attacker.Combat, "AttackAim"));
    }

    [Test]
    public void LandingDoesNotCancelAnUnrelatedShootPose()
    {
        Player player = SpawnPlayer(new Vector2(0f, 10f), "Team1", inputAuthority: true);
        Swing(player, -1, pressed: true);
        Invoke(player.Animator, "TriggerShoot");
        TickTimer shootTimer = Get<TickTimer>(player.Animator, "ActionTimer");
        Place(player.Object, new Vector2(0f, 1.05f));
        Swing(player, -1);
        Invoke(player.Animator, "Simulate");
        Assert.AreEqual(shootTimer, Get<TickTimer>(player.Animator, "ActionTimer"));
        Assert.AreEqual("Shoot", Get<object>(player.Animator, "OverrideState").ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DownSpikesNpcAndOpponentOnceWithoutMovingAttacker(bool historical)
    {
        SetField(config, "enableLagCompensation", historical);
        Player attacker = SpawnPlayer(new Vector2(0f, 10f), "Team1", inputAuthority: true);
        Player target = SpawnPlayer(new Vector2(0.7f, 8.63f), "Team2");
        NetworkObject enemy = SpawnEnemy(new Vector2(-0.7f, 8.63f));
        Component enemyHealth = Component(enemy, "Enemy");
        Rigidbody2D enemyBody = enemy.GetComponent<Rigidbody2D>();
        Step(2);
        Assert.IsTrue(runner.LocalPlayer.IsRealPlayer);
        Assert.IsNotNull(runner.LagCompensation);

        Invoke(target.Movement, "StartDash");
        Set(target.Movement, "JumpBufferCounter", 5);
        target.Body.linearVelocity = new Vector2(3f, 25f);
        enemyBody.linearVelocity = new Vector2(-3f, 25f);
        attacker.Body.linearVelocity = new Vector2(4f, 9f);
        Swing(attacker, -1, pressed: true);
        ActivateSwing(attacker);
        Swing(attacker, 1);
        Invoke(attacker.Animator, "Simulate");

        Assert.AreEqual(75f, Get<float>(target.Stats, "CurrentHealth"));
        Assert.AreEqual(75, Get<int>(enemyHealth, "CurrentHealth"));
        Assert.AreEqual(new Vector2(3f, -20f), target.Body.linearVelocity);
        Assert.AreEqual(new Vector2(-3f, -20f), enemyBody.linearVelocity);
        Assert.AreEqual(new Vector2(4f, 9f), attacker.Body.linearVelocity);
        Assert.IsFalse((bool)Invoke(target.Movement, "IsDashing"));
        Assert.AreEqual(0, Get<int>(target.Movement, "JumpBufferCounter"));
        Assert.IsTrue((bool)Invoke(enemyHealth, "IsKnockedBack"));
        Assert.AreEqual("AttackDown", Get<object>(attacker.Animator, "OverrideState").ToString());

        target.Body.linearVelocity = new Vector2(3f, 6f);
        enemyBody.linearVelocity = new Vector2(-3f, 6f);
        Swing(attacker, -1);
        Assert.AreEqual(75f, Get<float>(target.Stats, "CurrentHealth"));
        Assert.AreEqual(75, Get<int>(enemyHealth, "CurrentHealth"));
        Assert.AreEqual(6f, target.Body.linearVelocity.y);
        Assert.AreEqual(6f, enemyBody.linearVelocity.y);
    }

    [TestCase("friendly")]
    [TestCase("immune")]
    [TestCase("dead")]
    [TestCase("self")]
    [TestCase("cooldown")]
    public void RejectedTargetsKeepTheirVelocityAndDash(string rejection)
    {
        Player attacker = SpawnPlayer(new Vector2(0f, 10f), "Team1", inputAuthority: true);
        Player target = rejection == "self" ? attacker
            : SpawnPlayer(new Vector2(0f, 8.63f), rejection == "friendly" ? "Team1" : "Team2",
                immune: rejection == "immune");
        if (rejection == "dead") Set(target.Stats, "IsDead", true);
        if (rejection == "cooldown")
            Invoke(target.Stats, "ServerApplyDamage", 1f, attacker.Object.Id);
        float health = Get<float>(target.Stats, "CurrentHealth");
        Invoke(target.Movement, "StartDash");
        target.Body.linearVelocity = new Vector2(3f, 8f);

        Invoke(attacker.Combat, "TryApplyPlayerMeleeHit",
            target.Stats, new AttackHitRegistry(), false, -1, false);

        Assert.AreEqual(health, Get<float>(target.Stats, "CurrentHealth"));
        Assert.AreEqual(new Vector2(3f, 8f), target.Body.linearVelocity);
        Assert.IsTrue((bool)Invoke(target.Movement, "IsDashing"));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void OrdinaryHitsDoNotInheritAnEarlierSpike(int hitAim)
    {
        Player attacker = SpawnPlayer(new Vector2(0f, 10f), "Team1", inputAuthority: true);
        Player target = SpawnPlayer(new Vector2(1.37f, 10f), "Team2");
        NetworkObject enemy = SpawnEnemy(new Vector2(1.37f, 11f));
        Set(attacker.Combat, "AttackAim", -1);
        target.Body.linearVelocity = Vector2.zero;
        enemy.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();

        Invoke(attacker.Combat, "ApplyMeleeHits", new Vector2(1.37f, 10f),
            new Vector2(2f, 3f), new AttackHitRegistry(), hitAim);

        Assert.AreEqual(75f, Get<float>(target.Stats, "CurrentHealth"));
        Assert.AreEqual(75, Get<int>(Component(enemy, "Enemy"), "CurrentHealth"));
        Assert.Greater(target.Body.linearVelocity.y, 0f);
        Assert.Greater(enemy.GetComponent<Rigidbody2D>().linearVelocity.y, 0f);
    }

    [Test]
    public void AimAndFacingStayCommittedWhenTheAttackerTurns()
    {
        foreach (int aim in new[] { -1, 0, 1 })
        {
            Player player = SpawnPlayer(new Vector2(aim * 5f, 10f), "Team1", inputAuthority: true);
            Swing(player, aim, pressed: true);
            object input = Input(aim == 0 ? 1 : -aim);
            inputType.GetField("Horizontal").SetValue(input, (sbyte)-1);
            Invoke(player.Movement, "Simulate", input, default(NetworkButtons), default(NetworkButtons));
            Vector2 velocity = player.Body.linearVelocity;
            ActivateSwing(player);
            Invoke(player.Combat, "Simulate", input, default(NetworkButtons));

            Assert.IsFalse((bool)Invoke(player.Movement, "IsFacingRight"));
            Assert.IsTrue((bool)Get<NetworkBool>(player.Combat, "AttackFacingRight"));
            Assert.AreEqual(aim, Get<int>(player.Combat, "AttackAim"));
            Assert.AreEqual(velocity, player.Body.linearVelocity, "A missed swing must not move its attacker.");
            object[] box = { Vector2.zero, Vector2.zero };
            Invoke(player.Combat, "ResolveSwingBox", box);
            Vector2 offset = aim == 0 ? new Vector2(1.37f, 0f) : new Vector2(0f, aim * 1.37f);
            Assert.Less(Vector2.Distance((Vector2)player.Object.transform.position + offset,
                (Vector2)box[0]), 0.0001f);
        }
    }

    [Test]
    public void SpikeSurvivesMovementAndStopsAtTheFloorWithoutLockingJump()
    {
        Player player = SpawnPlayer(new Vector2(-6f, 3f), "Team2");
        NetworkObject enemy = SpawnEnemy(new Vector2(6f, 3f));
        Invoke(player.Movement, "StartDash");
        player.Body.linearVelocity = new Vector2(0f, 25f);
        Invoke(player.Movement, "ApplySpike", 20f);
        Invoke(Component(enemy, "Enemy"), "ServerTakeSpikeDamage", 1, 20f);

        Invoke(player.Movement, "Simulate", Input(0), default(NetworkButtons), default(NetworkButtons));
        Assert.AreEqual(-20f, player.Body.linearVelocity.y);
        Step(20);
        Assert.GreaterOrEqual(player.Object.GetComponent<Collider2D>().bounds.min.y, -0.05f);
        Assert.GreaterOrEqual(enemy.GetComponent<Collider2D>().bounds.min.y, -0.05f);
        Assert.Less(player.Body.position.y, 2f);
        Assert.IsTrue((bool)Invoke(player.Movement, "IsGrounded"));

        NetworkButtons jump = default;
        jump.Set(0, true);
        Invoke(player.Movement, "Simulate", Input(0), jump, default(NetworkButtons));
        Assert.Greater(player.Body.linearVelocity.y, 0f);
    }

    private Player SpawnPlayer(Vector2 position, string team, bool inputAuthority = false, bool immune = false)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/Player/PlayerPrefab.prefab");
        NetworkObject obj = runner.Spawn(prefab, position, Quaternion.identity,
            inputAuthority ? runner.LocalPlayer : PlayerRef.None);
        var player = new Player
        {
            Object = obj,
            Combat = Component(obj, "PlayerCombat"),
            Movement = Component(obj, "PlayerMovement"),
            Stats = Component(obj, "PlayerStatsHandler"),
            Animator = Component(obj, "PlayerAnimator"),
            Body = obj.GetComponent<Rigidbody2D>()
        };
        Component teamData = Component(obj, "PlayerTeamData");
        Invoke(teamData, "SetTeam", Enum.Parse(Type.GetType("Team, Assembly-CSharp", true), team));
        if (!immune) Set(player.Stats, "SpawnImmunityTimer", default(TickTimer));
        Place(obj, position);
        return player;
    }

    private NetworkObject SpawnEnemy(Vector2 position)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/Enemy/Prefabs/BlueEnemy.prefab");
        NetworkObject enemy = runner.Spawn(prefab, position, Quaternion.identity);
        Set(Component(enemy, "Enemy"), "CurrentHealth", 100);
        Place(enemy, position);
        return enemy;
    }

    private void Step(int ticks)
    {
        for (int i = 0; i < ticks; i++) runner.UpdateInternal(runner.DeltaTime);
    }

    private void Swing(Player player, int aim, bool pressed = false)
    {
        NetworkButtons buttons = default;
        buttons.Set(2, pressed);
        Invoke(player.Combat, "Simulate", Input(aim), buttons);
    }

    private object Input(int aim)
    {
        object input = Activator.CreateInstance(inputType);
        inputType.GetField("VerticalAim").SetValue(input, (sbyte)aim);
        return input;
    }

    private void ActivateSwing(Player player) =>
        Set(player.Combat, "AttackStartTick", (int)runner.Tick - 3);

    private static void Place(NetworkObject obj, Vector2 position)
    {
        obj.GetComponent<NetworkRigidbody2D>().Teleport(position);
        Physics2D.SyncTransforms();
    }

    private static Component Component(NetworkObject obj, string name) =>
        obj.GetComponent(Type.GetType(name + ", Assembly-CSharp", true));

    private static object Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Members).Invoke(target, args);

    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetProperty(name, Members).GetValue(target);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetProperty(name, Members).SetValue(target, value);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, Members).SetValue(target, value);
}
