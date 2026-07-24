using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyStats", menuName = "Enemy/Stats")]
public class EnemyStats : ScriptableObject
{
    [Header("Identity")]
    public string enemyName = "Enemy";

    [Header("Combat Stats")]
    public int maxHealth = 10;
    public int attackDamage = 1;
    public float attackCooldown = 1f; // Time between attacks in seconds

    [Header("Movement")]
    public float moveSpeed = 3f;

    [Tooltip("If true, the enemy flies: MoveToward drives both X and Y velocity toward the " +
             "target and skips the ground/jump logic. Requires Rigidbody2D gravityScale = 0 " +
             "on the prefab.")]
    public bool canFly = false;

    [Header("AI Ranges")]
    [Tooltip("How far the enemy senses players.")]
    public float detectionRange = 10f;

    [Tooltip("How close a player must be to start an attack.")]
    public float attackRange = 1.5f;

    [Tooltip("Hard max distance from home the enemy will ever travel (chase leash).")]
    public float leashRadius = 12f;

    [Tooltip("Area around home the enemy roams while idle. Keep <= leashRadius.")]
    public float wanderRadius = 5f;

    [Header("Progression")]
    public int level = 1;
    public string enemyTeam = "Team3";
}