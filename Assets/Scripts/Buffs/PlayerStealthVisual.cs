using UnityEngine;
using Fusion;

/// <summary>
/// Render-side stealth fade. Derives alpha from the networked PlayerBuffs.IsStealthed plus the
/// LOCAL viewer's team, so every client agrees on the state but renders its own perspective:
/// the owner/teammates see a light fade, enemies see near-invisible. Targets explicitly-assigned
/// BODY renderers (the prefab has multiple SpriteRenderers; do not auto-find).
///
/// Death-dim: PlayerStatsHandler dims the body sprite to alpha 0.5 via RPC on death.
/// While IsDead this script returns early so the death-dim owns alpha completely.
/// </summary>
public class PlayerStealthVisual : NetworkBehaviour
{
    [Tooltip("Body sprite renderers to fade. Assign the visible body sprite(s), NOT the weapon.")]
    [SerializeField] private SpriteRenderer[] bodyRenderers;

    [SerializeField, Range(0f, 1f)] private float ownerAlpha    = 0.5f;
    [SerializeField, Range(0f, 1f)] private float teammateAlpha = 0.5f;
    [SerializeField, Range(0f, 1f)] private float enemyAlpha    = 0.05f;

    private PlayerBuffs       buffs;
    private PlayerTeamData    myTeam;
    private PlayerStatsHandler statsHandler;

    // Cached local-viewer team data — resolved lazily once, re-resolved if destroyed.
    private PlayerTeamData _cachedLocalViewer;

    public override void Spawned()
    {
        if (DedicatedServerPresentation.IsHeadless) return;

        buffs        = GetComponent<PlayerBuffs>();
        myTeam       = GetComponent<PlayerTeamData>();
        statsHandler = GetComponent<PlayerStatsHandler>();
    }

    public override void Render()
    {
        if (DedicatedServerPresentation.IsHeadless) return;
        if (buffs == null || bodyRenderers == null) return;

        // Death-dim guard: let PlayerStatsHandler own alpha while dead.
        if (statsHandler != null && statsHandler.IsPlayerDead()) return;

        float alpha = buffs.IsStealthed ? AlphaForLocalViewer() : 1f;

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            SpriteRenderer r = bodyRenderers[i];
            if (r == null) continue;
            Color c = r.color;
            c.a = alpha;
            r.color = c;
        }
    }

    // -------------------------------------------------------------------------
    // Viewer-team helpers
    // -------------------------------------------------------------------------

    private float AlphaForLocalViewer()
    {
        // The client that owns this object always sees the owner fade.
        if (HasInputAuthority) return ownerAlpha;

        Team viewer = LocalViewerTeam();
        Team mine   = myTeam != null ? myTeam.Team : Team.None;

        if (viewer != Team.None && mine != Team.None && TeamUtil.AreEnemies(viewer, mine))
            return enemyAlpha;

        return teammateAlpha;
    }

    /// <summary>
    /// Returns the Team of the player that has input authority on this client (the local viewer).
    /// Resolved lazily and cached; only re-resolves if the cached reference has been destroyed.
    /// Uses FindObjectsByType per the codebase pattern (UIManager, PlayerCamera).
    /// </summary>
    private Team LocalViewerTeam()
    {
        // Use cached reference if still valid.
        if (_cachedLocalViewer != null) return _cachedLocalViewer.Team;

        // Resolve: find the PlayerTeamData whose object has input authority on this client.
        PlayerTeamData[] all = FindObjectsByType<PlayerTeamData>(FindObjectsSortMode.None);
        foreach (PlayerTeamData ptd in all)
        {
            if (ptd != null && ptd.Object != null && ptd.HasInputAuthority)
            {
                _cachedLocalViewer = ptd;
                return ptd.Team;
            }
        }

        return Team.None;
    }
}
