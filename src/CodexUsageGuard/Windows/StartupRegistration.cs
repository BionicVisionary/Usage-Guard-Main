using System.IO;
using Microsoft.Win32;

namespace CodexUsageGuard.Windows;

public interface IStartupValueStore
{
    string? Read(string name);

    void Write(string name, string value);

    void Delete(string name);
}

public sealed class WindowsRunStartupValueStore : IStartupValueStore
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    public void Write(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class StartupRegistration(
    IStartupValueStore valueStore,
    string executablePath)
{
    public const string ValueName = "Usage Guard";

    public const string LegacyValueName = "OpenAI Codex Usage Guard";

    public string ExecutablePath { get; } = Path.GetFullPath(executablePath);

    public string ExpectedCommand { get; } =
        $"\"{Path.GetFullPath(executablePath)}\" --background";

    public bool IsEnabled() => string.Equals(
        valueStore.Read(ValueName),
        ExpectedCommand,
        StringComparison.Ordinal);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            valueStore.Write(ValueName, ExpectedCommand);
            valueStore.Delete(LegacyValueName);
            return;
        }

        valueStore.Delete(ValueName);
        valueStore.Delete(LegacyValueName);
    }
}
