using System.Diagnostics;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.AppServer;

public sealed class AppServerUsageClient(
    IAppServerTransportFactory transportFactory,
    IObservationClock clock,
    TimeSpan? startupTimeout = null,
    TimeSpan? readTimeout = null,
    TimeSpan? shutdownTimeout = null)
{
    private const int MaximumMessagesPerResponse = 32;
    private readonly TimeSpan _startupTimeout =
        startupTimeout ?? TimeSpan.FromSeconds(8);
    private readonly TimeSpan _readTimeout =
        readTimeout ?? TimeSpan.FromSeconds(8);
    private readonly TimeSpan _shutdownTimeout =
        shutdownTimeout ?? TimeSpan.FromSeconds(3);

    public async Task<AppServerUsageObservation> ObserveAsync(
        CancellationToken cancellationToken = default)
    {
        IAppServerTransport? transport = null;
        AppServerUsageObservation observation;
        var startedAt = Stopwatch.StartNew();

        try
        {
            using var launchCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            launchCts.CancelAfter(_startupTimeout);
            transport = await transportFactory.StartAsync(launchCts.Token);
            if (startedAt.Elapsed > _startupTimeout)
            {
                observation = Error(AppServerUsageError.StartupTimedOut);
            }
            else
            {
                observation = await ExchangeAsync(transport, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            observation = Error(
                cancellationToken.IsCancellationRequested
                    ? AppServerUsageError.Cancelled
                    : AppServerUsageError.StartupTimedOut);
        }
        catch (AppServerLaunchException exception)
        {
            observation = Error(exception.Error);
        }
        catch
        {
            observation = Error(AppServerUsageError.ProtocolError);
        }

        if (transport is not null)
        {
            var shutdownSucceeded = await TryShutdownAsync(transport);
            if (!shutdownSucceeded)
            {
                observation = Error(AppServerUsageError.ShutdownTimedOut);
            }

            try
            {
                await transport.DisposeAsync();
            }
            catch
            {
                observation = Error(AppServerUsageError.ShutdownTimedOut);
            }
        }

        return observation;
    }

    private async Task<AppServerUsageObservation> ExchangeAsync(
        IAppServerTransport transport,
        CancellationToken cancellationToken)
    {
        try
        {
            using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            startupCts.CancelAfter(_startupTimeout);
            await transport.WriteLineAsync(
                AppServerProtocol.InitializeRequest,
                startupCts.Token);
            var initializeResponse = await ReadExpectedResponseAsync(
                transport,
                AppServerProtocol.InitializeRequestId,
                startupCts.Token);
            if (initializeResponse.Kind ==
                ProtocolResponseKind.AuthenticationRefreshRequested)
            {
                return Error(AppServerUsageError.AuthenticationRefreshRequested);
            }

            if (initializeResponse.Kind !=
                    ProtocolResponseKind.ExpectedResponse ||
                !AppServerProtocol.InitializeAccepted(initializeResponse.Json!))
            {
                return Error(AppServerUsageError.InitializeRejected);
            }

            await transport.WriteLineAsync(
                AppServerProtocol.InitializedNotification,
                startupCts.Token);
        }
        catch (OperationCanceledException)
        {
            return Error(
                cancellationToken.IsCancellationRequested
                    ? AppServerUsageError.Cancelled
                    : AppServerUsageError.StartupTimedOut);
        }

        try
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            readCts.CancelAfter(_readTimeout);
            await transport.WriteLineAsync(
                AppServerProtocol.RateLimitsRequest,
                readCts.Token);
            var rateLimitResponse = await ReadExpectedResponseAsync(
                transport,
                AppServerProtocol.RateLimitsRequestId,
                readCts.Token);
            if (rateLimitResponse.Kind ==
                ProtocolResponseKind.AuthenticationRefreshRequested)
            {
                return Error(AppServerUsageError.AuthenticationRefreshRequested);
            }

            if (rateLimitResponse.Kind !=
                ProtocolResponseKind.ExpectedResponse)
            {
                return Error(AppServerUsageError.ProtocolError);
            }

            return AppServerRateLimitParser.Parse(
                rateLimitResponse.Json!,
                clock.UtcNow);
        }
        catch (OperationCanceledException)
        {
            return Error(
                cancellationToken.IsCancellationRequested
                    ? AppServerUsageError.Cancelled
                    : AppServerUsageError.ReadTimedOut);
        }
    }

    private static async Task<ProtocolLine> ReadExpectedResponseAsync(
        IAppServerTransport transport,
        long expectedId,
        CancellationToken cancellationToken)
    {
        for (var count = 0; count < MaximumMessagesPerResponse; count++)
        {
            var line = await transport.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                return new ProtocolLine(ProtocolResponseKind.Invalid, null);
            }

            var kind = AppServerProtocol.ClassifyResponse(line, expectedId);
            if (kind != ProtocolResponseKind.Notification)
            {
                return new ProtocolLine(kind, line);
            }
        }

        return new ProtocolLine(ProtocolResponseKind.Invalid, null);
    }

    private async Task<bool> TryShutdownAsync(IAppServerTransport transport)
    {
        try
        {
            using var shutdownCts = new CancellationTokenSource(_shutdownTimeout);
            await transport.CompleteInputAsync(shutdownCts.Token);
            await transport.WaitForExitAsync(shutdownCts.Token);
            return true;
        }
        catch
        {
            transport.TerminateOwnedProcess();
            return false;
        }
    }

    private AppServerUsageObservation Error(AppServerUsageError error) =>
        AppServerUsageObservation.ErrorAt(clock.UtcNow, error);

    private sealed record ProtocolLine(
        ProtocolResponseKind Kind,
        string? Json);
}
