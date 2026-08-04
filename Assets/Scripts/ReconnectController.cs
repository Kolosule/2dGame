using System.Collections;
using UnityEngine;

/// <summary>
/// Client-side reconnection loop. GameNetworkManager decides that a drop was unexpected and calls
/// BeginReconnect; from there this owns the UI until it either reconnects or gives up to the menu.
///
/// Lives on the persistent GameNetworkManager GameObject. A shut-down NetworkRunner cannot be
/// restarted, so every attempt tears the runner component stack down and rebuilds it — which also
/// means every attempt must wait a frame between the two (Unity defers Destroy to end of frame).
///
/// See docs/superpowers/specs/2026-07-29-reconnection-design.md.
/// </summary>
[RequireComponent(typeof(GameNetworkManager))]
public class ReconnectController : MonoBehaviour
{
    private GameNetworkManager net;
    private Coroutine loop;

    public bool IsReconnecting => loop != null;

    private void Awake()
    {
        net = GetComponent<GameNetworkManager>();
    }

    /// <summary>
    /// Idempotent: Fusion may raise OnDisconnectedFromServer and OnShutdown for a single drop, and
    /// both call in here.
    /// </summary>
    public void BeginReconnect(string reason)
    {
        if (loop != null) return;
        loop = StartCoroutine(ReconnectLoop(reason));
    }

    /// <summary>
    /// User pressed Cancel. Stops the loop and returns to the idle menu. An in-flight StartGame task
    /// is not itself cancellable, so FallBackToMenu tears the runner down, which ends it.
    /// </summary>
    public void Cancel()
    {
        if (loop == null) return;
        StopCoroutine(loop);
        loop = null;
        StartCoroutine(FallBackToMenu("Reconnect cancelled."));
    }

    private IEnumerator ReconnectLoop(string reason)
    {
        // Once the runner dies the gameplay scene is full of despawned husks. Get back to the menu
        // scene LOCALLY (a plain scene load, not runner.LoadScene) so every attempt starts clean;
        // Fusion's scene sync drives the load back into gameplay when an attempt succeeds.
        UnityEngine.SceneManagement.SceneManager.LoadScene(net.MenuSceneIndex);
        yield return null;              // let the load settle before touching the new scene's UI
        net.ReacquireMenuUI();
        net.ShowReconnectingUI($"Connection lost ({reason}) — reconnecting…");

        for (int attempt = 1; attempt <= ReconnectBackoff.MaxAttempts; attempt++)
        {
            net.ShowReconnectingUI(
                $"Connection lost — reconnecting… (attempt {attempt} of {ReconnectBackoff.MaxAttempts})");
            yield return new WaitForSeconds(ReconnectBackoff.DelaySecondsForAttempt(attempt));

            net.TeardownRunner();
            yield return null;          // Destroy is deferred to end of frame; rebuild next frame
            net.BuildRunner();

            var task = net.TryReconnectAsync();
            while (!task.IsCompleted) yield return null;

            bool ok = !task.IsFaulted && !task.IsCanceled && task.Result;
            if (task.IsFaulted)
                Debug.LogWarning($"⚠️ Reconnect attempt {attempt} threw: {task.Exception}");

            if (ok)
            {
                loop = null;
                net.OnReconnectSucceeded();
                yield break;
            }
        }

        loop = null;
        yield return FallBackToMenu("Could not reconnect. Returning to the menu.");
    }

    private IEnumerator FallBackToMenu(string message)
    {
        // Leave a live, unused runner behind and the menu's Join button would call StartGame on a
        // dead one. Rebuild so a manual Join still works.
        net.TeardownRunner();
        yield return null;
        net.BuildRunner();
        net.HideReconnectingUI(message);
    }
}
