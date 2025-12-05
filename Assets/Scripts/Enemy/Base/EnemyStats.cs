using UnityEngine;

[CreateAssetMenu(fileName = "EnemyStats", menuName = "Game/Enemy Stats")]
public class EnemyStats : ScriptableObject
{
    [Header("Base Stats")]
    public string enemyName;
    public int maxHealth;
    public int attackDamage;
    public float attackCooldown;
    public float moveSpeed;

    [Header("Scaling")]
    public int level; // 1–5

    public string enemyTeam;
}