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

    // Wall-clock ceiling on a single StartGame attempt (Fix 3). A server that accepts the connection
    // but never finishes the handshake would otherwise stall the attempt counter forever, blowing
    // the spec's ~23s "then the main menu" budget. Time.realtimeSinceStartup, not scaled time — this
    // is a wall-clock deadline, not a gameplay one.
    private const float AttemptTimeoutSeconds = 15f;

    private IEnumerator ReconnectLoop(string reason)
    {
        // Once the runner dies the gameplay scene is full of despawned husks. Get back to the menu
        // scene LOCALLY (a plain scene load, not runner.LoadScene) so every attempt starts clean;
        // Fusion's scene sync drives the load back into gameplay when an attempt succeeds.
        UnityEngine.SceneManagement.SceneManager.LoadScene(net.MenuSceneIndex);
        yield return null;              // let the load settle before touching the new scene's UI
        net.ReacquireMenuUI();

        for (int attempt = 1; attempt <= ReconnectBackoff.MaxAttempts; attempt++)
        {
            net.ShowReconnectingUI(
                $"Connection lost ({reason}) — reconnecting… (attempt {attempt} of {ReconnectBackoff.MaxAttempts})");
            yield return new WaitForSecondsRealtime(ReconnectBackoff.DelaySecondsForAttempt(attempt));

            net.TeardownRunner();
            yield return null;          // Destroy is deferred to end of frame; rebuild next frame
            net.BuildRunner();

            var task = net.TryReconnectAsync();
            float deadline = Time.realtimeSinceStartup + AttemptTimeoutSeconds;
            bool timedOut = false;
            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    timedOut = true;
                    break;
                }
                yield return null;
            }

            if (timedOut)
            {
                // The abandoned task may still complete later (fire-and-forget) — that is accepted;
                // there is no cancellation path for an in-flight Fusion StartGame.
                Debug.LogWarning($"⚠️ Reconnect attempt {attempt} timed out after {AttemptTimeoutSeconds}s — moving on.");

                // Tear it down NOW, not after the next backoff wait. A merely-slow attempt that
                // completes during that wait is a live, successful connection: the server claims the
                // held slot and may spawn the avatar, and the next attempt then throws it all away —
                // burning a genuine reconnect.
                net.TeardownRunner();
                continue;
            }

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
        // The drop reason is the single most useful diagnostic after a ~23s wait, so carry it into
        // the terminal message (the spec's "Could not reconnect: {reason}").
        yield return FallBackToMenu($"Could not reconnect: {reason}");
    }

    private IEnumerator FallBackToMenu(string message)
    {
        // Latch BEFORE tearing the runner down: TeardownRunner -> runner.Shutdown() re-enters
        // GameNetworkManager.OnShutdown -> TryBeginReconnect, and without this latch the guard chain
        // passes (loop is already null here, so BeginReconnect's idempotency check doesn't fire) and
        // a second ReconnectLoop starts concurrently with this one. TryReconnectAsync clears the
        // latch on the next deliberate connection attempt, so it does not stick.
        net.MarkIntentionalDisconnect();

        // Leave a live, unused runner behind and the menu's Join button would call StartGame on a
        // dead one. Rebuild so a manual Join still works.
        net.TeardownRunner();
        yield return null;
        net.BuildRunner();
        net.HideReconnectingUI(message);
    }
}
