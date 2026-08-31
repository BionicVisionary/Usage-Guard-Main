namespace CodexUsageGuard.Windows;

public sealed class DesktopRequestRouter
{
    private readonly object _gate = new();
    private Action? _show;
    private Action? _shutdown;
    private bool _showPending;
    private bool _shutdownPending;

    public void RequestShow() => Request(isShutdown: false);

    public void RequestShutdown() => Request(isShutdown: true);

    public void Attach(Action show, Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(shutdown);

        bool showPending;
        bool shutdownPending;
        lock (_gate)
        {
            if (_show is not null || _shutdown is not null)
            {
                throw new InvalidOperationException("Desktop request handlers are already attached.");
            }

            _show = show;
            _shutdown = shutdown;
            showPending = _showPending;
            shutdownPending = _shutdownPending;
            _showPending = false;
            _shutdownPending = false;
        }

        if (shutdownPending)
        {
            shutdown();
        }
        else if (showPending)
        {
            show();
        }
    }

    public void Detach()
    {
        lock (_gate)
        {
            _show = null;
            _shutdown = null;
            _showPending = false;
            _shutdownPending = false;
        }
    }

    private void Request(bool isShutdown)
    {
        Action? callback;
        lock (_gate)
        {
            callback = isShutdown ? _shutdown : _show;
            if (callback is null)
            {
                if (isShutdown)
                {
                    _shutdownPending = true;
                }
                else
                {
                    _showPending = true;
                }
                return;
            }
        }

        callback();
    }
}
