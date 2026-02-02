using UnityEngine;
using Fusion;

[RequireComponent(typeof(Rigidbody2D))]
public class NetworkPlayerWrapper : NetworkBehaviour
{
    #region Networked Properties
    [Networked] public Vector3 NetworkPosition { get; set; }
    [Networked] public Vector2 NetworkVelocity { get; set; }
    [Networked] public float NetworkScaleX { get; set; }
    [Networked] public bool IsVisible { get; set; }
    #endregion

    #region Private Fields
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;
    #endregion

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        Debug.Log($"🌐 PLAYER SPAWNED: {gameObject.name}");
        Debug.Log($"🌐 HasInputAuthority: {HasInputAuthority}");
        Debug.Log($"🌐 HasStateAuthority: {HasStateAuthority}");
        Debug.Log($"🌐 Position: {transform.position}");

        if (HasStateAuthority)
        {
            NetworkPosition = transform.position;
            IsVisible = true;
            Debug.Log($"✅ [SERVER] Initialized at {NetworkPosition}");
        }
        else
        {
            transform.position = NetworkPosition;
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = IsVisible;
            }
            Debug.Log($"✅ [CLIENT] Applied position {NetworkPosition}");
        }

        StartCoroutine(SetupCameraDelayed());
        StartCoroutine(SetupTeammateCollisions());
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority || HasInputAuthority)
        {
            NetworkPosition = transform.position;
            if (rb != null)
            {
                NetworkVelocity = rb.linearVelocity;
            }
            NetworkScaleX = transform.localScale.x;
        }
    }

    public override void Render()
    {
        if (!HasInputAuthority)
        {
            // Smooth interpolation for remote players
            transform.position = Vector3.Lerp(
                transform.position,
                NetworkPosition,
                Time.deltaTime * 15f
            );

            if (rb != null)
            {
                rb.linearVelocity = NetworkVelocity;
            }

            Vector3 scale = transform.localScale;
            scale.x = NetworkScaleX;
            transform.localScale = scale;

            // Ensure sprite is visible
            if (spriteRenderer != null && !spriteRenderer.enabled)
            {
                spriteRenderer.enabled = true;
                Debug.LogWarning($"⚠️ Re-enabled sprite for {gameObject.name}");
            }
        }
    }

    private System.Collections.IEnumerator SetupCameraDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        if (!HasInputAuthority) yield break;

        CameraFollow cam = FindFirstObjectByType<CameraFollow>();
        if (cam != null)
        {
            cam.SetTarget(transform);
            Debug.Log("✅ Camera locked to player");
        }
    }

    private System.Collections.IEnumerator SetupTeammateCollisions()
    {
        yield return new WaitForSeconds(0.2f);

        Collider2D myCollider = GetComponent<Collider2D>();
        if (myCollider == null) yield break;

        PlayerTeamData myTeamData = GetComponent<PlayerTeamData>();
        if (myTeamData == null || myTeamData.Team == 0) yield break;

        NetworkPlayerWrapper[] allPlayers = FindObjectsByType<NetworkPlayerWrapper>(FindObjectsSortMode.None);

        foreach (NetworkPlayerWrapper otherPlayer in allPlayers)
        {
            if (otherPlayer == this) continue;

            PlayerTeamData otherTeamData = otherPlayer.GetComponent<PlayerTeamData>();
            if (otherTeamData == null) continue;

            if (myTeamData.Team == otherTeamData.Team)
            {
                Collider2D otherCollider = otherPlayer.GetComponent<Collider2D>();
                if (otherCollider != null)
                {
                    Physics2D.IgnoreCollision(myCollider, otherCollider, true);
                }
            }
        }
    }

    public void ForceInitializePosition(Vector3 position)
    {
        if (HasStateAuthority)
        {
            NetworkPosition = position;
            transform.position = position;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Cleanup if needed
    }
}