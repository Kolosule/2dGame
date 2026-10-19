using System.Linq;
using Game.PlayerAnimation.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class DirectionalAttackAnimationTests
{
    [Test]
    public void DirectionalStatesPreserveExistingSerializedIds()
    {
        Assert.AreEqual(5, (int)AnimState.Attack);
        Assert.AreEqual(6, (int)AnimState.AttackDown);
        Assert.AreEqual(7, (int)AnimState.Shoot);
        Assert.AreEqual(8, (int)AnimState.Stunned);
        Assert.AreEqual(9, (int)AnimState.Dead);
        Assert.AreEqual(10, (int)AnimState.AttackUp);
        Assert.AreEqual(255, (int)AnimState.None);
    }

    [TestCase(AnimState.Attack, "WeaponAttackSwing")]
    [TestCase(AnimState.AttackDown, "WeaponAttackDown")]
    [TestCase(AnimState.AttackUp, "WeaponAttackUp")]
    public void ControllersSelectTheMatchingWeaponAndShareTheBodyPose(AnimState state, string clipName)
    {
        AnimatorState weapon = StateFor("Weapon", state);
        Assert.AreEqual(clipName, weapon.motion.name);
        Assert.AreSame(StateFor("Player", AnimState.Attack).motion, StateFor("Player", state).motion);
    }

    [TestCase("WeaponAttackDown")]
    [TestCase("WeaponAttackUp")]
    public void DirectionalClipsCopyEverySideSpriteAndItsTiming(string name)
    {
        AnimationClip side = Clip("WeaponAttackSwing");
        AnimationClip vertical = Clip(name);
        EditorCurveBinding binding = AnimationUtility.GetObjectReferenceCurveBindings(side).Single();
        ObjectReferenceKeyframe[] expected = AnimationUtility.GetObjectReferenceCurve(side, binding);
        ObjectReferenceKeyframe[] actual = AnimationUtility.GetObjectReferenceCurve(vertical, binding);
        Assert.AreEqual(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i].time, actual[i].time);
            Assert.AreSame(expected[i].value, actual[i].value);
        }
        Assert.AreEqual(side.length, vertical.length, 0.0001f);
        Assert.IsFalse(vertical.isLooping);
    }

    [TestCase(AnimState.AttackUp, 1f, 1f)]
    [TestCase(AnimState.AttackUp, 1f, -1f)]
    [TestCase(AnimState.AttackDown, -1f, 1f)]
    [TestCase(AnimState.AttackDown, -1f, -1f)]
    public void WeaponPointsVerticallyAndReturnsToSideAndHidden(AnimState state, float aim, float facing)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Scripts/Player/PlayerPrefab.prefab");
        var root = new GameObject("Directional animation test");
        try
        {
            root.transform.localScale = new Vector3(facing, 1f, 1f);
            Transform pivot = Object.Instantiate(prefab.transform.Find("WeaponFacing"), root.transform);
            Animator animator = pivot.GetComponentInChildren<Animator>();
            Transform weapon = animator.transform;
            Vector3 sidePosition = weapon.localPosition;
            Quaternion sideRotation = weapon.localRotation;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);

            Show(animator, state);
            Assert.AreEqual(0f, weapon.localPosition.x, 0.0001f);
            Assert.AreEqual(aim * sidePosition.x, weapon.localPosition.y, 0.0001f);
            Vector3 direction = weapon.TransformVector(Vector3.left).normalized;
            Assert.Greater(Vector3.Dot(direction, Vector3.up * aim), 0.999f);
            Assert.IsNotNull(weapon.GetComponent<SpriteRenderer>().sprite);

            Show(animator, AnimState.Attack);
            Assert.Less(Vector3.Distance(sidePosition, weapon.localPosition), 0.0001f);
            Assert.Less(Quaternion.Angle(sideRotation, weapon.localRotation), 0.01f);
            direction = weapon.TransformVector(Vector3.left).normalized;
            Assert.Greater(Vector3.Dot(direction, Vector3.right * facing), 0.999f);

            Show(animator, AnimState.Idle);
            Assert.IsNull(weapon.GetComponent<SpriteRenderer>().sprite);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void Show(Animator animator, AnimState state)
    {
        animator.SetInteger("State", (int)state);
        animator.Update(0.01f);
        animator.Update(0.01f);
    }

    private static AnimatorState StateFor(string controllerName, AnimState state)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Animation/" + controllerName + ".controller");
        Assert.IsNotNull(controller);
        AnimatorStateTransition[] matches = controller.layers.Single().stateMachine.anyStateTransitions
            .Where(t => t.conditions.Any(c => c.parameter == "State" &&
                c.mode == AnimatorConditionMode.Equals && c.threshold == (int)state)).ToArray();
        Assert.AreEqual(1, matches.Length, controllerName + " " + state);
        Assert.IsFalse(matches[0].hasExitTime);
        Assert.IsFalse(matches[0].canTransitionToSelf);
        Assert.AreEqual(0f, matches[0].duration);
        Assert.IsTrue(matches[0].destinationState.writeDefaultValues);
        Assert.IsNotNull(matches[0].destinationState.motion);
        return matches[0].destinationState;
    }

    private static AnimationClip Clip(string name)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Animation/" + name + ".anim");
        Assert.IsNotNull(clip);
        return clip;
    }
}
