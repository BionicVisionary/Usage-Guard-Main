using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageGuard.Core;

public enum ObservationStatus
{
    Available,
    Unavailable,
    Error
}

public enum ObservationConfidence
{
    None,
    Medium,
    High
}

public enum ObservationFreshness
{
    Unknown,
    ObservedNow
}

public enum ObservationError
{
    CodexNotRunning,
    CodexWindowNotVisible,
    WeeklyUsageLabelNotVisible,
    RemainingPercentageNotFound,
    AmbiguousRemainingPercentage,
    AmbiguousWeeklyUsageStructure,
    AccessibilityScopeTooLarge,
    ObservationTooSlow,
    AccessibilityReadFailed,
    WindowStateTransitionFailed,
    NoDistinctForegroundWindow,
    TestChildTimedOut,
    TestChildFailed,
    WindowStateTestNotRun
}

public sealed record UsageObservation(
    ObservationStatus Status,
    decimal? RemainingPercent,
    DateTimeOffset ObservedAtUtc,
    ObservationConfidence Confidence,
    ObservationFreshness Freshness,
    ObservationError? Error)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public string ToSanitizedJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static UsageObservation? FromSanitizedJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UsageObservation>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static UsageObservation UnavailableAt(
        DateTimeOffset observedAtUtc,
        ObservationError error) => new(
            ObservationStatus.Unavailable,
            null,
            observedAtUtc,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error);

    public static UsageObservation ErrorAt(
        DateTimeOffset observedAtUtc,
        ObservationError error) => new(
            ObservationStatus.Error,
            null,
            observedAtUtc,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error);
}
