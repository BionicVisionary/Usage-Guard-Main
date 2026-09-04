using System.IO;
using CodexUsageGuard.AppServer;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.Monitoring;

public interface IUsageObservationSource
{
    Task<AppServerUsageObservation> ObserveAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AppServerObservationSource(
    AppServerUsageClient client) : IUsageObservationSource
{
    public Task<AppServerUsageObservation> ObserveAsync(
        CancellationToken cancellationToken = default) =>
        client.ObserveAsync(cancellationToken);
}

public sealed record MonitorStateChangedEventArgs(
    SanitizedUsageState Previous,
    SanitizedUsageState Current,
    bool IsMonitoring);

public enum SettingsUpdateAuthority
{
    PreserveLatches,
    UserApply
}

public sealed class UsageMonitor : IAsyncDisposable
{
    private readonly IUsageObservationSource _source;
    private readonly IGuardStorage _storage;
    private readonly IObservationClock _clock;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private Task<SanitizedUsageState>? _activeCheck;
    private GuardSettings _settings;
    private GuardPersistentState _state;
    private SanitizedUsageState _current;
    private bool _disposed;

    public UsageMonitor(
        IUsageObservationSource source,
        IGuardStorage storage,
        IObservationClock clock)
    {
        _source = source;
        _storage = storage;
        _clock = clock;

        var loadedSettings = storage.LoadSettings();
        _settings = loadedSettings.Status is StorageLoadStatus.Loaded or
            StorageLoadStatus.MissingDefaults
            ? loadedSettings.Settings
            : GuardSettings.Default with
            {
                UnrestrictedDevelopmentOverride = false
            };
        var loadedState = storage.LoadState();
        _state = loadedState.Status is StorageLoadStatus.Loaded or
            StorageLoadStatus.MissingDefaults
            ? loadedState.State
            : GuardPersistentState.Empty;
        _current = loadedSettings.Status is StorageLoadStatus.Loaded or
            StorageLoadStatus.MissingDefaults &&
            loadedState.Status is StorageLoadStatus.Loaded or
            StorageLoadStatus.MissingDefaults
            ? ConfiguredGuardEvaluator.FromStoredState(
                _settings,
                _state,
                clock.UtcNow)
            : ConfiguredGuardEvaluator.UnknownAt(
                _settings,
                clock.UtcNow,
                GuardDecisionReason.ConfigurationInvalid,
                null);
    }

    public event EventHandler<MonitorStateChangedEventArgs>? StateChanged;

    public GuardSettings Settings
    {
        get
        {
            lock (_sync)
            {
                return _settings;
            }
        }
    }

    public GuardPersistentState PersistentState
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public SanitizedUsageState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool IsMonitoring
    {
        get
        {
            lock (_sync)
            {
                return _monitorTask is { IsCompleted: false };
            }
        }
    }

    public void StartMonitoring()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_monitorTask is { IsCompleted: false })
            {
                return;
            }

            _monitorCts?.Dispose();
            _monitorCts = new CancellationTokenSource();
            _monitorTask = MonitorLoopAsync(_monitorCts.Token);
        }
    }

    public async Task StopMonitoringAsync()
    {
        Task? task;
        lock (_sync)
        {
            _monitorCts?.Cancel();
            task = _monitorTask;
        }

        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_sync)
        {
            _monitorTask = null;
            _monitorCts?.Dispose();
            _monitorCts = null;
        }
    }

    public Task<SanitizedUsageState> CheckNowAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_activeCheck is { IsCompleted: false })
            {
                return _activeCheck;
            }

            _activeCheck = CheckCoreAsync(cancellationToken);
            return _activeCheck;
        }
    }

    public void UpdateSettings(
        GuardSettings settings,
        SettingsUpdateAuthority authority = SettingsUpdateAuthority.PreserveLatches)
    {
        ThrowIfDisposed();
        if (GuardSettingsValidator.Validate(settings) !=
            SettingsValidationError.None)
        {
            throw new InvalidDataException("Settings failed validation.");
        }

        _storage.SaveSettings(settings);
        MonitorStateChangedEventArgs changed;
        lock (_sync)
        {
            var previous = _current;
            var stateForEvaluation = authority == SettingsUpdateAuthority.UserApply
                ? ReleaseObsoleteLatchesForUserApply(settings, _state, _current)
                : _state;
            _settings = settings;
            var now = _clock.UtcNow;
            if (_current.Windows is { Count: 2 } windows &&
                _current.Confidence == ObservationConfidence.High &&
                _current.Freshness == ObservationFreshness.ObservedNow)
            {
                var weekly = windows.Single(item =>
                    item.Kind == AppServerQuotaWindowKind.Weekly);
                var evaluation = ConfiguredGuardEvaluator.Evaluate(
                    settings,
                    stateForEvaluation,
                    new AppServerUsageObservation(
                        ObservationStatus.Available,
                        weekly.RemainingPercent,
                        weekly.ResetsAtUtc,
                        _current.ObservedAtUtc,
                        ObservationConfidence.High,
                        ObservationFreshness.ObservedNow,
                        null,
                        windows.Select(item => new AppServerQuotaWindowObservation(
                            item.Kind,
                            item.RemainingPercent,
                            item.ResetsAtUtc)).ToArray()),
                    now);
                _current = evaluation.Display;
                _state = evaluation.PersistentState;
            }
            else
            {
                _current = ConfiguredGuardEvaluator.FromStoredState(
                    settings,
                    stateForEvaluation,
                    now);
                _state = stateForEvaluation with { Current = _current };
            }

            _storage.SaveState(_state);
            changed = new MonitorStateChangedEventArgs(
                previous,
                _current,
                IsMonitoring);
        }

        WakeMonitor();
        StateChanged?.Invoke(this, changed);
    }

    private static GuardPersistentState ReleaseObsoleteLatchesForUserApply(
        GuardSettings settings,
        GuardPersistentState state,
        SanitizedUsageState current)
    {
        if (current.Windows is not { Count: 2 } windows ||
            current.Confidence != ObservationConfidence.High ||
            current.Freshness != ObservationFreshness.ObservedNow ||
            !current.IsSuccessfulLiveObservation)
        {
            return state;
        }

        var weekly = windows.Single(item =>
            item.Kind == AppServerQuotaWindowKind.Weekly);
        var fiveHour = windows.Single(item =>
            item.Kind == AppServerQuotaWindowKind.FiveHour);
        var releaseWeekly = state.LatchedWeeklyResetAtUtc is { } weeklyLatch &&
            WeeklyWindowIdentity.IsSameWindow(weekly.ResetsAtUtc, weeklyLatch) &&
            weekly.RemainingPercent > settings.SafeWrapThresholdPercent;
        var releaseFiveHour = state.LatchedFiveHourResetAtUtc is { } fiveHourLatch &&
            WeeklyWindowIdentity.IsSameWindow(fiveHour.ResetsAtUtc, fiveHourLatch) &&
            fiveHour.RemainingPercent > settings.FiveHourSafeWrapThresholdPercent;

        return state with
        {
            LatchedWeeklyResetAtUtc = releaseWeekly
                ? null
                : state.LatchedWeeklyResetAtUtc,
            LatchCreatedAtUtc = releaseWeekly ? null : state.LatchCreatedAtUtc,
            LatchedFiveHourResetAtUtc = releaseFiveHour
                ? null
                : state.LatchedFiveHourResetAtUtc,
            FiveHourLatchCreatedAtUtc = releaseFiveHour
                ? null
                : state.FiveHourLatchCreatedAtUtc
        };
    }

    public void MarkNotification(string key, DateTimeOffset shownAtUtc)
    {
        lock (_sync)
        {
            var kindPrefix = key.Split(':', 2)[0] + ":";
            var ledger = (_state.NotificationLedger ??
                new Dictionary<string, DateTimeOffset>())
                .Where(item => !item.Key.StartsWith(
                    kindPrefix,
                    StringComparison.Ordinal))
                .ToDictionary(item => item.Key, item => item.Value);
            ledger[key] = shownAtUtc;
            _state = _state with
            {
                LastNotificationKey = key,
                LastNotificationAtUtc = shownAtUtc,
                NotificationLedger = ledger
            };
            _storage.SaveState(_state);
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await CheckNowAsync(cancellationToken).ConfigureAwait(false);
            var delay = GetCurrentDelay();
            try
            {
                _ = await _wakeSignal.WaitAsync(delay, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<SanitizedUsageState> CheckCoreAsync(
        CancellationToken cancellationToken)
    {
        var loadedSettings = _storage.LoadSettings();
        if (loadedSettings.Status is not (
                StorageLoadStatus.Loaded or StorageLoadStatus.MissingDefaults))
        {
            return ApplyConfigurationFailure();
        }

        lock (_sync)
        {
            _settings = loadedSettings.Settings;
        }

        AppServerUsageObservation observation;
        try
        {
            observation = await _source.ObserveAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Current;
        }
        catch
        {
            observation = AppServerUsageObservation.ErrorAt(
                _clock.UtcNow,
                AppServerUsageError.ProtocolError);
        }

        if (observation.Error == AppServerUsageError.Cancelled &&
            cancellationToken.IsCancellationRequested)
        {
            return Current;
        }

        MonitorStateChangedEventArgs changed;
        SanitizedUsageState current;
        lock (_sync)
        {
            var previous = _current;
            var evaluation = ConfiguredGuardEvaluator.Evaluate(
                _settings,
                _state,
                observation,
                _clock.UtcNow);
            _state = evaluation.PersistentState;
            _current = evaluation.Display;
            _storage.SaveState(_state);
            current = _current;
            changed = new MonitorStateChangedEventArgs(
                previous,
                current,
                IsMonitoring);
        }

        StateChanged?.Invoke(this, changed);
        return current;
    }

    private SanitizedUsageState ApplyConfigurationFailure()
    {
        MonitorStateChangedEventArgs changed;
        SanitizedUsageState current;
        lock (_sync)
        {
            var previous = _current;
            _settings = GuardSettings.Default;
            _current = ConfiguredGuardEvaluator.UnknownAt(
                _settings,
                _clock.UtcNow,
                GuardDecisionReason.ConfigurationInvalid,
                null);
            _state = _state with
            {
                Current = _current,
                ConsecutiveFailures = checked(Math.Min(
                    _state.ConsecutiveFailures + 1,
                    30))
            };
            _storage.SaveState(_state);
            current = _current;
            changed = new MonitorStateChangedEventArgs(
                previous,
                current,
                IsMonitoring);
        }

        StateChanged?.Invoke(this, changed);
        return current;
    }

    private TimeSpan GetCurrentDelay()
    {
        lock (_sync)
        {
            var exponent = Math.Min(_state.ConsecutiveFailures, 3);
            var seconds = Math.Min(
                _settings.PollingIntervalSeconds * (1 << exponent),
                GuardSettings.MaximumPollingIntervalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    private void WakeMonitor()
    {
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopMonitoringAsync();
        _wakeSignal.Dispose();
    }
}
