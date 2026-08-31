using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageGuard.Core;

public enum StorageLoadStatus
{
    Loaded,
    MissingDefaults,
    Corrupt,
    UnsupportedVersion,
    Inaccessible
}

public sealed record SettingsLoadResult(
    GuardSettings Settings,
    StorageLoadStatus Status,
    SettingsValidationError ValidationError);

public sealed record StateLoadResult(
    GuardPersistentState State,
    StorageLoadStatus Status);

public interface IGuardStorage
{
    SettingsLoadResult LoadSettings();

    StateLoadResult LoadState();

    void SaveSettings(GuardSettings settings);

    void SaveState(GuardPersistentState state);
}

public static class GuardDataPaths
{
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenAI",
        "CodexUsageGuard");
}

public sealed class GuardFileStorage(string rootDirectory) : IGuardStorage
{
    private const string SettingsFileName = "settings.json";
    private const string StateFileName = "state.json";
    private readonly object _sync = new();
    private readonly string _storageMutexName = "Local\\OpenAI.CodexUsageGuard.Storage." +
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar)
                .ToUpperInvariant())))[..24];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public string RootDirectory { get; } = rootDirectory;

    public SettingsLoadResult LoadSettings()
    {
        lock (_sync)
        {
            var path = Path.Combine(RootDirectory, SettingsFileName);
            if (!File.Exists(path))
            {
                return new SettingsLoadResult(
                    GuardSettings.Default,
                    StorageLoadStatus.MissingDefaults,
                    SettingsValidationError.None);
            }

            try
            {
                var settings = JsonSerializer.Deserialize<GuardSettings>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (settings is null)
                {
                    return CorruptSettings();
                }

                var validation = GuardSettingsValidator.Validate(settings);
                if (validation == SettingsValidationError.UnsupportedSchema)
                {
                    return new SettingsLoadResult(
                        GuardSettings.Default,
                        StorageLoadStatus.UnsupportedVersion,
                        validation);
                }

                return validation == SettingsValidationError.None
                    ? new SettingsLoadResult(
                        settings,
                        StorageLoadStatus.Loaded,
                        SettingsValidationError.None)
                    : new SettingsLoadResult(
                        GuardSettings.Default,
                        StorageLoadStatus.Corrupt,
                        validation);
            }
            catch (UnauthorizedAccessException)
            {
                return InaccessibleSettings();
            }
            catch (IOException)
            {
                return InaccessibleSettings();
            }
            catch (JsonException)
            {
                return CorruptSettings();
            }
        }
    }

    public StateLoadResult LoadState()
    {
        lock (_sync)
        {
            var path = Path.Combine(RootDirectory, StateFileName);
            if (!File.Exists(path))
            {
                return new StateLoadResult(
                    GuardPersistentState.Empty,
                    StorageLoadStatus.MissingDefaults);
            }

            try
            {
                var state = JsonSerializer.Deserialize<GuardPersistentState>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (state is null)
                {
                    return CorruptState();
                }

                if (state.SchemaVersion != GuardPersistentState.CurrentSchemaVersion)
                {
                    return new StateLoadResult(
                        GuardPersistentState.Empty,
                        StorageLoadStatus.UnsupportedVersion);
                }

                if (state.ConsecutiveFailures is < 0 or > 30 ||
                    (state.LatchedWeeklyResetAtUtc is null) !=
                    (state.LatchCreatedAtUtc is null) ||
                    (state.LatchedFiveHourResetAtUtc is null) !=
                    (state.FiveHourLatchCreatedAtUtc is null))
                {
                    return CorruptState();
                }

                var ledger = state.NotificationLedger is null
                    ? new Dictionary<string, DateTimeOffset>()
                    : new Dictionary<string, DateTimeOffset>(
                        state.NotificationLedger,
                        StringComparer.Ordinal);
                if (state.LastNotificationKey is { Length: > 0 } priorKey &&
                    state.LastNotificationAtUtc is { } priorTime)
                {
                    ledger.TryAdd(priorKey, priorTime);
                }
                if (ledger.Count > 6 || ledger.Any(item =>
                    item.Key.Length > 160 ||
                    string.IsNullOrWhiteSpace(item.Key)))
                {
                    return CorruptState();
                }

                return new StateLoadResult(
                    state with { NotificationLedger = ledger },
                    StorageLoadStatus.Loaded);
            }
            catch (UnauthorizedAccessException)
            {
                return InaccessibleState();
            }
            catch (IOException)
            {
                return InaccessibleState();
            }
            catch (JsonException)
            {
                return CorruptState();
            }
        }
    }

    public void SaveSettings(GuardSettings settings)
    {
        var validation = GuardSettingsValidator.Validate(settings);
        if (validation != SettingsValidationError.None)
        {
            throw new InvalidDataException($"Invalid settings: {validation}.");
        }

        SaveAtomically(SettingsFileName, settings);
    }

    public void SaveState(GuardPersistentState state)
    {
        if (state.SchemaVersion != GuardPersistentState.CurrentSchemaVersion ||
            state.ConsecutiveFailures is < 0 or > 30 ||
            (state.LatchedWeeklyResetAtUtc is null) !=
            (state.LatchCreatedAtUtc is null) ||
            (state.LatchedFiveHourResetAtUtc is null) !=
            (state.FiveHourLatchCreatedAtUtc is null) ||
            state.NotificationLedger is { Count: > 6 } ||
            state.NotificationLedger?.Any(item =>
                item.Key.Length > 160 ||
                string.IsNullOrWhiteSpace(item.Key)) == true)
        {
            throw new InvalidDataException("Invalid sanitized state.");
        }

        WithCrossProcessWriteLock(() =>
        {
            var existing = LoadState();
            var merged = existing.Status == StorageLoadStatus.Loaded
                ? MergeNotificationMetadata(state, existing.State)
                : state;
            SaveAtomicallyUnderLock(StateFileName, merged);
        });
    }

    private void SaveAtomically<T>(string fileName, T value)
    {
        WithCrossProcessWriteLock(() => SaveAtomicallyUnderLock(fileName, value));
    }

    private void SaveAtomicallyUnderLock<T>(string fileName, T value)
    {
        lock (_sync)
        {
            Directory.CreateDirectory(RootDirectory);
            RestrictDirectoryToCurrentUser();
            var path = Path.Combine(RootDirectory, fileName);
            var temporaryPath = path + ".new";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, value, JsonOptions);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (IOException)
                {
                    // A bounded failed save remains fail-closed on the next load.
                }
                catch (UnauthorizedAccessException)
                {
                    // A bounded failed save remains fail-closed on the next load.
                }
            }
        }
    }

    private void WithCrossProcessWriteLock(Action action)
    {
        using var mutex = new Mutex(false, _storageMutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new IOException("Sanitized storage is busy.");
            }

            action();
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static GuardPersistentState MergeNotificationMetadata(
        GuardPersistentState current,
        GuardPersistentState existing)
    {
        var combined = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var item in existing.NotificationLedger ??
            new Dictionary<string, DateTimeOffset>())
        {
            combined[item.Key] = item.Value;
        }
        foreach (var item in current.NotificationLedger ??
            new Dictionary<string, DateTimeOffset>())
        {
            if (!combined.TryGetValue(item.Key, out var prior) || item.Value > prior)
            {
                combined[item.Key] = item.Value;
            }
        }

        var ledger = combined
            .GroupBy(item => item.Key.Split(':', 2)[0], StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Value).First())
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var useExistingLast = existing.LastNotificationAtUtc is { } existingAt &&
            (current.LastNotificationAtUtc is null ||
             existingAt > current.LastNotificationAtUtc.Value);
        return current with
        {
            LastNotificationKey = useExistingLast
                ? existing.LastNotificationKey
                : current.LastNotificationKey,
            LastNotificationAtUtc = useExistingLast
                ? existing.LastNotificationAtUtc
                : current.LastNotificationAtUtc,
            NotificationLedger = ledger
        };
    }

    private void RestrictDirectoryToCurrentUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ??
            throw new UnauthorizedAccessException(
                "The current Windows user identity is unavailable.");
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
        new DirectoryInfo(RootDirectory).SetAccessControl(security);
    }

    private static SettingsLoadResult CorruptSettings() => new(
        GuardSettings.Default,
        StorageLoadStatus.Corrupt,
        SettingsValidationError.ThresholdOrderInvalid);

    private static SettingsLoadResult InaccessibleSettings() => new(
        GuardSettings.Default,
        StorageLoadStatus.Inaccessible,
        SettingsValidationError.None);

    private static StateLoadResult CorruptState() => new(
        GuardPersistentState.Empty,
        StorageLoadStatus.Corrupt);

    private static StateLoadResult InaccessibleState() => new(
        GuardPersistentState.Empty,
        StorageLoadStatus.Inaccessible);
}
