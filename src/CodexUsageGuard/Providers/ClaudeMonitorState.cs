using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexUsageGuard.Monitoring;

namespace CodexUsageGuard.Providers;

public sealed record ClaudeMonitorState(
    int SchemaVersion,
    string? LastDecision,
    DateTimeOffset? FiveHourResetAtUtc,
    DateTimeOffset? WeeklyResetAtUtc,
    IReadOnlyDictionary<string, DateTimeOffset> NotificationLedger)
{
    public const int CurrentSchemaVersion = 1;

    public static ClaudeMonitorState Empty { get; } = new(
        CurrentSchemaVersion,
        null,
        null,
        null,
        new Dictionary<string, DateTimeOffset>());
}

public sealed class ClaudeMonitorStateStorage(string rootDirectory)
{
    private const string FileName = "claude-monitor-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public ClaudeMonitorState Load()
    {
        var path = Path.Combine(rootDirectory, FileName);
        if (!File.Exists(path)) return ClaudeMonitorState.Empty;
        try
        {
            var state = JsonSerializer.Deserialize<ClaudeMonitorState>(
                File.ReadAllText(path), JsonOptions);
            return IsValid(state) ? state! : ClaudeMonitorState.Empty;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException)
        {
            return ClaudeMonitorState.Empty;
        }
    }

    public void Save(ClaudeMonitorState state)
    {
        if (!IsValid(state))
        {
            throw new InvalidDataException("Claude monitor state is invalid.");
        }
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, FileName);
        var temporary = path + ".new";
        if (File.Exists(temporary))
        {
            throw new IOException("A previous Claude monitor-state write is incomplete.");
        }
        var created = false;
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                created = true;
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is
                    IOException or UnauthorizedAccessException)
                { }
            }
        }
    }

    private static bool IsValid(ClaudeMonitorState? state) =>
        state is not null &&
        state.SchemaVersion == ClaudeMonitorState.CurrentSchemaVersion &&
        (state.LastDecision is null || state.LastDecision is
            "normal" or "warning" or "safe_wrap" or "unknown" or
            "override_active") &&
        state.NotificationLedger.Count <= 128 &&
        state.NotificationLedger.All(item =>
            item.Key.Length is > 0 and <= 180 && item.Value != default);
}

public sealed record ClaudeNotificationTransition(
    GuardNotificationKind Kind,
    string Key,
    ClaudeMonitorState State);

public static class ClaudeNotificationPolicy
{
    private static readonly TimeSpan ResetTolerance = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(30);

    public static ClaudeNotificationTransition Evaluate(
        ClaudeGuardCheckOutput current,
        AiProviderConfiguration configuration,
        ClaudeMonitorState previous,
        DateTimeOffset nowUtc)
    {
        var fiveReset = Reset(current, "five_hour");
        var weekReset = Reset(current, "weekly");
        var resetDetected = IsNewReset(previous.FiveHourResetAtUtc, fiveReset) ||
            IsNewReset(previous.WeeklyResetAtUtc, weekReset);
        var kind = SelectKind(current.Decision, previous.LastDecision,
            resetDetected, configuration);
        var key = kind == GuardNotificationKind.None
            ? string.Empty
            : string.Join(':', kind, current.Decision,
                StableResetKey(fiveReset), StableResetKey(weekReset));
        var ledger = new Dictionary<string, DateTimeOffset>(
            previous.NotificationLedger,
            StringComparer.Ordinal);
        if (kind != GuardNotificationKind.None &&
            ledger.TryGetValue(key, out var shownAt) &&
            (kind == GuardNotificationKind.Reset || nowUtc - shownAt < Cooldown))
        {
            kind = GuardNotificationKind.None;
        }
        if (kind != GuardNotificationKind.None)
        {
            ledger[key] = nowUtc;
        }
        foreach (var stale in ledger.OrderByDescending(item => item.Value)
                     .Skip(128).Select(item => item.Key).ToArray())
        {
            ledger.Remove(stale);
        }
        var state = new ClaudeMonitorState(
            ClaudeMonitorState.CurrentSchemaVersion,
            current.Decision,
            fiveReset ?? previous.FiveHourResetAtUtc,
            weekReset ?? previous.WeeklyResetAtUtc,
            ledger);
        return new ClaudeNotificationTransition(kind, key, state);
    }

    private static GuardNotificationKind SelectKind(
        string current,
        string? previous,
        bool resetDetected,
        AiProviderConfiguration settings)
    {
        if (!settings.MonitoringEnabled) return GuardNotificationKind.None;
        if (resetDetected && settings.NotifyReset) return GuardNotificationKind.Reset;
        if (current == previous) return GuardNotificationKind.None;
        if (current == "warning" && settings.NotifyWarning)
            return GuardNotificationKind.Warning;
        if (current == "safe_wrap" && settings.NotifySafeWrap)
            return GuardNotificationKind.SafeWrap;
        if (current == "unknown" && settings.NotifyUnknown)
            return GuardNotificationKind.Unknown;
        if (previous == "unknown" && current is "normal" or "warning" &&
            settings.NotifyRecovery)
            return GuardNotificationKind.Recovery;
        return GuardNotificationKind.None;
    }

    private static DateTimeOffset? Reset(
        ClaudeGuardCheckOutput output,
        string kind) => output.Windows.SingleOrDefault(item =>
            item.Kind == kind)?.ResetsAtUtc;

    private static bool IsNewReset(
        DateTimeOffset? previous,
        DateTimeOffset? current) => previous is { } before && current is { } now &&
        now - before > ResetTolerance;

    private static string StableResetKey(DateTimeOffset? value) => value is null
        ? "none"
        : (value.Value.ToUnixTimeSeconds() / (long)ResetTolerance.TotalSeconds)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
}
