using System.IO;
using System.Diagnostics;
using CodexUsageGuard.AppServer;
using CodexUsageGuard.Core;
using CodexUsageGuard.Monitoring;
using CodexUsageGuard.Providers;
using CodexUsageGuard.Windows;

namespace CodexUsageGuard;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return RunDesktop(startHidden: false);
        }

        if (args.Length == 1 &&
            args[0].Equals("--background", StringComparison.Ordinal))
        {
            return RunDesktop(startHidden: true);
        }

        if (args.Length == 2 &&
            args[0].Equals("--layout-qa-display", StringComparison.Ordinal) &&
            Screen.AllScreens.Any(screen =>
                !screen.Primary && screen.DeviceName.Equals(
                    args[1], StringComparison.Ordinal)))
        {
            return RunDesktop(startHidden: false, args[1], layoutQaMode: true);
        }

        if (args.Length == 2 &&
            args[0].Equals("--layout-qa-single-display", StringComparison.Ordinal) &&
            Screen.AllScreens.Length == 1 &&
            Screen.AllScreens[0].Primary &&
            Screen.AllScreens[0].DeviceName.Equals(args[1], StringComparison.Ordinal))
        {
            return RunDesktop(startHidden: false, args[1], layoutQaMode: true);
        }

        if (args.Length == 1 &&
            args[0].Equals("--sandbox-layout-qa", StringComparison.Ordinal) &&
            Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.Ordinal) &&
            Environment.GetEnvironmentVariable("USAGE_GUARD_SANDBOX_QA_SESSION")
                ?.Equals("1", StringComparison.Ordinal) == true)
        {
            return RunDesktop(startHidden: false, layoutQaMode: true);
        }

        if (args.Length == 1 &&
            args[0].Equals("--shutdown", StringComparison.Ordinal))
        {
            return RequestDesktopShutdown();
        }

        if (args.Length == 1 &&
            args[0].Equals("--app-server-usage", StringComparison.Ordinal))
        {
            return RunAppServerObservation();
        }

        if (args.Length == 1 &&
            args[0].Equals("--guard-check", StringComparison.Ordinal))
        {
            return RunLiveGuardCheck();
        }

        if (args.Length == 1 &&
            args[0].Equals("--provider-status", StringComparison.Ordinal))
        {
            return RunProviderStatus();
        }

        if (args.Length == 1 &&
            args[0].Equals("--claude-statusline-ingest", StringComparison.Ordinal))
        {
            return RunClaudeStatusLineIngest();
        }

        if (args.Length == 2 &&
            args[0].Equals("--provider-guard-check", StringComparison.Ordinal) &&
            args[1].Equals("claude", StringComparison.Ordinal))
        {
            return RunClaudeGuardCheck();
        }

        if (args.Length == 2 &&
            args[0].Equals("--launch-provider", StringComparison.Ordinal))
        {
            return DesktopAiLaunchContract.Launch(
                args[1],
                Environment.ProcessPath ?? string.Empty);
        }

        var invalid = UsageObservation.ErrorAt(
            DateTimeOffset.UtcNow,
            ObservationError.TestChildFailed);
        Console.Out.WriteLine(invalid.ToSanitizedJson());
        return 1;
    }

    private static int RunDesktop(
        bool startHidden,
        string? initialScreenDeviceName = null,
        bool layoutQaMode = false)
    {
        ApplicationConfiguration.Initialize();
        MainForm? form = null;
        var requestRouter = new DesktopRequestRouter();
        using var coordinator = new SingleInstanceCoordinator(
            Environment.UserName,
            () => { },
            registerCallbacks: false);
        if (!coordinator.IsPrimary)
        {
            if (!startHidden)
            {
                coordinator.SignalPrimary();
            }

            return 0;
        }

        var monitor = CreateMonitor();
        using var desktopRequestTimer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };
        desktopRequestTimer.Tick += (_, _) =>
        {
            if (coordinator.TryConsumeShutdownSignal())
            {
                Application.Exit();
            }
            else if (coordinator.TryConsumeShowSignal())
            {
                requestRouter.RequestShow();
            }
        };
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return 1;
            }

            using (form = new MainForm(
                monitor,
                new StartupRegistration(
                    new WindowsRunStartupValueStore(),
                    executablePath),
                startHidden,
                initialScreenDeviceName: initialScreenDeviceName,
                layoutQaMode: layoutQaMode))
            {
                requestRouter.Attach(form.ShowPopup, form.RequestExit);
                try
                {
                    desktopRequestTimer.Start();
                    Application.Run(form);
                }
                finally
                {
                    desktopRequestTimer.Stop();
                    requestRouter.Detach();
                }
            }

            return 0;
        }
        finally
        {
            monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static int RequestDesktopShutdown()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            using var coordinator = new SingleInstanceCoordinator(
                Environment.UserName,
                () => { });
            if (!coordinator.IsPrimary)
            {
                coordinator.SignalShutdown();
                return coordinator.WaitForPrimaryExit(TimeSpan.FromSeconds(15))
                    ? 0
                    : 2;
            }

            if (!HasOtherExactHelperProcess())
            {
                return 0;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return 2;
            }

            Thread.Sleep(100);
        }
    }

    private static bool HasOtherExactHelperProcess()
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        currentPath = Path.GetFullPath(currentPath);
        var processName = Path.GetFileNameWithoutExtension(currentPath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId || process.HasExited)
                {
                    continue;
                }

                try
                {
                    var candidate = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(candidate) &&
                        Path.GetFullPath(candidate).Equals(
                            currentPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is
                    InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        return false;
    }

    private static int RunAppServerObservation()
    {
        var client = new AppServerUsageClient(
            new ProcessAppServerTransportFactory(),
            new SystemObservationClock());
        var observation = client.ObserveAsync().GetAwaiter().GetResult();
        Console.Out.WriteLine(observation.ToSanitizedJson());
        return ExitCode(observation.Status);
    }

    private static int RunLiveGuardCheck()
    {
        var clock = new SystemObservationClock();
        var storage = new GuardFileStorage(GuardDataPaths.RootDirectory);
        var loadedSettings = storage.LoadSettings();
        var loadedState = storage.LoadState();
        var settingsValid = loadedSettings.Status is StorageLoadStatus.Loaded or
            StorageLoadStatus.MissingDefaults;
        var stateValid = loadedState.Status is StorageLoadStatus.Loaded or
            StorageLoadStatus.MissingDefaults;
        SanitizedUsageState result;

        if (!settingsValid || !stateValid)
        {
            result = ConfiguredGuardEvaluator.UnknownAt(
                GuardSettings.Default with
                {
                    UnrestrictedDevelopmentOverride = false
                },
                clock.UtcNow,
                GuardDecisionReason.ConfigurationInvalid,
                null);
        }
        else if (loadedSettings.Settings.UnrestrictedDevelopmentOverride)
        {
            result = ConfiguredGuardEvaluator.FromStoredState(
                loadedSettings.Settings,
                loadedState.State,
                clock.UtcNow);
        }
        else
        {
            var client = new AppServerUsageClient(
                new ProcessAppServerTransportFactory(),
                clock);
            var observation = client.ObserveAsync().GetAwaiter().GetResult();
            var evaluation = ConfiguredGuardEvaluator.Evaluate(
                loadedSettings.Settings,
                loadedState.State,
                observation,
                clock.UtcNow);
            storage.SaveState(evaluation.PersistentState);
            result = evaluation.Display;
        }

        Console.Out.WriteLine(SanitizedJson.Serialize(result));
        return result.Decision switch
        {
            GuardRuntimeState.Normal => 0,
            GuardRuntimeState.Warning => 0,
            GuardRuntimeState.OverrideActive => 0,
            GuardRuntimeState.SafeWrap => 3,
            _ => 2
        };
    }

    private static UsageMonitor CreateMonitor()
    {
        var clock = new SystemObservationClock();
        var client = new AppServerUsageClient(
            new ProcessAppServerTransportFactory(),
            clock);
        return new UsageMonitor(
            new AppServerObservationSource(client),
            new GuardFileStorage(GuardDataPaths.RootDirectory),
            clock);
    }

    private static int RunProviderStatus()
    {
        var providers = new WindowsAiProviderDiscovery().Detect();
        Console.Out.WriteLine(SanitizedJson.Serialize(providers));
        return 0;
    }

    private static int RunClaudeStatusLineIngest()
    {
        var now = DateTimeOffset.UtcNow;
        var input = ReadBoundedStandardInput(
            ClaudeStatusLineParser.MaximumInputBytes);
        var snapshot = input is null
            ? ClaudeUsageSnapshot.UnavailableAt(now, "input_invalid")
            : ClaudeStatusLineParser.Parse(input, now);
        try
        {
            var storage = new ClaudeUsageStorage(GuardDataPaths.RootDirectory);
            // Load, reconcile and save under one bounded cross-process writer
            // lease because several Claude Code sessions can report at once.
            snapshot = storage.ReconcileAndSave(snapshot, now);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            snapshot = ClaudeUsageSnapshot.UnavailableAt(now, "storage_unavailable");
        }

        if (snapshot.Available)
        {
            var five = snapshot.Windows.Single(item =>
                item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent;
            var weekly = snapshot.Windows.Single(item =>
                item.Kind == QuotaWindowKind.Weekly).RemainingPercent;
            Console.Out.WriteLine(
                $"Usage Guard: Claude 5h {five:0.#}% | weekly {weekly:0.#}% remaining");
        }
        else
        {
            Console.Out.WriteLine("Usage Guard: Claude usage unavailable");
        }
        return 0;
    }

    private static int RunClaudeGuardCheck()
    {
        var now = DateTimeOffset.UtcNow;
        var catalog = new ProviderCatalogStorage(GuardDataPaths.RootDirectory).Load();
        var configuration = catalog.Status is
                ProviderCatalogLoadStatus.Loaded or
                ProviderCatalogLoadStatus.MissingDefaults
            ? catalog.Settings.Providers.SingleOrDefault(item =>
                item.ProviderId == AiProviderId.ClaudeCode)
            : null;
        var snapshot = new ClaudeUsageStorage(GuardDataPaths.RootDirectory).Load(now);
        var output = ClaudeGuardCheckOutput.Evaluate(configuration, snapshot, now);
        Console.Out.WriteLine(SanitizedJson.Serialize(output));
        return output.Decision switch
        {
            "normal" or "warning" or "override_active" => 0,
            "safe_wrap" => 3,
            _ => 2
        };
    }

    private static byte[]? ReadBoundedStandardInput(int maximumBytes)
    {
        using var input = Console.OpenStandardInput();
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return memory.ToArray();
            }
            if (memory.Length + read > maximumBytes)
            {
                return null;
            }
            memory.Write(buffer, 0, read);
        }
    }

    private static int ExitCode(ObservationStatus status) => status switch
    {
        ObservationStatus.Available => 0,
        ObservationStatus.Unavailable => 2,
        _ => 1
    };
}
