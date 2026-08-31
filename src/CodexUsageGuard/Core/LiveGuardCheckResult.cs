using System.Text.Json;
using System.Text.Json.Serialization;
using CodexUsageGuard.AppServer;

namespace CodexUsageGuard.Core;

public enum GuardCheckSource
{
    LiveAppServer
}

public sealed record GuardSourceProvenance(
    string Distribution,
    string CodexCliVersion,
    string ExecutableSha256);

public sealed record LiveGuardCheckResult(
    GuardPolicyClassification Decision,
    decimal? RemainingPercent,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ResetsAtUtc,
    ObservationConfidence Confidence,
    ObservationFreshness Freshness,
    GuardCheckSource Source,
    GuardSourceProvenance SourceProvenance,
    AppServerQuotaWindowKind? ControllingWindow,
    IReadOnlyList<SanitizedQuotaWindowState> Windows,
    GuardResumeRecommendation ResumeRecommendation,
    string? ResetsAtLocalDisplay)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static LiveGuardCheckResult FromLiveObservation(
        AppServerUsageObservation observation,
        DateTimeOffset evaluatedAtUtc)
    {
        var evaluation = ConfiguredGuardEvaluator.Evaluate(
            GuardSettings.Default with { UnrestrictedDevelopmentOverride = false },
            GuardPersistentState.Empty,
            observation,
            evaluatedAtUtc);
        var display = evaluation.Display;
        var trustworthy =
            display.Decision is GuardRuntimeState.Normal or
                GuardRuntimeState.Warning or GuardRuntimeState.SafeWrap &&
            display.Confidence == ObservationConfidence.High &&
            display.Freshness == ObservationFreshness.ObservedNow &&
            display.Windows is { Count: 2 };

        return new LiveGuardCheckResult(
            display.Decision switch
            {
                GuardRuntimeState.Normal => GuardPolicyClassification.Normal,
                GuardRuntimeState.Warning => GuardPolicyClassification.Warning,
                GuardRuntimeState.SafeWrap => GuardPolicyClassification.SafeWrap,
                _ => GuardPolicyClassification.Unknown
            },
            trustworthy ? display.RemainingPercent : null,
            observation.ObservedAtUtc,
            trustworthy ? display.ResetsAtUtc : null,
            trustworthy ? observation.Confidence : ObservationConfidence.None,
            trustworthy ? observation.Freshness : ObservationFreshness.Unknown,
            GuardCheckSource.LiveAppServer,
            new GuardSourceProvenance(
                ApprovedCodexCli.Distribution,
                ApprovedCodexCli.Version,
                ApprovedCodexCli.ExecutableSha256),
            trustworthy ? display.ControllingWindow : null,
            trustworthy ? display.Windows ?? [] : [],
            display.ResumeRecommendation ?? GuardResumePlanner.Unavailable(
                GuardSettings.Default),
            trustworthy ? display.ResetsAtLocalDisplay : null);
    }

    public string ToSanitizedJson() => JsonSerializer.Serialize(this, JsonOptions);
}
