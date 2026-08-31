namespace CodexUsageGuard.Windows;

public static class CodexDesktopIdentity
{
    public const string ExpectedPackageFamilyName =
        "OpenAI.Codex_2p2nqsd0c76g0";

    public static bool IsExpected(string processName, string packageFamilyName) =>
        (processName.Equals("Codex", StringComparison.OrdinalIgnoreCase) ||
         processName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)) &&
        packageFamilyName.Equals(
            ExpectedPackageFamilyName,
            StringComparison.OrdinalIgnoreCase);
}
