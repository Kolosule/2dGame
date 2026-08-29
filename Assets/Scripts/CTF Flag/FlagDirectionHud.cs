using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local-only HUD. For each team flag, shows a screen-edge arrow that points toward
/// the flag, but only while that flag is away from home AND off-screen. Each arrow is
/// recolored to its flag's owning-team color. Reads replicated flag state from
/// CTFGameManager.Instance; adds no networking of its own.
///
/// Attach to the Screen-Space HUD canvas. Wire one arrow RectTransform+Image per team.
/// Author arrow sprites pointing RIGHT (+X); rotation 0 = pointing east.
/// Arrow RectTransforms must be anchored AND pivoted at center (0.5, 0.5).
/// </summary>
public class FlagDirectionHud : MonoBehaviour
{
    [System.Serializable]
    public class FlagArrow
    {
        [Tooltip("Arrow UI element. Anchor + pivot must both be center (0.5, 0.5).")]
        public RectTransform arrow;

        [Tooltip("The arrow's Image, recolored to the flag's owning-team color.")]
        public Image arrowImage;
    }

    [Header("Arrows")]
    [Tooltip("Arrow for the Team1 (Blue) flag.")]
    [SerializeField] private FlagArrow team1Arrow;

    [Tooltip("Arrow for the Team2 (Red) flag.")]
    [SerializeField] private FlagArrow team2Arrow;

    [Header("Layout")]
    [Tooltip("Camera for world->screen. Defaults to Camera.main when left empty.")]
    [SerializeField] private Camera viewCamera;

    [Tooltip("Pixels the arrow is inset from the screen edge.")]
    [SerializeField] private float edgeMargin = 60f;

    private RectTransform canvasRect;

    private void Awake()
    {
        if (DedicatedServerPresentation.IsHeadless)
        {
            enabled = false;
            return;
        }

        canvasRect = transform as RectTransform;
        SetActive(team1Arrow, false);
        SetActive(team2Arrow, false);
    }

    private void LateUpdate()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || canvasRect == null || CTFGameManager.Instance == null)
            return;

        UpdateArrow(team1Arrow, CTFGameManager.Instance.Team1Flag);
        UpdateArrow(team2Arrow, CTFGameManager.Instance.Team2Flag);
    }

    private void UpdateArrow(FlagArrow a, Flag flag)
    {
        if (a == null || a.arrow == null) return;

        // Hide unless there's a valid flag that is out of its base.
        if (flag == null || flag.Object == null || !flag.Object.IsValid ||
            flag.State == Flag.FlagState.AtHome)
        {
            SetActive(a, false);
            return;
        }

        Vector3 vp = viewCamera.WorldToViewportPoint(flag.transform.position);
        bool behind = vp.z < 0f;
        bool onScreen = !behind && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

        // Flag is visible on screen -> no arrow needed.
        if (onScreen)
        {
            SetActive(a, false);
            return;
        }

        SetActive(a, true);
        ApplyColor(a, flag);

        // Direction from screen center to the flag, in centered viewport space.
        Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
        if (behind) dir = -dir;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
        dir.Normalize();

        // Clamp onto the inset canvas rectangle (box clamp from center).
        Vector2 half = canvasRect.rect.size * 0.5f - Vector2.one * edgeMargin;
        half = Vector2.Max(half, Vector2.zero);
        float scale = Mathf.Min(
            half.x / Mathf.Max(Mathf.Abs(dir.x), 1e-6f),
            half.y / Mathf.Max(Mathf.Abs(dir.y), 1e-6f));

        a.arrow.anchoredPosition = dir * scale;
        a.arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }

    private static void ApplyColor(FlagArrow a, Flag flag)
    {
        if (a.arrowImage == null || TeamManager.Instance == null) return;
        TeamData data = TeamManager.Instance.GetTeamData(flag.OwningTeamEnum);
        if (data != null) a.arrowImage.color = data.teamColor;
    }

    private static void SetActive(FlagArrow a, bool active)
    {
        if (a == null || a.arrow == null) return;
        if (a.arrow.gameObject.activeSelf != active)
            a.arrow.gameObject.SetActive(active);
    }
}
