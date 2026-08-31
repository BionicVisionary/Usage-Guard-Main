namespace CodexUsageGuard.Core;

public sealed class UsageObservationService(
    IAccessibilityProbe probe,
    IObservationClock clock,
    TimeSpan? maximumObservationDuration = null)
{
    private readonly TimeSpan _maximumObservationDuration =
        maximumObservationDuration ?? TimeSpan.FromSeconds(5);

    public UsageObservation Observe()
    {
        var startedAt = clock.UtcNow;

        try
        {
            var probeResult = probe.Capture();
            IReadOnlyList<decimal>? percentages = null;

            if (probeResult.State == AccessibilityProbeState.Success &&
                probeResult.UsageViews.Count == 1)
            {
                percentages = RemainingUsageParser.ExtractDistinctPercentages(
                    probeResult.UsageViews[0].AccessibleNames);
            }

            var observedAt = clock.UtcNow;
            var elapsed = observedAt - startedAt;
            if (elapsed < TimeSpan.Zero || elapsed > _maximumObservationDuration)
            {
                return Unavailable(observedAt, ObservationError.ObservationTooSlow);
            }

            var stateFailure = MapProbeFailure(probeResult.State, observedAt);
            if (stateFailure is not null)
            {
                return stateFailure;
            }

            if (probeResult.UsageViews.Count != 1)
            {
                return Unavailable(
                    observedAt,
                    ObservationError.AmbiguousWeeklyUsageStructure);
            }

            return percentages!.Count switch
            {
                0 => Unavailable(
                    observedAt,
                    ObservationError.RemainingPercentageNotFound),
                1 => new UsageObservation(
                    ObservationStatus.Available,
                    percentages[0],
                    observedAt,
                    ObservationConfidence.Medium,
                    ObservationFreshness.ObservedNow,
                    null),
                _ => Unavailable(
                    observedAt,
                    ObservationError.AmbiguousRemainingPercentage)
            };
        }
        catch
        {
            return Error(clock.UtcNow, ObservationError.AccessibilityReadFailed);
        }
    }

    private static UsageObservation? MapProbeFailure(
        AccessibilityProbeState state,
        DateTimeOffset observedAt) => state switch
        {
            AccessibilityProbeState.Success => null,
            AccessibilityProbeState.CodexNotRunning =>
                Unavailable(observedAt, ObservationError.CodexNotRunning),
            AccessibilityProbeState.CodexWindowNotVisible =>
                Unavailable(observedAt, ObservationError.CodexWindowNotVisible),
            AccessibilityProbeState.WeeklyUsageLabelNotVisible =>
                Unavailable(observedAt, ObservationError.WeeklyUsageLabelNotVisible),
            AccessibilityProbeState.ScopeTooLarge =>
                Unavailable(observedAt, ObservationError.AccessibilityScopeTooLarge),
            AccessibilityProbeState.AccessibilityUnavailable =>
                Error(observedAt, ObservationError.AccessibilityReadFailed),
            _ => Error(observedAt, ObservationError.AccessibilityReadFailed)
        };

    private static UsageObservation Unavailable(
        DateTimeOffset observedAt,
        ObservationError error) => new(
            ObservationStatus.Unavailable,
            null,
            observedAt,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error);

    private static UsageObservation Error(
        DateTimeOffset observedAt,
        ObservationError error) => new(
            ObservationStatus.Error,
            null,
            observedAt,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error);
}
