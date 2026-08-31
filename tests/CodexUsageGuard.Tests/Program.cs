using CodexUsageGuard.AppServer;
using CodexUsageGuard.Core;
using CodexUsageGuard.Windows;
using System.Text.Json;

namespace CodexUsageGuard.Tests;

public static class Program
{
    private static readonly DateTimeOffset BaseTime =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 &&
            args[0].Equals("--render-ui-evidence", StringComparison.Ordinal))
        {
            return UiEvidenceRenderer.Render(args[1]);
        }

        if (args.Length == 1 &&
            args[0].Equals("--configure-current-user-claude", StringComparison.Ordinal))
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(
                        "USAGE_GUARD_ALLOW_CURRENT_USER_CONFIGURE"),
                    "1",
                    StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Current-user configuration was not explicitly enabled.");
                return 65;
            }

            var configured = new ProviderInstructionConfigurator().Configure(
                InstructionProvider.ClaudeCode);
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                status = configured.Status.ToString().ToLowerInvariant(),
                backupCreated = configured.BackupPath is not null
            }));
            return configured.Status is InstructionConfigurationStatus.Configured or
                InstructionConfigurationStatus.AlreadyConfigured
                ? 0
                : 2;
        }

        var sandboxCoreOnly = args.Length == 1 &&
            args[0].Equals("--sandbox-core-tests", StringComparison.Ordinal);
        if (args.Length != 0 && !sandboxCoreOnly)
        {
            Console.Error.WriteLine("Unsupported test-runner arguments.");
            return 64;
        }

        var coreTests = new (string Name, Action Run)[]
        {
            ("suffix percentage is normalized", SuffixPercentageIsNormalized),
            ("prefix decimal percentage is normalized", PrefixPercentageIsNormalized),
            ("duplicate value is not ambiguous", DuplicateValueIsNotAmbiguous),
            ("multiple distinct values fail closed", MultipleValuesFailClosed),
            ("out of range value is unavailable", OutOfRangeIsUnavailable),
            ("missing view is unavailable", MissingViewIsUnavailable),
            ("multiple views fail closed", MultipleViewsFailClosed),
            ("slow observation fails closed", SlowObservationFailsClosed),
            ("provider exception is sanitized", ProviderExceptionIsSanitized),
            ("raw accessible names never reach JSON", RawNamesNeverReachJson),
            ("Codex package identities are narrowly accepted", PackageIdentityIsNarrow),
            ("equivalent marker captures are de-duplicated", EquivalentMarkersAreDeduplicated),
            ("nearest equivalent scope wins for one anchor", NearestEquivalentScopeWins),
            ("distinct duplicate weekly labels fail closed", DistinctWeeklyLabelsFailClosed),
            ("multiple legitimate quota windows fail closed", DistinctQuotaWindowsFailClosed),
            ("window states restore after successful reads", WindowStatesRestoreAfterSuccess),
            ("window states restore after observation errors", WindowStatesRestoreAfterErrors),
            ("transition failures remain unavailable", TransitionFailuresRemainUnavailable),
            ("restoration failure is reported", RestorationFailureIsReported),
            ("app server multi-bucket weekly window is normalized", AppServerMultiBucketIsNormalized),
            ("app server legacy view is used only when multi-bucket is absent", AppServerLegacyFallbackIsNormalized),
            ("app server missing weekly window fails closed", AppServerMissingWeeklyFailsClosed),
            ("app server missing five-hour window fails closed", AppServerMissingFiveHourFailsClosed),
            ("app server duplicate five-hour window fails closed", AppServerDuplicateFiveHourFailsClosed),
            ("app server duplicate weekly window fails closed", AppServerDuplicateWeeklyFailsClosed),
            ("app server conflicting weekly windows fail closed", AppServerConflictingWeeklyFailsClosed),
            ("app server invalid weekly values fail closed", AppServerInvalidWeeklyFailsClosed),
            ("app server expired reset is stale", AppServerStaleWeeklyFailsClosed),
            ("app server protocol emits only the approved requests", AppServerProtocolIsNarrow),
            ("app server request rejection is sanitized", AppServerRejectionIsSanitized),
            ("app server refuses authentication refresh", AppServerRefusesAuthenticationRefresh),
            ("app server inaccessible executable is exact", AppServerInaccessibleExecutableIsExact),
            ("app server startup timeout fails closed", AppServerStartupTimeoutFailsClosed),
            ("app server handshake timeout fails closed", AppServerHandshakeTimeoutFailsClosed),
            ("app server read timeout fails closed", AppServerReadTimeoutFailsClosed),
            ("app server shutdown timeout terminates its owned child", AppServerShutdownTimeoutFailsClosed),
            ("guard policy is normal above warning threshold", GuardPolicyNormalAboveWarning),
            ("guard policy warns at thirty percent", GuardPolicyWarnsAtThirty),
            ("guard policy warns at twenty-six percent", GuardPolicyWarnsAtTwentySix),
            ("guard policy safe-wraps at twenty-five percent", GuardPolicySafeWrapsAtTwentyFive),
            ("guard policy finishes only the checkpoint at twenty percent", GuardPolicyFinishesCheckpointAtTwenty),
            ("guard policy marks unavailable observations unknown", GuardPolicyMarksUnavailableUnknown),
            ("guard policy marks aged observations unknown", GuardPolicyMarksAgedUnknown),
            ("guard policy marks expired resets unknown", GuardPolicyMarksExpiredUnknown),
            ("guard policy marks invalid percentages unknown", GuardPolicyMarksInvalidUnknown),
            ("guard policy invalid ordering is unknown", GuardPolicyInvalidOrderingIsUnknown),
            ("guard policy supports validated custom thresholds", GuardPolicySupportsCustomThresholds),
            ("live guard output carries only approved provenance", LiveGuardOutputCarriesApprovedProvenance),
            ("unknown live guard output drops quota values", UnknownLiveGuardOutputDropsQuotaValues),
            ("internal safe-wrap fixture does not start phase two", InternalSafeWrapFixtureStopsBeforePhaseTwo)
        };
        var tests = sandboxCoreOnly
            ? coreTests.Where(test => !test.Name.Equals(
                "app server protocol emits only the approved requests",
                StringComparison.Ordinal)).ToArray()
            : coreTests.Concat(ProductionTests.All()).ToArray();

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                test.Run();
            }
            catch (Exception exception)
            {
                failures.Add($"{test.Name}: {exception.Message}");
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine($"PASS {tests.Length} synthetic tests");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(sandboxCoreOnly
                ? $"FAIL {failure.Split(':', 2)[0]}"
                : $"FAIL {failure}");
        }

        return 1;
    }

    private static void SuffixPercentageIsNormalized()
    {
        var result = Observe(View("Weekly usage", "72% remaining"));
        Equal(ObservationStatus.Available, result.Status);
        Equal(72m, result.RemainingPercent);
        Equal(ObservationConfidence.Medium, result.Confidence);
        Equal(ObservationFreshness.ObservedNow, result.Freshness);
        Equal(null, result.Error);
    }

    private static void PrefixPercentageIsNormalized()
    {
        var result = Observe(View("remaining: 12.5%"));
        Equal(12.5m, result.RemainingPercent);
    }

    private static void DuplicateValueIsNotAmbiguous()
    {
        var result = Observe(View("75% left", "75% remaining"));
        Equal(ObservationStatus.Available, result.Status);
        Equal(75m, result.RemainingPercent);
    }

    private static void MultipleValuesFailClosed()
    {
        var result = Observe(View("88% left", "Weekly: 41% remaining"));
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.AmbiguousRemainingPercentage, result.Error);
        Equal(null, result.RemainingPercent);
    }

    private static void OutOfRangeIsUnavailable()
    {
        var result = Observe(View("101% remaining"));
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.RemainingPercentageNotFound, result.Error);
    }

    private static void MissingViewIsUnavailable()
    {
        var result = Observe(AccessibilityProbeResult.WithoutViews(
            AccessibilityProbeState.WeeklyUsageLabelNotVisible));
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.WeeklyUsageLabelNotVisible, result.Error);
    }

    private static void MultipleViewsFailClosed()
    {
        var result = Observe(AccessibilityProbeResult.WithViews(
            new[] { Snapshot("80% left"), Snapshot("80% left") }));
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.AmbiguousWeeklyUsageStructure, result.Error);
    }

    private static void SlowObservationFailsClosed()
    {
        var result = Observe(
            View("90% left"),
            new SequenceClock(BaseTime, BaseTime.AddSeconds(6)));
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.ObservationTooSlow, result.Error);
        Equal(ObservationFreshness.Unknown, result.Freshness);
    }

    private static void ProviderExceptionIsSanitized()
    {
        const string sensitiveText = "synthetic private accessible content";
        var service = new UsageObservationService(
            new ThrowingProbe(sensitiveText),
            new SequenceClock(BaseTime, BaseTime.AddMilliseconds(1)));
        var result = service.Observe();
        var json = result.ToSanitizedJson();

        Equal(ObservationStatus.Error, result.Status);
        Equal(ObservationError.AccessibilityReadFailed, result.Error);
        False(json.Contains(sensitiveText, StringComparison.Ordinal));
    }

    private static void RawNamesNeverReachJson()
    {
        const string rawText = "synthetic account label that must not leak";
        var result = Observe(View(rawText, "67% left"));
        var json = result.ToSanitizedJson();

        Equal(ObservationStatus.Available, result.Status);
        False(json.Contains(rawText, StringComparison.Ordinal));
        False(json.Contains("left", StringComparison.OrdinalIgnoreCase));
    }

    private static void PackageIdentityIsNarrow()
    {
        var family = CodexDesktopIdentity.ExpectedPackageFamilyName;
        True(CodexDesktopIdentity.IsExpected("ChatGPT", family));
        True(CodexDesktopIdentity.IsExpected("codex", family));
        False(CodexDesktopIdentity.IsExpected("ChatGPT", "Other.App_publisher"));
        False(CodexDesktopIdentity.IsExpected("chrome", family));
    }

    private static void EquivalentMarkersAreDeduplicated()
    {
        var selected = UsageViewCandidateSelector.SelectMostSpecific(new[]
        {
            Candidate(101, "anchor-a", "weekly-row", "61% left"),
            Candidate(202, "anchor-a", "weekly-row", "61% remaining")
        });

        Equal(1, selected.Count);
        var result = Observe(AccessibilityProbeResult.WithViews(
            selected.Select(candidate => candidate.Snapshot).ToArray()));
        Equal(ObservationStatus.Available, result.Status);
        Equal(61m, result.RemainingPercent);
    }

    private static void NearestEquivalentScopeWins()
    {
        var outer = Candidate(101, "anchor-a", "page", "61% left");
        var inner = Candidate(
            101,
            "anchor-a",
            "weekly-row",
            new HashSet<string>(StringComparer.Ordinal) { "page" },
            "61% left");
        var selected = UsageViewCandidateSelector.SelectMostSpecific(
            new[] { outer, inner });

        Equal(1, selected.Count);
        Equal("weekly-row", selected[0].ScopeIdentity);
    }

    private static void DistinctWeeklyLabelsFailClosed()
    {
        var selected = UsageViewCandidateSelector.SelectMostSpecific(new[]
        {
            Candidate(101, "anchor-a", "weekly-row", "61% left"),
            Candidate(101, "anchor-b", "weekly-row", "61% left")
        });
        var result = Observe(AccessibilityProbeResult.WithViews(
            selected.Select(candidate => candidate.Snapshot).ToArray()));

        Equal(2, selected.Count);
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.AmbiguousWeeklyUsageStructure, result.Error);
    }

    private static void DistinctQuotaWindowsFailClosed()
    {
        var selected = UsageViewCandidateSelector.SelectMostSpecific(new[]
        {
            Candidate(101, "anchor-a", "weekly-row-a", "61% left"),
            Candidate(202, "anchor-b", "weekly-row-b", "42% left")
        });
        var result = Observe(AccessibilityProbeResult.WithViews(
            selected.Select(candidate => candidate.Snapshot).ToArray()));

        Equal(2, selected.Count);
        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(ObservationError.AmbiguousWeeklyUsageStructure, result.Error);
    }

    private static void WindowStatesRestoreAfterSuccess()
    {
        var controller = new FakeWindowController(
            originalForeground: 202,
            codexWindowHandle: 101);
        var runner = new SupervisedWindowStateTestRunner(
            controller,
            new FakeBoundObserver(),
            new ConstantClock(BaseTime));

        var report = runner.Run(101);

        Equal(ObservationStatus.Available, report.Focused.Status);
        Equal(ObservationStatus.Available, report.Unfocused.Status);
        Equal(ObservationStatus.Available, report.Minimized.Status);
        True(report.DistinctOriginalForegroundAvailable);
        Equal(WindowRestorationStatus.Restored, report.Restoration);
        Equal(202L, controller.ForegroundWindow);
        Equal(WindowShowState.Normal, controller.CodexShowState);
    }

    private static void WindowStatesRestoreAfterErrors()
    {
        var controller = new FakeWindowController(202, 101);
        var runner = new SupervisedWindowStateTestRunner(
            controller,
            new ThrowingBoundObserver(),
            new ConstantClock(BaseTime));

        var report = runner.Run(101);

        Equal(ObservationStatus.Error, report.Focused.Status);
        Equal(ObservationError.TestChildFailed, report.Focused.Error);
        Equal(WindowRestorationStatus.Restored, report.Restoration);
        Equal(202L, controller.ForegroundWindow);
        Equal(WindowShowState.Normal, controller.CodexShowState);
    }

    private static void TransitionFailuresRemainUnavailable()
    {
        var controller = new FakeWindowController(202, 101)
        {
            FailMinimize = true
        };
        var runner = new SupervisedWindowStateTestRunner(
            controller,
            new FakeBoundObserver(),
            new ConstantClock(BaseTime));

        var report = runner.Run(101);

        Equal(ObservationStatus.Unavailable, report.Minimized.Status);
        Equal(
            ObservationError.WindowStateTransitionFailed,
            report.Minimized.Error);
        Equal(WindowRestorationStatus.Restored, report.Restoration);
    }

    private static void RestorationFailureIsReported()
    {
        var controller = new FakeWindowController(202, 101)
        {
            FailRestore = true
        };
        var runner = new SupervisedWindowStateTestRunner(
            controller,
            new FakeBoundObserver(),
            new ConstantClock(BaseTime));

        var report = runner.Run(101);

        Equal(WindowRestorationStatus.Failed, report.Restoration);
    }

    private static void AppServerMultiBucketIsNormalized()
    {
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"codex\":{" +
            "\"primary\":" + Window(12m, 300, BaseTime.AddHours(4)) + "," +
            "\"secondary\":" + Window(35m, 10_080, BaseTime.AddDays(2)) +
            "}}," +
            "\"rateLimits\":{" +
            "\"primary\":" + Window(99m, 10_080, BaseTime.AddDays(2)) +
            "}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Available, result.Status);
        Equal(65m, result.RemainingPercent);
        Equal(BaseTime.AddDays(2), result.ResetsAtUtc);
        Equal(2, result.Windows!.Count);
        Equal(BaseTime.AddHours(4), result.Windows.Single(item =>
            item.Kind == AppServerQuotaWindowKind.FiveHour).ResetsAtUtc);
        Equal(BaseTime.AddDays(2), result.Windows.Single(item =>
            item.Kind == AppServerQuotaWindowKind.Weekly).ResetsAtUtc);
        Equal(ObservationConfidence.High, result.Confidence);
        Equal(ObservationFreshness.ObservedNow, result.Freshness);
    }

    private static void AppServerLegacyFallbackIsNormalized()
    {
        var response = RateResponse(
            "{\"rateLimits\":{" +
            "\"primary\":" + Window(20m, 300, BaseTime.AddHours(4)) + "," +
            "\"secondary\":" + Window(41.5m, 10_080, BaseTime.AddDays(1)) +
            "}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Available, result.Status);
        Equal(58.5m, result.RemainingPercent);
    }

    private static void AppServerMissingWeeklyFailsClosed()
    {
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"codex\":{\"primary\":" +
            Window(12m, 300, BaseTime.AddHours(1)) + "}}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.MissingWeeklyQuotaWindow, result.Error);
    }

    private static void AppServerMissingFiveHourFailsClosed()
    {
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"codex\":{\"secondary\":" +
            Window(35m, 10_080, BaseTime.AddDays(2)) + "}}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.MissingFiveHourQuotaWindow, result.Error);
    }

    private static void AppServerDuplicateFiveHourFailsClosed()
    {
        var five = Window(12m, 300, BaseTime.AddHours(4));
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"a\":{\"primary\":" + five + ",\"secondary\":" +
            Window(35m, 10_080, BaseTime.AddDays(2)) + "}," +
            "\"b\":{\"primary\":" + five + "}}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.DuplicateFiveHourQuotaWindow, result.Error);
    }

    private static void AppServerDuplicateWeeklyFailsClosed()
    {
        var window = Window(35m, 10_080, BaseTime.AddDays(2));
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"a\":{\"primary\":" + Window(10m, 300, BaseTime.AddHours(4)) +
            ",\"secondary\":" + window + "}," +
            "\"b\":{\"secondary\":" + window + "}}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.DuplicateWeeklyQuotaWindow, result.Error);
    }

    private static void AppServerConflictingWeeklyFailsClosed()
    {
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"a\":{\"primary\":" + Window(10m, 300, BaseTime.AddHours(4)) +
            ",\"secondary\":" + Window(35m, 10_080, BaseTime.AddDays(2)) + "}," +
            "\"b\":{\"secondary\":" +
            Window(36m, 10_080, BaseTime.AddDays(2)) + "}}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.ConflictingWeeklyQuotaWindow, result.Error);
    }

    private static void AppServerInvalidWeeklyFailsClosed()
    {
        var response = RateResponse(
            "{\"rateLimitsByLimitId\":{" +
            "\"codex\":{\"primary\":" + Window(10m, 300, BaseTime.AddHours(4)) +
            ",\"secondary\":" + Window(101m, 10_080, BaseTime.AddDays(2)) + "}}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.InvalidWeeklyQuotaWindow, result.Error);
    }

    private static void AppServerStaleWeeklyFailsClosed()
    {
        var response = RateResponse(
            "{\"rateLimits\":{" +
            "\"primary\":" + Window(10m, 300, BaseTime.AddHours(4)) + "," +
            "\"secondary\":" + Window(35m, 10_080, BaseTime) + "}}");

        var result = AppServerRateLimitParser.Parse(response, BaseTime);

        Equal(ObservationStatus.Unavailable, result.Status);
        Equal(AppServerUsageError.StaleWeeklyQuotaWindow, result.Error);
        Equal(ObservationFreshness.Unknown, result.Freshness);
    }

    private static void AppServerProtocolIsNarrow()
    {
        var transport = new ScriptedTransport(
            "{\"id\":1,\"result\":{}}",
            "{\"method\":\"unrelated/notification\",\"params\":{}}",
            RateResponse(
                "{\"rateLimits\":{" +
                "\"primary\":" + Window(10m, 300, BaseTime.AddHours(4)) + "," +
                "\"secondary\":" +
                Window(35m, 10_080, BaseTime.AddDays(2)) + "}}"));
        var result = ObserveAppServer(transport);

        Equal(ObservationStatus.Available, result.Status);
        Equal(3, transport.Writes.Count);
        Equal(AppServerProtocol.InitializeRequest, transport.Writes[0]);
        Equal(AppServerProtocol.InitializedNotification, transport.Writes[1]);
        Equal(AppServerProtocol.RateLimitsRequest, transport.Writes[2]);
        True(transport.InputCompleted);
    }

    private static void AppServerRejectionIsSanitized()
    {
        const string sensitiveText = "synthetic secret account detail";
        var transport = new ScriptedTransport(
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"error\":{" +
            "\"code\":-1,\"message\":\"" + sensitiveText + "\"}}");
        var result = ObserveAppServer(transport);
        var json = result.ToSanitizedJson();

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.RateLimitsRequestRejected, result.Error);
        False(json.Contains(sensitiveText, StringComparison.Ordinal));
    }

    private static void AppServerRefusesAuthenticationRefresh()
    {
        var transport = new ScriptedTransport(
            "{\"id\":1,\"result\":{}}",
            "{\"id\":91,\"method\":\"account/chatgptAuthTokens/refresh\"}");
        var result = ObserveAppServer(transport);

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.AuthenticationRefreshRequested, result.Error);
        Equal(3, transport.Writes.Count);
    }

    private static void AppServerStartupTimeoutFailsClosed()
    {
        var transport = new ScriptedTransport();
        var client = new AppServerUsageClient(
            new FakeTransportFactory(transport, TimeSpan.FromMilliseconds(20)),
            new ConstantClock(BaseTime),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50));

        var result = client.ObserveAsync().GetAwaiter().GetResult();

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.StartupTimedOut, result.Error);
    }

    private static void AppServerInaccessibleExecutableIsExact()
    {
        var client = new AppServerUsageClient(
            new FailingTransportFactory(
                AppServerUsageError.ExecutableInaccessible),
            new ConstantClock(BaseTime));

        var result = client.ObserveAsync().GetAwaiter().GetResult();

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.ExecutableInaccessible, result.Error);
    }

    private static void AppServerHandshakeTimeoutFailsClosed()
    {
        var transport = new ScriptedTransport(ScriptedTransport.TimeoutMarker);
        var client = new AppServerUsageClient(
            new FakeTransportFactory(transport),
            new ConstantClock(BaseTime),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(50));

        var result = client.ObserveAsync().GetAwaiter().GetResult();

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.StartupTimedOut, result.Error);
    }

    private static void AppServerReadTimeoutFailsClosed()
    {
        var transport = new ScriptedTransport(
            "{\"id\":1,\"result\":{}}",
            ScriptedTransport.TimeoutMarker);
        var result = ObserveAppServer(
            transport,
            readTimeout: TimeSpan.FromMilliseconds(10));

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.ReadTimedOut, result.Error);
    }

    private static void AppServerShutdownTimeoutFailsClosed()
    {
        var transport = new ScriptedTransport(
            "{\"id\":1,\"result\":{}}",
            RateResponse(
                "{\"rateLimits\":{" +
                "\"secondary\":" +
                Window(35m, 10_080, BaseTime.AddDays(2)) + "}}"))
        {
            HangOnShutdown = true
        };
        var result = ObserveAppServer(
            transport,
            shutdownTimeout: TimeSpan.FromMilliseconds(10));

        Equal(ObservationStatus.Error, result.Status);
        Equal(AppServerUsageError.ShutdownTimedOut, result.Error);
        True(transport.Terminated);
    }

    private static void GuardPolicyNormalAboveWarning()
    {
        var decision = EvaluatePolicy(31m);

        Equal(GuardPolicyClassification.Normal, decision.Classification);
        Equal(GuardPolicyReason.AboveWarningThreshold, decision.Reason);
        True(decision.StartNewPhaseAllowed);
        False(decision.FinishCurrentCheckpointOnly);
    }

    private static void GuardPolicyWarnsAtThirty()
    {
        var decision = EvaluatePolicy(30m);

        Equal(GuardPolicyClassification.Warning, decision.Classification);
        Equal(GuardPolicyReason.WarningThresholdReached, decision.Reason);
        True(decision.StartNewPhaseAllowed);
    }

    private static void GuardPolicyWarnsAtTwentySix()
    {
        var decision = EvaluatePolicy(26m);

        Equal(GuardPolicyClassification.Warning, decision.Classification);
        Equal(GuardPolicyReason.WarningThresholdReached, decision.Reason);
        True(decision.StartNewPhaseAllowed);
    }

    private static void GuardPolicySafeWrapsAtTwentyFive()
    {
        var decision = EvaluatePolicy(25m);

        Equal(GuardPolicyClassification.SafeWrap, decision.Classification);
        Equal(GuardPolicyReason.SafeWrapThresholdReached, decision.Reason);
        False(decision.StartNewPhaseAllowed);
        True(decision.FinishCurrentCheckpointOnly);
    }

    private static void GuardPolicyFinishesCheckpointAtTwenty()
    {
        var decision = EvaluatePolicy(20m);

        Equal(GuardPolicyClassification.SafeWrap, decision.Classification);
        Equal(
            GuardPolicyReason.FinishCurrentCheckpointThresholdReached,
            decision.Reason);
        False(decision.StartNewPhaseAllowed);
        True(decision.FinishCurrentCheckpointOnly);
    }

    private static void GuardPolicyMarksUnavailableUnknown()
    {
        var decision = GuardPolicyEvaluator.Evaluate(
            AppServerUsageObservation.UnavailableAt(
                BaseTime,
                AppServerUsageError.MissingWeeklyQuotaWindow),
            BaseTime);

        Equal(GuardPolicyClassification.Unknown, decision.Classification);
        Equal(GuardPolicyReason.ObservationUnknown, decision.Reason);
        False(decision.StartNewPhaseAllowed);
        True(decision.FinishCurrentCheckpointOnly);
    }

    private static void GuardPolicyMarksAgedUnknown()
    {
        var decision = GuardPolicyEvaluator.Evaluate(
            AvailableAppServerObservation(50m),
            BaseTime.AddMinutes(2).AddTicks(1));

        Equal(GuardPolicyClassification.Unknown, decision.Classification);
        Equal(GuardPolicyReason.ObservationStale, decision.Reason);
    }

    private static void GuardPolicyMarksExpiredUnknown()
    {
        var decision = GuardPolicyEvaluator.Evaluate(
            AvailableAppServerObservation(
                50m,
                resetsAtUtc: BaseTime.AddSeconds(1)),
            BaseTime.AddSeconds(1));

        Equal(GuardPolicyClassification.Unknown, decision.Classification);
        Equal(GuardPolicyReason.ObservationStale, decision.Reason);
    }

    private static void GuardPolicyMarksInvalidUnknown()
    {
        var decision = EvaluatePolicy(101m);

        Equal(GuardPolicyClassification.Unknown, decision.Classification);
        Equal(GuardPolicyReason.ObservationInvalid, decision.Reason);
    }

    private static void GuardPolicyInvalidOrderingIsUnknown()
    {
        var invalid = new GuardPolicyConfiguration(
            WarningAtOrBelowPercent: 20m,
            SafeWrapAtOrBelowPercent: 25m,
            FinishCurrentCheckpointAtOrBelowPercent: 10m,
            MaximumObservationAge: TimeSpan.FromMinutes(2));
        var decision = GuardPolicyEvaluator.Evaluate(
            AvailableAppServerObservation(50m),
            BaseTime,
            invalid);

        Equal(GuardPolicyClassification.Unknown, decision.Classification);
        Equal(GuardPolicyReason.ConfigurationInvalid, decision.Reason);
        False(decision.PolicyValid);
        False(decision.StartNewPhaseAllowed);
        True(decision.FinishCurrentCheckpointOnly);
    }

    private static void GuardPolicySupportsCustomThresholds()
    {
        var custom = new GuardPolicyConfiguration(
            WarningAtOrBelowPercent: 40m,
            SafeWrapAtOrBelowPercent: 35m,
            FinishCurrentCheckpointAtOrBelowPercent: 30m,
            MaximumObservationAge: TimeSpan.FromMinutes(5));
        var decision = GuardPolicyEvaluator.Evaluate(
            AvailableAppServerObservation(38m),
            BaseTime,
            custom);

        True(GuardPolicyEvaluator.IsValid(custom));
        Equal(GuardPolicyClassification.Warning, decision.Classification);
        Equal(GuardPolicyReason.WarningThresholdReached, decision.Reason);
    }

    private static void LiveGuardOutputCarriesApprovedProvenance()
    {
        var result = LiveGuardCheckResult.FromLiveObservation(
            AvailableAppServerObservation(25m),
            BaseTime);
        var json = result.ToSanitizedJson();

        Equal(GuardPolicyClassification.SafeWrap, result.Decision);
        Equal(25m, result.RemainingPercent);
        Equal(GuardCheckSource.LiveAppServer, result.Source);
        Equal(ApprovedCodexCli.Version, result.SourceProvenance.CodexCliVersion);
        Equal(
            ApprovedCodexCli.ExecutableSha256,
            result.SourceProvenance.ExecutableSha256);
        True(json.Contains("\"source\":\"live_app_server\"", StringComparison.Ordinal));
        False(json.Contains("simulate", StringComparison.OrdinalIgnoreCase));
    }

    private static void UnknownLiveGuardOutputDropsQuotaValues()
    {
        var result = LiveGuardCheckResult.FromLiveObservation(
            AppServerUsageObservation.UnavailableAt(
                BaseTime,
                AppServerUsageError.DuplicateWeeklyQuotaWindow),
            BaseTime);

        Equal(GuardPolicyClassification.Unknown, result.Decision);
        Equal(null, result.RemainingPercent);
        Equal(null, result.ResetsAtUtc);
        Equal(ObservationConfidence.None, result.Confidence);
        Equal(ObservationFreshness.Unknown, result.Freshness);
        Equal(GuardCheckSource.LiveAppServer, result.Source);
    }

    private static void InternalSafeWrapFixtureStopsBeforePhaseTwo()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "CodexUsageGuard.Tests",
            Guid.NewGuid().ToString("N"));
        var phaseOneCheckpoint = Path.Combine(testRoot, "phase-one.checkpoint");
        var phaseTwoMarker = Path.Combine(testRoot, "phase-two.started");
        var phaseOneObserved = false;

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(phaseOneCheckpoint, "synthetic checkpoint");
            phaseOneObserved = File.Exists(phaseOneCheckpoint);

            var decision = EvaluatePolicy(25m);
            if (decision.StartNewPhaseAllowed)
            {
                File.WriteAllText(phaseTwoMarker, "should not happen");
            }

            True(phaseOneObserved);
            Equal(GuardPolicyClassification.SafeWrap, decision.Classification);
            False(File.Exists(phaseTwoMarker));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        False(Directory.Exists(testRoot));
    }

    private static GuardPolicyDecision EvaluatePolicy(decimal remainingPercent) =>
        GuardPolicyEvaluator.Evaluate(
            AvailableAppServerObservation(remainingPercent),
            BaseTime);

    private static AppServerUsageObservation AvailableAppServerObservation(
        decimal remainingPercent,
        DateTimeOffset? resetsAtUtc = null) => new(
            ObservationStatus.Available,
            remainingPercent,
            resetsAtUtc ?? BaseTime.AddDays(1),
            BaseTime,
            ObservationConfidence.High,
            ObservationFreshness.ObservedNow,
            null,
            [
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    remainingPercent,
                    resetsAtUtc ?? BaseTime.AddHours(4)),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    remainingPercent,
                    resetsAtUtc ?? BaseTime.AddDays(1))
            ]);

    private static AppServerUsageObservation ObserveAppServer(
        ScriptedTransport transport,
        TimeSpan? readTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        var client = new AppServerUsageClient(
            new FakeTransportFactory(transport),
            new ConstantClock(BaseTime),
            TimeSpan.FromSeconds(1),
            readTimeout ?? TimeSpan.FromSeconds(1),
            shutdownTimeout ?? TimeSpan.FromSeconds(1));
        return client.ObserveAsync().GetAwaiter().GetResult();
    }

    private static string RateResponse(string result) =>
        "{\"id\":2,\"result\":" + result + "}";

    private static string Window(
        decimal usedPercent,
        long durationMinutes,
        DateTimeOffset resetsAtUtc) =>
        "{\"usedPercent\":" +
        usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"windowDurationMins\":" + durationMinutes +
        ",\"resetsAt\":" + resetsAtUtc.ToUnixTimeSeconds() + "}";

    private static UsageObservation Observe(
        AccessibilityProbeResult result,
        IObservationClock? clock = null)
    {
        var service = new UsageObservationService(
            new FakeProbe(result),
            clock ?? new SequenceClock(BaseTime, BaseTime.AddMilliseconds(1)));
        return service.Observe();
    }

    private static AccessibilityProbeResult View(params string[] names) =>
        AccessibilityProbeResult.WithViews(new[] { Snapshot(names) });

    private static UsageViewSnapshot Snapshot(params string[] names) => new(names);

    private static UsageViewCandidate Candidate(
        long sourceWindowHandle,
        string anchorIdentity,
        string scopeIdentity,
        params string[] names) => Candidate(
            sourceWindowHandle,
            anchorIdentity,
            scopeIdentity,
            new HashSet<string>(StringComparer.Ordinal),
            names);

    private static UsageViewCandidate Candidate(
        long sourceWindowHandle,
        string anchorIdentity,
        string scopeIdentity,
        IReadOnlySet<string> ancestorScopeIdentities,
        params string[] names) => new(
            sourceWindowHandle,
            anchorIdentity,
            scopeIdentity,
            ancestorScopeIdentities,
            Snapshot(names));

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"expected {expected}; actual {actual}");
        }
    }

    private static void False(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("condition was true");
        }
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("condition was false");
        }
    }

    private sealed class FakeProbe(AccessibilityProbeResult result) : IAccessibilityProbe
    {
        public AccessibilityProbeResult Capture() => result;
    }

    private sealed class ThrowingProbe(string message) : IAccessibilityProbe
    {
        public AccessibilityProbeResult Capture() =>
            throw new InvalidOperationException(message);
    }

    private sealed class SequenceClock(params DateTimeOffset[] values) : IObservationClock
    {
        private int _index;

        public DateTimeOffset UtcNow
        {
            get
            {
                var index = Math.Min(_index, values.Length - 1);
                _index++;
                return values[index];
            }
        }
    }

    private sealed class ConstantClock(DateTimeOffset value) : IObservationClock
    {
        public DateTimeOffset UtcNow => value;
    }

    private sealed class FakeTransportFactory(
        IAppServerTransport transport,
        TimeSpan? startDelay = null) : IAppServerTransportFactory
    {
        public ValueTask<IAppServerTransport> StartAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (startDelay is { } delay)
            {
                Thread.Sleep(delay);
            }

            return ValueTask.FromResult(transport);
        }
    }

    private sealed class FailingTransportFactory(
        AppServerUsageError error) : IAppServerTransportFactory
    {
        public ValueTask<IAppServerTransport> StartAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new AppServerLaunchException(error);
        }
    }

    private sealed class ScriptedTransport(params string[] reads) : IAppServerTransport
    {
        public const string TimeoutMarker = "__timeout__";
        private readonly Queue<string> _reads = new(reads);

        public List<string> Writes { get; } = new();

        public bool InputCompleted { get; private set; }

        public bool Terminated { get; private set; }

        public bool HangOnShutdown { get; init; }

        public bool HasExited => InputCompleted && !HangOnShutdown || Terminated;

        public ValueTask WriteLineAsync(
            string line,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add(line);
            return ValueTask.CompletedTask;
        }

        public async ValueTask<string?> ReadLineAsync(
            CancellationToken cancellationToken)
        {
            if (_reads.Count == 0)
            {
                return null;
            }

            var value = _reads.Dequeue();
            if (value == TimeoutMarker)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return value;
        }

        public ValueTask CompleteInputAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputCompleted = true;
            return ValueTask.CompletedTask;
        }

        public async ValueTask WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            if (HangOnShutdown)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public void TerminateOwnedProcess() => Terminated = true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBoundObserver : IBoundWindowObservationRunner
    {
        public UsageObservation Observe(long windowHandle)
        {
            _ = windowHandle;
            return new UsageObservation(
                ObservationStatus.Available,
                55m,
                BaseTime,
                ObservationConfidence.Medium,
                ObservationFreshness.ObservedNow,
                null);
        }
    }

    private sealed class ThrowingBoundObserver : IBoundWindowObservationRunner
    {
        public UsageObservation Observe(long windowHandle) =>
            throw new InvalidOperationException(
                $"synthetic observation failure for {windowHandle}");
    }

    private sealed class FakeWindowController : ISupervisedWindowController
    {
        private readonly long _originalForeground;
        private readonly long _codexWindowHandle;

        public FakeWindowController(
            long originalForeground,
            long codexWindowHandle)
        {
            _originalForeground = originalForeground;
            _codexWindowHandle = codexWindowHandle;
            ForegroundWindow = originalForeground;
        }

        public bool FailMinimize { get; init; }

        public bool FailRestore { get; init; }

        public long ForegroundWindow { get; private set; }

        public WindowShowState CodexShowState { get; private set; } =
            WindowShowState.Normal;

        public long GetForegroundWindow() => ForegroundWindow;

        public bool IsWindow(long windowHandle) =>
            windowHandle is not 0 &&
            (windowHandle == _originalForeground ||
             windowHandle == _codexWindowHandle);

        public WindowShowState GetShowState(long windowHandle) =>
            windowHandle == _codexWindowHandle
                ? CodexShowState
                : WindowShowState.Normal;

        public bool TrySetForeground(long windowHandle, TimeSpan timeout)
        {
            _ = timeout;
            if (!IsWindow(windowHandle))
            {
                return false;
            }

            ForegroundWindow = windowHandle;
            return true;
        }

        public bool TryMinimize(long windowHandle, TimeSpan timeout)
        {
            _ = timeout;
            if (FailMinimize || windowHandle != _codexWindowHandle)
            {
                return false;
            }

            CodexShowState = WindowShowState.Minimized;
            return true;
        }

        public bool TryRestoreShowState(
            long windowHandle,
            WindowShowState showState,
            TimeSpan timeout)
        {
            _ = timeout;
            if (FailRestore || windowHandle != _codexWindowHandle)
            {
                return false;
            }

            CodexShowState = showState;
            return true;
        }
    }
}
