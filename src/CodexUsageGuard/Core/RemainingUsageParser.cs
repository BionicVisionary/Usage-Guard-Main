using System.Globalization;
using System.Text.RegularExpressions;

namespace CodexUsageGuard.Core;

public static partial class RemainingUsageParser
{
    public const string WeeklyUsageLimitLabel = "Weekly usage limit";

    public static IReadOnlyList<decimal> ExtractDistinctPercentages(
        IEnumerable<string> accessibleNames)
    {
        var values = new HashSet<decimal>();

        foreach (var name in accessibleNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foreach (Match match in RemainingPercentageRegex().Matches(name))
            {
                if (decimal.TryParse(
                        match.Groups["value"].Value,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var value) &&
                    value is >= 0 and <= 100)
                {
                    values.Add(value);
                }
            }
        }

        return values.Order().ToArray();
    }

    public static bool IsWeeklyUsageLimitLabel(string accessibleName) =>
        accessibleName.Equals(
            WeeklyUsageLimitLabel,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsUsageLimitLabel(string accessibleName) =>
        UsageLimitLabelRegex().IsMatch(accessibleName);

    [GeneratedRegex(
        @"(?ix)(?:\b(?<value>\d{1,3}(?:\.\d+)?)\s*%\s*(?:remaining|left)\b|\b(?:remaining|left)\b\s*:?\s*(?<value>\d{1,3}(?:\.\d+)?)\s*%)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex RemainingPercentageRegex();

    [GeneratedRegex(
        @"(?ix)^\s*.+\busage\s+limit\s*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex UsageLimitLabelRegex();
}
