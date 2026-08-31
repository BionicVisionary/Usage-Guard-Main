using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.Providers;

public sealed record ClaudeUsageSnapshot(
    int SchemaVersion,
    bool Available,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<ProviderQuotaWindowObservation> Windows,
    string? Error)
{
    public const int CurrentSchemaVersion = 1;

    public static ClaudeUsageSnapshot UnavailableAt(
        DateTimeOffset observedAtUtc,
        string error) => new(
            CurrentSchemaVersion,
            false,
            observedAtUtc,
            [],
            error);

    /// <summary>
    /// Reconciles a newly observed snapshot with the stored one.
    /// </summary>
    /// <remarks>
    /// Every Claude Code session runs the status line and writes to this one
    /// shared state, and a session that has stopped making requests keeps
    /// reporting the rate limits it last saw. Blindly overwriting therefore let
    /// an idle session raise the remaining percentage back up and hide real
    /// consumption, which is the wrong direction for a guard to be wrong in.
    ///
    /// Within a single quota window, identified by its provider reset
    /// timestamp with the same small provider-jitter tolerance used elsewhere,
    /// remaining usage only ever falls. The lower observation is retained with
    /// its original timestamp, so it cannot be made artificially fresh by an
    /// idle session. A genuinely later reset replaces the old window. A
    /// backwards reset identity fails closed.
    /// </remarks>
    public static ClaudeUsageSnapshot Reconcile(
        ClaudeUsageSnapshot? previous,
        ClaudeUsageSnapshot observed)
    {
        if (!observed.Available || previous is null || !previous.Available)
        {
            return observed;
        }
        var resetIdentityRegressed = false;
        var windows = observed.Windows.Select(window =>
        {
            var earlier = previous.Windows.SingleOrDefault(item =>
                item.Kind == window.Kind);
            if (earlier?.ResetsAtUtc is not { } earlierReset ||
                window.ResetsAtUtc is not { } observedReset)
            {
                return window;
            }
            if (WeeklyWindowIdentity.IsSameWindow(earlierReset, observedReset))
            {
                return earlier.RemainingPercent < window.RemainingPercent
                    ? earlier
                    : window;
            }
            if (observedReset < earlierReset)
            {
                resetIdentityRegressed = true;
            }
            return window;
        }).ToArray();
        if (resetIdentityRegressed)
        {
            return UnavailableAt(observed.ObservedAtUtc, "reset_identity_regressed");
        }
        return observed with
        {
            ObservedAtUtc = windows.Min(item => item.ObservedAtUtc),
            Windows = windows
        };
    }
}

public sealed record ClaudeGuardWindowOutput(
    string Kind,
    decimal? RemainingPercent,
    DateTimeOffset? ResetsAtUtc,
    DateTimeOffset? ObservedAtUtc);

public sealed record ClaudeGuardCheckOutput(
    string Decision,
    string Provider,
    string Source,
    string Confidence,
    string Freshness,
    string? ControllingWindow,
    bool CriticalBufferReached,
    bool StartNewPhaseAllowed,
    bool FinishCurrentCheckpointOnly,
    IReadOnlyList<ClaudeGuardWindowOutput> Windows)
{
    public static ClaudeGuardCheckOutput Evaluate(
        AiProviderConfiguration? configuration,
        ClaudeUsageSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        if (configuration is null || !configuration.Enabled)
        {
            return Unknown();
        }
        if (configuration.UnrestrictedDevelopmentOverride)
        {
            return new ClaudeGuardCheckOutput(
                "override_active",
                "claude_code",
                "user_override",
                "high",
                "configured",
                null,
                false,
                true,
                false,
                []);
        }
        var decision = MultiWindowProviderPolicy.Evaluate(
            configuration,
            snapshot.Available ? snapshot.Windows : [],
            nowUtc);
        if (decision.Classification == GuardPolicyClassification.Unknown)
        {
            return Unknown();
        }
        var decisionText = decision.Classification switch
        {
            GuardPolicyClassification.Normal => "normal",
            GuardPolicyClassification.Warning => "warning",
            GuardPolicyClassification.SafeWrap => "safe_wrap",
            _ => "unknown"
        };
        return new ClaudeGuardCheckOutput(
            decisionText,
            "claude_code",
            "claude_statusline",
            "high",
            "observed_now",
            decision.ControllingWindow switch
            {
                QuotaWindowKind.RollingFiveHour => "five_hour",
                QuotaWindowKind.Weekly => "weekly",
                _ => null
            },
            decision.Classification == GuardPolicyClassification.SafeWrap &&
                configuration.QuotaWindows.Where(item => item.Required).Any(policy =>
                    decision.Windows.Single(item => item.Kind == policy.Kind)
                        .RemainingPercent <= policy.CriticalBufferPercent),
            decision.Classification is GuardPolicyClassification.Normal or
                GuardPolicyClassification.Warning,
            decision.Classification == GuardPolicyClassification.SafeWrap,
            decision.Windows.Select(item => new ClaudeGuardWindowOutput(
                item.Kind == QuotaWindowKind.RollingFiveHour
                    ? "five_hour"
                    : "weekly",
                item.RemainingPercent,
                item.ResetsAtUtc,
                item.ObservedAtUtc)).ToArray());
    }

    private static ClaudeGuardCheckOutput Unknown() => new(
        "unknown",
        "claude_code",
        "claude_statusline",
        "none",
        "unknown",
        null,
        false,
        false,
        true,
        []);
}

public static class ClaudeStatusLineParser
{
    public const int MaximumInputBytes = 65_536;

    public static ClaudeUsageSnapshot Parse(
        ReadOnlyMemory<byte> json,
        DateTimeOffset observedAtUtc)
    {
        if (json.Length is 0 or > MaximumInputBytes)
        {
            return ClaudeUsageSnapshot.UnavailableAt(observedAtUtc, "input_invalid");
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 24,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                Count(root, "rate_limits") != 1 ||
                !root.TryGetProperty("rate_limits", out var limits) ||
                limits.ValueKind != JsonValueKind.Object ||
                Count(limits, "five_hour") != 1 ||
                Count(limits, "seven_day") != 1 ||
                !TryWindow(
                    limits,
                    "five_hour",
                    QuotaWindowKind.RollingFiveHour,
                    observedAtUtc,
                    out var fiveHour) ||
                !TryWindow(
                    limits,
                    "seven_day",
                    QuotaWindowKind.Weekly,
                    observedAtUtc,
                    out var weekly))
            {
                return ClaudeUsageSnapshot.UnavailableAt(
                    observedAtUtc,
                    "required_rate_limits_missing_or_invalid");
            }
            return new ClaudeUsageSnapshot(
                ClaudeUsageSnapshot.CurrentSchemaVersion,
                true,
                observedAtUtc,
                [fiveHour!, weekly!],
                null);
        }
        catch (JsonException)
        {
            return ClaudeUsageSnapshot.UnavailableAt(observedAtUtc, "input_invalid");
        }
    }

    private static bool TryWindow(
        JsonElement limits,
        string propertyName,
        QuotaWindowKind kind,
        DateTimeOffset observedAtUtc,
        out ProviderQuotaWindowObservation? observation)
    {
        observation = null;
        if (!limits.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            Count(window, "used_percentage") != 1 ||
            Count(window, "resets_at") != 1 ||
            !window.TryGetProperty("used_percentage", out var usedElement) ||
            !usedElement.TryGetDecimal(out var used) ||
            used is < 0 or > 100 ||
            !window.TryGetProperty("resets_at", out var resetElement) ||
            !resetElement.TryGetInt64(out var resetSeconds))
        {
            return false;
        }
        DateTimeOffset reset;
        try
        {
            reset = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        if (reset <= observedAtUtc)
        {
            return false;
        }
        observation = new ProviderQuotaWindowObservation(
            kind,
            100m - used,
            reset,
            observedAtUtc,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null);
        return true;
    }

    private static int Count(JsonElement element, string name) =>
        element.EnumerateObject().Count(property => property.NameEquals(name));
}

public sealed class ClaudeUsageStorage(string rootDirectory)
{
    private const string FileName = "claude-state.json";
    private const string LockFileName = "claude-state.lock";
    private const int MaximumStateBytes = 32 * 1024;
    private static readonly TimeSpan WriterLockTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public ClaudeUsageSnapshot Load(DateTimeOffset nowUtc)
    {
        var path = Path.Combine(rootDirectory, FileName);
        if (!File.Exists(path))
        {
            return ClaudeUsageSnapshot.UnavailableAt(nowUtc, "no_observation_yet");
        }
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return ClaudeUsageSnapshot.UnavailableAt(
                    nowUtc,
                    "stored_state_invalid");
            }
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > MaximumStateBytes)
            {
                return ClaudeUsageSnapshot.UnavailableAt(
                    nowUtc,
                    "stored_state_invalid");
            }
            var snapshot = JsonSerializer.Deserialize<ClaudeUsageSnapshot>(
                stream,
                JsonOptions);
            return IsValid(snapshot)
                ? snapshot!
                : ClaudeUsageSnapshot.UnavailableAt(nowUtc, "stored_state_invalid");
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException)
        {
            return ClaudeUsageSnapshot.UnavailableAt(nowUtc, "stored_state_unavailable");
        }
    }

    public void Save(ClaudeUsageSnapshot snapshot)
    {
        if (!IsValid(snapshot))
        {
            throw new InvalidDataException("Claude snapshot is invalid.");
        }
        Directory.CreateDirectory(rootDirectory);
        RestrictDirectory();
        using var writerLock = AcquireWriterLock();
        SaveOwned(snapshot);
    }

    public ClaudeUsageSnapshot ReconcileAndSave(
        ClaudeUsageSnapshot observed,
        DateTimeOffset nowUtc)
    {
        if (!IsValid(observed))
        {
            throw new InvalidDataException("Claude snapshot is invalid.");
        }
        Directory.CreateDirectory(rootDirectory);
        RestrictDirectory();
        using var writerLock = AcquireWriterLock();
        var reconciled = ClaudeUsageSnapshot.Reconcile(Load(nowUtc), observed);
        SaveOwned(reconciled);
        return reconciled;
    }

    private void SaveOwned(ClaudeUsageSnapshot snapshot)
    {
        var path = Path.Combine(rootDirectory, FileName);
        var temporary = path + ".new";
        if (File.Exists(temporary))
        {
            // The cross-process writer lock proves no live helper owns this
            // fixed temporary path. Refuse a reparse point, then take an
            // exclusive handle before deleting the interrupted owned write.
            if ((File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The Claude state temporary is not a regular file.");
            }
            try
            {
                using var abandoned = new FileStream(
                    temporary,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Delete,
                    1,
                    FileOptions.WriteThrough);
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                throw new IOException("A previous Claude state write is still in progress.");
            }
        }
        var temporaryCreated = false;
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
                temporaryCreated = true;
                JsonSerializer.Serialize(stream, snapshot, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (temporaryCreated)
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private FileStream AcquireWriterLock()
    {
        var path = Path.Combine(rootDirectory, LockFileName);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                if (elapsed.Elapsed >= WriterLockTimeout)
                {
                    throw new IOException(
                        "Another Claude state update did not finish within the bounded wait.",
                        exception);
                }
                Thread.Sleep(25);
            }
        }
    }

    private static bool IsValid(ClaudeUsageSnapshot? snapshot)
    {
        if (snapshot is null ||
            snapshot.SchemaVersion != ClaudeUsageSnapshot.CurrentSchemaVersion ||
            snapshot.ObservedAtUtc == default)
        {
            return false;
        }
        if (!snapshot.Available)
        {
            return snapshot.Windows.Count == 0 &&
                !string.IsNullOrWhiteSpace(snapshot.Error);
        }
        return snapshot.Error is null &&
            snapshot.Windows.Count == 2 &&
            snapshot.Windows.Select(item => item.Kind).Distinct().Count() == 2 &&
            snapshot.Windows.All(item =>
                item.RemainingPercent is >= 0 and <= 100 &&
                item.ResetsAtUtc > item.ObservedAtUtc &&
                item.Confidence == ObservationConfidence.High &&
                item.Freshness == ObservationFreshness.ObservedNow &&
                item.Error is null);
    }

    private void RestrictDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new UnauthorizedAccessException();
        var inheritance = InheritanceFlags.ContainerInherit |
            InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(rootDirectory).SetAccessControl(security);
    }
}
