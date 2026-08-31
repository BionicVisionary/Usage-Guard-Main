using CodexUsageGuard.Core;

namespace CodexUsageGuard.Providers;

public enum AiProviderId
{
    Codex,
    ClaudeCode
}

public enum ProviderUsageCapability
{
    LiveQuotaWindows,
    DetectionOnly
}

public enum QuotaWindowKind
{
    Weekly,
    RollingFiveHour
}

public sealed record QuotaWindowPolicySettings(
    QuotaWindowKind Kind,
    string DisplayName,
    bool Required,
    decimal WarningThresholdPercent,
    decimal SafeWrapThresholdPercent,
    decimal CriticalBufferPercent)
{
    public GuardPolicyConfiguration ToPolicy() => new(
        WarningThresholdPercent,
        SafeWrapThresholdPercent,
        CriticalBufferPercent,
        TimeSpan.FromMinutes(2));
}

public sealed record AiProviderConfiguration(
    AiProviderId ProviderId,
    string DisplayName,
    bool Enabled,
    bool MonitoringEnabled,
    int PollingIntervalSeconds,
    bool NotifyWarning,
    bool NotifySafeWrap,
    bool NotifyUnknown,
    bool NotifyRecovery,
    bool NotifyReset,
    bool UnrestrictedDevelopmentOverride,
    IReadOnlyList<QuotaWindowPolicySettings> QuotaWindows);

public sealed record ProviderCatalogSettings(
    int SchemaVersion,
    IReadOnlyList<AiProviderConfiguration> Providers)
{
    public const int CurrentSchemaVersion = 1;

    public static AiProviderConfiguration DefaultCodex { get; } = new(
        AiProviderId.Codex,
        "Codex",
        Enabled: true,
        MonitoringEnabled: true,
        PollingIntervalSeconds: 60,
        NotifyWarning: true,
        NotifySafeWrap: true,
        NotifyUnknown: true,
        NotifyRecovery: true,
        NotifyReset: true,
        UnrestrictedDevelopmentOverride: false,
        QuotaWindows:
        [
            new QuotaWindowPolicySettings(
                QuotaWindowKind.RollingFiveHour,
                "5-hour usage limit",
                Required: true,
                WarningThresholdPercent: 30m,
                SafeWrapThresholdPercent: 25m,
                CriticalBufferPercent: 20m),
            new QuotaWindowPolicySettings(
                QuotaWindowKind.Weekly,
                "Weekly usage limit",
                Required: true,
                WarningThresholdPercent: 30m,
                SafeWrapThresholdPercent: 25m,
                CriticalBufferPercent: 20m)
        ]);

    public static AiProviderConfiguration DefaultClaudeCode { get; } = new(
        AiProviderId.ClaudeCode,
        "Claude",
        Enabled: true,
        MonitoringEnabled: true,
        PollingIntervalSeconds: 60,
        NotifyWarning: true,
        NotifySafeWrap: true,
        NotifyUnknown: true,
        NotifyRecovery: true,
        NotifyReset: true,
        UnrestrictedDevelopmentOverride: false,
        QuotaWindows:
        [
            new QuotaWindowPolicySettings(
                QuotaWindowKind.RollingFiveHour,
                "5-hour usage limit",
                Required: true,
                WarningThresholdPercent: 30m,
                SafeWrapThresholdPercent: 25m,
                CriticalBufferPercent: 20m),
            new QuotaWindowPolicySettings(
                QuotaWindowKind.Weekly,
                "Weekly usage limit",
                Required: true,
                WarningThresholdPercent: 30m,
                SafeWrapThresholdPercent: 25m,
                CriticalBufferPercent: 20m)
        ]);

    public static ProviderCatalogSettings Default => new(
        CurrentSchemaVersion,
        [DefaultCodex]);
}

public enum ProviderCatalogValidationError
{
    None,
    UnsupportedSchema,
    MissingProvider,
    DuplicateProvider,
    DuplicateWindow,
    MissingRequiredWindow,
    InvalidThreshold,
    InvalidThresholdOrder,
    InvalidPollingInterval,
    InvalidDisplayName
}

public static class ProviderCatalogValidator
{
    public static ProviderCatalogValidationError Validate(
        ProviderCatalogSettings settings)
    {
        if (settings.SchemaVersion != ProviderCatalogSettings.CurrentSchemaVersion)
        {
            return ProviderCatalogValidationError.UnsupportedSchema;
        }

        if (settings.Providers.Count == 0)
        {
            return ProviderCatalogValidationError.MissingProvider;
        }

        if (settings.Providers.GroupBy(provider => provider.ProviderId)
            .Any(group => group.Count() != 1))
        {
            return ProviderCatalogValidationError.DuplicateProvider;
        }

        foreach (var provider in settings.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.DisplayName) ||
                provider.DisplayName.Length > 80)
            {
                return ProviderCatalogValidationError.InvalidDisplayName;
            }

            if (provider.PollingIntervalSeconds is <
                    GuardSettings.MinimumPollingIntervalSeconds or >
                    GuardSettings.MaximumPollingIntervalSeconds)
            {
                return ProviderCatalogValidationError.InvalidPollingInterval;
            }

            if (provider.QuotaWindows.Count == 0 ||
                !provider.QuotaWindows.Any(window => window.Required))
            {
                return ProviderCatalogValidationError.MissingRequiredWindow;
            }

            if (provider.QuotaWindows.GroupBy(window => window.Kind)
                .Any(group => group.Count() != 1))
            {
                return ProviderCatalogValidationError.DuplicateWindow;
            }

            foreach (var window in provider.QuotaWindows)
            {
                if (string.IsNullOrWhiteSpace(window.DisplayName) ||
                    window.DisplayName.Length > 80)
                {
                    return ProviderCatalogValidationError.InvalidDisplayName;
                }

                if (window.WarningThresholdPercent is < 0 or > 100 ||
                    window.SafeWrapThresholdPercent is < 0 or > 100 ||
                    window.CriticalBufferPercent is < 0 or > 100)
                {
                    return ProviderCatalogValidationError.InvalidThreshold;
                }

                if (window.CriticalBufferPercent >
                        window.SafeWrapThresholdPercent ||
                    window.SafeWrapThresholdPercent >
                        window.WarningThresholdPercent)
                {
                    return ProviderCatalogValidationError.InvalidThresholdOrder;
                }
            }
        }

        return ProviderCatalogValidationError.None;
    }
}

public sealed record ProviderDetectionResult(
    AiProviderId ProviderId,
    string DisplayName,
    bool Detected,
    ProviderUsageCapability UsageCapability,
    string? Version,
    string Status);

public sealed record ProviderQuotaWindowObservation(
    QuotaWindowKind Kind,
    decimal? RemainingPercent,
    DateTimeOffset? ResetsAtUtc,
    DateTimeOffset ObservedAtUtc,
    ObservationConfidence Confidence,
    ObservationFreshness Freshness,
    string? Error);

public sealed record ProviderPolicyDecision(
    AiProviderId ProviderId,
    GuardPolicyClassification Classification,
    QuotaWindowKind? ControllingWindow,
    IReadOnlyList<ProviderQuotaWindowObservation> Windows,
    string Reason);

public static class MultiWindowProviderPolicy
{
    public static ProviderPolicyDecision Evaluate(
        AiProviderConfiguration configuration,
        IReadOnlyList<ProviderQuotaWindowObservation> observations,
        DateTimeOffset evaluatedAtUtc)
    {
        if (ProviderCatalogValidator.Validate(new ProviderCatalogSettings(
                ProviderCatalogSettings.CurrentSchemaVersion,
                [configuration])) != ProviderCatalogValidationError.None)
        {
            return Unknown(configuration, observations, "configuration_invalid");
        }

        var decisions = new List<(QuotaWindowKind Kind, GuardPolicyClassification Decision)>();
        foreach (var policy in configuration.QuotaWindows.Where(window => window.Required))
        {
            var matches = observations.Where(item => item.Kind == policy.Kind).ToArray();
            if (matches.Length != 1)
            {
                return Unknown(configuration, observations, "required_window_missing_or_duplicate");
            }

            var observation = matches[0];
            if (observation.RemainingPercent is not { } remaining ||
                remaining is < 0 or > 100 ||
                observation.ResetsAtUtc is null ||
                observation.ResetsAtUtc <= evaluatedAtUtc ||
                observation.Confidence != ObservationConfidence.High ||
                observation.Freshness != ObservationFreshness.ObservedNow ||
                observation.Error is not null ||
                observation.ObservedAtUtc > evaluatedAtUtc.AddMinutes(1) ||
                evaluatedAtUtc - observation.ObservedAtUtc > policy.ToPolicy().MaximumObservationAge)
            {
                return Unknown(configuration, observations, "required_window_untrusted");
            }

            var decision = remaining <= policy.SafeWrapThresholdPercent
                ? GuardPolicyClassification.SafeWrap
                : remaining <= policy.WarningThresholdPercent
                    ? GuardPolicyClassification.Warning
                    : GuardPolicyClassification.Normal;
            decisions.Add((policy.Kind, decision));
        }

        var controlling = decisions
            .OrderByDescending(item => Severity(item.Decision))
            .ThenBy(item => item.Kind)
            .First();
        return new ProviderPolicyDecision(
            configuration.ProviderId,
            controlling.Decision,
            controlling.Kind,
            observations,
            "strictest_required_window");
    }

    private static ProviderPolicyDecision Unknown(
        AiProviderConfiguration configuration,
        IReadOnlyList<ProviderQuotaWindowObservation> observations,
        string reason) => new(
            configuration.ProviderId,
            GuardPolicyClassification.Unknown,
            null,
            observations,
            reason);

    private static int Severity(GuardPolicyClassification decision) => decision switch
    {
        GuardPolicyClassification.SafeWrap => 3,
        GuardPolicyClassification.Warning => 2,
        GuardPolicyClassification.Normal => 1,
        _ => 4
    };
}
