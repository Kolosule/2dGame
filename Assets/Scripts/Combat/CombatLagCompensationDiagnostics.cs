using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

/// <summary>
/// Server-only aggregate counters. Logging is disabled by default and occurs only at the configured
/// interval, so normal combat does not emit per-query messages.
/// </summary>
public static class CombatLagCompensationDiagnostics
{
    private static long queryCount;
    private static long historicalPlayerHits;
    private static long currentTickEnemyHits;
    private static long rejectedDuplicateHits;
    private static long queryStopwatchTicks;
    private static double nextSummaryTime;

    public static long BeginQuery(bool enabled)
    {
        return enabled ? Stopwatch.GetTimestamp() : 0L;
    }

    public static void RecordQuery(bool enabled, long startedAt)
    {
        if (!enabled) return;
        queryCount++;
        queryStopwatchTicks += Stopwatch.GetTimestamp() - startedAt;
    }

    public static void RecordHistoricalPlayerHit(bool enabled)
    {
        if (enabled) historicalPlayerHits++;
    }

    public static void RecordCurrentTickEnemyHit(bool enabled)
    {
        if (enabled) currentTickEnemyHits++;
    }

    public static void RecordRejectedDuplicate(bool enabled)
    {
        if (enabled) rejectedDuplicateHits++;
    }

    public static void MaybeLog(bool enabled, float intervalSeconds)
    {
        if (!enabled) return;

        double now = Time.realtimeSinceStartupAsDouble;
        if (nextSummaryTime <= 0d)
        {
            nextSummaryTime = now + Mathf.Max(5f, intervalSeconds);
            return;
        }

        if (now < nextSummaryTime) return;

        double averageMicroseconds = queryCount > 0
            ? queryStopwatchTicks * 1_000_000d / Stopwatch.Frequency / queryCount
            : 0d;

        Debug.Log(
            $"Lag compensation summary: melee queries={queryCount}, " +
            $"historical player hits={historicalPlayerHits}, " +
            $"current-tick enemy hits={currentTickEnemyHits}, " +
            $"rejected duplicates={rejectedDuplicateHits}, " +
            $"average query={averageMicroseconds:F2} us.");

        queryCount = 0;
        historicalPlayerHits = 0;
        currentTickEnemyHits = 0;
        rejectedDuplicateHits = 0;
        queryStopwatchTicks = 0;
        nextSummaryTime = now + Mathf.Max(5f, intervalSeconds);
    }
}
