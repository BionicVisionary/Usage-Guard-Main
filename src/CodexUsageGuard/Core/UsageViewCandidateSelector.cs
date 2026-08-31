namespace CodexUsageGuard.Core;

public sealed record UsageViewCandidate(
    long SourceWindowHandle,
    string AnchorIdentity,
    string ScopeIdentity,
    IReadOnlySet<string> AncestorScopeIdentities,
    UsageViewSnapshot Snapshot);

public static class UsageViewCandidateSelector
{
    public static IReadOnlyList<UsageViewCandidate> SelectMostSpecific(
        IEnumerable<UsageViewCandidate> candidates)
    {
        var canonical = candidates
            .GroupBy(candidate => (candidate.AnchorIdentity, candidate.ScopeIdentity))
            .Select(MergeEquivalentCaptures)
            .ToArray();

        return canonical
            .Where(candidate => !canonical.Any(other =>
                !ReferenceEquals(candidate, other) &&
                candidate.AnchorIdentity.Equals(
                    other.AnchorIdentity,
                    StringComparison.Ordinal) &&
                other.AncestorScopeIdentities.Contains(candidate.ScopeIdentity) &&
                HaveEquivalentPercentages(candidate, other)))
            .ToArray();
    }

    private static UsageViewCandidate MergeEquivalentCaptures(
        IGrouping<(string AnchorIdentity, string ScopeIdentity), UsageViewCandidate> group)
    {
        var first = group.First();
        var names = group
            .SelectMany(candidate => candidate.Snapshot.AccessibleNames)
            .ToArray();
        var ancestors = group
            .SelectMany(candidate => candidate.AncestorScopeIdentities)
            .ToHashSet(StringComparer.Ordinal);

        return first with
        {
            AncestorScopeIdentities = ancestors,
            Snapshot = new UsageViewSnapshot(names)
        };
    }

    private static bool HaveEquivalentPercentages(
        UsageViewCandidate first,
        UsageViewCandidate second)
    {
        var firstValues = RemainingUsageParser
            .ExtractDistinctPercentages(first.Snapshot.AccessibleNames)
            .ToHashSet();
        var secondValues = RemainingUsageParser
            .ExtractDistinctPercentages(second.Snapshot.AccessibleNames)
            .ToHashSet();

        return firstValues.SetEquals(secondValues);
    }
}
