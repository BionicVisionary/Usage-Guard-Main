namespace CodexUsageGuard.Core;

public interface IAccessibilityProbe
{
    AccessibilityProbeResult Capture();
}

public enum AccessibilityProbeState
{
    Success,
    CodexNotRunning,
    CodexWindowNotVisible,
    WeeklyUsageLabelNotVisible,
    ScopeTooLarge,
    AccessibilityUnavailable
}

public sealed record UsageViewSnapshot(IReadOnlyList<string> AccessibleNames);

public sealed record AccessibilityProbeResult(
    AccessibilityProbeState State,
    IReadOnlyList<UsageViewSnapshot> UsageViews)
{
    public static AccessibilityProbeResult WithoutViews(AccessibilityProbeState state) =>
        new(state, Array.Empty<UsageViewSnapshot>());

    public static AccessibilityProbeResult WithViews(IReadOnlyList<UsageViewSnapshot> views) =>
        new(AccessibilityProbeState.Success, views);
}

public interface IObservationClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemObservationClock : IObservationClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
