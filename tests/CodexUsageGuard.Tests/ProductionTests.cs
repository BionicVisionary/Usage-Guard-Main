using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using CodexUsageGuard.AppServer;
using CodexUsageGuard.Core;
using CodexUsageGuard.Monitoring;
using CodexUsageGuard.Providers;
using CodexUsageGuard.Windows;

namespace CodexUsageGuard.Tests;

internal static class ProductionTests
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("provider catalog defaults are valid", ProviderCatalogDefaultsAreValid);
        yield return ("Claude policy requires five-hour and weekly windows", ClaudeRequiresBothWindows);
        yield return ("Claude status line parses only both trusted windows", ClaudeStatusLineParsesBothWindows);
        yield return ("Claude status-line boundary accepts extra windows and rejects ambiguous required data", ClaudeStatusLineBoundaryIsForwardCompatibleAndFailClosed);
        yield return ("Claude status line fails closed on malformed missing duplicate or stale windows", ClaudeStatusLineFailsClosed);
        yield return ("Claude sanitized state round trips and rejects corruption", ClaudeStateRoundTripsAndRejectsCorruption);
        yield return ("Claude state recovers from an interrupted write instead of stranding Unknown", ClaudeStateRecoversFromInterruptedWrite);
        yield return ("concurrent Claude observations serialize and keep the conservative value", ConcurrentClaudeObservationsAreSerialized);
        yield return ("Claude bridge allows the helper cold start without killing it", ClaudeBridgeAllowsHelperColdStart);
        yield return ("Claude PowerShell timeout cleanup leaves no owned orphan", ClaudePowerShellTimeoutCleanupLeavesNoOrphan);
        yield return ("Claude Configure upgrades only known prior assets and refuses unknown ones", ClaudeConfigureUpgradesOwnedAssetsAndRefusesUnknown);
        yield return ("Claude idle session cannot raise remaining usage back up", ClaudeIdleSessionCannotRaiseRemainingUsage);
        yield return ("Claude Configure leaves user settings unread and uses isolated session settings", ClaudeConfigureUsesIsolatedSessionSettings);
        yield return ("Claude configured decision uses the strictest live window", ClaudeDecisionUsesStrictestWindow);
        yield return ("Claude notifications dedupe transitions and reset jitter across restart", ClaudeNotificationsDedupeAcrossRestart);
        yield return ("Claude monitoring off suppresses provider notifications", ClaudeMonitoringOffSuppressesNotifications);
        yield return ("multi-window provider uses strictest independent window", MultiWindowUsesStrictestWindow);
        yield return ("multi-window provider fails closed on missing window", MultiWindowFailsClosedOnMissingWindow);
        yield return ("provider policies remain isolated", ProviderPoliciesRemainIsolated);
        yield return ("provider catalog round trips atomically", ProviderCatalogRoundTripsAtomically);
        yield return ("legacy Codex provider settings gain five-hour defaults without losing choices", LegacyCodexProviderSettingsGainFiveHourDefaults);
        yield return ("update channel fails safely when unconfigured", UpdateChannelIsExplicitlyUnconfigured);
        yield return ("GitHub update channel detects a newer approved release", GitHubUpdateChannelDetectsNewerRelease);
        yield return ("GitHub update channel rejects a foreign release URL", GitHubUpdateChannelRejectsForeignUrl);
        yield return ("GitHub update channel rejects the retired repository", GitHubUpdateChannelRejectsRetiredRepository);
        yield return ("GitHub update channel requires immutable digested assets", GitHubUpdateChannelRequiresImmutableDigests);
        yield return ("update notification is deduplicated by version", UpdateNotificationIsDeduplicated);
        yield return ("in-app updater verifies installer SHA-256", InAppUpdaterVerifiesInstallerHash);
        yield return ("in-app updater requires immutable release verification", InAppUpdaterRequiresImmutableRelease);
        yield return ("in-app updater rejects a mismatched installer", InAppUpdaterRejectsMismatchedHash);
        yield return ("settings defaults are valid", SettingsDefaultsAreValid);
        yield return ("settings reject reversed thresholds", SettingsRejectReversedThresholds);
        yield return ("settings reject reversed five-hour thresholds", SettingsRejectReversedFiveHourThresholds);
        yield return ("settings reject threshold range", SettingsRejectThresholdRange);
        yield return ("settings reject polling below range", SettingsRejectPollingBelowRange);
        yield return ("settings reject polling above range", SettingsRejectPollingAboveRange);
        yield return ("restore defaults preserves override", RestoreDefaultsPreservesOverride);
        yield return ("custom threshold values classify boundaries", CustomThresholdBoundaries);
        yield return ("Codex strictest five-hour or weekly window controls", CodexStrictestWindowControls);
        yield return ("zero and one hundred classify safely", ZeroAndOneHundredClassifySafely);
        yield return ("override exposes underlying live state", OverrideExposesUnderlyingState);
        yield return ("override removal restores latch enforcement", OverrideRemovalRestoresLatch);
        yield return ("genuine live safe-wrap creates reset latch", GenuineLiveCreatesLatch);
        yield return ("five-hour SafeWrap resume uses the exact live reset", FiveHourResumeUsesExactLiveReset);
        yield return ("weekly SafeWrap resume uses the exact live reset", WeeklyResumeUsesExactLiveReset);
        yield return ("both constraining windows resume after the later exact reset", BothConstraintsResumeAfterLatestExactReset);
        yield return ("reset jitter keeps one resume identity but preserves exact live timestamps", ResumeIdentitySurvivesProviderJitter);
        yield return ("stale ambiguous or latch-only data cannot schedule resume", UntrustedResetDataCannotScheduleResume);
        yield return ("unknown retains genuine latch", UnknownRetainsGenuineLatch);
        yield return ("fresh new weekly window rearms latch", FreshNewWindowRearmsLatch);
        yield return ("weekly reset timestamp jitter keeps one window", WeeklyResetTimestampJitterKeepsOneWindow);
        yield return ("backward reset identity fails closed", BackwardResetIdentityFailsClosed);
        yield return ("invalid required window cannot publish a reset transition", InvalidRequiredWindowCannotPublishReset);
        yield return ("reset notification is once per stable window across restart", ResetNotificationIsOncePerStableWindowAcrossRestart);
        yield return ("stale new window cannot clear latch", StaleNewWindowCannotClearLatch);
        yield return ("clock rollback cannot clear latch", ClockRollbackCannotClearLatch);
        yield return ("provenance mismatch is explicit unknown", ProvenanceMismatchIsExplicit);
        yield return ("low confidence input is unknown", LowConfidenceIsUnknown);
        yield return ("settings round trip atomically", SettingsRoundTripAtomically);
        yield return ("override persists through storage restart", OverridePersistsThroughStorageRestart);
        yield return ("latch persists through storage restart", LatchPersistsThroughStorageRestart);
        yield return ("concurrent state writers merge notification dedupe metadata", ConcurrentStateWritersMergeNotificationMetadata);
        yield return ("corrupt settings fail closed", CorruptSettingsFailClosed);
        yield return ("partial state fails closed", PartialStateFailsClosed);
        yield return ("future settings version fails closed", FutureSettingsFailClosed);
        yield return ("future state version fails closed", FutureStateFailsClosed);
        yield return ("unknown fields fail closed", UnknownFieldsFailClosed);
        yield return ("notification transition is deduplicated", NotificationIsDeduplicated);
        yield return ("notification cooldown permits later reminder", NotificationCooldownPermitsLaterReminder);
        yield return ("reset notification is distinct", ResetNotificationIsDistinct);
        yield return ("identical transition notification is deduplicated after restart", IdenticalTransitionIsDeduplicatedAfterRestart);
        yield return ("startup registration is exact and reversible", StartupRegistrationIsExactAndReversible);
        yield return ("startup detects foreign command as disabled", StartupForeignCommandIsDisabled);
        yield return ("Windows startup value round trip preserves prior state", WindowsStartupRoundTripPreservesPriorState);
        yield return ("Launch Together shortcuts are fixed user scoped and reversible", LaunchTogetherShortcutsAreFixedAndReversible);
        yield return ("Launch Together preserves foreign shortcuts and rolls back partial creation", LaunchTogetherPreservesForeignShortcut);
        yield return ("Launch Together provider arguments reject arbitrary targets", LaunchTogetherArgumentsAreFixed);
        yield return ("Windows Launch Together shortcut round trips without activation", WindowsLaunchTogetherShortcutRoundTrips);
        yield return ("shareable installer is console free and user scoped", ShareableInstallerIsConsoleFreeAndUserScoped);
        yield return ("single instance signals primary", SingleInstanceSignalsPrimary);
        yield return ("single instance signals graceful shutdown", SingleInstanceSignalsShutdown);
        yield return ("early desktop requests are delivered after the form attaches", EarlyDesktopRequestsAreDeferred);
        yield return ("desktop requests marshal through a stable UI control", DesktopRequestsUseStableUiDispatcher);
        yield return ("shutdown requester waits for exact primary exit", ShutdownRequesterWaitsForPrimaryExit);
        yield return ("monitor coalesces overlapping checks", MonitorCoalescesChecks);
        yield return ("monitor cancellation stops cleanly", MonitorCancellationStopsCleanly);
        yield return ("monitor recovers failure counter after success", MonitorFailureCounterRecovers);
        yield return ("monitor reloads externally changed validated settings", MonitorReloadsExternalSettings);
        yield return ("app server caller cancellation cleans owned transport", AppServerCancellationCleansTransport);
        yield return ("configured output contains no raw protocol", ConfiguredOutputIsSanitized);
        yield return ("production rejects simulation arguments", ProductionRejectsSimulationArguments);
        yield return ("production rejects retired desktop diagnostic arguments", ProductionRejectsDesktopDiagnosticArguments);
        yield return ("Sandbox window policy never selects unrelated windows and fails on display drift", SandboxWindowPolicyIsFailClosed);
        yield return ("Sandbox template disables host integrations and separates mappings", SandboxTemplateIsLockedDown);
        yield return ("Sandbox launcher detects early guest failure and waits for owned exit", SandboxLauncherHandlesFailureAndExit);
        yield return ("installer refuses non-owned destination content", InstallerRefusesNonOwnedDestination);
        yield return ("App Server diagnostics are drained and suppressed", AppServerDiagnosticsAreSuppressed);
        yield return ("Claude status bridge forwards only quota fields", ClaudeStatusBridgeFiltersAtBoundary);
        yield return ("Windows PowerShell entrypoints use compatible path validation", PowerShellEntrypointsAreWindows51Compatible);
        yield return ("provider status output is sanitized", ProviderStatusOutputIsSanitized);
        yield return ("WinExe wrapper waits captures output and returns exit code", WinExeWrapperCapturesOutputAndExitCode);
        yield return ("wrapper accepts one strict sanitized decision", WrapperAcceptsStrictSanitizedDecision);
        yield return ("wrapper rejects multiple decision objects", WrapperRejectsMultipleDecisionObjects);
        yield return ("wrapper rejects null and Boolean quota values", WrapperRejectsNonNumericQuotaValues);
        yield return ("Claude WinExe wrapper validates one sanitized provider decision", ClaudeWrapperValidatesStrictOutput);
        yield return ("light and dark palettes remain readable", ThemePalettesAreReadable);
        yield return ("popup has accessible keyboard-first contract", PopupAccessibilityContract);
        yield return ("applying settings preserves one stable UI tree", ApplyingSettingsKeepsStableUiTree);
        yield return ("popup creates one tab per configured detected provider", PopupCreatesProviderTabs);
        yield return ("layout QA reports monitoring stopped without changing saved preferences", LayoutQaReportsMonitoringStopped);
        yield return ("layout QA shortcuts are opt-in and provider-only", LayoutQaShortcutsAreNarrow);
        yield return ("left-clicking tray icon opens status popup", LeftTrayClickOpensPopup);
        yield return ("monitoring uses one clear start-stop toggle", MonitoringToggleIsClear);
        yield return ("integration guidance is one-click and provider-specific", IntegrationGuidanceIsProviderSpecific);
        yield return ("embedded Codex integration matches repository source", EmbeddedCodexIntegrationMatchesRepository);
        yield return ("embedded Claude integration matches repository source", EmbeddedClaudeIntegrationMatchesRepository);
        yield return ("Codex Configure installs appends backs up and is idempotent", CodexFallbackIsNonDestructive);
        yield return ("Claude Configure preserves settings and instructions and is idempotent", ClaudeConfigureIsNonDestructive);
        yield return ("Claude Configure refuses conflicts and missing CLI", ClaudeConfigureRefusesUnsafeSetup);
        yield return ("automatic Configure refuses unsafe or unsupported setup", CodexFallbackRefusesUnsafeSetup);
        yield return ("instructions popup is accessible and read-only", InstructionsPopupIsAccessible);
        yield return ("popup disposes its owned tray icon", PopupDisposesOwnedTrayIcon);
    }

    private static void ProviderCatalogDefaultsAreValid()
    {
        Equal(ProviderCatalogValidationError.None,
            ProviderCatalogValidator.Validate(ProviderCatalogSettings.Default));
        Equal(AiProviderId.Codex,
            ProviderCatalogSettings.Default.Providers.Single().ProviderId);
        var windows = ProviderCatalogSettings.Default.Providers.Single().QuotaWindows;
        Equal(2, windows.Count);
        True(windows.Any(window => window.Kind == QuotaWindowKind.RollingFiveHour));
        True(windows.Any(window => window.Kind == QuotaWindowKind.Weekly));
    }

    private static void ClaudeRequiresBothWindows()
    {
        var claude = ProviderCatalogSettings.DefaultClaudeCode;
        Equal(2, claude.QuotaWindows.Count);
        True(claude.QuotaWindows.All(window => window.Required));
        True(claude.QuotaWindows.Any(window =>
            window.Kind == QuotaWindowKind.RollingFiveHour));
        True(claude.QuotaWindows.Any(window =>
            window.Kind == QuotaWindowKind.Weekly));
    }

    private static void ClaudeStatusLineParsesBothWindows()
    {
        var json = Encoding.UTF8.GetBytes(
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":35.5,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":12,\"resets_at\":1788271200},\"seven_day_opus\":{\"used_percentage\":90,\"resets_at\":1788271201}},\"unrelated\":\"discarded\"}");
        var snapshot = ClaudeStatusLineParser.Parse(json, BaseTime);

        True(snapshot.Available);
        Equal(2, snapshot.Windows.Count);
        Equal(64.5m, snapshot.Windows.Single(item =>
            item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);
        Equal(88m, snapshot.Windows.Single(item =>
            item.Kind == QuotaWindowKind.Weekly).RemainingPercent);
        Equal(null, snapshot.Error);
    }

    private static void ClaudeStatusLineFailsClosed()
    {
        foreach (var input in new[]
        {
            "not-json",
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"resets_at\":1787666400}}}",
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"resets_at\":1787666400},\"five_hour\":{\"used_percentage\":2,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":3,\"resets_at\":1788271200}}}",
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"used_percentage\":2,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":3,\"resets_at\":1788271200}}}",
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":101,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":3,\"resets_at\":1788271200}}}",
            "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"resets_at\":1},\"seven_day\":{\"used_percentage\":3,\"resets_at\":1788271200}}}"
        })
        {
            False(ClaudeStatusLineParser.Parse(
                Encoding.UTF8.GetBytes(input), BaseTime).Available);
        }

        False(ClaudeStatusLineParser.Parse(
            new byte[ClaudeStatusLineParser.MaximumInputBytes + 1],
            BaseTime).Available);
    }

    private static void ClaudeStatusLineBoundaryIsForwardCompatibleAndFailClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = FindRepositoryRoot();
        var bridge = Path.Combine(root, "integrations", "claude", "claude-statusline.ps1");
        var temporary = Path.Combine(
            Path.GetTempPath(),
            "UsageGuard-claude-filter-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            File.WriteAllText(temporary, $$"""
                $ErrorActionPreference = 'Stop'
                . '{{bridge.Replace("'", "''", StringComparison.Ordinal)}}'
                $RawInput = [Console]::In.ReadToEnd()
                try {
                    [Console]::Out.Write((ConvertTo-UsageGuardClaudeFilteredJson $RawInput))
                    exit 0
                }
                catch { exit 2 }
                """, new UTF8Encoding(false));

            var extraWindow = "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":35.5,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":12,\"resets_at\":1788271200},\"seven_day_opus\":{\"used_percentage\":90,\"resets_at\":1788271201}},\"session_id\":\"must-not-cross-boundary\"}";
            var accepted = RunPowerShellFilter(temporary, extraWindow);
            Equal(0, accepted.ExitCode);
            using (var filtered = JsonDocument.Parse(accepted.Output))
            {
                var outputRoot = filtered.RootElement;
                Equal(1, outputRoot.EnumerateObject().Count());
                False(outputRoot.TryGetProperty("session_id", out _));
                var limits = outputRoot.GetProperty("rate_limits");
                Equal(2, limits.EnumerateObject().Count());
                False(limits.TryGetProperty("seven_day_opus", out _));
                Equal(35.5m, limits.GetProperty("five_hour")
                    .GetProperty("used_percentage").GetDecimal());
                Equal(12m, limits.GetProperty("seven_day")
                    .GetProperty("used_percentage").GetDecimal());
            }

            foreach (var rejected in new[]
            {
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"resets_at\":1787666400}}}",
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"resets_at\":1787666400},\"five_hour\":{\"used_percentage\":2,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":3,\"resets_at\":1788271200}}}",
                "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":1,\"used_percentage\":2,\"resets_at\":1787666400},\"seven_day\":{\"used_percentage\":3,\"resets_at\":1788271200}}}",
                new string('[', 25) + "null" + new string(']', 25),
                "not-json"
            })
            {
                Equal(2, RunPowerShellFilter(temporary, rejected).ExitCode);
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static (int ExitCode, string Output) RunPowerShellFilter(
        string script,
        string input)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        True(process.Start());
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        True(process.WaitForExit(5_000));
        Equal(string.Empty, errors.GetAwaiter().GetResult());
        return (process.ExitCode, output.GetAwaiter().GetResult());
    }

    private static void ClaudeStateRoundTripsAndRejectsCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ClaudeUsageStorage(root);
            var snapshot = new ClaudeUsageSnapshot(
                ClaudeUsageSnapshot.CurrentSchemaVersion,
                true,
                BaseTime,
                [
                    ProviderWindow(QuotaWindowKind.RollingFiveHour, 71m),
                    ProviderWindow(QuotaWindowKind.Weekly, 82m)
                ],
                null);
            storage.Save(snapshot);
            var loaded = storage.Load(BaseTime.AddSeconds(1));
            True(loaded.Available);
            Equal(2, loaded.Windows.Count);
            False(File.ReadAllText(Path.Combine(root, "claude-state.json"))
                .Contains("unrelated", StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(Path.Combine(root, "claude-state.json"), "{broken");
            var corrupt = storage.Load(BaseTime.AddSeconds(2));
            False(corrupt.Available);
            Equal("stored_state_unavailable", corrupt.Error);

            File.WriteAllBytes(
                Path.Combine(root, "claude-state.json"),
                new byte[32 * 1024 + 1]);
            var oversized = storage.Load(BaseTime.AddSeconds(3));
            False(oversized.Available);
            Equal("stored_state_invalid", oversized.Error);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ClaudeStateRecoversFromInterruptedWrite()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-interrupted-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ClaudeUsageStorage(root);
            var temporary = Path.Combine(root, "claude-state.json.new");
            Directory.CreateDirectory(root);
            // Exactly what a helper killed mid-write leaves behind. Before the
            // fix this made every later Save throw, so a single interrupted
            // write stranded Claude usage at Unknown permanently.
            File.WriteAllText(temporary, "{\"schemaVersion\":1,\"available\":fal");

            var snapshot = new ClaudeUsageSnapshot(
                ClaudeUsageSnapshot.CurrentSchemaVersion,
                true,
                BaseTime,
                [
                    ProviderWindow(QuotaWindowKind.RollingFiveHour, 64m),
                    ProviderWindow(QuotaWindowKind.Weekly, 77m)
                ],
                null);
            storage.Save(snapshot);

            var loaded = storage.Load(BaseTime.AddSeconds(1));
            True(loaded.Available);
            Equal(2, loaded.Windows.Count);
            Equal(64m, loaded.Windows.Single(item =>
                item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);
            // The abandoned temporary must not survive to block the next write.
            False(File.Exists(temporary));

            // A second interrupted write must still recover, not latch.
            File.WriteAllText(temporary, "{partial again");
            storage.Save(snapshot);
            True(storage.Load(BaseTime.AddSeconds(2)).Available);
            False(File.Exists(temporary));

            // A live writer lease must not be bypassed or mistaken for an
            // abandoned write. The bounded wait ends fail-closed.
            using var liveWriter = new FileStream(
                Path.Combine(root, "claude-state.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            Throws<IOException>(() => storage.Save(snapshot));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ConcurrentClaudeObservationsAreSerialized()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-concurrent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ClaudeUsageStorage(root);
            var fiveReset = BaseTime.AddHours(3);
            var weeklyReset = BaseTime.AddDays(4);
            ClaudeUsageSnapshot Snapshot(decimal fiveHour, decimal weekly) => new(
                ClaudeUsageSnapshot.CurrentSchemaVersion,
                true,
                BaseTime,
                [
                    new ProviderQuotaWindowObservation(
                        QuotaWindowKind.RollingFiveHour, fiveHour, fiveReset,
                        BaseTime, ObservationConfidence.High,
                        ObservationFreshness.ObservedNow, null),
                    new ProviderQuotaWindowObservation(
                        QuotaWindowKind.Weekly, weekly, weeklyReset,
                        BaseTime, ObservationConfidence.High,
                        ObservationFreshness.ObservedNow, null)
                ],
                null);

            using var ready = new CountdownEvent(2);
            using var start = new ManualResetEventSlim(false);
            var observations = new[] { Snapshot(61m, 82m), Snapshot(24m, 74m) };
            var writers = observations.Select(observation => Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                storage.ReconcileAndSave(observation, BaseTime);
            })).ToArray();
            True(ready.Wait(TimeSpan.FromSeconds(5)));
            start.Set();
            True(Task.WaitAll(writers, TimeSpan.FromSeconds(10)));

            var stored = storage.Load(BaseTime.AddSeconds(1));
            True(stored.Available);
            Equal(24m, stored.Windows.Single(item =>
                item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);
            Equal(74m, stored.Windows.Single(item =>
                item.Kind == QuotaWindowKind.Weekly).RemainingPercent);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ClaudeBridgeAllowsHelperColdStart()
    {
        var bridge = Path.Combine(
            FindRepositoryRoot(), "integrations", "claude", "claude-statusline.ps1");
        var text = File.ReadAllText(bridge);

        // The helper is a self-contained single-file build whose first run
        // extracts and JITs. A two-second budget killed it mid-write, which is
        // how the stranded-Unknown state above was produced in the first place.
        False(text.Contains("WaitForExit(2500)", StringComparison.Ordinal));
        True(text.Contains("$Process.WaitForExit(10000)", StringComparison.Ordinal));
        // Still bounded: the status line must never hang a Claude Code session.
        False(text.Contains("WaitForExit(30001)", StringComparison.Ordinal));
        False(text.Contains(".Kill($true)", StringComparison.Ordinal));
        True(text.Contains("System32\\taskkill.exe", StringComparison.Ordinal));
    }

    private static void ClaudePowerShellTimeoutCleanupLeavesNoOrphan()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-ps5-timeout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            static string Quote(string value) => value.Replace("'", "''",
                StringComparison.Ordinal);
            var powerShell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            var wrapper = Path.Combine(
                FindRepositoryRoot(), ".agents", "skills", "claude-usage-guard",
                "scripts", "invoke_guard_process.ps1");
            var pidFile = Path.Combine(root, "child.pid");
            var child = Path.Combine(root, "child.ps1");
            var driver = Path.Combine(root, "driver.ps1");
            File.WriteAllText(child,
                $"[IO.File]::WriteAllText('{Quote(pidFile)}',[string]$PID); " +
                "Start-Sleep -Seconds 30\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(driver,
                $". '{Quote(wrapper)}'\r\n" +
                $"$ChildArguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{child}\"'\r\n" +
                "try {\r\n" +
                $"  [void](Invoke-CodexUsageGuardProcess -ExecutablePath '{Quote(powerShell)}' -Arguments $ChildArguments -TimeoutMilliseconds 1000)\r\n" +
                "  exit 9\r\n" +
                "}\r\ncatch { exit 0 }\r\n",
                new UTF8Encoding(false));

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = powerShell,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{driver}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            True(process.WaitForExit(20_000));
            Equal(0, process.ExitCode);
            True(File.Exists(pidFile));
            True(int.TryParse(File.ReadAllText(pidFile), out var childPid));
            var childExited = false;
            try
            {
                using var childProcess = Process.GetProcessById(childPid);
                childExited = childProcess.WaitForExit(5_000);
            }
            catch (ArgumentException)
            {
                childExited = true;
            }
            True(childExited);

            var bridge = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "integrations", "claude",
                "claude-statusline.ps1"));
            var wrapperText = File.ReadAllText(wrapper);
            False(bridge.Contains(".Kill($true)", StringComparison.Ordinal));
            False(wrapperText.Contains(".Kill($true)", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ClaudeConfigureUpgradesOwnedAssetsAndRefusesUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-owned-" + Guid.NewGuid().ToString("N"));
        try
        {
            var claudeRoot = Path.Combine(root, ".claude");
            Directory.CreateDirectory(claudeRoot);
            var executable = Path.Combine(root, ".local", "bin", "claude.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, [0x4D, 0x5A]);
            var configurator = new ProviderInstructionConfigurator(
                root,
                () => new DateTimeOffset(2026, 8, 29, 9, 30, 0, TimeSpan.Zero),
                Path.Combine(root, "data"),
                () => executable);

            Equal(InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                configurator.Configure(InstructionProvider.ClaudeCode).Status);

            var assets = EmbeddedClaudeIntegration.ReadVerifiedAssets();
            var skillDirectory = Path.Combine(claudeRoot, "skills", "claude-usage-guard");
            var bridge = Path.Combine(claudeRoot, "usage-guard", "claude-statusline.ps1");

            // Reconstruct the exact bridge shipped immediately before the
            // PowerShell 5.1 cleanup fix, then the older cold-start variant.
            // Only their pinned SHA-256 values are accepted as ours.
            var currentBridge = new UTF8Encoding(false, true)
                .GetString(assets["claude-statusline.ps1"])
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var stopStart = currentBridge.IndexOf(
                "function Stop-CodexUsageGuardOwnedProcess", StringComparison.Ordinal);
            var stopEnd = currentBridge.IndexOf(
                "function ConvertTo-UsageGuardClaudeFilteredJson", StringComparison.Ordinal);
            True(stopStart >= 0 && stopEnd > stopStart);
            var preCleanupBridge = currentBridge.Remove(stopStart, stopEnd - stopStart)
                .Replace("    $Started = $false\n", string.Empty, StringComparison.Ordinal)
                .Replace("        $Started = $true\n", string.Empty, StringComparison.Ordinal)
                .Replace(
                    "            Stop-CodexUsageGuardOwnedProcess -Process $Process\n",
                    "            $Process.Kill($true)\n            $Process.WaitForExit()\n",
                    StringComparison.Ordinal)
                .Replace(
                    "        if ($Started -and -not $Process.HasExited) {\n" +
                    "            try { Stop-CodexUsageGuardOwnedProcess -Process $Process } catch { }\n" +
                    "        }\n",
                    "        if (-not $Process.HasExited) { $Process.Kill($true) }\n",
                    StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
            Equal(
                "EED29F25B2774B568C16BCB77501E8AD135508ACDCB6E46C08B7ED0CB835D905",
                Convert.ToHexString(SHA256.HashData(
                    new UTF8Encoding(false).GetBytes(preCleanupBridge))));
            const string coldStartBlock =
                "        # The helper is a self-contained single-file build, so its first run\r\n" +
                "        # after an install extracts and JITs before it can answer. That cold\r\n" +
                "        # start easily exceeds a two-second budget even though warm runs finish\r\n" +
                "        # in well under a second, and killing it mid-write is what previously\r\n" +
                "        # stranded Claude usage at Unknown. Stay bounded, but allow the cold\r\n" +
                "        # start to finish.\r\n" +
                "        if (-not $Process.WaitForExit(10000)) {";
            var priorBridge = preCleanupBridge.Replace(
                coldStartBlock,
                "        if (-not $Process.WaitForExit(2500)) {",
                StringComparison.Ordinal);
            False(priorBridge == currentBridge);
            Equal(
                "2EE64FE7C8473CD6160DC160BCE590EB9C35B918C79049A3B4C7B09ED7ED4F95",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    new UTF8Encoding(false).GetBytes(priorBridge))));
            File.WriteAllText(bridge, priorBridge, new UTF8Encoding(false));

            var upgraded = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(
                InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                upgraded.Status);
            True(upgraded.BackupPath is not null);
            foreach (var asset in assets)
            {
                var installed = asset.Key == "claude-statusline.ps1"
                    ? bridge
                    : Path.Combine(skillDirectory, asset.Key);
                True(File.ReadAllBytes(installed).AsSpan().SequenceEqual(asset.Value));
            }

            // Marker-like text is not ownership proof. Unknown content must be
            // refused and left exactly as it was.
            const string unrelated = "# another tool's status line\nWrite-Host hi\n";
            File.WriteAllText(bridge, unrelated, new UTF8Encoding(false));
            var refused = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(InstructionConfigurationStatus.ConflictingIntegration, refused.Status);
            Equal(unrelated, File.ReadAllText(bridge).Replace("\r\n", "\n",
                StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ClaudeIdleSessionCannotRaiseRemainingUsage()
    {
        static ProviderQuotaWindowObservation Window(
            QuotaWindowKind kind,
            decimal remaining,
            DateTimeOffset resetsAt,
            DateTimeOffset observedAt) => new(
                kind,
                remaining,
                resetsAt,
                observedAt,
                ObservationConfidence.High,
                ObservationFreshness.ObservedNow,
                null);

        var fiveReset = BaseTime.AddHours(3);
        var weeklyReset = BaseTime.AddDays(4);

        // The active session has consumed more of the window.
        var active = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion, true, BaseTime,
            [
                Window(QuotaWindowKind.RollingFiveHour, 54m, fiveReset, BaseTime),
                Window(QuotaWindowKind.Weekly, 85m, weeklyReset, BaseTime)
            ], null);

        // An idle session still reports the limits it last saw. Remaining usage
        // only falls within a window, so this must never be allowed to raise the
        // stored value back up and hide real consumption.
        var stale = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion, true, BaseTime.AddSeconds(1),
            [
                Window(QuotaWindowKind.RollingFiveHour, 84m, fiveReset, BaseTime.AddSeconds(1)),
                Window(QuotaWindowKind.Weekly, 89m, weeklyReset, BaseTime.AddSeconds(1))
            ], null);

        var reconciled = ClaudeUsageSnapshot.Reconcile(active, stale);
        Equal(54m, reconciled.Windows.Single(item =>
            item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);
        Equal(85m, reconciled.Windows.Single(item =>
            item.Kind == QuotaWindowKind.Weekly).RemainingPercent);
        // Retaining the conservative earlier percentage also retains its real
        // observation time. An idle writer cannot make it appear fresh.
        Equal(BaseTime, reconciled.ObservedAtUtc);
        Equal(BaseTime, reconciled.Windows.Single(item =>
            item.Kind == QuotaWindowKind.RollingFiveHour).ObservedAtUtc);

        // Small reset timestamp jitter is the same provider window and must not
        // allow a stale higher percentage to replace the lower reading.
        var jitter = stale with
        {
            ObservedAtUtc = BaseTime.AddSeconds(2),
            Windows = stale.Windows.Select(item => item with
            {
                ResetsAtUtc = item.ResetsAtUtc!.Value.AddSeconds(1),
                ObservedAtUtc = BaseTime.AddSeconds(2)
            }).ToArray()
        };
        var jittered = ClaudeUsageSnapshot.Reconcile(active, jitter);
        Equal(54m, jittered.Windows.Single(item =>
            item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);
        Equal(BaseTime, jittered.ObservedAtUtc);

        // Genuine further consumption must still be recorded.
        var lower = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion, true, BaseTime.AddSeconds(2),
            [
                Window(QuotaWindowKind.RollingFiveHour, 21m, fiveReset, BaseTime.AddSeconds(2)),
                Window(QuotaWindowKind.Weekly, 80m, weeklyReset, BaseTime.AddSeconds(2))
            ], null);
        Equal(21m, ClaudeUsageSnapshot.Reconcile(reconciled, lower).Windows.Single(item =>
            item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);

        // A genuine reset carries a later provider reset timestamp and must
        // replace the old window outright, never be clamped down by it.
        var afterReset = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion, true, BaseTime.AddSeconds(3),
            [
                Window(QuotaWindowKind.RollingFiveHour, 100m, fiveReset.AddHours(5), BaseTime.AddSeconds(3)),
                Window(QuotaWindowKind.Weekly, 80m, weeklyReset, BaseTime.AddSeconds(3))
            ], null);
        var rolled = ClaudeUsageSnapshot.Reconcile(lower, afterReset);
        Equal(100m, rolled.Windows.Single(item =>
            item.Kind == QuotaWindowKind.RollingFiveHour).RemainingPercent);
        // The weekly window did not reset, so it stays clamped to the lower value.
        Equal(80m, rolled.Windows.Single(item =>
            item.Kind == QuotaWindowKind.Weekly).RemainingPercent);

        // Unavailable input never inherits stale percentages.
        var unavailable = ClaudeUsageSnapshot.UnavailableAt(
            BaseTime.AddSeconds(4), "required_rate_limits_missing_or_invalid");
        False(ClaudeUsageSnapshot.Reconcile(active, unavailable).Available);
        Equal(0, ClaudeUsageSnapshot.Reconcile(active, unavailable).Windows.Count);

        var regressed = active with
        {
            ObservedAtUtc = BaseTime.AddMinutes(1),
            Windows = active.Windows.Select(item => item with
            {
                ResetsAtUtc = item.ResetsAtUtc!.Value.AddMinutes(-10),
                ObservedAtUtc = BaseTime.AddMinutes(1)
            }).ToArray()
        };
        var regressedResult = ClaudeUsageSnapshot.Reconcile(active, regressed);
        False(regressedResult.Available);
        Equal("reset_identity_regressed", regressedResult.Error);
    }

    private static void ClaudeConfigureUsesIsolatedSessionSettings()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-isolated-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var claudeRoot = Path.Combine(root, ".claude");
            Directory.CreateDirectory(claudeRoot);
            var executable = Path.Combine(root, ".local", "bin", "claude.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, [0x4D, 0x5A]);
            var configurator = new ProviderInstructionConfigurator(
                root,
                () => new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero),
                Path.Combine(root, "data"),
                () => executable);
            var settingsPath = Path.Combine(claudeRoot, "settings.json");
            const string originalSettings =
                "{\"theme\":\"dark\",\"env\":{\"SECRET_ADJACENT\":\"do-not-copy\"}," +
                "\"statusLine\":{\"type\":\"command\",\"command\":\"custom\"," +
                "\"padding\":4,\"hideVimModeIndicator\":true}}";
            File.WriteAllText(settingsPath, originalSettings, new UTF8Encoding(false));
            var originalHash = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settingsPath)));

            using (var unreadableUserSettings = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None))
            {
                Equal(InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                    configurator.Configure(InstructionProvider.ClaudeCode).Status);
            }
            Equal(originalHash, Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settingsPath))));
            Equal(0, Directory.GetFiles(
                claudeRoot, "settings.json.backup-UsageGuard-*").Length);

            var isolated = Path.Combine(
                claudeRoot, "usage-guard", "claude-session-settings.json");
            using (var document = JsonDocument.Parse(File.ReadAllText(isolated)))
            {
                var status = document.RootElement.GetProperty("statusLine");
                Equal("command", status.GetProperty("type").GetString());
                True(status.GetProperty("command").GetString()!.Contains(
                    "claude-statusline.ps1", StringComparison.Ordinal));
                False(status.TryGetProperty("refreshInterval", out _));
                False(status.TryGetProperty("padding", out _));
            }

            Equal(InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                configurator.Configure(InstructionProvider.ClaudeCode).Status);
            Equal(originalHash, Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settingsPath))));

            const string unrelatedOwnedPathContent =
                "{\"statusLine\":{\"command\":\"other\"}}";
            File.WriteAllText(
                isolated,
                unrelatedOwnedPathContent,
                new UTF8Encoding(false));
            Equal(InstructionConfigurationStatus.ConflictingIntegration,
                configurator.Configure(InstructionProvider.ClaudeCode).Status);
            Equal(unrelatedOwnedPathContent, File.ReadAllText(isolated));
            Equal(originalHash, Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settingsPath))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ClaudeDecisionUsesStrictestWindow()
    {
        var snapshot = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion,
            true,
            BaseTime,
            [
                ProviderWindow(QuotaWindowKind.RollingFiveHour, 24m),
                ProviderWindow(QuotaWindowKind.Weekly, 90m)
            ],
            null);
        var result = ClaudeGuardCheckOutput.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            snapshot,
            BaseTime);
        Equal("safe_wrap", result.Decision);
        Equal("five_hour", result.ControllingWindow);
        Equal("claude_statusline", result.Source);
        Equal(2, result.Windows.Count);
        False(result.StartNewPhaseAllowed);
        True(result.FinishCurrentCheckpointOnly);

        var criticalSnapshot = snapshot with
        {
            Windows =
            [
                ProviderWindow(QuotaWindowKind.RollingFiveHour, 20m),
                ProviderWindow(QuotaWindowKind.Weekly, 90m)
            ]
        };
        var critical = ClaudeGuardCheckOutput.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            criticalSnapshot,
            BaseTime);
        Equal("safe_wrap", critical.Decision);
        True(critical.CriticalBufferReached);

        var unknown = ClaudeGuardCheckOutput.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            snapshot with { ObservedAtUtc = BaseTime.AddMinutes(-3), Windows = snapshot.Windows.Select(item => item with { ObservedAtUtc = BaseTime.AddMinutes(-3) }).ToArray() },
            BaseTime);
        Equal("unknown", unknown.Decision);
        False(unknown.StartNewPhaseAllowed);
    }

    private static void ClaudeNotificationsDedupeAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-notify-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ClaudeMonitorStateStorage(root);
            var normal = ClaudeOutput(90m, 90m, BaseTime.AddHours(5),
                BaseTime.AddDays(7));
            var first = ClaudeNotificationPolicy.Evaluate(
                normal,
                ProviderCatalogSettings.DefaultClaudeCode,
                storage.Load(),
                BaseTime);
            Equal(GuardNotificationKind.None, first.Kind);
            storage.Save(first.State);

            var warning = ClaudeOutput(29m, 90m, BaseTime.AddHours(5),
                BaseTime.AddDays(7));
            var transition = ClaudeNotificationPolicy.Evaluate(
                warning,
                ProviderCatalogSettings.DefaultClaudeCode,
                storage.Load(),
                BaseTime.AddSeconds(10));
            Equal(GuardNotificationKind.Warning, transition.Kind);
            storage.Save(transition.State);

            var afterRestart = ClaudeNotificationPolicy.Evaluate(
                warning,
                ProviderCatalogSettings.DefaultClaudeCode,
                storage.Load(),
                BaseTime.AddSeconds(20));
            Equal(GuardNotificationKind.None, afterRestart.Kind);
            storage.Save(afterRestart.State);

            var jitter = ClaudeOutput(29m, 90m, BaseTime.AddHours(5).AddSeconds(1),
                BaseTime.AddDays(7).AddSeconds(1));
            var jitterResult = ClaudeNotificationPolicy.Evaluate(
                jitter,
                ProviderCatalogSettings.DefaultClaudeCode,
                storage.Load(),
                BaseTime.AddSeconds(30));
            Equal(GuardNotificationKind.None, jitterResult.Kind);
            storage.Save(jitterResult.State);

            var reset = ClaudeOutput(90m, 90m, BaseTime.AddHours(10),
                BaseTime.AddDays(14));
            var resetResult = ClaudeNotificationPolicy.Evaluate(
                reset,
                ProviderCatalogSettings.DefaultClaudeCode,
                storage.Load(),
                BaseTime.AddHours(5));
            Equal(GuardNotificationKind.Reset, resetResult.Kind);
            storage.Save(resetResult.State);
            var duplicateReset = ClaudeNotificationPolicy.Evaluate(
                reset,
                ProviderCatalogSettings.DefaultClaudeCode,
                storage.Load(),
                BaseTime.AddHours(5).AddSeconds(10));
            Equal(GuardNotificationKind.None, duplicateReset.Kind);

            File.WriteAllText(Path.Combine(root, "claude-monitor-state.json.new"),
                "partial");
            Throws<IOException>(() => storage.Save(duplicateReset.State));
            Equal("partial", File.ReadAllText(Path.Combine(
                root, "claude-monitor-state.json.new")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ClaudeGuardCheckOutput ClaudeOutput(
        decimal fiveRemaining,
        decimal weeklyRemaining,
        DateTimeOffset fiveReset,
        DateTimeOffset weeklyReset)
    {
        var snapshot = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion,
            true,
            BaseTime,
            [
                new ProviderQuotaWindowObservation(
                    QuotaWindowKind.RollingFiveHour,
                    fiveRemaining,
                    fiveReset,
                    BaseTime,
                    ObservationConfidence.High,
                    ObservationFreshness.ObservedNow,
                    null),
                new ProviderQuotaWindowObservation(
                    QuotaWindowKind.Weekly,
                    weeklyRemaining,
                    weeklyReset,
                    BaseTime,
                    ObservationConfidence.High,
                    ObservationFreshness.ObservedNow,
                    null)
            ],
            null);
        return ClaudeGuardCheckOutput.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            snapshot,
            BaseTime);
    }

    private static void MultiWindowUsesStrictestWindow()
    {
        var observations = new[]
        {
            ProviderWindow(QuotaWindowKind.RollingFiveHour, 24m),
            ProviderWindow(QuotaWindowKind.Weekly, 80m)
        };
        var decision = MultiWindowProviderPolicy.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            observations,
            BaseTime);

        Equal(GuardPolicyClassification.SafeWrap, decision.Classification);
        Equal(QuotaWindowKind.RollingFiveHour, decision.ControllingWindow);
    }

    private static void MultiWindowFailsClosedOnMissingWindow()
    {
        var decision = MultiWindowProviderPolicy.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            [ProviderWindow(QuotaWindowKind.Weekly, 90m)],
            BaseTime);

        Equal(GuardPolicyClassification.Unknown, decision.Classification);
        Equal(null, decision.ControllingWindow);
    }

    private static void ProviderPoliciesRemainIsolated()
    {
        var codex = MultiWindowProviderPolicy.Evaluate(
            ProviderCatalogSettings.DefaultCodex,
            [
                ProviderWindow(QuotaWindowKind.RollingFiveHour, 90m),
                ProviderWindow(QuotaWindowKind.Weekly, 90m)
            ],
            BaseTime);
        var claude = MultiWindowProviderPolicy.Evaluate(
            ProviderCatalogSettings.DefaultClaudeCode,
            [
                ProviderWindow(QuotaWindowKind.RollingFiveHour, 24m),
                ProviderWindow(QuotaWindowKind.Weekly, 90m)
            ],
            BaseTime);

        Equal(GuardPolicyClassification.Normal, codex.Classification);
        Equal(GuardPolicyClassification.SafeWrap, claude.Classification);
        Equal(AiProviderId.Codex, codex.ProviderId);
        Equal(AiProviderId.ClaudeCode, claude.ProviderId);
    }

    private static void ProviderCatalogRoundTripsAtomically()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-provider-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ProviderCatalogStorage(root);
            var catalog = new ProviderCatalogSettings(
                ProviderCatalogSettings.CurrentSchemaVersion,
                [
                    ProviderCatalogSettings.DefaultCodex,
                    ProviderCatalogSettings.DefaultClaudeCode
                ]);
            storage.Save(catalog);
            var loaded = storage.Load();
            Equal(ProviderCatalogLoadStatus.Loaded, loaded.Status);
            Equal(2, loaded.Settings.Providers.Count);
            Equal(AiProviderId.ClaudeCode,
                loaded.Settings.Providers[1].ProviderId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void LegacyCodexProviderSettingsGainFiveHourDefaults()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-provider-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ProviderCatalogStorage(root);
            storage.Save(new ProviderCatalogSettings(
                ProviderCatalogSettings.CurrentSchemaVersion,
                [
                    ProviderCatalogSettings.DefaultCodex with
                    {
                        MonitoringEnabled = false,
                        PollingIntervalSeconds = 123,
                        QuotaWindows =
                        [
                            ProviderCatalogSettings.DefaultCodex.QuotaWindows.Single(
                                window => window.Kind == QuotaWindowKind.Weekly) with
                            {
                                WarningThresholdPercent = 44m,
                                SafeWrapThresholdPercent = 33m,
                                CriticalBufferPercent = 22m
                            }
                        ]
                    }
                ]));

            var loaded = storage.Load();

            Equal(ProviderCatalogLoadStatus.Loaded, loaded.Status);
            var codex = loaded.Settings.Providers.Single();
            False(codex.MonitoringEnabled);
            Equal(123, codex.PollingIntervalSeconds);
            Equal(2, codex.QuotaWindows.Count);
            var weekly = codex.QuotaWindows.Single(
                window => window.Kind == QuotaWindowKind.Weekly);
            Equal(44m, weekly.WarningThresholdPercent);
            Equal(33m, weekly.SafeWrapThresholdPercent);
            Equal(22m, weekly.CriticalBufferPercent);
            var fiveHour = codex.QuotaWindows.Single(
                window => window.Kind == QuotaWindowKind.RollingFiveHour);
            Equal(30m, fiveHour.WarningThresholdPercent);
            Equal(25m, fiveHour.SafeWrapThresholdPercent);
            Equal(20m, fiveHour.CriticalBufferPercent);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ProviderQuotaWindowObservation ProviderWindow(
        QuotaWindowKind kind,
        decimal remaining) => new(
            kind,
            remaining,
            BaseTime.AddDays(1),
            BaseTime,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null);

    private static void SettingsDefaultsAreValid()
    {
        Equal(SettingsValidationError.None,
            GuardSettingsValidator.Validate(GuardSettings.Default));
        Equal(30m, GuardSettings.Default.WarningThresholdPercent);
        Equal(25m, GuardSettings.Default.SafeWrapThresholdPercent);
        Equal(20m, GuardSettings.Default.CriticalBufferPercent);
        Equal(60, GuardSettings.Default.PollingIntervalSeconds);
        False(GuardSettings.Default.ResetWakeUpEnabled);
        False(GuardSettings.Default.UnrestrictedDevelopmentOverride);
        False(GuardSettings.Default.StartAtSignIn);
        False(GuardSettings.Default.LaunchTogetherShortcutsEnabled);
    }

    private static void UpdateChannelIsExplicitlyUnconfigured()
    {
        var result = new UnconfiguredUpdateService()
            .CheckAsync()
            .GetAwaiter()
            .GetResult();
        Equal(UpdateCheckStatus.ChannelNotConfigured, result.Status);
        Equal("0.003", result.CurrentVersion);
        Equal(null, result.AvailableVersion);
        Equal("Usage Guard v.0.003", UsageGuardRelease.ProductNameWithVersion);
    }

    private static void GitHubUpdateChannelDetectsNewerRelease()
    {
        var handler = new StaticHttpHandler(
            HttpStatusCode.OK,
            """
            {"tag_name":"v0.004","html_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/tag/v0.004","draft":false,"prerelease":false,"immutable":true,"assets":[{"name":"UsageGuard-Setup-0.004.exe","digest":"sha256:1111111111111111111111111111111111111111111111111111111111111111","browser_download_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/v0.004/UsageGuard-Setup-0.004.exe"},{"name":"UsageGuard-Setup-0.004.exe.sha256","digest":"sha256:2222222222222222222222222222222222222222222222222222222222222222","browser_download_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/v0.004/UsageGuard-Setup-0.004.exe.sha256"}]}
            """);
        var result = new GitHubReleaseUpdateService(handler)
            .CheckAsync()
            .GetAwaiter()
            .GetResult();

        Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Equal("0.004", result.AvailableVersion);
        True(result.Message.Contains("Usage Guard v.0.004", StringComparison.Ordinal));
        True(result.ReleasePage is not null);
        True(result.InstallerAsset is not null);
        True(result.ChecksumAsset is not null);
        Equal(GitHubReleaseUpdateService.LatestReleaseEndpoint,
            handler.RequestUri);
        Equal("UsageGuard/0.003", handler.UserAgent);
    }

    private static void GitHubUpdateChannelRejectsForeignUrl()
    {
        var handler = new StaticHttpHandler(
            HttpStatusCode.OK,
            """
            {"tag_name":"v0.004","html_url":"https://example.invalid/releases/tag/v0.004","draft":false,"prerelease":false,"immutable":true}
            """);
        var result = new GitHubReleaseUpdateService(handler)
            .CheckAsync()
            .GetAwaiter()
            .GetResult();

        Equal(UpdateCheckStatus.Unavailable, result.Status);
        Equal(null, result.ReleasePage);
    }

    private static void GitHubUpdateChannelRejectsRetiredRepository()
    {
        var handler = new StaticHttpHandler(
            HttpStatusCode.OK,
            """
            {"tag_name":"v0.004","html_url":"https://github.com/BionicVisionary/Usage-Guard/releases/tag/v0.004","draft":false,"prerelease":false,"immutable":true,"assets":[{"name":"UsageGuard-Setup-0.004.exe","digest":"sha256:1111111111111111111111111111111111111111111111111111111111111111","browser_download_url":"https://github.com/BionicVisionary/Usage-Guard/releases/download/v0.004/UsageGuard-Setup-0.004.exe"},{"name":"UsageGuard-Setup-0.004.exe.sha256","digest":"sha256:2222222222222222222222222222222222222222222222222222222222222222","browser_download_url":"https://github.com/BionicVisionary/Usage-Guard/releases/download/v0.004/UsageGuard-Setup-0.004.exe.sha256"}]}
            """);
        var result = new GitHubReleaseUpdateService(handler)
            .CheckAsync()
            .GetAwaiter()
            .GetResult();

        Equal(UpdateCheckStatus.Unavailable, result.Status);
        Equal(null, result.ReleasePage);
    }

    private static void GitHubUpdateChannelRequiresImmutableDigests()
    {
        foreach (var json in new[]
        {
            """
            {"tag_name":"v0.004","html_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/tag/v0.004","draft":false,"prerelease":false,"immutable":false,"assets":[]}
            """,
            """
            {"tag_name":"v0.004","html_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/tag/v0.004","draft":false,"prerelease":false,"immutable":true,"assets":[{"name":"UsageGuard-Setup-0.004.exe","browser_download_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/v0.004/UsageGuard-Setup-0.004.exe"},{"name":"UsageGuard-Setup-0.004.exe.sha256","digest":"sha256:2222222222222222222222222222222222222222222222222222222222222222","browser_download_url":"https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/v0.004/UsageGuard-Setup-0.004.exe.sha256"}]}
            """
        })
        {
            var result = new GitHubReleaseUpdateService(
                    new StaticHttpHandler(HttpStatusCode.OK, json))
                .CheckAsync()
                .GetAwaiter()
                .GetResult();
            Equal(UpdateCheckStatus.Unavailable, result.Status);
        }
    }
    private static void UpdateNotificationIsDeduplicated()
    {
        var result = InstallableUpdateResult();
        True(UpdateNotificationPolicy.ShouldNotify(result, null));
        var ledger = new Dictionary<string, DateTimeOffset>
        {
            [UpdateNotificationPolicy.KeyFor("0.004")] = BaseTime
        };
        False(UpdateNotificationPolicy.ShouldNotify(result, ledger));
        False(UpdateNotificationPolicy.ShouldNotify(
            result with { Status = UpdateCheckStatus.UpToDate },
            null));
    }

    private static void InAppUpdaterVerifiesInstallerHash()
    {
        var installer = Encoding.UTF8.GetBytes("synthetic verified installer bytes");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var result = InstallableUpdateResult(installer);
        var handler = new AssetHttpHandler(new Dictionary<string, byte[]>
        {
            [result.InstallerAsset!.AbsoluteUri] = installer,
            [result.ChecksumAsset!.AbsoluteUri] = Encoding.ASCII.GetBytes(
                $"{hash}  UsageGuard-Setup-0.004.exe\r\n")
        });
        var prepared = new GitHubReleaseUpdateInstaller(handler)
            .DownloadAndVerifyAsync(result)
            .GetAwaiter()
            .GetResult();
        Equal(UpdatePreparationStatus.Ready, prepared.Status);
        True(prepared.InstallerPath is not null && File.Exists(prepared.InstallerPath));
        True(installer.SequenceEqual(File.ReadAllBytes(prepared.InstallerPath!)));
        Directory.Delete(Path.GetDirectoryName(prepared.InstallerPath!)!, recursive: true);
    }

    private static void InAppUpdaterRequiresImmutableRelease()
    {
        var installer = Encoding.UTF8.GetBytes("synthetic installer");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var result = InstallableUpdateResult(installer) with
        {
            IsImmutableRelease = false
        };
        var handler = new AssetHttpHandler(new Dictionary<string, byte[]>
        {
            [result.InstallerAsset!.AbsoluteUri] = installer,
            [result.ChecksumAsset!.AbsoluteUri] = Encoding.ASCII.GetBytes(
                $"{hash}  UsageGuard-Setup-0.004.exe\r\n")
        });
        var prepared = new GitHubReleaseUpdateInstaller(handler)
            .DownloadAndVerifyAsync(result)
            .GetAwaiter()
            .GetResult();
        Equal(UpdatePreparationStatus.Unavailable, prepared.Status);
        Equal(null, prepared.InstallerPath);
    }

    private static void InAppUpdaterRejectsMismatchedHash()
    {
        var result = InstallableUpdateResult(
            Encoding.UTF8.GetBytes("expected installer"));
        var handler = new AssetHttpHandler(new Dictionary<string, byte[]>
        {
            [result.InstallerAsset!.AbsoluteUri] = Encoding.UTF8.GetBytes("tampered"),
            [result.ChecksumAsset!.AbsoluteUri] = Encoding.ASCII.GetBytes(
                $"{new string('0', 64)}  UsageGuard-Setup-0.004.exe\r\n")
        });
        var prepared = new GitHubReleaseUpdateInstaller(handler)
            .DownloadAndVerifyAsync(result)
            .GetAwaiter()
            .GetResult();
        Equal(UpdatePreparationStatus.VerificationFailed, prepared.Status);
        Equal(null, prepared.InstallerPath);
    }
    private static UpdateCheckResult InstallableUpdateResult(
        byte[]? installer = null)
    {
        installer ??= Encoding.UTF8.GetBytes("synthetic verified installer bytes");
        var installerHash = Convert.ToHexString(SHA256.HashData(installer));
        var checksum = Encoding.ASCII.GetBytes(
            $"{installerHash.ToLowerInvariant()}  UsageGuard-Setup-0.004.exe\r\n");
        return new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            "0.003",
            "0.004",
            "Update available",
            new Uri("https://github.com/BionicVisionary/Usage-Guard-Main/releases/tag/v0.004"),
            new Uri("https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/v0.004/UsageGuard-Setup-0.004.exe"),
            new Uri("https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/v0.004/UsageGuard-Setup-0.004.exe.sha256"),
            IsImmutableRelease: true,
            installerHash,
            Convert.ToHexString(SHA256.HashData(checksum)));
    }

    private static void SettingsRejectReversedThresholds() => Equal(
        SettingsValidationError.ThresholdOrderInvalid,
        GuardSettingsValidator.Validate(GuardSettings.Default with
        {
            SafeWrapThresholdPercent = 31m
        }));

    private static void SettingsRejectReversedFiveHourThresholds() => Equal(
        SettingsValidationError.ThresholdOrderInvalid,
        GuardSettingsValidator.Validate(GuardSettings.Default with
        {
            FiveHourSafeWrapThresholdPercent = 31m
        }));

    private static void SettingsRejectThresholdRange() => Equal(
        SettingsValidationError.ThresholdOutOfRange,
        GuardSettingsValidator.Validate(GuardSettings.Default with
        {
            WarningThresholdPercent = 101m
        }));

    private static void SettingsRejectPollingBelowRange() => Equal(
        SettingsValidationError.PollingIntervalOutOfRange,
        GuardSettingsValidator.Validate(GuardSettings.Default with
        {
            PollingIntervalSeconds = 29
        }));

    private static void SettingsRejectPollingAboveRange() => Equal(
        SettingsValidationError.PollingIntervalOutOfRange,
        GuardSettingsValidator.Validate(GuardSettings.Default with
        {
            PollingIntervalSeconds = 301
        }));

    private static void RestoreDefaultsPreservesOverride()
    {
        var settings = GuardSettings.Default with
        {
            WarningThresholdPercent = 44m,
            UnrestrictedDevelopmentOverride = false
        };
        var restored = settings.RestoreDefaultsPreservingOverride();
        Equal(30m, restored.WarningThresholdPercent);
        False(restored.UnrestrictedDevelopmentOverride);
    }

    private static void CustomThresholdBoundaries()
    {
        var settings = GuardSettings.Default with
        {
            WarningThresholdPercent = 40m,
            SafeWrapThresholdPercent = 35m,
            CriticalBufferPercent = 30m,
            UnrestrictedDevelopmentOverride = false
        };
        Equal(GuardRuntimeState.Normal, Evaluate(settings, 40.1m).Display.Decision);
        Equal(GuardRuntimeState.Warning, Evaluate(settings, 40m).Display.Decision);
        Equal(GuardRuntimeState.Warning, Evaluate(settings, 35.1m).Display.Decision);
        Equal(GuardRuntimeState.SafeWrap, Evaluate(settings, 35m).Display.Decision);
        Equal(GuardDecisionReason.CriticalBufferReached,
            Evaluate(settings, 30m).Display.Reason);
    }

    private static void CodexStrictestWindowControls()
    {
        var settings = EnforcingSettings() with
        {
            FiveHourWarningThresholdPercent = 40m,
            FiveHourSafeWrapThresholdPercent = 35m,
            FiveHourCriticalBufferPercent = 30m
        };
        var fiveControls = ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            AvailableWithWindows(34m, 90m),
            BaseTime);
        Equal(GuardRuntimeState.SafeWrap, fiveControls.Display.Decision);
        Equal(AppServerQuotaWindowKind.FiveHour,
            fiveControls.Display.ControllingWindow);
        Equal(2, fiveControls.Display.Windows!.Count);

        var weeklyControls = ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            AvailableWithWindows(90m, 24m),
            BaseTime);
        Equal(GuardRuntimeState.SafeWrap, weeklyControls.Display.Decision);
        Equal(AppServerQuotaWindowKind.Weekly,
            weeklyControls.Display.ControllingWindow);
    }

    private static void ZeroAndOneHundredClassifySafely()
    {
        var settings = EnforcingSettings();
        Equal(GuardRuntimeState.SafeWrap, Evaluate(settings, 0m).Display.Decision);
        Equal(GuardRuntimeState.Normal, Evaluate(settings, 100m).Display.Decision);
    }

    private static void OverrideExposesUnderlyingState()
    {
        var result = Evaluate(OverrideSettings(), 25m);
        Equal(GuardRuntimeState.OverrideActive, result.Display.Decision);
        Equal(GuardRuntimeState.SafeWrap, result.Display.UnderlyingDecision);
        True(result.Display.StartNewPhaseAllowed);
        False(result.Display.FinishCurrentCheckpointOnly);
        True(result.PersistentState.LatchedWeeklyResetAtUtc is not null);
    }

    private static void OverrideRemovalRestoresLatch()
    {
        var active = Evaluate(OverrideSettings(), 25m);
        var stored = ConfiguredGuardEvaluator.FromStoredState(
            EnforcingSettings(),
            active.PersistentState,
            BaseTime.AddSeconds(1));
        Equal(GuardRuntimeState.SafeWrap, stored.Decision);
        Equal(GuardDecisionReason.GenuineLatchActive, stored.Reason);
        False(stored.StartNewPhaseAllowed);
    }

    private static void GenuineLiveCreatesLatch()
    {
        var evaluation = Evaluate(EnforcingSettings(), 25m);
        Equal(BaseTime.AddDays(1),
            evaluation.PersistentState.LatchedWeeklyResetAtUtc);
        Equal(BaseTime, evaluation.PersistentState.LatchCreatedAtUtc);
    }

    private static void FiveHourResumeUsesExactLiveReset()
    {
        var fiveReset = BaseTime.AddHours(3).AddMinutes(17).AddSeconds(11);
        var weeklyReset = BaseTime.AddDays(5).AddMinutes(43).AddSeconds(29);
        var evaluation = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings() with { ResetWakeUpEnabled = true },
            GuardPersistentState.Empty,
            AvailableWithWindowDetails(20m, fiveReset, 80m, weeklyReset),
            BaseTime);

        var resume = evaluation.Display.ResumeRecommendation!;
        Equal(GuardResumeStatus.Recommended, resume.Status);
        Equal(GuardResumeReason.FiveHourConstraint, resume.Reason);
        Equal(fiveReset.Add(GuardResumePlanner.ProviderJitterMargin),
            resume.RecommendedAtUtc);
        True(resume.OneShotWakeUpOptIn);
        Equal(1, resume.ConstrainingWindows.Count);
        Equal(fiveReset, resume.ConstrainingWindows[0].ResetsAtUtc);
        Equal(fiveReset, evaluation.Display.Windows!
            .Single(item => item.Kind == AppServerQuotaWindowKind.FiveHour)
            .ResetsAtUtc);
        Equal(weeklyReset, evaluation.Display.Windows!
            .Single(item => item.Kind == AppServerQuotaWindowKind.Weekly)
            .ResetsAtUtc);
        True(!string.IsNullOrWhiteSpace(resume.RecommendedAtLocalDisplay));
    }

    private static void WeeklyResumeUsesExactLiveReset()
    {
        var fiveReset = BaseTime.AddHours(4).AddSeconds(8);
        var weeklyReset = BaseTime.AddDays(4).AddHours(1).AddSeconds(33);
        var evaluation = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings() with { ResetWakeUpEnabled = true },
            GuardPersistentState.Empty,
            AvailableWithWindowDetails(85m, fiveReset, 25m, weeklyReset),
            BaseTime);

        var resume = evaluation.Display.ResumeRecommendation!;
        Equal(GuardResumeStatus.Recommended, resume.Status);
        Equal(GuardResumeReason.WeeklyConstraint, resume.Reason);
        Equal(weeklyReset.Add(GuardResumePlanner.ProviderJitterMargin),
            resume.RecommendedAtUtc);
        Equal(weeklyReset, resume.ConstrainingWindows.Single().ResetsAtUtc);
    }

    private static void BothConstraintsResumeAfterLatestExactReset()
    {
        var fiveReset = BaseTime.AddHours(4).AddSeconds(19);
        var weeklyReset = BaseTime.AddDays(3).AddSeconds(41);
        var evaluation = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings() with { ResetWakeUpEnabled = true },
            GuardPersistentState.Empty,
            AvailableWithWindowDetails(25m, fiveReset, 20m, weeklyReset),
            BaseTime);

        var resume = evaluation.Display.ResumeRecommendation!;
        Equal(GuardResumeStatus.Recommended, resume.Status);
        Equal(GuardResumeReason.AllConstrainingWindows, resume.Reason);
        Equal(2, resume.ConstrainingWindows.Count);
        Equal(weeklyReset.Add(GuardResumePlanner.ProviderJitterMargin),
            resume.RecommendedAtUtc);
        True(resume.RecommendedAtUtc > fiveReset);
    }

    private static void ResumeIdentitySurvivesProviderJitter()
    {
        var settings = EnforcingSettings() with { ResetWakeUpEnabled = true };
        var fiveReset = BaseTime.AddHours(4).AddSeconds(27);
        var weeklyReset = BaseTime.AddDays(3).AddSeconds(27);
        var first = ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            AvailableWithWindowDetails(20m, fiveReset, 90m, weeklyReset),
            BaseTime);
        var firstResume = first.Display.ResumeRecommendation!;

        WithTempStorage((_, storage) =>
        {
            storage.SaveState(first.PersistentState);
            var reloaded = storage.LoadState().State;
            var exactJitteredReset = fiveReset.AddSeconds(1);
            var second = ConfiguredGuardEvaluator.Evaluate(
                settings,
                reloaded,
                AvailableWithWindowDetails(
                    20m,
                    exactJitteredReset,
                    90m,
                    weeklyReset.AddSeconds(1),
                    BaseTime.AddMinutes(1)),
                BaseTime.AddMinutes(1));
            var secondResume = second.Display.ResumeRecommendation!;

            Equal(firstResume.ResetIdentity, secondResume.ResetIdentity);
            Equal(exactJitteredReset,
                secondResume.ConstrainingWindows.Single().ResetsAtUtc);
            Equal(exactJitteredReset.Add(GuardResumePlanner.ProviderJitterMargin),
                secondResume.RecommendedAtUtc);
        });
    }

    private static void UntrustedResetDataCannotScheduleResume()
    {
        var settings = EnforcingSettings() with { ResetWakeUpEnabled = true };
        var genuine = ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            AvailableWithWindowDetails(
                20m,
                BaseTime.AddHours(4),
                90m,
                BaseTime.AddDays(3)),
            BaseTime);
        var latchOnly = ConfiguredGuardEvaluator.FromStoredState(
            settings,
            genuine.PersistentState,
            BaseTime.AddMinutes(1));
        Equal(GuardRuntimeState.SafeWrap, latchOnly.Decision);
        Equal(GuardResumeStatus.Unavailable,
            latchOnly.ResumeRecommendation!.Status);
        Equal(null, latchOnly.ResumeRecommendation.RecommendedAtUtc);

        var stale = ConfiguredGuardEvaluator.Evaluate(
            settings,
            genuine.PersistentState,
            AvailableWithWindowDetails(
                20m,
                BaseTime.AddHours(4),
                90m,
                BaseTime.AddDays(3),
                BaseTime.AddMinutes(-3)),
            BaseTime);
        Equal(GuardResumeStatus.Unavailable,
            stale.Display.ResumeRecommendation!.Status);

        var duplicate = AvailableWithWindowDetails(
            20m,
            BaseTime.AddHours(4),
            90m,
            BaseTime.AddDays(3)) with
        {
            Windows =
            [
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    20m,
                    BaseTime.AddHours(4)),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    20m,
                    BaseTime.AddHours(4)),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    90m,
                    BaseTime.AddDays(3))
            ]
        };
        var ambiguous = ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            duplicate,
            BaseTime);
        Equal(GuardRuntimeState.Unknown, ambiguous.Display.Decision);
        Equal(GuardResumeStatus.Unavailable,
            ambiguous.Display.ResumeRecommendation!.Status);
    }

    private static void UnknownRetainsGenuineLatch()
    {
        var initial = Evaluate(EnforcingSettings(), 25m);
        var unknown = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings(),
            initial.PersistentState,
            AppServerUsageObservation.ErrorAt(
                BaseTime.AddMinutes(1),
                AppServerUsageError.ReadTimedOut),
            BaseTime.AddMinutes(1));
        Equal(GuardRuntimeState.SafeWrap, unknown.Display.Decision);
        Equal(GuardDecisionSource.GenuineLiveLatch, unknown.Display.Source);
        Equal(initial.PersistentState.LatchedWeeklyResetAtUtc,
            unknown.PersistentState.LatchedWeeklyResetAtUtc);
        Equal(null, unknown.Display.RemainingPercent);
    }

    private static void FreshNewWindowRearmsLatch()
    {
        var initial = Evaluate(EnforcingSettings(), 25m);
        var next = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings(),
            initial.PersistentState,
            Available(90m, BaseTime.AddDays(8), BaseTime.AddMinutes(1)),
            BaseTime.AddMinutes(1));
        Equal(GuardRuntimeState.Normal, next.Display.Decision);
        True(next.Display.ResetDetected);
        Equal(null, next.PersistentState.LatchedWeeklyResetAtUtc);
    }

    private static void WeeklyResetTimestampJitterKeepsOneWindow()
    {
        var settings = EnforcingSettings();
        var firstReset = BaseTime.AddDays(7).AddSeconds(27);
        var first = ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            Available(95m, firstReset),
            BaseTime);
        var jittered = ConfiguredGuardEvaluator.Evaluate(
            settings,
            first.PersistentState,
            Available(95m, firstReset.AddSeconds(1), BaseTime.AddMinutes(1)),
            BaseTime.AddMinutes(1));

        False(jittered.Display.ResetDetected);
        Equal(firstReset,
            jittered.PersistentState.LastSuccessfulWeeklyResetAtUtc);
    }

    private static void BackwardResetIdentityFailsClosed()
    {
        var settings = EnforcingSettings();
        var priorReset = BaseTime.AddDays(8);
        var prior = Evaluate(settings, 95m).PersistentState with
        {
            LastSuccessfulWeeklyResetAtUtc = priorReset
        };
        var result = ConfiguredGuardEvaluator.Evaluate(
            settings,
            prior,
            Available(95m, priorReset.AddDays(-1)),
            BaseTime);
        Equal(GuardRuntimeState.Unknown, result.Display.Decision);
        Equal(GuardDecisionReason.ObservationInvalid, result.Display.Reason);
        Equal(null, result.Display.RemainingPercent);
        Equal(priorReset,
            result.PersistentState.LastSuccessfulWeeklyResetAtUtc);
    }

    private static void InvalidRequiredWindowCannotPublishReset()
    {
        var settings = EnforcingSettings();
        var prior = Evaluate(settings, 95m).PersistentState with
        {
            LastSuccessfulFiveHourResetAtUtc = BaseTime.AddHours(4),
            LastSuccessfulWeeklyResetAtUtc = BaseTime.AddDays(8)
        };
        var observation = new AppServerUsageObservation(
            ObservationStatus.Available,
            95m,
            BaseTime.AddDays(7),
            BaseTime,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null,
            [
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    95m,
                    BaseTime.AddHours(10)),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    95m,
                    BaseTime.AddDays(7))
            ]);

        var result = ConfiguredGuardEvaluator.Evaluate(
            settings,
            prior,
            observation,
            BaseTime);

        Equal(GuardRuntimeState.Unknown, result.Display.Decision);
        False(result.Display.ResetDetected);
        Equal(BaseTime.AddHours(4),
            result.PersistentState.LastSuccessfulFiveHourResetAtUtc);
        Equal(BaseTime.AddDays(8),
            result.PersistentState.LastSuccessfulWeeklyResetAtUtc);
    }

    private static void ResetNotificationIsOncePerStableWindowAcrossRestart()
    {
        var settings = EnforcingSettings();
        var oldReset = BaseTime.AddHours(1);
        var old = Evaluate(settings, 95m).PersistentState with
        {
            LastSuccessfulWeeklyResetAtUtc = oldReset
        };
        var newReset = BaseTime.AddDays(7).AddSeconds(27);
        var first = ConfiguredGuardEvaluator.Evaluate(
            settings,
            old,
            Available(95m, newReset, BaseTime.AddMinutes(1)),
            BaseTime.AddMinutes(1));
        True(first.Display.ResetDetected);
        var notification = NotificationTransitionPolicy.Evaluate(
            old.Current!,
            first.Display,
            settings,
            first.PersistentState,
            BaseTime.AddMinutes(1));
        Equal(GuardNotificationKind.Reset, notification.Kind);

        var persisted = first.PersistentState with
        {
            LastNotificationKey = notification.Key,
            LastNotificationAtUtc = BaseTime.AddMinutes(1),
            NotificationLedger = new Dictionary<string, DateTimeOffset>
            {
                [notification.Key] = BaseTime.AddMinutes(1)
            }
        };
        WithTempStorage((_, storage) =>
        {
            storage.SaveState(persisted);
            var reloaded = storage.LoadState();
            Equal(StorageLoadStatus.Loaded, reloaded.Status);
            True(reloaded.State.NotificationLedger!.ContainsKey(notification.Key));

            var afterRestart = ConfiguredGuardEvaluator.Evaluate(
                settings,
                reloaded.State,
                Available(95m, newReset.AddSeconds(1), BaseTime.AddMinutes(2)),
                BaseTime.AddMinutes(2));
            False(afterRestart.Display.ResetDetected);
            Equal(newReset,
                afterRestart.PersistentState.LastSuccessfulWeeklyResetAtUtc);

            var genuinelyNew = ConfiguredGuardEvaluator.Evaluate(
                settings,
                afterRestart.PersistentState,
                Available(95m, newReset.AddDays(7), BaseTime.AddMinutes(3)),
                BaseTime.AddMinutes(3));
            True(genuinelyNew.Display.ResetDetected);
            var nextNotification = NotificationTransitionPolicy.Evaluate(
                afterRestart.Display,
                genuinelyNew.Display,
                settings,
                genuinelyNew.PersistentState,
                BaseTime.AddMinutes(3));
            Equal(GuardNotificationKind.Reset, nextNotification.Kind);
            False(nextNotification.Key == notification.Key);
        });
    }

    private static void StaleNewWindowCannotClearLatch()
    {
        var initial = Evaluate(EnforcingSettings(), 25m);
        var now = BaseTime.AddMinutes(5);
        var next = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings(),
            initial.PersistentState,
            Available(90m, BaseTime.AddDays(8), BaseTime),
            now);
        Equal(GuardRuntimeState.SafeWrap, next.Display.Decision);
        False(next.Display.ResetDetected);
        True(next.PersistentState.LatchedWeeklyResetAtUtc is not null);
    }

    private static void ClockRollbackCannotClearLatch()
    {
        var initial = Evaluate(EnforcingSettings(), 25m);
        var next = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings(),
            initial.PersistentState,
            Available(90m, BaseTime.AddDays(8), BaseTime),
            BaseTime.AddMinutes(-1));
        Equal(GuardRuntimeState.SafeWrap, next.Display.Decision);
        True(next.PersistentState.LatchedWeeklyResetAtUtc is not null);
    }

    private static void ProvenanceMismatchIsExplicit()
    {
        var evaluation = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings(),
            GuardPersistentState.Empty,
            AppServerUsageObservation.ErrorAt(
                BaseTime,
                AppServerUsageError.ExecutableNotApproved),
            BaseTime);
        Equal(GuardRuntimeState.ProvenanceMismatch, evaluation.Display.Decision);
        Equal(GuardDecisionReason.ProvenanceMismatch, evaluation.Display.Reason);
        Equal(null, evaluation.Display.RemainingPercent);
    }

    private static void LowConfidenceIsUnknown()
    {
        var observation = Available(50m) with
        {
            Confidence = ObservationConfidence.Medium
        };
        var result = ConfiguredGuardEvaluator.Evaluate(
            EnforcingSettings(),
            GuardPersistentState.Empty,
            observation,
            BaseTime);
        Equal(GuardRuntimeState.Unknown, result.Display.Decision);
        Equal(null, result.Display.RemainingPercent);
    }

    private static void SettingsRoundTripAtomically() => WithTempStorage((root, storage) =>
    {
        var settings = GuardSettings.Default with { PollingIntervalSeconds = 120 };
        storage.SaveSettings(settings);
        var loaded = storage.LoadSettings();
        Equal(StorageLoadStatus.Loaded, loaded.Status);
        Equal(settings, loaded.Settings);
        False(File.Exists(Path.Combine(root, "settings.json.new")));
    });

    private static void OverridePersistsThroughStorageRestart() => WithTempStorage((_, storage) =>
    {
        storage.SaveSettings(OverrideSettings());
        var reloaded = new GuardFileStorage(storage.RootDirectory).LoadSettings();
        True(reloaded.Settings.UnrestrictedDevelopmentOverride);
    });

    private static void LatchPersistsThroughStorageRestart() => WithTempStorage((_, storage) =>
    {
        var evaluated = Evaluate(EnforcingSettings(), 25m);
        storage.SaveState(evaluated.PersistentState);
        var reloaded = new GuardFileStorage(storage.RootDirectory).LoadState();
        Equal(evaluated.PersistentState.LatchedWeeklyResetAtUtc,
            reloaded.State.LatchedWeeklyResetAtUtc);
    });

    private static void ConcurrentStateWritersMergeNotificationMetadata() =>
        WithTempStorage((root, first) =>
        {
            var second = new GuardFileStorage(root);
            var warningKey = "Warning:Warning:one";
            var resetKey = "Reset:Normal:two";
            first.SaveState(GuardPersistentState.Empty with
            {
                LastNotificationKey = warningKey,
                LastNotificationAtUtc = BaseTime,
                NotificationLedger = new Dictionary<string, DateTimeOffset>
                {
                    [warningKey] = BaseTime
                }
            });
            second.SaveState(GuardPersistentState.Empty with
            {
                LastNotificationKey = resetKey,
                LastNotificationAtUtc = BaseTime.AddMinutes(1),
                NotificationLedger = new Dictionary<string, DateTimeOffset>
                {
                    [resetKey] = BaseTime.AddMinutes(1)
                }
            });

            var loaded = first.LoadState();
            Equal(StorageLoadStatus.Loaded, loaded.Status);
            True(loaded.State.NotificationLedger!.ContainsKey(warningKey));
            True(loaded.State.NotificationLedger.ContainsKey(resetKey));
            Equal(resetKey, loaded.State.LastNotificationKey);
        });

    private static void CorruptSettingsFailClosed() => WithTempStorage((root, storage) =>
    {
        File.WriteAllText(Path.Combine(root, "settings.json"), "not-json");
        Equal(StorageLoadStatus.Corrupt, storage.LoadSettings().Status);
    });

    private static void PartialStateFailsClosed() => WithTempStorage((root, storage) =>
    {
        File.WriteAllText(Path.Combine(root, "state.json"), "{\"schemaVersion\":1");
        Equal(StorageLoadStatus.Corrupt, storage.LoadState().Status);
    });

    private static void FutureSettingsFailClosed() => WithTempStorage((root, storage) =>
    {
        var json = SanitizedJson.Serialize(GuardSettings.Default with { SchemaVersion = 2 });
        File.WriteAllText(Path.Combine(root, "settings.json"), json);
        Equal(StorageLoadStatus.UnsupportedVersion, storage.LoadSettings().Status);
    });

    private static void FutureStateFailsClosed() => WithTempStorage((root, storage) =>
    {
        var json = SanitizedJson.Serialize(GuardPersistentState.Empty with { SchemaVersion = 2 });
        File.WriteAllText(Path.Combine(root, "state.json"), json);
        Equal(StorageLoadStatus.UnsupportedVersion, storage.LoadState().Status);
    });

    private static void UnknownFieldsFailClosed() => WithTempStorage((root, storage) =>
    {
        var json = SanitizedJson.Serialize(GuardSettings.Default);
        json = json[..^1] + ",\"rawProtocol\":\"forbidden\"}";
        File.WriteAllText(Path.Combine(root, "settings.json"), json);
        Equal(StorageLoadStatus.Corrupt, storage.LoadSettings().Status);
    });

    private static void NotificationIsDeduplicated()
    {
        var settings = EnforcingSettings();
        var previous = Evaluate(settings, 50m).Display;
        var current = Evaluate(settings, 30m).Display;
        var first = NotificationTransitionPolicy.Evaluate(
            previous, current, settings, GuardPersistentState.Empty, BaseTime);
        Equal(GuardNotificationKind.Warning, first.Kind);
        var state = GuardPersistentState.Empty with
        {
            LastNotificationKey = first.Key,
            LastNotificationAtUtc = BaseTime
        };
        var duplicate = NotificationTransitionPolicy.Evaluate(
            previous, current, settings, state, BaseTime.AddMinutes(1));
        Equal(GuardNotificationKind.None, duplicate.Kind);
    }

    private static void NotificationCooldownPermitsLaterReminder()
    {
        var settings = EnforcingSettings();
        var previous = Evaluate(settings, 50m).Display;
        var current = Evaluate(settings, 30m).Display;
        var first = NotificationTransitionPolicy.Evaluate(
            previous, current, settings, GuardPersistentState.Empty, BaseTime);
        var state = GuardPersistentState.Empty with
        {
            LastNotificationKey = first.Key,
            LastNotificationAtUtc = BaseTime
        };
        var later = NotificationTransitionPolicy.Evaluate(
            previous,
            current,
            settings,
            state,
            BaseTime + NotificationTransitionPolicy.RepeatCooldown + TimeSpan.FromSeconds(1));
        Equal(GuardNotificationKind.Warning, later.Kind);
    }

    private static void ResetNotificationIsDistinct()
    {
        var settings = EnforcingSettings();
        var previous = Evaluate(settings, 25m).Display;
        var current = Evaluate(settings, 90m).Display with { ResetDetected = true };
        var result = NotificationTransitionPolicy.Evaluate(
            previous, current, settings, GuardPersistentState.Empty, BaseTime);
        Equal(GuardNotificationKind.Reset, result.Kind);
    }

    private static void IdenticalTransitionIsDeduplicatedAfterRestart()
    {
        var settings = EnforcingSettings();
        var previous = ConfiguredGuardEvaluator.UnknownAt(
            settings,
            BaseTime,
            GuardDecisionReason.ObservationUnknown,
            null);
        var current = Evaluate(settings, 95m).Display;
        var first = NotificationTransitionPolicy.Evaluate(
            previous,
            current,
            settings,
            GuardPersistentState.Empty,
            BaseTime);
        Equal(GuardNotificationKind.Recovery, first.Kind);
        var persisted = GuardPersistentState.Empty with
        {
            NotificationLedger = new Dictionary<string, DateTimeOffset>
            {
                [first.Key] = BaseTime
            }
        };
        var duplicate = NotificationTransitionPolicy.Evaluate(
            previous,
            current,
            settings,
            persisted,
            BaseTime.AddMinutes(1));
        Equal(GuardNotificationKind.None, duplicate.Kind);
    }

    private static void StartupRegistrationIsExactAndReversible()
    {
        var values = new FakeStartupValues();
        var registration = new StartupRegistration(values, @"C:\Tools\Guard.exe");
        False(registration.IsEnabled());
        registration.SetEnabled(true);
        True(registration.IsEnabled());
        Equal("\"C:\\Tools\\Guard.exe\" --background",
            values.Read(StartupRegistration.ValueName));
        registration.SetEnabled(false);
        False(registration.IsEnabled());
    }

    private static void StartupForeignCommandIsDisabled()
    {
        var values = new FakeStartupValues();
        values.Write(StartupRegistration.ValueName, "foreign.exe");
        var registration = new StartupRegistration(values, @"C:\Tools\Guard.exe");
        False(registration.IsEnabled());
    }

    private static void WindowsStartupRoundTripPreservesPriorState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsRunStartupValueStore();
        var prior = store.Read(StartupRegistration.ValueName);
        var priorLegacy = store.Read(StartupRegistration.LegacyValueName);
        var registration = new StartupRegistration(
            store,
            @"C:\Users\synthetic\.codex\tools\codex-usage-guard\CodexUsageGuard.exe");
        try
        {
            registration.SetEnabled(true);
            True(registration.IsEnabled());
            registration.SetEnabled(false);
            False(registration.IsEnabled());
        }
        finally
        {
            if (prior is null)
            {
                store.Delete(StartupRegistration.ValueName);
            }
            else
            {
                store.Write(StartupRegistration.ValueName, prior);
            }
            if (priorLegacy is null)
            {
                store.Delete(StartupRegistration.LegacyValueName);
            }
            else
            {
                store.Write(StartupRegistration.LegacyValueName, priorLegacy);
            }
        }
    }

    private static void LaunchTogetherShortcutsAreFixedAndReversible()
    {
        var platform = new FakeLaunchTogetherPlatform("codex", "claude");
        var programs = Path.Combine(
            Path.GetTempPath(),
            "UsageGuard-shortcuts-" + Guid.NewGuid().ToString("N"));
        var registration = new LaunchTogetherRegistration(
            platform,
            @"C:\Tools\CodexUsageGuard.exe",
            programs);

        False(registration.IsEnabled());
        Equal(2, registration.AvailableProviders().Count);
        registration.SetEnabled(true);
        True(registration.IsEnabled());
        Equal(2, platform.Shortcuts.Count);
        True(platform.Shortcuts.Values.Any(item =>
            item.Arguments == "--launch-provider codex"));
        True(platform.Shortcuts.Values.Any(item =>
            item.Arguments == "--launch-provider claude"));
        True(platform.Shortcuts.Values.All(item =>
            item.TargetPath == @"C:\Tools\CodexUsageGuard.exe"));

        registration.SetEnabled(true);
        Equal(2, platform.Shortcuts.Count);
        registration.SetEnabled(false);
        Equal(0, platform.Shortcuts.Count);
        False(registration.IsEnabled());
    }

    private static void LaunchTogetherPreservesForeignShortcut()
    {
        var platform = new FakeLaunchTogetherPlatform("codex", "claude");
        var programs = Path.Combine(
            Path.GetTempPath(),
            "UsageGuard-shortcuts-conflict-" + Guid.NewGuid().ToString("N"));
        var claudePath = Path.Combine(
            programs,
            "Usage Guard",
            "Usage Guard + Claude.lnk");
        var foreign = new ShortcutDefinition(
            @"C:\Other\tool.exe",
            "--anything",
            "Unrelated",
            @"C:\Other\tool.exe,0");
        platform.Shortcuts[claudePath] = foreign;
        var registration = new LaunchTogetherRegistration(
            platform,
            @"C:\Tools\CodexUsageGuard.exe",
            programs);

        Throws<InvalidOperationException>(() => registration.SetEnabled(true));
        Equal(1, platform.Shortcuts.Count);
        Equal(foreign, platform.Shortcuts[claudePath]);
        registration.SetEnabled(false);
        Equal(foreign, platform.Shortcuts[claudePath]);
    }

    private static void LaunchTogetherArgumentsAreFixed()
    {
        True(DesktopAiLaunchContract.TryGetUri("codex", out var codex));
        Equal("codex:", codex);
        True(DesktopAiLaunchContract.TryGetUri("claude", out var claude));
        Equal("claude:", claude);
        False(DesktopAiLaunchContract.TryGetUri(
            "codex --anything", out var rejected));
        Equal(string.Empty, rejected);
    }

    private static void WindowsLaunchTogetherShortcutRoundTrips()
    {
        if (!OperatingSystem.IsWindows()) return;
        var programs = Path.Combine(Path.GetTempPath(),
            "UsageGuard-real-shortcut-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executable = Path.ChangeExtension(
                typeof(CodexUsageGuard.Program).Assembly.Location,
                ".exe");
            var registration = new LaunchTogetherRegistration(
                new WindowsLaunchTogetherPlatform(),
                executable,
                programs);
            if (registration.AvailableProviders().Count == 0) return;
            registration.SetEnabled(true);
            True(registration.IsEnabled());
            True(Directory.GetFiles(programs, "*.lnk", SearchOption.AllDirectories)
                .Length >= 1);
            registration.SetEnabled(false);
            False(Directory.Exists(Path.Combine(programs, "Usage Guard")));
        }
        finally
        {
            if (Directory.Exists(programs)) Directory.Delete(programs, true);
        }
    }

    private static void ShareableInstallerIsConsoleFreeAndUserScoped()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root, "installer", "UsageGuardBootstrapper.cs"));
        var builder = File.ReadAllText(Path.Combine(
            root, "scripts", "New-BootstrapperInstaller.ps1"));
        var package = File.ReadAllText(Path.Combine(
            root, "scripts", "New-Package.ps1"));
        True(source.Contains("CreateNoWindow = true", StringComparison.Ordinal));
        True(source.Contains("current Windows user", StringComparison.Ordinal));
        True(source.Contains("UsageGuard.Payload.zip", StringComparison.Ordinal));
        True(builder.Contains("/target:winexe", StringComparison.Ordinal));
        True(builder.Contains("CreateNoWindow = $true", StringComparison.Ordinal));
        True(package.Contains("New-BootstrapperInstaller.ps1", StringComparison.Ordinal));
        True(package.Contains(".sha256", StringComparison.Ordinal));
        True(source.Contains("--install-directory", StringComparison.Ordinal));
        False(source.Contains("http://", StringComparison.OrdinalIgnoreCase));
        False(source.Contains("https://", StringComparison.OrdinalIgnoreCase));
        False(source.Contains("credential", StringComparison.OrdinalIgnoreCase));
        False(builder.Contains("iexpress", StringComparison.OrdinalIgnoreCase));
        False(builder.Contains("makecab", StringComparison.OrdinalIgnoreCase));
    }

    private static void SingleInstanceSignalsPrimary()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var signaled = new ManualResetEventSlim();
        using var first = new SingleInstanceCoordinator(suffix, signaled.Set);
        using var second = new SingleInstanceCoordinator(suffix, () => { });
        True(first.IsPrimary);
        False(second.IsPrimary);
        second.SignalPrimary();
        True(signaled.Wait(TimeSpan.FromSeconds(2)));
    }

    private static void SingleInstanceSignalsShutdown()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var shutdown = new ManualResetEventSlim();
        using var first = new SingleInstanceCoordinator(
            suffix,
            () => { },
            shutdown.Set);
        using var second = new SingleInstanceCoordinator(suffix, () => { });
        True(first.IsPrimary);
        False(second.IsPrimary);
        second.SignalShutdown();
        True(shutdown.Wait(TimeSpan.FromSeconds(2)));
    }

    private static void EarlyDesktopRequestsAreDeferred()
    {
        var router = new DesktopRequestRouter();
        var shown = 0;
        var shutdown = 0;

        router.RequestShow();
        router.RequestShutdown();
        router.Attach(() => shown++, () => shutdown++);
        Equal(0, shown);
        Equal(1, shutdown);

        router.RequestShow();
        Equal(1, shown);
        router.Detach();
        router.RequestShow();
        router.Attach(() => shown++, () => shutdown++);
        Equal(2, shown);
        Equal(1, shutdown);
    }

    private static void DesktopRequestsUseStableUiDispatcher()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CodexUsageGuard",
            "Program.cs"));
        True(program.Contains("desktopRequestTimer", StringComparison.Ordinal));
        True(program.Contains("Interval = 100", StringComparison.Ordinal));
        True(program.Contains("TryConsumeShutdownSignal()", StringComparison.Ordinal));
        True(program.Contains("TryConsumeShowSignal()", StringComparison.Ordinal));
        True(program.Contains("requestRouter.RequestShow()", StringComparison.Ordinal));
        var mainForm = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CodexUsageGuard",
            "Windows",
            "MainForm.cs"));
        True(mainForm.Contains("Application.ExitThread()", StringComparison.Ordinal));
        False(mainForm.Contains("private async Task ExitAsync()", StringComparison.Ordinal));
        True(mainForm.Contains("_monitor.Settings.MonitoringEnabled", StringComparison.Ordinal));
    }

    private static void ShutdownRequesterWaitsForPrimaryExit()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var primaryReady = new ManualResetEventSlim();
        using var shutdownRequested = new ManualResetEventSlim();
        Exception? failure = null;
        var primaryThread = new Thread(() =>
        {
            try
            {
                using var primary = new SingleInstanceCoordinator(
                    suffix,
                    () => { },
                    shutdownRequested.Set);
                True(primary.IsPrimary);
                primaryReady.Set();
                True(shutdownRequested.Wait(TimeSpan.FromSeconds(2)));
            }
            catch (Exception exception)
            {
                failure = exception;
                primaryReady.Set();
            }
        });
        primaryThread.Start();
        True(primaryReady.Wait(TimeSpan.FromSeconds(2)));
        using var requester = new SingleInstanceCoordinator(suffix, () => { });
        False(requester.IsPrimary);
        False(requester.WaitForPrimaryExit(TimeSpan.FromMilliseconds(25)));
        requester.SignalShutdown();
        True(requester.WaitForPrimaryExit(TimeSpan.FromSeconds(2)));
        True(primaryThread.Join(TimeSpan.FromSeconds(2)));
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void MonitorCoalescesChecks()
    {
        var source = new BlockingSource();
        var monitor = NewMonitor(source);
        try
        {
            var first = monitor.CheckNowAsync();
            var second = monitor.CheckNowAsync();
            True(ReferenceEquals(first, second));
            source.Release(Available(50m));
            Equal(GuardRuntimeState.Normal,
                first.GetAwaiter().GetResult().Decision);
            Equal(1, source.CallCount);
        }
        finally
        {
            monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void MonitorCancellationStopsCleanly()
    {
        var source = new CancellingSource();
        var monitor = NewMonitor(source);
        monitor.StartMonitoring();
        True(source.Started.Wait(TimeSpan.FromSeconds(2)));
        monitor.StopMonitoringAsync().GetAwaiter().GetResult();
        True(source.Cancelled);
        False(monitor.IsMonitoring);
        monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void MonitorFailureCounterRecovers()
    {
        var source = new QueueSource(
            AppServerUsageObservation.ErrorAt(BaseTime, AppServerUsageError.ReadTimedOut),
            Available(50m));
        var monitor = NewMonitor(source);
        try
        {
            monitor.CheckNowAsync().GetAwaiter().GetResult();
            Equal(1, monitor.PersistentState.ConsecutiveFailures);
            monitor.CheckNowAsync().GetAwaiter().GetResult();
            Equal(0, monitor.PersistentState.ConsecutiveFailures);
        }
        finally
        {
            monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void MonitorReloadsExternalSettings()
    {
        var storage = new InMemoryStorage();
        storage.SetExternalSettings(OverrideSettings());
        var monitor = new UsageMonitor(
            new QueueSource(Available(95m)),
            storage,
            new ConstantClock(BaseTime));
        try
        {
            Equal(GuardRuntimeState.OverrideActive, monitor.Current.Decision);
            storage.SetExternalSettings(EnforcingSettings());
            var checkedState = monitor.CheckNowAsync().GetAwaiter().GetResult();
            False(monitor.Settings.UnrestrictedDevelopmentOverride);
            Equal(GuardRuntimeState.Normal, checkedState.Decision);
            Equal(GuardDecisionSource.LiveAppServer, checkedState.Source);
        }
        finally
        {
            monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void AppServerCancellationCleansTransport()
    {
        var transport = new CancellableTransport();
        var client = new AppServerUsageClient(
            new SingleTransportFactory(transport),
            new ConstantClock(BaseTime),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        var task = client.ObserveAsync(cancellation.Token);
        True(transport.ReadStarted.Wait(TimeSpan.FromSeconds(2)));
        cancellation.Cancel();
        var result = task.GetAwaiter().GetResult();
        Equal(AppServerUsageError.Cancelled, result.Error);
        True(transport.InputCompleted);
        True(transport.Disposed);
        False(transport.Terminated);
    }

    private static void ConfiguredOutputIsSanitized()
    {
        var output = SanitizedJson.Serialize(Evaluate(OverrideSettings(), 50m).Display);
        False(output.Contains("token", StringComparison.OrdinalIgnoreCase));
        False(output.Contains("cookie", StringComparison.OrdinalIgnoreCase));
        False(output.Contains("chat", StringComparison.OrdinalIgnoreCase));
        False(output.Contains("raw", StringComparison.OrdinalIgnoreCase));
        True(output.Contains("override_active", StringComparison.Ordinal));
    }

    private static void ProductionRejectsSimulationArguments()
    {
        var executable = Path.ChangeExtension(
            typeof(CodexUsageGuard.Program).Assembly.Location,
            ".exe");
        True(File.Exists(executable));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--simulate-remaining");
        start.ArgumentList.Add("25");
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEnd();
        var stderr = child.StandardError.ReadToEnd();
        True(child.WaitForExit(5_000));
        Equal(1, child.ExitCode);
        False(stdout.Contains("\"remainingPercent\":25", StringComparison.Ordinal));
        False(stdout.Contains("simulate", StringComparison.OrdinalIgnoreCase));
        False(stderr.Contains("simulate", StringComparison.OrdinalIgnoreCase));
    }

    private static void ProductionRejectsDesktopDiagnosticArguments()
    {
        var productionAssembly = typeof(CodexUsageGuard.Program).Assembly;
        True(productionAssembly.GetType(
            "CodexUsageGuard.Windows.SupervisedWindowStateTestMode") is null);
        True(productionAssembly.GetType(
            "CodexUsageGuard.Windows.WindowsCodexAccessibilityProbe") is null);
        var executable = Path.ChangeExtension(
            typeof(CodexUsageGuard.Program).Assembly.Location,
            ".exe");
        foreach (var argument in new[]
        {
            "--accessibility-usage",
            "--locate-bound-window",
            "--supervised-window-state-test",
            "--sandbox-layout-qa"
        })
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEnd();
            var stderr = child.StandardError.ReadToEnd();
            True(child.WaitForExit(5_000));
            Equal(1, child.ExitCode);
            False(stdout.Contains(
                "\"status\":\"available\"",
                StringComparison.Ordinal));
            Equal(string.Empty, stderr);
        }
    }

    private static void WinExeWrapperCapturesOutputAndExitCode()
    {
        var repositoryRoot = FindRepositoryRoot();
        var processHelper = Path.Combine(
            repositoryRoot,
            ".agents",
            "skills",
            "codex-usage-guard",
            "scripts",
            "invoke_guard_process.ps1");
        var testDriver = Path.Combine(
            repositoryRoot,
            "tests",
            "InvokeGuardProcessTest.ps1");
        var executable = Path.ChangeExtension(
            typeof(CodexUsageGuard.Program).Assembly.Location,
            ".exe");
        True(File.Exists(processHelper));
        True(File.Exists(testDriver));
        True(File.Exists(executable));

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            testDriver,
            "-ProcessHelper",
            processHelper,
            "-WinExe",
            executable
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEnd();
        var stderr = child.StandardError.ReadToEnd();
        True(child.WaitForExit(10_000));
        Equal(0, child.ExitCode);
        Equal(string.Empty, stderr);

        using var result = JsonDocument.Parse(stdout);
        Equal(1, result.RootElement.GetProperty("ExitCode").GetInt32());
        Equal(string.Empty,
            result.RootElement.GetProperty("StandardError").GetString());
        var captured = result.RootElement
            .GetProperty("StandardOutput")
            .GetString();
        True(!string.IsNullOrWhiteSpace(captured));
        using var sanitized = JsonDocument.Parse(captured!);
        False(sanitized.RootElement.TryGetProperty("token", out _));
        False(sanitized.RootElement.TryGetProperty("cookie", out _));
    }

    private static void SandboxWindowPolicyIsFailClosed()
    {
        var root = FindRepositoryRoot();
        var test = Path.Combine(root, "tests", "Test-SandboxWindowPolicy.ps1");
        var module = Path.Combine(
            root,
            "scripts",
            "sandbox",
            "SandboxWindowPolicy.psm1");
        var result = RunProcess(
            "powershell.exe",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            test,
            "-PolicyModule",
            module);
        Equal(0, result.ExitCode);
        Equal(string.Empty, result.StandardError);
        True(result.StandardOutput.Contains(
            "PASS Sandbox window policy tests",
            StringComparison.Ordinal));
    }

    private static void SandboxTemplateIsLockedDown()
    {
        var root = FindRepositoryRoot();
        var template = File.ReadAllText(Path.Combine(
            root,
            "sandbox",
            "UsageGuard-QA.wsb.template"));
        foreach (var element in new[]
        {
            "<VGpu>Disable</VGpu>",
            "<Networking>Disable</Networking>",
            "<AudioInput>Disable</AudioInput>",
            "<VideoInput>Disable</VideoInput>",
            "<PrinterRedirection>Disable</PrinterRedirection>",
            "<ClipboardRedirection>Disable</ClipboardRedirection>",
            "<ProtectedClient>Enable</ProtectedClient>"
        })
        {
            True(template.Contains(element, StringComparison.Ordinal));
        }
        Equal(2, CountOccurrences(template, "<MappedFolder>"));
        Equal(1, CountOccurrences(template, "<ReadOnly>true</ReadOnly>"));
        Equal(1, CountOccurrences(template, "<ReadOnly>false</ReadOnly>"));
        True(template.Contains("{{INPUT_HOST_FOLDER}}", StringComparison.Ordinal));
        True(template.Contains("{{EVIDENCE_HOST_FOLDER}}", StringComparison.Ordinal));
    }

    private static void SandboxLauncherHandlesFailureAndExit()
    {
        var root = FindRepositoryRoot();
        var launcher = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "sandbox",
            "Invoke-UsageGuardSandboxQa.ps1"));
        True(launcher.Contains(
            "The isolated guest QA failed before host-capture readiness.",
            StringComparison.Ordinal));
        True(launcher.Contains(
            "$OwnedExitDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)",
            StringComparison.Ordinal));
        True(launcher.Contains(
            "Get-Process -Id $OwnedTarget.ProcessId",
            StringComparison.Ordinal));
        Equal(1, CountOccurrences(launcher, "-p:PublishSingleFile=true"));
        True(launcher.Contains(
            "$CleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)",
            StringComparison.Ordinal));

        var guest = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "sandbox",
            "Run-GuestQa.ps1"));
        True(guest.Contains("--sandbox-core-tests", StringComparison.Ordinal));
        True(guest.Contains("$Process.Refresh()", StringComparison.Ordinal));
        True(guest.Contains("GetExitCodeProcess", StringComparison.Ordinal));
        True(guest.Contains("$ArgumentLine", StringComparison.Ordinal));

        var installer = File.ReadAllText(Path.Combine(root, "scripts", "Install-User.ps1"));
        False(installer.Contains("-Encoding utf8NoBOM", StringComparison.OrdinalIgnoreCase));
        True(installer.Contains("[Text.UTF8Encoding]::new($false)", StringComparison.Ordinal));
        var rollback = File.ReadAllText(Path.Combine(root, "scripts", "Rollback-User.ps1"));
        True(rollback.Contains(
            "[string]::IsNullOrWhiteSpace($ShortcutDirectory)",
            StringComparison.Ordinal));
        True(rollback.Contains("just-exited WinExe", StringComparison.Ordinal));
        True(guest.Contains("failureStep = $CurrentStep", StringComparison.Ordinal));
        True(guest.Contains("C:\\UsageGuardQA\\Work", StringComparison.Ordinal));
        True(guest.Contains(
            "The guest-local QA copy did not match the staged manifest.",
            StringComparison.Ordinal));
    }

    private static void InstallerRefusesNonOwnedDestination()
    {
        var root = FindRepositoryRoot();
        var installer = File.ReadAllText(Path.Combine(root, "scripts", "Install-User.ps1"));
        True(installer.Contains("Assert-OwnedInstallDirectory", StringComparison.Ordinal));
        True(installer.Contains("Refusing to replace a non-empty destination", StringComparison.Ordinal));
        True(installer.Contains("$Entry.Name -notin $AllowedAppFiles", StringComparison.Ordinal));
        True(installer.IndexOf("Assert-OwnedInstallDirectory", StringComparison.Ordinal) <
            installer.IndexOf("Stop-HelperAtPath -ExecutablePath", StringComparison.Ordinal));
    }

    private static void AppServerDiagnosticsAreSuppressed()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CodexUsageGuard",
            "AppServer",
            "ProcessAppServerTransport.cs")).Replace(
                "\r\n", "\n", StringComparison.Ordinal);
        True(source.Contains("RedirectStandardError = true", StringComparison.Ordinal));
        True(source.Contains("CopyToAsync(\n                Stream.Null", StringComparison.Ordinal));
        False(source.Contains("RedirectStandardError = false", StringComparison.Ordinal));
    }

    private static void ClaudeStatusBridgeFiltersAtBoundary()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "integrations",
            "claude",
            "claude-statusline.ps1"));
        True(source.Contains("$Filtered = [ordered]@{", StringComparison.Ordinal));
        True(source.Contains("$Process.StandardInput.Write($FilteredInput)", StringComparison.Ordinal));
        False(source.Contains("$Process.StandardInput.Write($RawInput)", StringComparison.Ordinal));
        True(source.Contains("$FilteredInput = '{}'", StringComparison.Ordinal));
        True(source.Contains(
            "distinguishes \"callback reached the helper\" from \"callback never ran\"",
            StringComparison.Ordinal));
        True(source.Contains("duplicate json property", StringComparison.Ordinal));
        False(source.Contains("@{ Name = 'used_percentage'; Count = 2 }", StringComparison.Ordinal));
    }

    private static void PowerShellEntrypointsAreWindows51Compatible()
    {
        var root = FindRepositoryRoot();
        var paths = new[]
        {
            Path.Combine(root, "integrations", "claude", "claude-statusline.ps1"),
            Path.Combine(root, ".agents", "skills", "claude-usage-guard", "scripts", "check_usage.ps1"),
            Path.Combine(root, ".agents", "skills", "codex-usage-guard", "scripts", "check_usage.ps1"),
            Path.Combine(root, "scripts", "Rollback-User.ps1")
        };
        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            False(source.Contains("IsPathFullyQualified", StringComparison.Ordinal));
            True(source.Contains("IsPathRooted", StringComparison.Ordinal));
        }
        var uiHarness = File.ReadAllText(Path.Combine(
            root, "scripts", "Inspect-UsageGuardUi.ps1"));
        False(uiHarness.Contains(") =>", StringComparison.Ordinal));
        False(uiHarness.Contains("[ushort[]]", StringComparison.Ordinal));
        False(uiHarness.Contains("-Encoding utf8NoBOM", StringComparison.Ordinal));
        True(uiHarness.Contains(
            "function Get-CurrentApprovedScreen",
            StringComparison.Ordinal));
        True(uiHarness.Contains(
            "$currentScreens = @([Windows.Forms.Screen]::AllScreens)",
            StringComparison.Ordinal));
        False(uiHarness.Contains(
            "-ArgumentList '--shutdown'",
            StringComparison.Ordinal));
        var configurator = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CodexUsageGuard",
            "Core",
            "ClaudeIntegrationConfigurator.cs"));
        // An upgrade may replace only exact embedded assets or exact hashes of
        // known prior releases. Marker text alone is not ownership proof.
        True(configurator.Contains("SHA256.HashData", StringComparison.Ordinal));
        True(configurator.Contains(
            "SupportedPreColdStartFixStatusLineSha256",
            StringComparison.Ordinal));
        False(configurator.Contains("ContainsOwnedMarker", StringComparison.Ordinal));
        True(System.Text.RegularExpressions.Regex.Matches(
            configurator, "\"[0-9A-F]{64}\"").Count >= 7);
    }

    private static void WrapperAcceptsStrictSanitizedDecision()
    {
        var decision = SanitizedJson.Serialize(Evaluate(EnforcingSettings(), 95m).Display);
        var result = RunDecisionValidator(decision, 0);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"validator exited {result.ExitCode}: {result.StandardError.Trim()}");
        }
        Equal(string.Empty, result.StandardError);
        using var parsed = JsonDocument.Parse(result.StandardOutput);
        Equal("normal", parsed.RootElement.GetProperty("decision").GetString());
        Equal(95m, parsed.RootElement.GetProperty("remainingPercent").GetDecimal());
    }

    private static void ProviderStatusOutputIsSanitized()
    {
        var executable = Path.ChangeExtension(
            typeof(CodexUsageGuard.Program).Assembly.Location,
            ".exe");
        var result = RunProcess(executable, "--provider-status");
        Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        True(document.RootElement.ValueKind == JsonValueKind.Array);
        var json = result.StandardOutput.ToLowerInvariant();
        False(json.Contains("executablepath", StringComparison.Ordinal));
        False(json.Contains("auth", StringComparison.Ordinal));
        False(json.Contains("token", StringComparison.Ordinal));
        False(json.Contains("cookie", StringComparison.Ordinal));
    }

    private static void WrapperRejectsMultipleDecisionObjects()
    {
        var decision = SanitizedJson.Serialize(Evaluate(EnforcingSettings(), 95m).Display);
        var result = RunDecisionValidator(decision + Environment.NewLine + decision, 0);
        True(result.ExitCode != 0);
        Equal(string.Empty, result.StandardOutput);
    }

    private static void WrapperRejectsNonNumericQuotaValues()
    {
        var decision = SanitizedJson.Serialize(
            ConfiguredGuardEvaluator.Evaluate(
                EnforcingSettings(),
                GuardPersistentState.Empty,
                AvailableWithWindows(95m, 94m),
                BaseTime).Display);
        var nullQuota = decision.Replace(
            "\"remainingPercent\":95",
            "\"remainingPercent\":null",
            StringComparison.Ordinal);
        var result = RunDecisionValidator(nullQuota, 0);
        True(result.ExitCode != 0);
        Equal(string.Empty, result.StandardOutput);
    }

    private static void ClaudeWrapperValidatesStrictOutput()
    {
        var configuration = ProviderCatalogSettings.DefaultClaudeCode;
        var now = BaseTime;
        var snapshot = new ClaudeUsageSnapshot(
            ClaudeUsageSnapshot.CurrentSchemaVersion,
            true,
            now,
            [
                new ProviderQuotaWindowObservation(
                    QuotaWindowKind.RollingFiveHour, 76m, now.AddHours(2), now,
                    ObservationConfidence.High,
                    ObservationFreshness.ObservedNow,
                    null),
                new ProviderQuotaWindowObservation(
                    QuotaWindowKind.Weekly, 64m, now.AddDays(4), now,
                    ObservationConfidence.High,
                    ObservationFreshness.ObservedNow,
                    null)
            ],
            null);
        var output = ClaudeGuardCheckOutput.Evaluate(configuration, snapshot, now);
        var json = SanitizedJson.Serialize(output);

        var accepted = RunClaudeDecisionValidator(json, 0);
        Equal(0, accepted.ExitCode);
        Equal(string.Empty, accepted.StandardError);
        using (var parsed = JsonDocument.Parse(accepted.StandardOutput))
        {
            Equal("normal", parsed.RootElement.GetProperty("decision").GetString());
            Equal("claude_code", parsed.RootElement.GetProperty("provider").GetString());
        }

        var unknownOutput = ClaudeGuardCheckOutput.Evaluate(
            configuration,
            ClaudeUsageSnapshot.UnavailableAt(now, "no_observation_yet"),
            now);
        var acceptedUnknown = RunClaudeDecisionValidator(
            SanitizedJson.Serialize(unknownOutput),
            2);
        Equal(0, acceptedUnknown.ExitCode);
        Equal(string.Empty, acceptedUnknown.StandardError);
        using (var unknownParsed = JsonDocument.Parse(
                   acceptedUnknown.StandardOutput))
        {
            Equal("unknown",
                unknownParsed.RootElement.GetProperty("decision").GetString());
            False(unknownParsed.RootElement.TryGetProperty(
                "controllingWindow",
                out _));
        }

        foreach (var rejected in new[]
        {
            RunClaudeDecisionValidator(json + Environment.NewLine + json, 0),
            RunClaudeDecisionValidator(json.Replace(
                "\"source\":\"claude_statusline\"",
                "\"source\":\"user_override\"",
                StringComparison.Ordinal), 0),
            RunClaudeDecisionValidator(json.Replace(
                "\"remainingPercent\":76",
                "\"remainingPercent\":true",
                StringComparison.Ordinal), 0),
            RunClaudeDecisionValidator(json, 3)
        })
        {
            True(rejected.ExitCode != 0);
            Equal(string.Empty, rejected.StandardOutput);
        }
    }

    private static ProcessResult RunClaudeDecisionValidator(
        string decision,
        int exitCode)
    {
        var repositoryRoot = FindRepositoryRoot();
        var processHelper = Path.Combine(
            repositoryRoot,
            ".agents", "skills", "claude-usage-guard", "scripts",
            "invoke_guard_process.ps1");
        var testDriver = Path.Combine(repositoryRoot, "tests", "InvokeGuardProcessTest.ps1");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive",
            "-ExecutionPolicy", "Bypass", "-File", testDriver,
            "-ProcessHelper", processHelper,
            "-WinExe", Path.ChangeExtension(
                typeof(CodexUsageGuard.Program).Assembly.Location, ".exe"),
            "-Mode", "ValidateClaude",
            "-DecisionJson", decision,
            "-DecisionExitCode", exitCode.ToString()
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEnd();
        var stderr = child.StandardError.ReadToEnd();
        True(child.WaitForExit(10_000));
        return new ProcessResult(child.ExitCode, stdout, stderr);
    }

    private static ProcessResult RunDecisionValidator(string decision, int exitCode)
    {
        var repositoryRoot = FindRepositoryRoot();
        var processHelper = Path.Combine(
            repositoryRoot,
            ".agents",
            "skills",
            "codex-usage-guard",
            "scripts",
            "invoke_guard_process.ps1");
        var testDriver = Path.Combine(
            repositoryRoot,
            "tests",
            "InvokeGuardProcessTest.ps1");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive",
            "-ExecutionPolicy", "Bypass", "-File", testDriver,
            "-ProcessHelper", processHelper,
            "-WinExe", Path.ChangeExtension(
                typeof(CodexUsageGuard.Program).Assembly.Location, ".exe"),
            "-Mode", "Validate",
            "-DecisionJson", decision,
            "-DecisionExitCode", exitCode.ToString()
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEnd();
        var stderr = child.StandardError.ReadToEnd();
        True(child.WaitForExit(10_000));
        return new ProcessResult(child.ExitCode, stdout, stderr);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private static ProcessResult RunProcess(
        string executable,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var child = Process.Start(start) ??
            throw new InvalidOperationException("test child failed to start");
        var stdout = child.StandardOutput.ReadToEnd();
        var stderr = child.StandardError.ReadToEnd();
        True(child.WaitForExit(10_000));
        return new ProcessResult(child.ExitCode, stdout, stderr);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodexUsageGuard.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("repository root was not found");
    }

    private static void ThemePalettesAreReadable()
    {
        var current = WindowsTheme.Current();
        False(current.Window == current.Text);
        False(current.Surface == current.Text);
        False(current.Error == current.Window);
    }

    private static void PopupAccessibilityContract()
    {
        Exception? failure = null;
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-accessibility-ui-" + Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var monitor = NewMonitor(new QueueSource(Available(50m)));
            try
            {
                var startup = new StartupRegistration(
                    new FakeStartupValues(),
                    @"C:\Tools\Guard.exe");
                using var form = new MainForm(
                    monitor,
                    startup,
                    startHidden: false,
                    providerStorage: new ProviderCatalogStorage(root),
                    providerDiscovery: new ThrowingProviderDiscovery());
                form.Size = form.MinimumSize;
                form.PerformLayout();
                Equal("Usage Guard v.0.003", form.Text);
                Equal(new Size(560, 620), form.MinimumSize);
                Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                True(!string.IsNullOrWhiteSpace(form.AccessibleName));
                var controls = Descendants(form).ToArray();
                var isolationExplanation = controls.OfType<Label>().Single(control =>
                    control.AccessibleName == "Provider isolation explanation");
                True(isolationExplanation.MaximumSize.Width > 0);
                True(isolationExplanation.MaximumSize.Width < form.ClientSize.Width);
                True(isolationExplanation.GetPreferredSize(
                    new Size(isolationExplanation.MaximumSize.Width, 0)).Height >
                    isolationExplanation.Font.Height);
                foreach (var expected in new[]
                {
                    "Check now",
                    "Start Monitoring",
                    "Configure AI",
                    "Settings",
                    "Check for updates",
                    "Minimize to tray",
                    "Restore defaults",
                    "Exit"
                })
                {
                    if (!controls.Any(control =>
                        control.Text.Equals(expected, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(control.AccessibleName)))
                    {
                        throw new InvalidOperationException(
                            $"accessible control missing: {expected}");
                    }
                }

                var interactive = controls.Where(control =>
                    control is Button or CheckBox or NumericUpDown).ToArray();
                True(controls.Any(control => control.Text.Equals(
                    "Critical SafeWrap threshold (% remaining)",
                    StringComparison.Ordinal)));
                True(controls.Any(control => control.Text.Equals(
                    "5-hour usage limit",
                    StringComparison.Ordinal)));
                True(controls.Any(control => control.Text.Equals(
                    "Weekly usage limit",
                    StringComparison.Ordinal)));
                True(controls.Any(control => control.Text.Contains(
                    "never instantly stops, cancels, or kills a task",
                    StringComparison.Ordinal)));
                var scrollHosts = controls.OfType<SmoothScrollPanel>().ToArray();
                True(scrollHosts.Length >= 1);
                True(scrollHosts.All(control => control.UsesNativePainting));
                True(controls.OfType<SmoothTableLayoutPanel>()
                    .All(control => control.UsesNativePainting));
                True(controls.OfType<SmoothFlowLayoutPanel>()
                    .All(control => control.UsesNativePainting));
                True(controls.OfType<SmoothTabControl>()
                    .All(control => control.UsesNativePainting));
                var noTabStop = interactive.FirstOrDefault(control => !control.TabStop);
                if (noTabStop is not null)
                {
                    throw new InvalidOperationException(
                        $"interactive control has no tab stop: {noTabStop.Text}");
                }
                var noName = interactive.FirstOrDefault(control =>
                    string.IsNullOrWhiteSpace(control.AccessibleName));
                if (noName is not null)
                {
                    throw new InvalidOperationException(
                        $"interactive control has no accessible name: {noName.Text}");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null)
        {
            throw failure;
        }
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ApplyingSettingsKeepsStableUiTree()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-apply-ui-" + Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var monitor = NewMonitor(new QueueSource(Available(80m)));
            try
            {
                using var form = new MainForm(
                    monitor,
                    new StartupRegistration(
                        new FakeStartupValues(),
                        @"C:\Tools\Guard.exe"),
                    startHidden: false,
                    showTrayIcon: false,
                    providerStorage: new ProviderCatalogStorage(root),
                    providerDiscovery: new ThrowingProviderDiscovery());
                var before = Descendants(form).ToArray();
                var apply = before.OfType<Button>().Single(button =>
                    button.Text == "Apply settings");
                RaiseButtonClick(apply);
                RaiseButtonClick(apply);
                var after = Descendants(form).ToArray();
                Equal(before.Length, after.Length);
                Equal(1, after.Count(control => control.Text == "Apply settings"));
                Equal(1, after.Count(control => control.Text == "Restore defaults"));
                Equal(1, after.Count(control => control.Text ==
                    "Unrestricted development override"));
            }
            finally
            {
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(10)));
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static void PopupDisposesOwnedTrayIcon()
    {
        Exception? failure = null;
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-dispose-ui-" + Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var monitor = NewMonitor(new QueueSource(Available(95m)));
            try
            {
                var startup = new StartupRegistration(
                    new FakeStartupValues(),
                    @"C:\Tools\Guard.exe");
                var form = new MainForm(
                    monitor,
                    startup,
                    startHidden: false,
                    showTrayIcon: false,
                    providerStorage: new ProviderCatalogStorage(root),
                    providerDiscovery: new FixedProviderDiscovery());
                form.Dispose();
                var disposedField = typeof(MainForm).GetField(
                    "_trayDisposed",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                True(disposedField is not null);
                True((bool)disposedField!.GetValue(form)!);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null)
        {
            throw failure;
        }
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void PopupCreatesProviderTabs()
    {
        Exception? failure = null;
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-provider-ui-" + Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var monitor = NewMonitor(new QueueSource(Available(95m)));
            try
            {
                var startup = new StartupRegistration(
                    new FakeStartupValues(),
                    @"C:\Tools\Guard.exe");
                var providerStorage = new ProviderCatalogStorage(root);
                providerStorage.Save(new ProviderCatalogSettings(
                    ProviderCatalogSettings.CurrentSchemaVersion,
                    [
                        ProviderCatalogSettings.DefaultCodex,
                        ProviderCatalogSettings.DefaultClaudeCode
                    ]));
                using var form = new MainForm(
                    monitor,
                    startup,
                    startHidden: false,
                    showTrayIcon: false,
                    providerStorage: providerStorage,
                    providerDiscovery: new FixedProviderDiscovery());
                var tabs = Descendants(form).OfType<TabControl>().Single();
                Equal(2, tabs.TabPages.Count);
                Equal("Codex", tabs.TabPages[0].Text);
                Equal("Claude", tabs.TabPages[1].Text);
                foreach (TabPage page in tabs.TabPages)
                {
                    var pageButtons = Descendants(page).OfType<Button>()
                        .Select(button => button.Text)
                        .ToArray();
                    True(pageButtons.Contains("Check now", StringComparer.Ordinal));
                    True(pageButtons.Contains("Start Monitoring", StringComparer.Ordinal) ||
                        pageButtons.Contains("Stop Monitoring", StringComparer.Ordinal));
                    True(pageButtons.Contains("Configure AI", StringComparer.Ordinal));
                    False(pageButtons.Contains("Settings", StringComparer.Ordinal));
                    False(pageButtons.Contains("Minimize to tray", StringComparer.Ordinal));
                    False(pageButtons.Contains("Exit", StringComparer.Ordinal));
                    var startButton = Descendants(page).OfType<Button>()
                        .SingleOrDefault(button => button.Text == "Start Monitoring");
                    if (startButton is not null)
                    {
                        var checkButton = Descendants(page).OfType<Button>().Single(
                            button => button.Text == "Check now");
                        Equal(FlatStyle.Standard, startButton.FlatStyle);
                        Equal(checkButton.BackColor, startButton.BackColor);
                    }
                }
                var applicationButtons = form.Controls.Cast<Control>()
                    .SelectMany(Descendants)
                    .OfType<Button>()
                    .Where(button => !tabs.TabPages.Cast<TabPage>()
                        .Any(page => page.Contains(button)))
                    .Select(button => button.Text)
                    .ToArray();
                True(applicationButtons.Contains("Settings", StringComparer.Ordinal));
                True(applicationButtons.Contains("Minimize to tray", StringComparer.Ordinal));
                True(applicationButtons.Contains("Exit", StringComparer.Ordinal));
                var claudeText = Descendants(tabs.TabPages[1])
                    .Select(control => control.Text)
                    .ToArray();
                True(claudeText.Any(text => text.Contains(
                    "5-hour usage limit",
                    StringComparison.Ordinal)));
                True(claudeText.Any(text => text.Contains(
                    "Weekly usage limit",
                    StringComparison.Ordinal)));
                True(claudeText.Any(text => text.Contains(
                    "official Claude Code CLI's local status-line",
                    StringComparison.Ordinal)));

                var codexToggle = Descendants(tabs.TabPages[0]).OfType<Button>()
                    .Single(button => button.Text == "Start Monitoring");
                var claudeToggle = Descendants(tabs.TabPages[1]).OfType<Button>()
                    .Single(button => button.Text == "Stop Monitoring");

                RaiseButtonClick(codexToggle);
                Application.DoEvents();
                True(monitor.IsMonitoring);
                Equal("Stop Monitoring", codexToggle.Text);
                True(providerStorage.Load().Settings.Providers.Single(item =>
                    item.ProviderId == AiProviderId.Codex).MonitoringEnabled);

                RaiseButtonClick(claudeToggle);
                Application.DoEvents();
                False(providerStorage.Load().Settings.Providers.Single(item =>
                    item.ProviderId == AiProviderId.ClaudeCode).MonitoringEnabled);
                Equal("Start Monitoring", claudeToggle.Text);
                Equal(FlatStyle.Standard, claudeToggle.FlatStyle);

                RaiseButtonClick(claudeToggle);
                Application.DoEvents();
                True(providerStorage.Load().Settings.Providers.Single(item =>
                    item.ProviderId == AiProviderId.ClaudeCode).MonitoringEnabled);
                Equal("Stop Monitoring", claudeToggle.Text);

                RaiseButtonClick(codexToggle);
                var stopDeadline = DateTime.UtcNow.AddSeconds(2);
                while ((monitor.IsMonitoring ||
                        codexToggle.Text != "Start Monitoring" ||
                        codexToggle.FlatStyle != FlatStyle.Standard) &&
                    DateTime.UtcNow < stopDeadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                False(monitor.IsMonitoring);
                Equal("Start Monitoring", codexToggle.Text);
                Equal(FlatStyle.Standard, codexToggle.FlatStyle);
                False(providerStorage.Load().Settings.Providers.Single(item =>
                    item.ProviderId == AiProviderId.Codex).MonitoringEnabled);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(15)));
        try
        {
            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ClaudeMonitoringOffSuppressesNotifications()
    {
        var warning = ClaudeOutput(29m, 90m, BaseTime.AddHours(5),
            BaseTime.AddDays(7));
        var disabled = ProviderCatalogSettings.DefaultClaudeCode with
        {
            MonitoringEnabled = false
        };
        var transition = ClaudeNotificationPolicy.Evaluate(
            warning,
            disabled,
            ClaudeMonitorState.Empty,
            BaseTime);

        Equal(GuardNotificationKind.None, transition.Kind);
        Equal("warning", transition.State.LastDecision);
        True(transition.State.NotificationLedger.Count == 0);
    }

    private static void LeftTrayClickOpensPopup()
    {
        True(TrayInteractionPolicy.OpensStatus(MouseButtons.Left));
        False(TrayInteractionPolicy.OpensStatus(MouseButtons.Right));
        False(TrayInteractionPolicy.OpensStatus(MouseButtons.Middle));
    }

    private static void LayoutQaReportsMonitoringStopped()
    {
        Exception? failure = null;
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard-layout-qa-monitoring-" + Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            var monitor = NewMonitor(new QueueSource(Available(95m)));
            try
            {
                var storage = new ProviderCatalogStorage(root);
                storage.Save(new ProviderCatalogSettings(
                    ProviderCatalogSettings.CurrentSchemaVersion,
                    [
                        ProviderCatalogSettings.DefaultCodex,
                        ProviderCatalogSettings.DefaultClaudeCode
                    ]));
                using var form = new MainForm(
                    monitor,
                    new StartupRegistration(
                        new FakeStartupValues(),
                        @"C:\Tools\Guard.exe"),
                    startHidden: false,
                    showTrayIcon: false,
                    layoutQaMode: true,
                    providerStorage: storage,
                    providerDiscovery: new FixedProviderDiscovery());
                Equal("Usage Guard QA - Codex", form.Text);
                var tabs = Descendants(form).OfType<TabControl>().Single();
                foreach (TabPage page in tabs.TabPages)
                {
                    var toggle = Descendants(page).OfType<Button>().Single(button =>
                        button.AccessibleName?.Contains(
                            "Monitoring for",
                            StringComparison.OrdinalIgnoreCase) == true);
                    Equal("Start Monitoring", toggle.Text);
                    Equal(FlatStyle.Standard, toggle.FlatStyle);
                    True(toggle.AccessibleDescription?.Contains(
                        "suppressed",
                        StringComparison.OrdinalIgnoreCase) == true);
                    RaiseButtonClick(toggle);
                    Application.DoEvents();
                    Equal("Start Monitoring", toggle.Text);
                }

                var claudeMonitoring = Descendants(tabs.TabPages[1])
                    .OfType<Label>()
                    .Single(label => label.AccessibleName ==
                        "Claude monitoring status");
                Equal("Off (QA mode)", claudeMonitoring.Text);
                True(storage.Load().Settings.Providers.All(provider =>
                    provider.MonitoringEnabled));
                False(monitor.IsMonitoring);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(5)));
        try
        {
            if (failure is not null)
            {
                throw failure;
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void LayoutQaShortcutsAreNarrow()
    {
        Equal(0, LayoutQaShortcutPolicy.ProviderIndex(Keys.Control | Keys.D1));
        Equal(1, LayoutQaShortcutPolicy.ProviderIndex(Keys.Control | Keys.D2));
        Equal(null, LayoutQaShortcutPolicy.ProviderIndex(Keys.Control | Keys.D3));
        Equal(null, LayoutQaShortcutPolicy.ProviderIndex(Keys.D1));
        True(LayoutQaShortcutPolicy.IsShutdown(
            Keys.Control | Keys.Shift | Keys.F12));
        False(LayoutQaShortcutPolicy.IsShutdown(Keys.Alt | Keys.F4));

        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CodexUsageGuard",
            "Program.cs"));
        True(program.Contains(
            "--layout-qa-single-display",
            StringComparison.Ordinal));
        True(program.Contains(
            "Screen.AllScreens.Length == 1",
            StringComparison.Ordinal));
        True(program.Contains(
            "Screen.AllScreens[0].Primary",
            StringComparison.Ordinal));
    }

    private static void MonitoringToggleIsClear()
    {
        var start = MonitoringTogglePolicy.For(isMonitoring: false);
        Equal("Start Monitoring", start.Text);
        Equal(Color.White, start.Background);
        Equal(Color.Black, start.Foreground);

        var stop = MonitoringTogglePolicy.For(isMonitoring: true);
        Equal("Stop Monitoring", stop.Text);
        True(stop.Background.R > stop.Background.G);
        True(stop.Background.R > stop.Background.B);
        Equal(Color.White, stop.Foreground);
    }

    private static void IntegrationGuidanceIsProviderSpecific()
    {
        True(UsageIntegrationInstructions.CodexSetup.Contains(
            "press Configure Codex",
            StringComparison.Ordinal));
        True(UsageIntegrationInstructions.CodexSetup.Contains(
            "creating a dated backup",
            StringComparison.Ordinal));
        True(UsageIntegrationInstructions.ClaudeSetup.Contains(
            "5-hour and weekly",
            StringComparison.Ordinal));
        True(UsageIntegrationInstructions.ClaudeSetup.Contains(
            "press Configure Claude",
            StringComparison.Ordinal));
        True(UsageIntegrationInstructions.ClaudeChatLimits.Contains(
            "does not claim to control normal chats",
            StringComparison.Ordinal));
        // Desktop cannot deliver the callback by itself, so Configure explains
        // the official CLI requirement without launching a private bundled
        // executable or turning cached session data into a timed refresh.
        True(UsageIntegrationInstructions.ClaudeTerminalSetup.Contains(
            "winget install Anthropic.ClaudeCode", StringComparison.Ordinal));
        True(File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "src", "CodexUsageGuard", "Windows",
                "InstructionsForm.cs"))
            .Contains("UseMnemonic = false", StringComparison.Ordinal));
        foreach (var expected in new[]
        {
            "PowerShell terminal",
            "Step 1.",
            "Step 5.",
            "workspace trust prompt",
            "claude --settings",
            "does not read, copy, or edit your Claude user settings",
            "does not use a timer"
        })
        {
            True(UsageIntegrationInstructions.ClaudeTerminalSetup.Contains(
                expected, StringComparison.Ordinal));
        }
        False(UsageIntegrationInstructions.ClaudeTerminalSetup.Contains(
            "Packages\\Claude_*",
            StringComparison.Ordinal));
        False(UsageIntegrationInstructions.ClaudeTerminalSetup.Contains(
            "CLAUDE_CODE_*",
            StringComparison.Ordinal));
        False(UsageIntegrationInstructions.ClaudeAgreement.Contains(
            "codex-usage-guard",
            StringComparison.OrdinalIgnoreCase));
        True(UsageIntegrationInstructions.CodexAgreement.Contains(
            "never consume a usage-reset credit automatically",
            StringComparison.OrdinalIgnoreCase));
        True(UsageIntegrationInstructions.CodexAgreement.Contains(
            "point-in-time phase-admission decision",
            StringComparison.Ordinal));
        True(UsageIntegrationInstructions.CodexAgreement.Contains(
            "Never begin a long or open-ended phase",
            StringComparison.Ordinal));
    }

    private static void EmbeddedCodexIntegrationMatchesRepository()
    {
        var root = FindRepositoryRoot();
        var skillRoot = Path.Combine(
            root,
            ".agents",
            "skills",
            "codex-usage-guard");
        foreach (var asset in EmbeddedCodexIntegration.ReadVerifiedAssets())
        {
            var source = File.ReadAllBytes(Path.Combine(skillRoot, asset.Key));
            True(source.AsSpan().SequenceEqual(asset.Value));
        }
    }

    private static void EmbeddedClaudeIntegrationMatchesRepository()
    {
        var root = FindRepositoryRoot();
        var skillRoot = Path.Combine(
            root,
            ".agents",
            "skills",
            "claude-usage-guard");
        foreach (var asset in EmbeddedClaudeIntegration.ReadVerifiedAssets())
        {
            var source = asset.Key == "claude-statusline.ps1"
                ? Path.Combine(root, "integrations", "claude", asset.Key)
                : Path.Combine(skillRoot, asset.Key);
            True(File.ReadAllBytes(source).AsSpan().SequenceEqual(asset.Value));
        }
        var configuratorSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "CodexUsageGuard",
            "Core",
            "ClaudeIntegrationConfigurator.cs"));
        True(configuratorSource.Contains(
            "SupportedPriorInvokeWrapperSha256",
            StringComparison.Ordinal));
    }

    private static void ClaudeConfigureIsNonDestructive()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-configure-" + Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(root, "data");
        try
        {
            var claudeRoot = Path.Combine(root, ".claude");
            Directory.CreateDirectory(claudeRoot);
            var instructions = Path.Combine(claudeRoot, "CLAUDE.md");
            const string originalInstructions =
                "# Existing Claude instructions\r\n\r\n- Preserve this line.\r\n";
            File.WriteAllText(instructions, originalInstructions, new UTF8Encoding(false));
            var settings = Path.Combine(claudeRoot, "settings.json");
            const string originalSettings =
                "{\"theme\":\"dark\",\"env\":{\"PRESERVE\":\"yes\"}}";
            File.WriteAllText(settings, originalSettings, new UTF8Encoding(false));
            var executable = Path.Combine(root, ".local", "bin", "claude.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, [0x4D, 0x5A]);
            var fixedTime = new DateTimeOffset(
                2026, 8, 25, 10, 11, 12, TimeSpan.Zero);
            var configurator = new ProviderInstructionConfigurator(
                root,
                () => fixedTime,
                dataRoot,
                () => executable);
            var originalSettingsHash = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settings)));

            var configured = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(
                InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                configured.Status);
            True(configured.BackupPath is not null);
            var updatedInstructions = File.ReadAllText(instructions);
            True(updatedInstructions.StartsWith(originalInstructions, StringComparison.Ordinal));
            Equal(1, CountOccurrences(updatedInstructions,
                "<!-- BEGIN USAGE GUARD CLAUDE WORKING AGREEMENT -->"));
            Equal(originalSettingsHash, Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settings))));
            Equal(0, Directory.GetFiles(
                claudeRoot, "settings.json.backup-UsageGuard-*").Length);
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                claudeRoot,
                "usage-guard",
                "claude-session-settings.json"))))
            {
                Equal("command", document.RootElement.GetProperty("statusLine")
                    .GetProperty("type").GetString());
                var command = document.RootElement.GetProperty("statusLine")
                    .GetProperty("command").GetString()!;
                True(command.Contains("powershell.exe -NoLogo", StringComparison.Ordinal));
                True(command.Contains("/.claude/usage-guard/claude-statusline.ps1",
                    StringComparison.Ordinal));
                False(command.Contains("\\.claude\\usage-guard", StringComparison.Ordinal));
            }
            foreach (var asset in EmbeddedClaudeIntegration.ReadVerifiedAssets())
            {
                var installed = asset.Key == "claude-statusline.ps1"
                    ? Path.Combine(claudeRoot, "usage-guard", asset.Key)
                    : Path.Combine(claudeRoot, "skills", "claude-usage-guard", asset.Key);
                True(File.ReadAllBytes(installed).AsSpan().SequenceEqual(asset.Value));
            }
            var catalog = new ProviderCatalogStorage(dataRoot).Load();
            Equal(ProviderCatalogLoadStatus.Loaded, catalog.Status);
            True(catalog.Settings.Providers.Any(item =>
                item.ProviderId == AiProviderId.ClaudeCode));
            var backupCount = Directory.GetFiles(
                claudeRoot, "*.backup-UsageGuard-*").Length;

            var second = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(
                InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                second.Status);
            Equal(backupCount, Directory.GetFiles(
                claudeRoot, "*.backup-UsageGuard-*").Length);
            Equal(updatedInstructions, File.ReadAllText(instructions));
            Equal(originalSettingsHash, Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(settings))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ClaudeConfigureRefusesUnsafeSetup()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "UsageGuard-claude-refuse-" + Guid.NewGuid().ToString("N"));
        try
        {
            var missing = new ProviderInstructionConfigurator(
                root,
                providerDataRoot: Path.Combine(root, "data"),
                claudeExecutableResolver: () => null)
                .Configure(InstructionProvider.ClaudeCode);
            Equal(InstructionConfigurationStatus.MissingProvider, missing.Status);
            False(Directory.Exists(Path.Combine(root, ".claude")));

            var claudeRoot = Path.Combine(root, ".claude");
            Directory.CreateDirectory(claudeRoot);
            var executable = Path.Combine(root, "claude.exe");
            File.WriteAllBytes(executable, [0x4D, 0x5A]);
            var settings = Path.Combine(claudeRoot, "settings.json");
            const string custom =
                "{\"statusLine\":{\"type\":\"command\",\"command\":\"custom-status\"}}";
            File.WriteAllText(settings, custom);
            var configurator = new ProviderInstructionConfigurator(
                root,
                providerDataRoot: Path.Combine(root, "data"),
                claudeExecutableResolver: () => executable);
            var configured = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(
                InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                configured.Status);
            Equal(custom, File.ReadAllText(settings));
            var isolated = Path.Combine(
                claudeRoot, "usage-guard", "claude-session-settings.json");
            const string unrelated = "{\"unrelated\":true}";
            File.WriteAllText(isolated, unrelated, new UTF8Encoding(false));
            var conflict = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(InstructionConfigurationStatus.ConflictingIntegration, conflict.Status);
            Equal(unrelated, File.ReadAllText(isolated));
            Equal(custom, File.ReadAllText(settings));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void CodexFallbackIsNonDestructive()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "UsageGuard-instructions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var agents = Path.Combine(root, ".codex", "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(agents)!);
            const string original = "# Existing user instructions\r\n\r\n- Keep this line.\r\n";
            File.WriteAllText(agents, original, new UTF8Encoding(false));
            var fixedTime = new DateTimeOffset(
                2026,
                8,
                25,
                9,
                10,
                11,
                TimeSpan.Zero);
            var configurator = new ProviderInstructionConfigurator(
                root,
                () => fixedTime);

            var configured = configurator.Configure(InstructionProvider.Codex);
            Equal(InstructionConfigurationStatus.Configured, configured.Status);
            True(configured.BackupPath is not null);
            Equal(original, File.ReadAllText(configured.BackupPath!));
            var embedded = EmbeddedCodexIntegration.ReadVerifiedAssets();
            foreach (var asset in embedded)
            {
                var installed = Path.Combine(
                    root,
                    ".codex",
                    "skills",
                    "codex-usage-guard",
                    asset.Key);
                True(File.ReadAllBytes(installed).AsSpan().SequenceEqual(asset.Value));
            }
            var updated = File.ReadAllText(agents);
            True(updated.StartsWith(original, StringComparison.Ordinal));
            Equal(1, CountOccurrences(
                updated,
                "<!-- BEGIN CODEX USAGE GUARD WORKING AGREEMENT -->"));
            False(File.Exists(agents + ".usage-guard-new"));

            var second = configurator.Configure(InstructionProvider.Codex);
            Equal(InstructionConfigurationStatus.AlreadyConfigured, second.Status);
            Equal(updated, File.ReadAllText(agents));
            Equal(1, Directory.GetFiles(
                Path.GetDirectoryName(agents)!,
                "AGENTS.md.backup-UsageGuard-*").Length);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CodexFallbackRefusesUnsafeSetup()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "UsageGuard-instructions-refuse-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configurator = new ProviderInstructionConfigurator(
                root,
                providerDataRoot: Path.Combine(root, "data"),
                claudeExecutableResolver: () => null);
            Directory.CreateDirectory(Path.Combine(root, ".codex"));
            File.WriteAllText(
                Path.Combine(root, ".codex", "AGENTS.override.md"),
                "# Existing override");
            var shadowed = configurator.Configure(InstructionProvider.Codex);
            Equal(InstructionConfigurationStatus.Shadowed, shadowed.Status);
            False(File.Exists(Path.Combine(root, ".codex", "AGENTS.md")));
            False(Directory.Exists(Path.Combine(
                root,
                ".codex",
                "skills",
                "codex-usage-guard")));

            File.Delete(Path.Combine(root, ".codex", "AGENTS.override.md"));
            var skill = Path.Combine(
                root,
                ".codex",
                "skills",
                "codex-usage-guard",
                "SKILL.md");
            Directory.CreateDirectory(Path.GetDirectoryName(skill)!);
            File.WriteAllText(skill, "unrelated existing skill");
            var conflict = configurator.Configure(InstructionProvider.Codex);
            Equal(InstructionConfigurationStatus.ConflictingIntegration, conflict.Status);
            Equal("unrelated existing skill", File.ReadAllText(skill));
            False(File.Exists(Path.Combine(root, ".codex", "AGENTS.md")));

            var claude = configurator.Configure(InstructionProvider.ClaudeCode);
            Equal(
                InstructionConfigurationStatus.MissingProvider,
                claude.Status);
            False(File.Exists(Path.Combine(root, ".claude", "CLAUDE.md")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void InstructionsPopupIsAccessible()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new InstructionsForm(WindowsTheme.Dark);
                Equal("Configure AI & instructions", form.Text);
                Equal(new Size(620, 520), form.MinimumSize);
                var controls = Descendants(form).ToArray();
                var tabs = controls.OfType<TabControl>().Single();
                Equal(3, tabs.TabPages.Count);
                Equal("Overview", tabs.TabPages[0].Text);
                Equal("Codex", tabs.TabPages[1].Text);
                Equal("Claude", tabs.TabPages[2].Text);
                True(controls.OfType<TextBox>().All(control => control.ReadOnly));
                foreach (var name in new[]
                {
                    "Copy Codex AGENTS.md agreement",
                    "Configure Codex",
                    "Copy Claude CLAUDE.md agreement",
                    "Configure Claude",
                    "Close instructions"
                })
                {
                    True(controls.OfType<Button>().Any(button =>
                        button.AccessibleName == name && button.TabStop));
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static int CountOccurrences(string value, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }
        return count;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RaiseButtonClick(Button button)
    {
        var method = typeof(Button).GetMethod(
            "OnClick",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        True(method is not null);
        method!.Invoke(button, [EventArgs.Empty]);
    }

    private static UsageMonitor NewMonitor(IUsageObservationSource source)
    {
        var storage = new InMemoryStorage();
        return new UsageMonitor(source, storage, new ConstantClock(BaseTime));
    }

    private static GuardEvaluation Evaluate(
        GuardSettings settings,
        decimal remaining) => ConfiguredGuardEvaluator.Evaluate(
            settings,
            GuardPersistentState.Empty,
            Available(remaining),
            BaseTime);

    private static GuardSettings EnforcingSettings() =>
        GuardSettings.Default with { UnrestrictedDevelopmentOverride = false };

    private static GuardSettings OverrideSettings() =>
        GuardSettings.Default with { UnrestrictedDevelopmentOverride = true };

    private static AppServerUsageObservation Available(
        decimal remaining,
        DateTimeOffset? reset = null,
        DateTimeOffset? observed = null) => new(
            ObservationStatus.Available,
            remaining,
            reset ?? BaseTime.AddDays(1),
            observed ?? BaseTime,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null,
            [
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    remaining,
                    reset ?? BaseTime.AddHours(4)),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    remaining,
                    reset ?? BaseTime.AddDays(1))
            ]);

    private static AppServerUsageObservation AvailableWithWindows(
        decimal fiveHourRemaining,
        decimal weeklyRemaining) => new(
            ObservationStatus.Available,
            weeklyRemaining,
            BaseTime.AddDays(1),
            BaseTime,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null,
            [
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    fiveHourRemaining,
                    BaseTime.AddHours(4)),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    weeklyRemaining,
                    BaseTime.AddDays(1))
            ]);

    private static AppServerUsageObservation AvailableWithWindowDetails(
        decimal fiveHourRemaining,
        DateTimeOffset fiveHourReset,
        decimal weeklyRemaining,
        DateTimeOffset weeklyReset,
        DateTimeOffset? observedAt = null) => new(
            ObservationStatus.Available,
            weeklyRemaining,
            weeklyReset,
            observedAt ?? BaseTime,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null,
            [
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    fiveHourRemaining,
                    fiveHourReset),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    weeklyRemaining,
                    weeklyReset)
            ]);

    private static void WithTempStorage(
        Action<string, GuardFileStorage> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            action(root, new GuardFileStorage(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"expected {expected}; actual {actual}");
        }
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("condition was false");
        }
    }

    private static void False(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("condition was true");
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"expected exception {typeof(TException).Name} was not thrown");
    }

    private sealed class ConstantClock(DateTimeOffset now) : IObservationClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class InMemoryStorage : IGuardStorage
    {
        private GuardSettings _settings = GuardSettings.Default;
        private GuardPersistentState _state = GuardPersistentState.Empty;

        public SettingsLoadResult LoadSettings() => new(
            _settings,
            StorageLoadStatus.Loaded,
            SettingsValidationError.None);

        public StateLoadResult LoadState() => new(_state, StorageLoadStatus.Loaded);

        public void SaveSettings(GuardSettings settings) => _settings = settings;

        public void SaveState(GuardPersistentState state) => _state = state;

        public void SetExternalSettings(GuardSettings settings) =>
            _settings = settings;
    }

    private sealed class FakeStartupValues : IStartupValueStore
    {
        private readonly Dictionary<string, string> _values = new();

        public string? Read(string name) =>
            _values.TryGetValue(name, out var value) ? value : null;

        public void Write(string name, string value) => _values[name] = value;

        public void Delete(string name) => _values.Remove(name);
    }

    private sealed class FakeLaunchTogetherPlatform(
        params string[] schemes) : ILaunchTogetherPlatform
    {
        private readonly HashSet<string> _schemes = new(
            schemes,
            StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ShortcutDefinition> Shortcuts { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public bool IsUriSchemeRegistered(string scheme) =>
            _schemes.Contains(scheme);

        public ShortcutDefinition? ReadShortcut(string path) =>
            Shortcuts.TryGetValue(path, out var value) ? value : null;

        public void WriteShortcut(string path, ShortcutDefinition definition) =>
            Shortcuts[path] = definition;

        public void DeleteShortcut(string path) => Shortcuts.Remove(path);
    }

    private sealed class FixedProviderDiscovery : IAiProviderDiscovery
    {
        public IReadOnlyList<ProviderDetectionResult> Detect() =>
        [
            new ProviderDetectionResult(
                AiProviderId.Codex,
                "Codex",
                true,
                ProviderUsageCapability.LiveQuotaWindows,
                ApprovedCodexCli.Version,
                "official_cli_verified"),
            new ProviderDetectionResult(
                AiProviderId.ClaudeCode,
                "Claude",
                true,
                ProviderUsageCapability.LiveQuotaWindows,
                "1.0-test",
                "official_statusline_rate_limits_supported")
        ];
    }

    private sealed class ThrowingProviderDiscovery : IAiProviderDiscovery
    {
        public IReadOnlyList<ProviderDetectionResult> Detect() =>
            throw new InvalidOperationException(
                "Provider discovery must not block form construction.");
    }

    private sealed class StaticHttpHandler(
        HttpStatusCode status,
        string content) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class AssetHttpHandler(
        IReadOnlyDictionary<string, byte[]> assets) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri is null ||
                !assets.TryGetValue(request.RequestUri.AbsoluteUri, out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class BlockingSource : IUsageObservationSource
    {
        private readonly TaskCompletionSource<AppServerUsageObservation> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task<AppServerUsageObservation> ObserveAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            cancellationToken.Register(() => _result.TrySetCanceled(cancellationToken));
            return _result.Task;
        }

        public void Release(AppServerUsageObservation observation) =>
            _result.TrySetResult(observation);
    }

    private sealed class CancellingSource : IUsageObservationSource
    {
        public ManualResetEventSlim Started { get; } = new();

        public bool Cancelled { get; private set; }

        public async Task<AppServerUsageObservation> ObserveAsync(
            CancellationToken cancellationToken = default)
        {
            Started.Set();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }

    private sealed class QueueSource(
        params AppServerUsageObservation[] observations) : IUsageObservationSource
    {
        private readonly Queue<AppServerUsageObservation> _queue = new(observations);

        public Task<AppServerUsageObservation> ObserveAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_queue.Dequeue());
        }
    }

    private sealed class SingleTransportFactory(
        IAppServerTransport transport) : IAppServerTransportFactory
    {
        public ValueTask<IAppServerTransport> StartAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(transport);
        }
    }

    private sealed class CancellableTransport : IAppServerTransport
    {
        public ManualResetEventSlim ReadStarted { get; } = new();

        public bool InputCompleted { get; private set; }

        public bool Disposed { get; private set; }

        public bool Terminated { get; private set; }

        public bool HasExited => InputCompleted;

        public ValueTask WriteLineAsync(
            string line,
            CancellationToken cancellationToken)
        {
            _ = line;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<string?> ReadLineAsync(
            CancellationToken cancellationToken)
        {
            ReadStarted.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public ValueTask CompleteInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputCompleted = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void TerminateOwnedProcess() => Terminated = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
