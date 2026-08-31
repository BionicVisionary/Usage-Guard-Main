using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace CodexUsageGuard.Core;

public enum GuardResumeStatus
{
    NotRequired,
    Recommended,
    Unavailable
}

public enum GuardResumeReason
{
    DecisionAllowsWork,
    UserOverrideActive,
    FiveHourConstraint,
    WeeklyConstraint,
    AllConstrainingWindows,
    ResetDataUnavailable,
    ResetDataStale
}

public sealed record SanitizedResumeWindow(
    AppServerQuotaWindowKind Kind,
    DateTimeOffset ResetsAtUtc,
    string ResetsAtLocalDisplay);

public sealed record GuardResumeRecommendation(
    GuardResumeStatus Status,
    GuardResumeReason Reason,
    DateTimeOffset? RecommendedAtUtc,
    string? RecommendedAtLocalDisplay,
    string? ResetIdentity,
    int ProviderJitterMarginSeconds,
    bool OneShotWakeUpOptIn,
    IReadOnlyList<SanitizedResumeWindow> ConstrainingWindows);

public static class GuardResumePlanner
{
    public static readonly TimeSpan ProviderJitterMargin = TimeSpan.FromMinutes(2);

    public static GuardResumeRecommendation NotRequired(
        GuardSettings settings,
        bool overrideActive) => new(
        GuardResumeStatus.NotRequired,
        overrideActive
            ? GuardResumeReason.UserOverrideActive
            : GuardResumeReason.DecisionAllowsWork,
        null,
        null,
        null,
        checked((int)ProviderJitterMargin.TotalSeconds),
        settings.ResetWakeUpEnabled,
        []);

    public static GuardResumeRecommendation Unavailable(
        GuardSettings settings,
        GuardResumeReason reason = GuardResumeReason.ResetDataUnavailable) => new(
        GuardResumeStatus.Unavailable,
        reason,
        null,
        null,
        null,
        checked((int)ProviderJitterMargin.TotalSeconds),
        settings.ResetWakeUpEnabled,
        []);

    public static GuardResumeRecommendation ForConstraints(
        GuardSettings settings,
        DateTimeOffset evaluatedAtUtc,
        IEnumerable<SanitizedResumeWindow> constraints,
        IReadOnlyDictionary<AppServerQuotaWindowKind, DateTimeOffset> stableIdentityResets)
    {
        var normalized = constraints
            .OrderBy(item => item.Kind)
            .ToArray();
        if (normalized.Length is < 1 or > 2 ||
            normalized.Select(item => item.Kind).Distinct().Count() != normalized.Length)
        {
            return Unavailable(settings);
        }

        var recommendedAtUtc = normalized.Max(item => item.ResetsAtUtc) +
            ProviderJitterMargin;
        if (recommendedAtUtc <= evaluatedAtUtc)
        {
            return Unavailable(settings, GuardResumeReason.ResetDataStale);
        }

        if (normalized.Any(item =>
            !stableIdentityResets.TryGetValue(item.Kind, out var stable) ||
            !WeeklyWindowIdentity.IsSameWindow(stable, item.ResetsAtUtc)))
        {
            return Unavailable(settings);
        }
        var canonical = string.Join(
            "|",
            normalized.Select(item =>
                $"{item.Kind}:{stableIdentityResets[item.Kind].UtcTicks}"));
        var identity = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24]
            .ToLowerInvariant();
        var reason = normalized.Length == 2
            ? GuardResumeReason.AllConstrainingWindows
            : normalized[0].Kind == AppServerQuotaWindowKind.FiveHour
                ? GuardResumeReason.FiveHourConstraint
                : GuardResumeReason.WeeklyConstraint;

        return new GuardResumeRecommendation(
            GuardResumeStatus.Recommended,
            reason,
            recommendedAtUtc.ToUniversalTime(),
            LocalDisplay(recommendedAtUtc),
            identity,
            checked((int)ProviderJitterMargin.TotalSeconds),
            settings.ResetWakeUpEnabled,
            normalized);
    }

    public static string LocalDisplay(DateTimeOffset value) =>
        value.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss zzz",
            CultureInfo.InvariantCulture);
}
