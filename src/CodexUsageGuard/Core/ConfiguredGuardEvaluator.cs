using CodexUsageGuard.AppServer;

namespace CodexUsageGuard.Core;

public sealed record GuardEvaluation(
    SanitizedUsageState Display,
    GuardPersistentState PersistentState);

public static class ConfiguredGuardEvaluator
{
    public static GuardEvaluation Evaluate(
        GuardSettings settings,
        GuardPersistentState state,
        AppServerUsageObservation observation,
        DateTimeOffset evaluatedAtUtc)
    {
        var settingsError = GuardSettingsValidator.Validate(settings);
        var windowEvaluations = EvaluateRequiredWindows(settings, observation, evaluatedAtUtc);
        var trustworthy = settingsError == SettingsValidationError.None &&
            observation.Status == ObservationStatus.Available &&
            observation.Confidence == ObservationConfidence.High &&
            observation.Freshness == ObservationFreshness.ObservedNow &&
            windowEvaluations is { Count: 2 } &&
            windowEvaluations.All(item =>
                item.Decision.PolicyValid &&
                item.Decision.Classification != GuardPolicyClassification.Unknown);

        var controlling = trustworthy
            ? windowEvaluations!
                .OrderByDescending(item => Severity(item.Decision.Classification))
                .ThenBy(item => item.Window.Kind)
                .First()
            : null;
        var underlying = MapPolicyDecision(
            settingsError,
            observation,
            controlling?.Decision);

        var weeklyLatch = state.LatchedWeeklyResetAtUtc;
        var weeklyLatchCreated = state.LatchCreatedAtUtc;
        var fiveLatch = state.LatchedFiveHourResetAtUtc;
        var fiveLatchCreated = state.FiveHourLatchCreatedAtUtc;
        var lastWeeklyReset = state.LastSuccessfulWeeklyResetAtUtc;
        var lastFiveReset = state.LastSuccessfulFiveHourResetAtUtc;
        var resetDetected = false;

        if (trustworthy)
        {
            foreach (var item in windowEvaluations!)
            {
                var prior = item.Window.Kind == AppServerQuotaWindowKind.Weekly
                    ? lastWeeklyReset
                    : lastFiveReset;
                var stableReset = WeeklyWindowIdentity.StableKey(
                    item.Window.ResetsAtUtc,
                    prior);
                if (prior is not null &&
                    !WeeklyWindowIdentity.IsSameWindow(prior.Value, item.Window.ResetsAtUtc))
                {
                    if (item.Window.ResetsAtUtc < prior.Value)
                    {
                        trustworthy = false;
                        underlying = MappedDecision.Unknown(
                            GuardDecisionReason.ObservationInvalid);
                        break;
                    }

                    resetDetected = true;
                    if (item.Window.Kind == AppServerQuotaWindowKind.Weekly &&
                        weeklyLatch is not null &&
                        item.Window.RemainingPercent > settings.SafeWrapThresholdPercent)
                    {
                        weeklyLatch = null;
                        weeklyLatchCreated = null;
                    }
                    else if (item.Window.Kind == AppServerQuotaWindowKind.FiveHour &&
                        fiveLatch is not null &&
                        item.Window.RemainingPercent >
                            settings.FiveHourSafeWrapThresholdPercent)
                    {
                        fiveLatch = null;
                        fiveLatchCreated = null;
                    }
                }

                if (item.Decision.Classification == GuardPolicyClassification.SafeWrap)
                {
                    if (item.Window.Kind == AppServerQuotaWindowKind.Weekly)
                    {
                        if (weeklyLatch is null ||
                            !WeeklyWindowIdentity.IsSameWindow(weeklyLatch.Value, stableReset))
                        {
                            weeklyLatchCreated = null;
                        }
                        weeklyLatch = stableReset;
                        weeklyLatchCreated ??= evaluatedAtUtc;
                    }
                    else
                    {
                        if (fiveLatch is null ||
                            !WeeklyWindowIdentity.IsSameWindow(fiveLatch.Value, stableReset))
                        {
                            fiveLatchCreated = null;
                        }
                        fiveLatch = stableReset;
                        fiveLatchCreated ??= evaluatedAtUtc;
                    }
                }

                if (item.Window.Kind == AppServerQuotaWindowKind.Weekly)
                {
                    lastWeeklyReset = stableReset;
                }
                else
                {
                    lastFiveReset = stableReset;
                }
            }
        }
        if (!trustworthy)
        {
            // A reset observed in one window is not publishable when another
            // required window makes the complete observation invalid.
            resetDetected = false;
        }

        var weekly = FindWindow(observation, AppServerQuotaWindowKind.Weekly);
        var five = FindWindow(observation, AppServerQuotaWindowKind.FiveHour);
        var weeklyLatchApplies = weeklyLatch is not null &&
            (!trustworthy || weekly is null ||
             WeeklyWindowIdentity.IsSameWindow(weekly.ResetsAtUtc, weeklyLatch.Value));
        var fiveLatchApplies = fiveLatch is not null &&
            (!trustworthy || five is null ||
             WeeklyWindowIdentity.IsSameWindow(five.ResetsAtUtc, fiveLatch.Value));
        if ((weeklyLatchApplies || fiveLatchApplies) &&
            underlying.State != GuardRuntimeState.SafeWrap)
        {
            underlying = underlying with
            {
                State = GuardRuntimeState.SafeWrap,
                Reason = GuardDecisionReason.GenuineLatchActive,
                Source = trustworthy
                    ? GuardDecisionSource.LiveAppServer
                    : GuardDecisionSource.GenuineLiveLatch,
                StartNewPhaseAllowed = false,
                FinishCurrentCheckpointOnly = true
            };
            controlling = fiveLatchApplies && five is not null
                ? windowEvaluations?.SingleOrDefault(item =>
                    item.Window.Kind == AppServerQuotaWindowKind.FiveHour)
                : windowEvaluations?.SingleOrDefault(item =>
                    item.Window.Kind == AppServerQuotaWindowKind.Weekly);
        }

        if (resetDetected && trustworthy &&
            underlying.State is GuardRuntimeState.Normal or GuardRuntimeState.Warning)
        {
            underlying = underlying with
            {
                TransitionState = GuardRuntimeState.ResetDetected,
                Reason = GuardDecisionReason.NewQuotaWindowProven
            };
        }

        var canExposeQuota = trustworthy && underlying.CanExposeQuota;
        var sanitizedWindows = canExposeQuota
            ? observation.Windows!.Select(item => new SanitizedQuotaWindowState(
                item.Kind,
                item.RemainingPercent,
                item.ResetsAtUtc.ToUniversalTime(),
                GuardResumePlanner.LocalDisplay(item.ResetsAtUtc))).ToArray()
            : [];
        var controllingWindow = canExposeQuota ? controlling?.Window : null;
        var decision = settings.UnrestrictedDevelopmentOverride
            ? GuardRuntimeState.OverrideActive
            : underlying.State;
        var resumeRecommendation = BuildResumeRecommendation(
            settings,
            decision,
            underlying.State,
            evaluatedAtUtc,
            trustworthy && weeklyLatchApplies ? weekly?.ResetsAtUtc : null,
            trustworthy && fiveLatchApplies ? five?.ResetsAtUtc : null,
            weeklyLatch,
            fiveLatch,
            exactLiveResetData: trustworthy);
        var display = new SanitizedUsageState(
            decision,
            underlying.State,
            settings.UnrestrictedDevelopmentOverride
                ? GuardDecisionReason.UnrestrictedDevelopmentOverride
                : underlying.Reason,
            settings.UnrestrictedDevelopmentOverride
                ? GuardDecisionSource.UserOverride
                : underlying.Source,
            controllingWindow?.RemainingPercent,
            observation.ObservedAtUtc,
            controllingWindow?.ResetsAtUtc,
            canExposeQuota ? observation.Confidence : ObservationConfidence.None,
            canExposeQuota ? observation.Freshness : ObservationFreshness.Unknown,
            observation.Error,
            settings.UnrestrictedDevelopmentOverride || underlying.StartNewPhaseAllowed,
            !settings.UnrestrictedDevelopmentOverride && underlying.FinishCurrentCheckpointOnly,
            resetDetected,
            Provenance(),
            controllingWindow?.Kind,
            sanitizedWindows,
            resumeRecommendation,
            controllingWindow is null
                ? null
                : GuardResumePlanner.LocalDisplay(controllingWindow.ResetsAtUtc));

        var updated = state with
        {
            Current = display,
            LatchedWeeklyResetAtUtc = weeklyLatch,
            LatchCreatedAtUtc = weeklyLatchCreated,
            LatchedFiveHourResetAtUtc = fiveLatch,
            FiveHourLatchCreatedAtUtc = fiveLatchCreated,
            LastSuccessfulObservationAtUtc = trustworthy
                ? observation.ObservedAtUtc
                : state.LastSuccessfulObservationAtUtc,
            LastSuccessfulWeeklyResetAtUtc = trustworthy
                ? lastWeeklyReset
                : state.LastSuccessfulWeeklyResetAtUtc,
            LastSuccessfulFiveHourResetAtUtc = trustworthy
                ? lastFiveReset
                : state.LastSuccessfulFiveHourResetAtUtc,
            ConsecutiveFailures = trustworthy
                ? 0
                : checked(Math.Min(state.ConsecutiveFailures + 1, 30))
        };
        return new GuardEvaluation(display, updated);
    }

    public static SanitizedUsageState FromStoredState(
        GuardSettings settings,
        GuardPersistentState state,
        DateTimeOffset nowUtc)
    {
        if (state.Current is not { } current)
        {
            return UnknownAt(settings, nowUtc, GuardDecisionReason.ObservationUnknown, null);
        }

        var latched = state.LatchedWeeklyResetAtUtc is not null ||
            state.LatchedFiveHourResetAtUtc is not null;
        var underlying = latched ? GuardRuntimeState.SafeWrap : GuardRuntimeState.Unknown;
        var decision = settings.UnrestrictedDevelopmentOverride
            ? GuardRuntimeState.OverrideActive
            : underlying;
        return current with
        {
            Decision = decision,
            UnderlyingDecision = underlying,
            Reason = settings.UnrestrictedDevelopmentOverride
                ? GuardDecisionReason.UnrestrictedDevelopmentOverride
                : latched
                    ? GuardDecisionReason.GenuineLatchActive
                    : GuardDecisionReason.ObservationStale,
            Source = settings.UnrestrictedDevelopmentOverride
                ? GuardDecisionSource.UserOverride
                : latched
                    ? GuardDecisionSource.GenuineLiveLatch
                    : GuardDecisionSource.Unavailable,
            RemainingPercent = null,
            ResetsAtUtc = null,
            Confidence = ObservationConfidence.None,
            Freshness = ObservationFreshness.Unknown,
            StartNewPhaseAllowed = settings.UnrestrictedDevelopmentOverride,
            FinishCurrentCheckpointOnly = !settings.UnrestrictedDevelopmentOverride,
            ControllingWindow = null,
            Windows = [],
            ResumeRecommendation = BuildResumeRecommendation(
                settings,
                decision,
                underlying,
                nowUtc,
                state.LatchedWeeklyResetAtUtc,
                state.LatchedFiveHourResetAtUtc,
                state.LatchedWeeklyResetAtUtc,
                state.LatchedFiveHourResetAtUtc,
                exactLiveResetData: false),
            ResetsAtLocalDisplay = null
        };
    }

    public static SanitizedUsageState UnknownAt(
        GuardSettings settings,
        DateTimeOffset nowUtc,
        GuardDecisionReason reason,
        AppServerUsageError? error)
    {
        var overrideActive = settings.UnrestrictedDevelopmentOverride;
        var provenanceMismatch = reason == GuardDecisionReason.ProvenanceMismatch;
        return new SanitizedUsageState(
            overrideActive
                ? GuardRuntimeState.OverrideActive
                : provenanceMismatch
                    ? GuardRuntimeState.ProvenanceMismatch
                    : GuardRuntimeState.Unknown,
            provenanceMismatch
                ? GuardRuntimeState.ProvenanceMismatch
                : GuardRuntimeState.Unknown,
            overrideActive
                ? GuardDecisionReason.UnrestrictedDevelopmentOverride
                : reason,
            overrideActive
                ? GuardDecisionSource.UserOverride
                : GuardDecisionSource.Unavailable,
            null,
            nowUtc,
            null,
            ObservationConfidence.None,
            ObservationFreshness.Unknown,
            error,
            StartNewPhaseAllowed: overrideActive,
            FinishCurrentCheckpointOnly: !overrideActive,
            ResetDetected: false,
            Provenance(),
            null,
            [],
            GuardResumePlanner.Unavailable(settings),
            null);
    }

    private static GuardResumeRecommendation BuildResumeRecommendation(
        GuardSettings settings,
        GuardRuntimeState decision,
        GuardRuntimeState underlying,
        DateTimeOffset evaluatedAtUtc,
        DateTimeOffset? weeklyConstraintReset,
        DateTimeOffset? fiveHourConstraintReset,
        DateTimeOffset? stableWeeklyIdentityReset,
        DateTimeOffset? stableFiveHourIdentityReset,
        bool exactLiveResetData)
    {
        if (decision == GuardRuntimeState.OverrideActive)
        {
            return GuardResumePlanner.NotRequired(settings, overrideActive: true);
        }
        if (underlying is GuardRuntimeState.Normal or GuardRuntimeState.Warning)
        {
            return GuardResumePlanner.NotRequired(settings, overrideActive: false);
        }
        if (underlying != GuardRuntimeState.SafeWrap)
        {
            return GuardResumePlanner.Unavailable(settings);
        }
        if (!exactLiveResetData)
        {
            return GuardResumePlanner.Unavailable(settings);
        }

        var constraints = new List<SanitizedResumeWindow>();
        var stableIdentityResets = new Dictionary<
            AppServerQuotaWindowKind,
            DateTimeOffset>();
        if (fiveHourConstraintReset is { } fiveReset)
        {
            constraints.Add(new SanitizedResumeWindow(
                AppServerQuotaWindowKind.FiveHour,
                fiveReset.ToUniversalTime(),
                GuardResumePlanner.LocalDisplay(fiveReset)));
            if (stableFiveHourIdentityReset is { } stableFive)
            {
                stableIdentityResets[AppServerQuotaWindowKind.FiveHour] = stableFive;
            }
        }
        if (weeklyConstraintReset is { } weeklyReset)
        {
            constraints.Add(new SanitizedResumeWindow(
                AppServerQuotaWindowKind.Weekly,
                weeklyReset.ToUniversalTime(),
                GuardResumePlanner.LocalDisplay(weeklyReset)));
            if (stableWeeklyIdentityReset is { } stableWeekly)
            {
                stableIdentityResets[AppServerQuotaWindowKind.Weekly] = stableWeekly;
            }
        }
        return GuardResumePlanner.ForConstraints(
            settings,
            evaluatedAtUtc,
            constraints,
            stableIdentityResets);
    }

    private static IReadOnlyList<WindowEvaluation>? EvaluateRequiredWindows(
        GuardSettings settings,
        AppServerUsageObservation observation,
        DateTimeOffset evaluatedAtUtc)
    {
        if (observation.Windows is not { } windows)
        {
            return null;
        }
        var results = new List<WindowEvaluation>();
        foreach (var kind in new[]
        {
            AppServerQuotaWindowKind.FiveHour,
            AppServerQuotaWindowKind.Weekly
        })
        {
            var matches = windows.Where(item => item.Kind == kind).ToArray();
            if (matches.Length != 1)
            {
                return null;
            }
            var window = matches[0];
            var single = new AppServerUsageObservation(
                observation.Status,
                window.RemainingPercent,
                window.ResetsAtUtc,
                observation.ObservedAtUtc,
                observation.Confidence,
                observation.Freshness,
                observation.Error);
            var decision = GuardPolicyEvaluator.Evaluate(
                single,
                evaluatedAtUtc,
                kind == AppServerQuotaWindowKind.FiveHour
                    ? settings.ToFiveHourPolicy()
                    : settings.ToPolicy());
            results.Add(new WindowEvaluation(window, decision));
        }
        return results;
    }

    private static AppServerQuotaWindowObservation? FindWindow(
        AppServerUsageObservation observation,
        AppServerQuotaWindowKind kind)
    {
        var matches = observation.Windows?
            .Where(item => item.Kind == kind)
            .Take(2)
            .ToArray();
        return matches is { Length: 1 } ? matches[0] : null;
    }

    private static MappedDecision MapPolicyDecision(
        SettingsValidationError settingsError,
        AppServerUsageObservation observation,
        GuardPolicyDecision? policy)
    {
        if (settingsError != SettingsValidationError.None)
        {
            return MappedDecision.Unknown(GuardDecisionReason.ConfigurationInvalid);
        }
        if (observation.Error == AppServerUsageError.ExecutableNotApproved)
        {
            return new MappedDecision(
                GuardRuntimeState.ProvenanceMismatch,
                GuardRuntimeState.ProvenanceMismatch,
                GuardDecisionReason.ProvenanceMismatch,
                GuardDecisionSource.Unavailable,
                false,
                true,
                false);
        }
        if (policy is null)
        {
            return MappedDecision.Unknown(GuardDecisionReason.ObservationUnknown);
        }
        return policy.Classification switch
        {
            GuardPolicyClassification.Normal => new(
                GuardRuntimeState.Normal,
                GuardRuntimeState.Normal,
                GuardDecisionReason.AboveWarningThreshold,
                GuardDecisionSource.LiveAppServer,
                true,
                false,
                true),
            GuardPolicyClassification.Warning => new(
                GuardRuntimeState.Warning,
                GuardRuntimeState.Warning,
                GuardDecisionReason.WarningThresholdReached,
                GuardDecisionSource.LiveAppServer,
                true,
                false,
                true),
            GuardPolicyClassification.SafeWrap => new(
                GuardRuntimeState.SafeWrap,
                GuardRuntimeState.SafeWrap,
                policy.Reason == GuardPolicyReason.FinishCurrentCheckpointThresholdReached
                    ? GuardDecisionReason.CriticalBufferReached
                    : GuardDecisionReason.SafeWrapThresholdReached,
                GuardDecisionSource.LiveAppServer,
                false,
                true,
                true),
            _ => MappedDecision.Unknown(policy.Reason switch
            {
                GuardPolicyReason.ObservationStale => GuardDecisionReason.ObservationStale,
                GuardPolicyReason.ObservationInvalid => GuardDecisionReason.ObservationInvalid,
                GuardPolicyReason.ConfigurationInvalid => GuardDecisionReason.ConfigurationInvalid,
                _ => GuardDecisionReason.ObservationUnknown
            })
        };
    }

    private static GuardSourceProvenance Provenance() => new(
        ApprovedCodexCli.Distribution,
        ApprovedCodexCli.Version,
        ApprovedCodexCli.ExecutableSha256);

    private static int Severity(GuardPolicyClassification classification) =>
        classification switch
        {
            GuardPolicyClassification.SafeWrap => 3,
            GuardPolicyClassification.Warning => 2,
            GuardPolicyClassification.Normal => 1,
            _ => 4
        };

    private sealed record WindowEvaluation(
        AppServerQuotaWindowObservation Window,
        GuardPolicyDecision Decision);

    private sealed record MappedDecision(
        GuardRuntimeState State,
        GuardRuntimeState TransitionState,
        GuardDecisionReason Reason,
        GuardDecisionSource Source,
        bool StartNewPhaseAllowed,
        bool FinishCurrentCheckpointOnly,
        bool CanExposeQuota)
    {
        public static MappedDecision Unknown(GuardDecisionReason reason) => new(
            GuardRuntimeState.Unknown,
            GuardRuntimeState.Unknown,
            reason,
            GuardDecisionSource.Unavailable,
            false,
            true,
            false);
    }
}
