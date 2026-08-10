using UnityEngine;
using UnityEngine.InputSystem;
using Game.Audio.Core;

/// <summary>
/// Local-only: reads the UI/Scoreboard action (hold Tab) and forwards press/release to the bound
/// ScoreboardPanel. Not networked -- every client decides independently whether to show its own
/// already-replicated copy of the board. Matches the project convention that UI input reads are
/// local/non-simulation.
/// </summary>
public class ScoreboardInputReader : MonoBehaviour
{
    [SerializeField] private ScoreboardPanel panel;
    [SerializeField] private InputActionReference scoreboardAction;

    private void OnEnable()
    {
        if (scoreboardAction == null || scoreboardAction.action == null) return;
        scoreboardAction.action.performed += OnPerformed;
        scoreboardAction.action.canceled += OnCanceled;
        scoreboardAction.action.Enable();
    }

    private void OnDisable()
    {
        if (scoreboardAction != null && scoreboardAction.action != null)
        {
            scoreboardAction.action.performed -= OnPerformed;
            scoreboardAction.action.canceled -= OnCanceled;
        }

        // If this reader (or its GameObject) is disabled while Tab is held, canceled never fires
        // and the board would otherwise stay open indefinitely -- release the hold explicitly.
        if (panel != null) panel.SetHeld(false);
    }

    private void OnPerformed(InputAction.CallbackContext ctx)
    {
        Audio.PlayUi(AudioCueId.PanelOpen);
        if (panel != null) panel.SetHeld(true);
    }

    private void OnCanceled(InputAction.CallbackContext ctx)
    {
        Audio.PlayUi(AudioCueId.PanelClose);
        if (panel != null) panel.SetHeld(false);
    }
}
