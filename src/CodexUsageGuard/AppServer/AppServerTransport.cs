using CodexUsageGuard.Core;

namespace CodexUsageGuard.AppServer;

public interface IAppServerTransport : IAsyncDisposable
{
    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);

    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);

    ValueTask CompleteInputAsync(CancellationToken cancellationToken);

    ValueTask WaitForExitAsync(CancellationToken cancellationToken);

    bool HasExited { get; }

    void TerminateOwnedProcess();
}

public interface IAppServerTransportFactory
{
    ValueTask<IAppServerTransport> StartAsync(
        CancellationToken cancellationToken);
}

public sealed class AppServerLaunchException(
    AppServerUsageError error) : Exception
{
    public AppServerUsageError Error { get; } = error;
}
