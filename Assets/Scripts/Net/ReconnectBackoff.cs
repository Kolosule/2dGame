/// <summary>
/// The client's retry schedule: 5 attempts at 1 / 2 / 4 / 8 / 8 seconds (~23 s), then fall back to
/// the main menu. A fast first retry catches a momentary blip; the backoff avoids hammering a
/// server that is genuinely down.
///
/// Giving up is not final: the hold lasts the rest of the match, so the player can still reconnect
/// manually from the menu minutes later and get their state back.
/// </summary>
public static class ReconnectBackoff
{
    public const int MaxAttempts = 5;

    private static readonly float[] Delays = { 1f, 2f, 4f, 8f, 8f };

    /// <summary>Seconds to wait BEFORE attempt number `attempt` (1-based). Out of range -> 0.</summary>
    public static float DelaySecondsForAttempt(int attempt)
    {
        if (attempt < 1 || attempt > MaxAttempts) return 0f;
        return Delays[attempt - 1];
    }
}
