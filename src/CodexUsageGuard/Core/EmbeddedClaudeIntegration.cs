using System.IO;
using System.Reflection;

namespace CodexUsageGuard.Core;

public static class EmbeddedClaudeIntegration
{
    private static readonly IReadOnlyDictionary<string, string> Resources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SKILL.md"] = "UsageGuard.ClaudeIntegration.SKILL.md",
            [Path.Combine("scripts", "check_usage.ps1")] =
                "UsageGuard.ClaudeIntegration.scripts.check_usage.ps1",
            [Path.Combine("scripts", "invoke_guard_process.ps1")] =
                "UsageGuard.ClaudeIntegration.scripts.invoke_guard_process.ps1",
            ["claude-statusline.ps1"] =
                "UsageGuard.ClaudeIntegration.claude-statusline.ps1"
        };

    public static IReadOnlyDictionary<string, byte[]> ReadVerifiedAssets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return Resources.ToDictionary(
            item => item.Key,
            item =>
            {
                using var stream = assembly.GetManifestResourceStream(item.Value) ??
                    throw new InvalidOperationException(
                        $"Embedded Claude integration asset is missing: {item.Key}");
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            },
            StringComparer.Ordinal);
    }
}
