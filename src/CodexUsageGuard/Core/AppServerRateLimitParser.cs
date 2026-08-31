using System.Text.Json;

namespace CodexUsageGuard.Core;

public static class AppServerRateLimitParser
{
    public const long FiveHourWindowDurationMinutes = 300;
    public const long WeeklyWindowDurationMinutes = 10_080;

    public static AppServerUsageObservation Parse(
        string responseJson,
        DateTimeOffset observedAtUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasExpectedId(root, AppServerProtocol.RateLimitsRequestId))
            {
                return Error(observedAtUtc, AppServerUsageError.ProtocolError);
            }

            if (root.TryGetProperty("error", out var error) &&
                error.ValueKind != JsonValueKind.Null)
            {
                return Error(
                    observedAtUtc,
                    AppServerUsageError.RateLimitsRequestRejected);
            }

            if (!root.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object)
            {
                return Error(observedAtUtc, AppServerUsageError.ProtocolError);
            }

            var candidates = new List<QuotaCandidate>();
            var sourceState = ReadPreferredSource(result, candidates);
            if (sourceState == SourceReadState.Invalid)
            {
                return Unavailable(
                    observedAtUtc,
                    AppServerUsageError.InvalidWeeklyQuotaWindow);
            }

            var fiveHour = SelectExactlyOne(
                candidates,
                FiveHourWindowDurationMinutes,
                observedAtUtc,
                AppServerUsageError.MissingFiveHourQuotaWindow,
                AppServerUsageError.DuplicateFiveHourQuotaWindow,
                AppServerUsageError.ConflictingFiveHourQuotaWindow,
                AppServerUsageError.InvalidFiveHourQuotaWindow,
                AppServerUsageError.StaleFiveHourQuotaWindow);
            if (fiveHour.Error is { } fiveHourError)
            {
                return Unavailable(observedAtUtc, fiveHourError);
            }

            var weekly = SelectExactlyOne(
                candidates,
                WeeklyWindowDurationMinutes,
                observedAtUtc,
                AppServerUsageError.MissingWeeklyQuotaWindow,
                AppServerUsageError.DuplicateWeeklyQuotaWindow,
                AppServerUsageError.ConflictingWeeklyQuotaWindow,
                AppServerUsageError.InvalidWeeklyQuotaWindow,
                AppServerUsageError.StaleWeeklyQuotaWindow);
            if (weekly.Error is { } weeklyError)
            {
                return Unavailable(observedAtUtc, weeklyError);
            }

            var windows = new[]
            {
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.FiveHour,
                    100m - fiveHour.Candidate!.UsedPercent,
                    fiveHour.ResetsAtUtc!.Value),
                new AppServerQuotaWindowObservation(
                    AppServerQuotaWindowKind.Weekly,
                    100m - weekly.Candidate!.UsedPercent,
                    weekly.ResetsAtUtc!.Value)
            };
            // The top-level values remain the weekly compatibility view. Policy
            // and UI code must use Windows and select the strictest configured
            // required window rather than treating this compatibility value as
            // the complete Codex quota state.

            return new AppServerUsageObservation(
                ObservationStatus.Available,
                windows[1].RemainingPercent,
                windows[1].ResetsAtUtc,
                observedAtUtc,
                ObservationConfidence.High,
                ObservationFreshness.ObservedNow,
                null,
                windows);
        }
        catch (JsonException)
        {
            return Error(observedAtUtc, AppServerUsageError.ProtocolError);
        }
        catch (InvalidOperationException)
        {
            return Error(observedAtUtc, AppServerUsageError.ProtocolError);
        }
    }

    private static SourceReadState ReadPreferredSource(
        JsonElement result,
        ICollection<QuotaCandidate> candidates)
    {
        if (result.TryGetProperty("rateLimitsByLimitId", out var multiBucket) &&
            multiBucket.ValueKind != JsonValueKind.Null)
        {
            if (multiBucket.ValueKind != JsonValueKind.Object)
            {
                return SourceReadState.Invalid;
            }

            foreach (var bucket in multiBucket.EnumerateObject())
            {
                if (bucket.Value.ValueKind != JsonValueKind.Object ||
                    ReadBucket(bucket.Value, candidates) == SourceReadState.Invalid)
                {
                    return SourceReadState.Invalid;
                }
            }

            return SourceReadState.Valid;
        }

        if (!result.TryGetProperty("rateLimits", out var legacy) ||
            legacy.ValueKind != JsonValueKind.Object)
        {
            return SourceReadState.Valid;
        }

        return ReadBucket(legacy, candidates);
    }

    private static SourceReadState ReadBucket(
        JsonElement bucket,
        ICollection<QuotaCandidate> candidates)
    {
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(name, out var window) ||
                window.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("windowDurationMins", out var duration))
            {
                return SourceReadState.Invalid;
            }

            if (!duration.TryGetInt64(out var durationMinutes))
            {
                return SourceReadState.Invalid;
            }

            if (durationMinutes is not (
                    FiveHourWindowDurationMinutes or WeeklyWindowDurationMinutes))
            {
                continue;
            }

            if (!window.TryGetProperty("usedPercent", out var usedPercentElement) ||
                !usedPercentElement.TryGetDecimal(out var usedPercent) ||
                !window.TryGetProperty("resetsAt", out var resetsAtElement) ||
                !resetsAtElement.TryGetInt64(out var resetsAt))
            {
                return SourceReadState.Invalid;
            }

            candidates.Add(new QuotaCandidate(
                durationMinutes,
                usedPercent,
                resetsAt));
        }

        return SourceReadState.Valid;
    }

    private static bool HasExpectedId(JsonElement root, long expectedId) =>
        root.TryGetProperty("id", out var id) && id.TryGetInt64(out var value) &&
        value == expectedId;

    private static bool TryUnixTime(long seconds, out DateTimeOffset value)
    {
        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static CandidateSelection SelectExactlyOne(
        IReadOnlyCollection<QuotaCandidate> candidates,
        long durationMinutes,
        DateTimeOffset observedAtUtc,
        AppServerUsageError missing,
        AppServerUsageError duplicate,
        AppServerUsageError conflicting,
        AppServerUsageError invalid,
        AppServerUsageError stale)
    {
        var matches = candidates.Where(candidate =>
            candidate.DurationMinutes == durationMinutes).ToArray();
        if (matches.Length == 0)
        {
            return new CandidateSelection(null, null, missing);
        }
        if (matches.Length > 1)
        {
            return new CandidateSelection(
                null,
                null,
                matches.All(candidate => candidate == matches[0])
                    ? duplicate
                    : conflicting);
        }

        var selected = matches[0];
        if (selected.UsedPercent is < 0 or > 100 ||
            !TryUnixTime(selected.ResetsAt, out var resetsAtUtc))
        {
            return new CandidateSelection(null, null, invalid);
        }
        return resetsAtUtc <= observedAtUtc
            ? new CandidateSelection(null, null, stale)
            : new CandidateSelection(selected, resetsAtUtc, null);
    }

    private static AppServerUsageObservation Unavailable(
        DateTimeOffset observedAtUtc,
        AppServerUsageError error) =>
        AppServerUsageObservation.UnavailableAt(observedAtUtc, error);

    private static AppServerUsageObservation Error(
        DateTimeOffset observedAtUtc,
        AppServerUsageError error) =>
        AppServerUsageObservation.ErrorAt(observedAtUtc, error);

    private enum SourceReadState
    {
        Valid,
        Invalid
    }

    private sealed record QuotaCandidate(
        long DurationMinutes,
        decimal UsedPercent,
        long ResetsAt);

    private sealed record CandidateSelection(
        QuotaCandidate? Candidate,
        DateTimeOffset? ResetsAtUtc,
        AppServerUsageError? Error);
}
