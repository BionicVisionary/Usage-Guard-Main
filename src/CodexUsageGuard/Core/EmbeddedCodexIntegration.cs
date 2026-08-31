using System.IO;
using System.Reflection;

namespace CodexUsageGuard.Core;

public static class EmbeddedCodexIntegration
{
    private static readonly IReadOnlyDictionary<string, string> Resources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SKILL.md"] = "UsageGuard.CodexIntegration.SKILL.md",
            [Path.Combine("scripts", "check_usage.ps1")] =
                "UsageGuard.CodexIntegration.scripts.check_usage.ps1",
            [Path.Combine("scripts", "invoke_guard_process.ps1")] =
                "UsageGuard.CodexIntegration.scripts.invoke_guard_process.ps1"
        };

    public static IReadOnlyDictionary<string, byte[]> ReadVerifiedAssets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var resource in Resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource.Value) ??
                throw new InvalidOperationException(
                    $"Embedded integration asset is missing: {resource.Key}");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            assets.Add(resource.Key, memory.ToArray());
        }
        return assets;
    }
}
