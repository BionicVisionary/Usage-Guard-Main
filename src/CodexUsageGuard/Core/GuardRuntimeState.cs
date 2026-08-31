using CodexUsageGuard.AppServer;

namespace CodexUsageGuard.Core;

public enum GuardRuntimeState
{
    Normal,
    Warning,
    SafeWrap,
    Unknown,
    OverrideActive,
    ResetDetected,
    ProvenanceMismatch
}

public enum GuardDecisionReason
{
    AboveWarningThreshold,
    WarningThresholdReached,
    SafeWrapThresholdReached,
    CriticalBufferReached,
    GenuineLatchActive,
    ObservationUnknown,
    ObservationStale,
    ObservationInvalid,
    ConfigurationInvalid,
    ProvenanceMismatch,
    UnrestrictedDevelopmentOverride,
    NewWeeklyWindowProven,
    NewQuotaWindowProven
}

public enum GuardDecisionSource
{
    LiveAppServer,
    GenuineLiveLatch,
    UserOverride,
    Unavailable
}

public sealed record SanitizedQuotaWindowState(
    AppServerQuotaWindowKind Kind,
    decimal RemainingPercent,
    DateTimeOffset ResetsAtUtc,
    string ResetsAtLocalDisplay);

public sealed record SanitizedUsageState(
    GuardRuntimeState Decision,
    GuardRuntimeState UnderlyingDecision,
    GuardDecisionReason Reason,
    GuardDecisionSource Source,
    decimal? RemainingPercent,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ResetsAtUtc,
    ObservationConfidence Confidence,
    ObservationFreshness Freshness,
    AppServerUsageError? Error,
    bool StartNewPhaseAllowed,
    bool FinishCurrentCheckpointOnly,
    bool ResetDetected,
    GuardSourceProvenance SourceProvenance,
    AppServerQuotaWindowKind? ControllingWindow = null,
    IReadOnlyList<SanitizedQuotaWindowState>? Windows = null,
    GuardResumeRecommendation? ResumeRecommendation = null,
    string? ResetsAtLocalDisplay = null)
{
    public bool IsSuccessfulLiveObservation =>
        Confidence == ObservationConfidence.High &&
        Freshness == ObservationFreshness.ObservedNow &&
        RemainingPercent is not null &&
        ResetsAtUtc is not null &&
        Windows is { Count: 2 };
}

public sealed record GuardPersistentState(
    int SchemaVersion,
    SanitizedUsageState? Current,
    DateTimeOffset? LatchedWeeklyResetAtUtc,
    DateTimeOffset? LatchCreatedAtUtc,
    DateTimeOffset? LastSuccessfulObservationAtUtc,
    DateTimeOffset? LastSuccessfulWeeklyResetAtUtc,
    string? LastNotificationKey,
    DateTimeOffset? LastNotificationAtUtc,
    int ConsecutiveFailures,
    IReadOnlyDictionary<string, DateTimeOffset>? NotificationLedger = null,
    DateTimeOffset? LatchedFiveHourResetAtUtc = null,
    DateTimeOffset? FiveHourLatchCreatedAtUtc = null,
    DateTimeOffset? LastSuccessfulFiveHourResetAtUtc = null)
{
    public const int CurrentSchemaVersion = 1;

    public static GuardPersistentState Empty { get; } = new(
        CurrentSchemaVersion,
        Current: null,
        LatchedWeeklyResetAtUtc: null,
        LatchCreatedAtUtc: null,
        LastSuccessfulObservationAtUtc: null,
        LastSuccessfulWeeklyResetAtUtc: null,
        LastNotificationKey: null,
        LastNotificationAtUtc: null,
        ConsecutiveFailures: 0,
        NotificationLedger: new Dictionary<string, DateTimeOffset>(),
        LatchedFiveHourResetAtUtc: null,
        FiveHourLatchCreatedAtUtc: null,
        LastSuccessfulFiveHourResetAtUtc: null);
}
