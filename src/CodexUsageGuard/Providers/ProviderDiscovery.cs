using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using CodexUsageGuard.AppServer;

namespace CodexUsageGuard.Providers;

public interface IAiProviderDiscovery
{
    IReadOnlyList<ProviderDetectionResult> Detect();
}

public sealed class WindowsAiProviderDiscovery : IAiProviderDiscovery
{
    public IReadOnlyList<ProviderDetectionResult> Detect() =>
    [
        DetectCodex(),
        DetectClaudeCode()
    ];

    private static ProviderDetectionResult DetectCodex()
    {
        var validation = ApprovedCodexCli.Validate();
        return new ProviderDetectionResult(
            AiProviderId.Codex,
            "Codex",
            validation.Error is null,
            ProviderUsageCapability.LiveQuotaWindows,
            validation.Error is null ? ApprovedCodexCli.Version : null,
            validation.Error is null ? "official_cli_verified" : "not_available_or_untrusted");
    }

    private static ProviderDetectionResult DetectClaudeCode()
    {
        var path = FindAllowlistedExecutable("claude.exe",
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "bin",
                "claude.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WinGet",
                "Links",
                "claude.exe")
        ]);

        var desktopDetected = IsUriSchemeRegistered("claude");
        return new ProviderDetectionResult(
            AiProviderId.ClaudeCode,
            "Claude",
            path is not null || desktopDetected,
            ProviderUsageCapability.LiveQuotaWindows,
            path is null ? null : ReadFileVersion(path),
            path is null && !desktopDetected
                ? "official_claude_desktop_or_code_not_detected"
                : path is not null
                    ? "official_statusline_rate_limits_supported"
                    : "claude_desktop_detected_official_cli_required_for_live_statusline");
    }

    internal static bool IsUriSchemeRegistered(string scheme)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(scheme, writable: false);
            return key is not null &&
                key.GetValue("URL Protocol") is string &&
                key.GetValue(null) is string description &&
                description.StartsWith("URL:", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    internal static string? FindAllowlistedExecutable(
        string fileName,
        IReadOnlyList<string> fixedCandidates)
    {
        foreach (var candidate in fixedCandidates)
        {
            try
            {
                if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!Path.IsPathFullyQualified(directory))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(Path.Combine(directory, fileName));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
            {
            }
        }

        return null;
    }

    private static string? ReadFileVersion(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            return null;
        }
    }
}
