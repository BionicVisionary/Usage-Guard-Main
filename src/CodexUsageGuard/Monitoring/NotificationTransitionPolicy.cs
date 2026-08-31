using CodexUsageGuard.Core;

namespace CodexUsageGuard.Monitoring;

public enum GuardNotificationKind
{
    None,
    Warning,
    SafeWrap,
    Unknown,
    Recovery,
    Reset
}

public sealed record GuardNotificationDecision(
    GuardNotificationKind Kind,
    string Key);

public static class NotificationTransitionPolicy
{
    public static readonly TimeSpan RepeatCooldown = TimeSpan.FromMinutes(30);

    public static GuardNotificationDecision Evaluate(
        SanitizedUsageState previous,
        SanitizedUsageState current,
        GuardSettings settings,
        GuardPersistentState persistentState,
        DateTimeOffset nowUtc)
    {
        var kind = SelectKind(previous, current, settings);
        if (kind == GuardNotificationKind.None)
        {
            return new GuardNotificationDecision(kind, string.Empty);
        }

        var fiveHourReset = StableResetFor(
            current,
            AppServerQuotaWindowKind.FiveHour,
            persistentState.LastSuccessfulFiveHourResetAtUtc);
        var weeklyReset = StableResetFor(
            current,
            AppServerQuotaWindowKind.Weekly,
            persistentState.LastSuccessfulWeeklyResetAtUtc);
        var key = string.Join(
            ':',
            kind,
            current.UnderlyingDecision,
            fiveHourReset?.ToUnixTimeSeconds().ToString() ?? "none",
            weeklyReset?.ToUnixTimeSeconds().ToString() ?? "none");
        var ledger = persistentState.NotificationLedger;
        var shownAt = default(DateTimeOffset);
        var wasShown = ledger is not null && ledger.TryGetValue(key, out shownAt);
        if ((kind == GuardNotificationKind.Reset && wasShown) ||
            (wasShown && nowUtc - shownAt < RepeatCooldown) ||
            (!wasShown && persistentState.LastNotificationKey == key &&
             persistentState.LastNotificationAtUtc is { } last &&
             (kind == GuardNotificationKind.Reset || nowUtc - last < RepeatCooldown)))
        {
            return new GuardNotificationDecision(
                GuardNotificationKind.None,
                key);
        }

        return new GuardNotificationDecision(kind, key);
    }

    private static DateTimeOffset? StableResetFor(
        SanitizedUsageState current,
        AppServerQuotaWindowKind kind,
        DateTimeOffset? prior)
    {
        var observed = current.Windows?.SingleOrDefault(item => item.Kind == kind)
            ?.ResetsAtUtc;
        return observed is { } reset
            ? WeeklyWindowIdentity.StableKey(reset, prior)
            : prior;
    }

    private static GuardNotificationKind SelectKind(
        SanitizedUsageState previous,
        SanitizedUsageState current,
        GuardSettings settings)
    {
        if (current.ResetDetected && settings.NotifyReset)
        {
            return GuardNotificationKind.Reset;
        }

        if (current.UnderlyingDecision == previous.UnderlyingDecision)
        {
            return GuardNotificationKind.None;
        }

        if (current.UnderlyingDecision == GuardRuntimeState.Warning &&
            settings.NotifyWarning)
        {
            return GuardNotificationKind.Warning;
        }

        if (current.UnderlyingDecision == GuardRuntimeState.SafeWrap &&
            settings.NotifySafeWrap)
        {
            return GuardNotificationKind.SafeWrap;
        }

        if (current.UnderlyingDecision is GuardRuntimeState.Unknown or
                GuardRuntimeState.ProvenanceMismatch &&
            settings.NotifyUnknown)
        {
            return GuardNotificationKind.Unknown;
        }

        if (previous.UnderlyingDecision is GuardRuntimeState.Unknown or
                GuardRuntimeState.ProvenanceMismatch &&
            current.UnderlyingDecision is GuardRuntimeState.Normal or
                GuardRuntimeState.Warning &&
            settings.NotifyRecovery)
        {
            return GuardNotificationKind.Recovery;
        }

        return GuardNotificationKind.None;
    }
}
