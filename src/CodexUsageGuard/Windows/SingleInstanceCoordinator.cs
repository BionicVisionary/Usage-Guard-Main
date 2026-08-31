namespace CodexUsageGuard.Windows;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly EventWaitHandle _shutdownEvent;
    private readonly RegisteredWaitHandle? _registeredWait;
    private readonly RegisteredWaitHandle? _registeredShutdownWait;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceCoordinator(
        string instanceSuffix,
        Action showRequested,
        Action? shutdownRequested = null,
        bool registerCallbacks = true)
    {
        var identity = Sanitize(instanceSuffix);
        _mutex = new Mutex(
            initiallyOwned: true,
            $"Local\\OpenAI.CodexUsageGuard.{identity}.Mutex",
            out _ownsMutex);
        _showEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\OpenAI.CodexUsageGuard.{identity}.Show");
        _shutdownEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\OpenAI.CodexUsageGuard.{identity}.Shutdown");
        if (_ownsMutex && registerCallbacks)
        {
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (_, timedOut) =>
                {
                    if (!timedOut && !_disposed)
                    {
                        showRequested();
                    }
                },
                null,
                Timeout.InfiniteTimeSpan,
                executeOnlyOnce: false);
            _registeredShutdownWait = ThreadPool.RegisterWaitForSingleObject(
                _shutdownEvent,
                (_, timedOut) =>
                {
                    if (!timedOut && !_disposed)
                    {
                        shutdownRequested?.Invoke();
                    }
                },
                null,
                Timeout.InfiniteTimeSpan,
                executeOnlyOnce: false);
        }
    }

    public bool IsPrimary => _ownsMutex;

    public void SignalPrimary() => _showEvent.Set();

    public void SignalShutdown() => _shutdownEvent.Set();

    public bool TryConsumeShowSignal() =>
        _ownsMutex && _showEvent.WaitOne(TimeSpan.Zero);

    public bool TryConsumeShutdownSignal() =>
        _ownsMutex && _shutdownEvent.WaitOne(TimeSpan.Zero);

    public bool WaitForPrimaryExit(TimeSpan timeout)
    {
        if (_ownsMutex)
        {
            return true;
        }
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        try
        {
            if (!_mutex.WaitOne(timeout))
            {
                return false;
            }
            _mutex.ReleaseMutex();
            return true;
        }
        catch (AbandonedMutexException)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            return true;
        }
    }

    private static string Sanitize(string value) => string.Concat(
        value.Select(character => char.IsLetterOrDigit(character)
            ? character
            : '_'));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait?.Unregister(null);
        _registeredShutdownWait?.Unregister(null);
        _showEvent.Dispose();
        _shutdownEvent.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
