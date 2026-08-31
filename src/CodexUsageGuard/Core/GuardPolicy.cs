namespace CodexUsageGuard.Core;

public enum GuardPolicyClassification
{
    Normal,
    Warning,
    SafeWrap,
    Unknown
}

public enum GuardPolicyReason
{
    AboveWarningThreshold,
    WarningThresholdReached,
    SafeWrapThresholdReached,
    FinishCurrentCheckpointThresholdReached,
    ObservationUnknown,
    ObservationStale,
    ObservationInvalid,
    ConfigurationInvalid
}

public sealed record GuardPolicyConfiguration(
    decimal WarningAtOrBelowPercent,
    decimal SafeWrapAtOrBelowPercent,
    decimal FinishCurrentCheckpointAtOrBelowPercent,
    TimeSpan MaximumObservationAge)
{
    public static GuardPolicyConfiguration Default { get; } = new(
        WarningAtOrBelowPercent: 30m,
        SafeWrapAtOrBelowPercent: 25m,
        FinishCurrentCheckpointAtOrBelowPercent: 20m,
        MaximumObservationAge: TimeSpan.FromMinutes(2));
}

public sealed record GuardPolicyDecision(
    GuardPolicyClassification Classification,
    GuardPolicyReason Reason,
    bool StartNewPhaseAllowed,
    bool FinishCurrentCheckpointOnly,
    bool PolicyValid);

public static class GuardPolicyEvaluator
{
    public static GuardPolicyDecision Evaluate(
        AppServerUsageObservation observation,
        DateTimeOffset evaluatedAtUtc,
        GuardPolicyConfiguration? configuration = null)
    {
        var policy = configuration ?? GuardPolicyConfiguration.Default;
        if (!IsValid(policy))
        {
            return new GuardPolicyDecision(
                GuardPolicyClassification.Unknown,
                GuardPolicyReason.ConfigurationInvalid,
                StartNewPhaseAllowed: false,
                FinishCurrentCheckpointOnly: true,
                PolicyValid: false);
        }

        if (observation.Status != ObservationStatus.Available ||
            observation.Freshness != ObservationFreshness.ObservedNow ||
            observation.Confidence != ObservationConfidence.High ||
            observation.RemainingPercent is null ||
            observation.ResetsAtUtc is null)
        {
            return Unknown(GuardPolicyReason.ObservationUnknown);
        }

        var remaining = observation.RemainingPercent.Value;
        if (remaining is < 0 or > 100)
        {
            return Unknown(GuardPolicyReason.ObservationInvalid);
        }

        var age = evaluatedAtUtc - observation.ObservedAtUtc;
        if (age < TimeSpan.Zero ||
            age > policy.MaximumObservationAge ||
            observation.ResetsAtUtc <= evaluatedAtUtc)
        {
            return Unknown(GuardPolicyReason.ObservationStale);
        }

        if (remaining <= policy.FinishCurrentCheckpointAtOrBelowPercent)
        {
            return new GuardPolicyDecision(
                GuardPolicyClassification.SafeWrap,
                GuardPolicyReason.FinishCurrentCheckpointThresholdReached,
                StartNewPhaseAllowed: false,
                FinishCurrentCheckpointOnly: true,
                PolicyValid: true);
        }

        if (remaining <= policy.SafeWrapAtOrBelowPercent)
        {
            return new GuardPolicyDecision(
                GuardPolicyClassification.SafeWrap,
                GuardPolicyReason.SafeWrapThresholdReached,
                StartNewPhaseAllowed: false,
                FinishCurrentCheckpointOnly: true,
                PolicyValid: true);
        }

        if (remaining <= policy.WarningAtOrBelowPercent)
        {
            return new GuardPolicyDecision(
                GuardPolicyClassification.Warning,
                GuardPolicyReason.WarningThresholdReached,
                StartNewPhaseAllowed: true,
                FinishCurrentCheckpointOnly: false,
                PolicyValid: true);
        }

        return new GuardPolicyDecision(
            GuardPolicyClassification.Normal,
            GuardPolicyReason.AboveWarningThreshold,
            StartNewPhaseAllowed: true,
            FinishCurrentCheckpointOnly: false,
            PolicyValid: true);
    }

    public static bool IsValid(GuardPolicyConfiguration configuration) =>
        configuration.MaximumObservationAge > TimeSpan.Zero &&
        configuration.FinishCurrentCheckpointAtOrBelowPercent is >= 0 and <= 100 &&
        configuration.SafeWrapAtOrBelowPercent is >= 0 and <= 100 &&
        configuration.WarningAtOrBelowPercent is >= 0 and <= 100 &&
        configuration.FinishCurrentCheckpointAtOrBelowPercent <=
            configuration.SafeWrapAtOrBelowPercent &&
        configuration.SafeWrapAtOrBelowPercent <=
            configuration.WarningAtOrBelowPercent;

    private static GuardPolicyDecision Unknown(GuardPolicyReason reason) => new(
        GuardPolicyClassification.Unknown,
        reason,
        StartNewPhaseAllowed: false,
        FinishCurrentCheckpointOnly: true,
        PolicyValid: true);
}
