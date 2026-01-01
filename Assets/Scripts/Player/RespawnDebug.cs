using UnityEngine;
using Fusion;

/// <summary>
/// Diagnostic script to identify spawn and damage issues
/// Attach this to your Player prefab temporarily to debug
/// </summary>
public class SpawnDiagnostics : NetworkBehaviour
{
    private void Start()
    {
        Debug.Log("=== SPAWN DIAGNOSTICS ===");
        Debug.Log($"Player spawned at position: {transform.position}");
        Debug.Log($"Player name: {gameObject.name}");

        // Check team assignment
        PlayerTeamComponent teamComp = GetComponent<PlayerTeamComponent>();
        if (teamComp != null)
        {
            Debug.Log($"Team ID: {teamComp.teamID}");
        }
        else
        {
            Debug.LogError("NO PlayerTeamComponent found!");
        }

        // Check team data
        PlayerTeamData teamData = GetComponent<PlayerTeamData>();
        if (teamData != null)
        {
            Debug.Log($"Team Data - Team: {teamData.Team}");
        }
        else
        {
            Debug.LogError("NO PlayerTeamData found!");
        }

        // Check all colliders
        Collider2D[] colliders = GetComponents<Collider2D>();
        Debug.Log($"Number of colliders: {colliders.Length}");
        foreach (var col in colliders)
        {
            Debug.Log($"  - Collider: Type={col.GetType().Name}, IsTrigger={col.isTrigger}, Layer={LayerMask.LayerToName(gameObject.layer)}");
        }

        // Check what we're overlapping with at spawn
        Collider2D[] overlapping = Physics2D.OverlapBoxAll(transform.position, Vector2.one, 0f);
        Debug.Log($"Overlapping {overlapping.Length} objects at spawn:");
        foreach (var obj in overlapping)
        {
            if (obj.gameObject != gameObject)
            {
                Debug.Log($"  - {obj.gameObject.name} (Layer: {LayerMask.LayerToName(obj.gameObject.layer)}, IsTrigger: {obj.isTrigger})");
            }
        }

        Debug.Log("=== END DIAGNOSTICS ===");
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log($"[TRIGGER] Player entered trigger with: {collision.gameObject.name} (Layer: {LayerMask.LayerToName(collision.gameObject.layer)})");
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Debug.Log($"[COLLISION] Player collided with: {collision.gameObject.name} (Layer: {LayerMask.LayerToName(collision.gameObject.layer)})");
    }
}