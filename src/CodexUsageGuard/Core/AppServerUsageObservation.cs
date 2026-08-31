using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageGuard.Core;

public enum AppServerUsageError
{
    ExecutableNotFound,
    ExecutableInaccessible,
    LaunchFailed,
    StartupTimedOut,
    InitializeRejected,
    AuthenticationRefreshRequested,
    RateLimitsRequestRejected,
    ReadTimedOut,
    ProtocolError,
    MissingFiveHourQuotaWindow,
    DuplicateFiveHourQuotaWindow,
    ConflictingFiveHourQuotaWindow,
    InvalidFiveHourQuotaWindow,
    StaleFiveHourQuotaWindow,
    MissingWeeklyQuotaWindow,
    DuplicateWeeklyQuotaWindow,
    ConflictingWeeklyQuotaWindow,
    InvalidWeeklyQuotaWindow,
    StaleWeeklyQuotaWindow,
    ExecutableNotApproved,
    ShutdownTimedOut,
    Cancelled
}

public enum AppServerQuotaWindowKind
{
    FiveHour,
    Weekly
}

public sealed record AppServerQuotaWindowObservation(
    AppServerQuotaWindowKind Kind,
    decimal RemainingPercent,
    DateTimeOffset ResetsAtUtc);

public sealed record AppServerUsageObservation(
    ObservationStatus Status,
    decimal? RemainingPercent,
    DateTimeOffset? ResetsAtUtc,
    DateTimeOffset ObservedAtUtc,
    ObservationConfidence Confidence,
    ObservationFreshness Freshness,
    AppServerUsageError? Error,
    IReadOnlyList<AppServerQuotaWindowObservation>? Windows = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public string ToSanitizedJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AppServerUsageObservation UnavailableAt(
        DateTimeOffset observedAtUtc,
        AppServerUsageError error) => new(
            ObservationStatus.Unavailable,
            null,
            null,
            observedAtUtc,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error,
            []);

    public static AppServerUsageObservation ErrorAt(
        DateTimeOffset observedAtUtc,
        AppServerUsageError error) => new(
            ObservationStatus.Error,
            null,
            null,
            observedAtUtc,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error,
            []);
}
