using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexUsageGuard.Windows;

public enum DesktopAiProvider
{
    Codex,
    Claude
}

public sealed record LaunchTogetherShortcut(
    DesktopAiProvider Provider,
    string DisplayName,
    string UriScheme,
    string FileName,
    string Argument);

public sealed record ShortcutDefinition(
    string TargetPath,
    string Arguments,
    string Description,
    string IconLocation);

public interface ILaunchTogetherPlatform
{
    bool IsUriSchemeRegistered(string scheme);

    ShortcutDefinition? ReadShortcut(string path);

    void WriteShortcut(string path, ShortcutDefinition definition);

    void DeleteShortcut(string path);
}

public sealed class WindowsLaunchTogetherPlatform : ILaunchTogetherPlatform
{
    public bool IsUriSchemeRegistered(string scheme)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(scheme, writable: false);
        return key is not null &&
            key.GetValue("URL Protocol") is string &&
            key.GetValue(null) is string description &&
            description.StartsWith("URL:", StringComparison.OrdinalIgnoreCase);
    }

    public ShortcutDefinition? ReadShortcut(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var (shell, shortcut) = OpenShortcut(path);
        try
        {
            return new ShortcutDefinition(
                Convert.ToString(shortcut.TargetPath) ?? string.Empty,
                Convert.ToString(shortcut.Arguments) ?? string.Empty,
                Convert.ToString(shortcut.Description) ?? string.Empty,
                Convert.ToString(shortcut.IconLocation) ?? string.Empty);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public void WriteShortcut(string path, ShortcutDefinition definition)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ??
            throw new InvalidOperationException("Shortcut directory is unavailable."));
        var (shell, shortcut) = OpenShortcut(path);
        try
        {
            shortcut.TargetPath = definition.TargetPath;
            shortcut.Arguments = definition.Arguments;
            shortcut.Description = definition.Description;
            shortcut.IconLocation = definition.IconLocation;
            shortcut.WorkingDirectory = Path.GetDirectoryName(definition.TargetPath);
            shortcut.Save();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public void DeleteShortcut(string path)
    {
        File.Delete(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory) &&
            !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static (dynamic Shell, dynamic Shortcut) OpenShortcut(string path)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true) ??
            throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(type) ??
            throw new InvalidOperationException("Windows shortcut support is unavailable.");
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            return (shell, shortcut);
        }
        catch
        {
            Marshal.FinalReleaseComObject(shell);
            throw;
        }
    }
}

public sealed class LaunchTogetherRegistration
{
    private static readonly LaunchTogetherShortcut[] Targets =
    [
        new(
            DesktopAiProvider.Codex,
            "Codex",
            "codex",
            "Usage Guard + Codex.lnk",
            "--launch-provider codex"),
        new(
            DesktopAiProvider.Claude,
            "Claude",
            "claude",
            "Usage Guard + Claude.lnk",
            "--launch-provider claude")
    ];

    private readonly ILaunchTogetherPlatform _platform;
    private readonly string _executablePath;
    private readonly string _shortcutDirectory;

    public LaunchTogetherRegistration(
        ILaunchTogetherPlatform platform,
        string executablePath,
        string? programsDirectory = null)
    {
        _platform = platform;
        _executablePath = Path.GetFullPath(executablePath);
        var programs = Path.GetFullPath(programsDirectory ??
            Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        _shortcutDirectory = Path.Combine(programs, "Usage Guard");
    }

    public IReadOnlyList<DesktopAiProvider> AvailableProviders() => Targets
        .Where(item => _platform.IsUriSchemeRegistered(item.UriScheme))
        .Select(item => item.Provider)
        .ToArray();

    public bool IsEnabled()
    {
        var available = Targets.Where(item =>
            _platform.IsUriSchemeRegistered(item.UriScheme)).ToArray();
        return available.Length > 0 && available.All(item =>
            IsOwned(_platform.ReadShortcut(PathFor(item)), DefinitionFor(item)));
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            foreach (var target in Targets)
            {
                var path = PathFor(target);
                var existing = _platform.ReadShortcut(path);
                if (IsOwned(existing, DefinitionFor(target)))
                {
                    _platform.DeleteShortcut(path);
                }
            }
            return;
        }

        var available = Targets.Where(item =>
            _platform.IsUriSchemeRegistered(item.UriScheme)).ToArray();
        if (available.Length == 0)
        {
            throw new InvalidOperationException(
                "No supported Codex or Claude desktop URI registration was found.");
        }

        var created = new List<string>();
        try
        {
            foreach (var target in available)
            {
                var path = PathFor(target);
                var expected = DefinitionFor(target);
                var existing = _platform.ReadShortcut(path);
                if (existing is not null && !IsOwned(existing, expected))
                {
                    throw new InvalidOperationException(
                        $"A different shortcut already uses {target.FileName}.");
                }
                if (existing is null)
                {
                    _platform.WriteShortcut(path, expected);
                    created.Add(path);
                }
            }
        }
        catch
        {
            foreach (var path in created)
            {
                try
                {
                    _platform.DeleteShortcut(path);
                }
                catch (Exception exception) when (exception is
                    IOException or UnauthorizedAccessException or COMException)
                {
                }
            }
            throw;
        }
    }

    private string PathFor(LaunchTogetherShortcut target) =>
        Path.Combine(_shortcutDirectory, target.FileName);

    private ShortcutDefinition DefinitionFor(LaunchTogetherShortcut target) => new(
        _executablePath,
        target.Argument,
        $"Start Usage Guard and {target.DisplayName} together",
        _executablePath + ",0");

    private static bool IsOwned(
        ShortcutDefinition? actual,
        ShortcutDefinition expected)
    {
        if (actual is null)
        {
            return false;
        }
        try
        {
            return Path.IsPathFullyQualified(actual.TargetPath) &&
                Path.GetFullPath(actual.TargetPath).Equals(
                    Path.GetFullPath(expected.TargetPath),
                    StringComparison.OrdinalIgnoreCase) &&
                actual.Arguments.Equals(expected.Arguments, StringComparison.Ordinal) &&
                actual.Description.Equals(expected.Description, StringComparison.Ordinal) &&
                actual.IconLocation.Equals(
                    expected.IconLocation,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }
}

public static class DesktopAiLaunchContract
{
    public static bool TryGetUri(string provider, out string uri)
    {
        uri = provider switch
        {
            "codex" => "codex:",
            "claude" => "claude:",
            _ => string.Empty
        };
        return uri.Length > 0;
    }

    public static int Launch(string provider, string executablePath)
    {
        if (!TryGetUri(provider, out var uri) ||
            !CodexUsageGuard.Providers.WindowsAiProviderDiscovery
                .IsUriSchemeRegistered(provider) ||
            !Path.GetFileName(executablePath).Equals(
                "CodexUsageGuard.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "--background" }
            })?.Dispose();
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            })?.Dispose();
            return 0;
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return 2;
        }
    }
}
