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

    [Header("Progression")]
    public int level = 1;
    public string enemyTeam = "Team3";
}