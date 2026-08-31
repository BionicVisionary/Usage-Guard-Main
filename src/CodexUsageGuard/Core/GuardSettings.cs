namespace CodexUsageGuard.Core;

public sealed record GuardSettings(
    int SchemaVersion,
    decimal WarningThresholdPercent,
    decimal SafeWrapThresholdPercent,
    decimal CriticalBufferPercent,
    int PollingIntervalSeconds,
    bool NotifyWarning,
    bool NotifySafeWrap,
    bool NotifyUnknown,
    bool NotifyRecovery,
    bool NotifyReset,
    bool MinimizeToTray,
    bool MonitoringEnabled,
    bool StartAtSignIn,
    bool LaunchTogetherShortcutsEnabled,
    bool UnrestrictedDevelopmentOverride,
    decimal FiveHourWarningThresholdPercent = 30m,
    decimal FiveHourSafeWrapThresholdPercent = 25m,
    decimal FiveHourCriticalBufferPercent = 20m,
    bool ResetWakeUpEnabled = false)
{
    public const int CurrentSchemaVersion = 1;
    public const int MinimumPollingIntervalSeconds = 30;
    public const int MaximumPollingIntervalSeconds = 300;

    public static GuardSettings Default { get; } = new(
        CurrentSchemaVersion,
        WarningThresholdPercent: 30m,
        SafeWrapThresholdPercent: 25m,
        CriticalBufferPercent: 20m,
        PollingIntervalSeconds: 60,
        NotifyWarning: true,
        NotifySafeWrap: true,
        NotifyUnknown: true,
        NotifyRecovery: true,
        NotifyReset: true,
        MinimizeToTray: true,
        MonitoringEnabled: true,
        StartAtSignIn: false,
        LaunchTogetherShortcutsEnabled: false,
        UnrestrictedDevelopmentOverride: false,
        FiveHourWarningThresholdPercent: 30m,
        FiveHourSafeWrapThresholdPercent: 25m,
        FiveHourCriticalBufferPercent: 20m,
        ResetWakeUpEnabled: false);

    public GuardPolicyConfiguration ToPolicy() => new(
        WarningThresholdPercent,
        SafeWrapThresholdPercent,
        CriticalBufferPercent,
        TimeSpan.FromMinutes(2));

    public GuardPolicyConfiguration ToFiveHourPolicy() => new(
        FiveHourWarningThresholdPercent,
        FiveHourSafeWrapThresholdPercent,
        FiveHourCriticalBufferPercent,
        TimeSpan.FromMinutes(2));

    public GuardSettings RestoreDefaultsPreservingOverride() =>
        Default with
        {
            UnrestrictedDevelopmentOverride = UnrestrictedDevelopmentOverride
        };
}

public enum SettingsValidationError
{
    None,
    UnsupportedSchema,
    ThresholdOutOfRange,
    ThresholdOrderInvalid,
    PollingIntervalOutOfRange
}

public static class GuardSettingsValidator
{
    public static SettingsValidationError Validate(GuardSettings settings)
    {
        if (settings.SchemaVersion != GuardSettings.CurrentSchemaVersion)
        {
            return SettingsValidationError.UnsupportedSchema;
        }

        if (settings.WarningThresholdPercent is < 0 or > 100 ||
            settings.SafeWrapThresholdPercent is < 0 or > 100 ||
            settings.CriticalBufferPercent is < 0 or > 100 ||
            settings.FiveHourWarningThresholdPercent is < 0 or > 100 ||
            settings.FiveHourSafeWrapThresholdPercent is < 0 or > 100 ||
            settings.FiveHourCriticalBufferPercent is < 0 or > 100)
        {
            return SettingsValidationError.ThresholdOutOfRange;
        }

        if (settings.CriticalBufferPercent > settings.SafeWrapThresholdPercent ||
            settings.SafeWrapThresholdPercent > settings.WarningThresholdPercent ||
            settings.FiveHourCriticalBufferPercent >
                settings.FiveHourSafeWrapThresholdPercent ||
            settings.FiveHourSafeWrapThresholdPercent >
                settings.FiveHourWarningThresholdPercent)
        {
            return SettingsValidationError.ThresholdOrderInvalid;
        }

        if (settings.PollingIntervalSeconds is <
                GuardSettings.MinimumPollingIntervalSeconds or >
                GuardSettings.MaximumPollingIntervalSeconds)
        {
            return SettingsValidationError.PollingIntervalOutOfRange;
        }

        return SettingsValidationError.None;
    }
}
