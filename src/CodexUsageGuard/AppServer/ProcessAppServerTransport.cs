using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.AppServer;

public sealed class ProcessAppServerTransportFactory : IAppServerTransportFactory
{
    public ValueTask<IAppServerTransport> StartAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var approval = ApprovedCodexCli.Validate();
        if (approval.Error is { } error)
        {
            throw new AppServerLaunchException(error);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = approval.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        // Do not pass API-key credentials into this managed-ChatGPT-auth probe.
        startInfo.Environment.Remove("OPENAI_API_KEY");
        startInfo.Environment.Remove("CODEX_API_KEY");

        try
        {
            var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new AppServerLaunchException(
                    AppServerUsageError.LaunchFailed);
            }

            var suppressedStandardError = process.StandardError.BaseStream.CopyToAsync(
                Stream.Null,
                CancellationToken.None);
            return ValueTask.FromResult<IAppServerTransport>(
                new ProcessAppServerTransport(process, suppressedStandardError));
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            throw new AppServerLaunchException(
                AppServerUsageError.ExecutableNotFound);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            throw new AppServerLaunchException(
                AppServerUsageError.ExecutableInaccessible);
        }
        catch (AppServerLaunchException)
        {
            throw;
        }
        catch
        {
            throw new AppServerLaunchException(AppServerUsageError.LaunchFailed);
        }
    }
}

internal sealed class ProcessAppServerTransport(
    Process process,
    Task suppressedStandardError) : IAppServerTransport
{
    private const int MaximumJsonLineCharacters = 1_048_576;

    public bool HasExited
    {
        get
        {
            try
            {
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public async ValueTask WriteLineAsync(
        string line,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(
            line.AsMemory(),
            cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    public async ValueTask<string?> ReadLineAsync(
        CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            var count = await process.StandardOutput.ReadAsync(
                character.AsMemory(),
                cancellationToken);
            if (count == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (character[0] == '\n')
            {
                if (line.Length > 0 && line[^1] == '\r')
                {
                    line.Length--;
                }

                return line.ToString();
            }

            if (line.Length >= MaximumJsonLineCharacters)
            {
                throw new InvalidDataException("App Server JSONL line too large.");
            }

            line.Append(character[0]);
        }
    }

    public ValueTask CompleteInputAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        process.StandardInput.Close();
        return ValueTask.CompletedTask;
    }

    public async ValueTask WaitForExitAsync(
        CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken);
        await suppressedStandardError.WaitAsync(cancellationToken);
    }

    public void TerminateOwnedProcess()
    {
        if (!HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (process.HasExited)
        {
            try
            {
                await suppressedStandardError.ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
        process.Dispose();
    }
}
