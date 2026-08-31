namespace CodexUsageGuard.Core;

public static class WeeklyWindowIdentity
{
    public static readonly TimeSpan ResetTimestampTolerance =
        TimeSpan.FromMinutes(2);

    public static bool IsSameWindow(
        DateTimeOffset first,
        DateTimeOffset second) =>
        (first - second).Duration() <= ResetTimestampTolerance;

    public static DateTimeOffset StableKey(
        DateTimeOffset observedReset,
        DateTimeOffset? priorStableKey) =>
        priorStableKey is { } prior && IsSameWindow(observedReset, prior)
            ? prior
            : observedReset;
}
