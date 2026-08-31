using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexUsageGuard.Providers;

namespace CodexUsageGuard.Core;

internal sealed class ClaudeIntegrationConfigurator(
    string userProfile,
    Func<DateTimeOffset> utcNow,
    string providerDataRoot,
    Func<string?>? executableResolver)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    // Only exact, shipped prior hashes prove ownership. A textual marker is not
    // an ownership boundary because unrelated or edited content can contain it.
    private const string SupportedPriorInvokeWrapperSha256 =
        "104F6D2AB79BBEF7EB7042D300810736F0E525C8655312AF424661D588E1B8AE";
    private const string SupportedIntermediateInvokeWrapperSha256 =
        "F7B7A70E2AAFA928E9B5A14B7E3204874CD2591B9BE473D018716B5FF69D255B";
    private const string SupportedPrePs5CleanupInvokeWrapperSha256 =
        "B9DE43C85B0897DED33951A55BE9D8C6B394F5C042693F26395FE3B801AC9645";
    private const string SupportedPriorStatusLineSha256 =
        "589EF68EFBD4BDC3CE4C313131411D62935CC150991356A245019A15245BB19A";
    private const string SupportedIntermediateStatusLineSha256 =
        "8DAAAF836886765E831FB862AAEB52E6E6BD8E8BC6B78496B0E887A8EA0F984C";
    private const string SupportedRepairedStatusLineSha256 =
        "7773C4A55F5210BB5A403305763A0D2F16A9C62C731310504496656F572371FA";
    private const string SupportedPreColdStartFixStatusLineSha256 =
        "2EE64FE7C8473CD6160DC160BCE590EB9C35B918C79049A3B4C7B09ED7ED4F95";
    private const string SupportedPrePs5CleanupStatusLineSha256 =
        "EED29F25B2774B568C16BCB77501E8AD135508ACDCB6E46C08B7ED0CB835D905";
    private const string SupportedPriorCheckWrapperSha256 =
        "00856DFAA24D4331500CA89EDFC74FDB4ECC75AD719A45C1EE20D4E35CE11F1B";

    public InstructionConfigurationResult Configure()
    {
        var claudeRoot = Path.Combine(userProfile, ".claude");
        var instructions = Path.Combine(claudeRoot, "CLAUDE.md");
        var cliPath = executableResolver is null
            ? WindowsAiProviderDiscovery.FindAllowlistedExecutable(
                "claude.exe",
                [
                    Path.Combine(userProfile, ".local", "bin", "claude.exe"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Microsoft",
                        "WinGet",
                        "Links",
                        "claude.exe")
                ])
            : executableResolver();
        var desktopDetected = executableResolver is null &&
            WindowsAiProviderDiscovery.IsUriSchemeRegistered("claude");
        if (cliPath is null && !desktopDetected)
        {
            return Result(
                InstructionConfigurationStatus.MissingProvider,
                instructions,
                null,
                "Neither the official Claude Desktop app nor Claude Code CLI was detected through an approved ordinary registration. No file was changed.");
        }

        var integrationRoot = Path.Combine(claudeRoot, "usage-guard");
        var statusScript = Path.Combine(integrationRoot, "claude-statusline.ps1");
        var sessionSettingsPath = Path.Combine(
            integrationRoot,
            "claude-session-settings.json");
        // Claude Code may route Windows status lines through Git Bash. Bash
        // consumes unquoted backslashes, so the documented Windows-safe form
        // uses forward slashes even though the target is a Windows path.
        var portableStatusScript = statusScript.Replace('\\', '/');
        var command = $"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{portableStatusScript}\"";
        var sessionSettingsBytes = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object>
            {
                ["statusLine"] = new Dictionary<string, string>
                {
                    ["type"] = "command",
                    ["command"] = command
                }
            },
            new JsonSerializerOptions { WriteIndented = true });
        var portableSessionSettings = sessionSettingsPath.Replace('\\', '/');
        var launchCommand = $"claude --settings \"{portableSessionSettings}\"";
        var instructionsExisted = File.Exists(instructions);
        var originalInstructions = instructionsExisted
            ? File.ReadAllBytes(instructions)
            : [];

        string instructionText;
        try
        {
            instructionText = Utf8.GetString(StripBom(originalInstructions));
        }
        catch (DecoderFallbackException)
        {
            return Result(
                InstructionConfigurationStatus.UnsupportedEncoding,
                instructions,
                null,
                "CLAUDE.md is not valid UTF-8. It was preserved; no file was changed.");
        }
        var agreement = UsageIntegrationInstructions.ClaudeAgreement;
        var section = Inspect(instructionText, agreement);
        if (section == SectionState.Conflicting)
        {
            return Result(
                InstructionConfigurationStatus.ConflictingOwnedSection,
                instructions,
                null,
                "A different or incomplete Usage Guard Claude section exists. It was preserved; no file was changed.");
        }

        var assets = EmbeddedClaudeIntegration.ReadVerifiedAssets();
        var skillDirectory = Path.Combine(claudeRoot, "skills", "claude-usage-guard");
        var expectedSkill = assets.Where(item => item.Key != "claude-statusline.ps1")
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var skillState = InspectSkillDirectory(skillDirectory, expectedSkill);
        var statusScriptState = InspectStatusScript(
            statusScript,
            assets["claude-statusline.ps1"]);
        var sessionSettingsState = InspectExactOwnedFile(
            sessionSettingsPath,
            sessionSettingsBytes);
        if (skillState == ManagedAssetState.Conflicting ||
            statusScriptState == ManagedAssetState.Conflicting ||
            sessionSettingsState == ManagedAssetState.Conflicting)
        {
            return Result(
                InstructionConfigurationStatus.ConflictingIntegration,
                instructions,
                null,
                "A materially different Claude Usage Guard integration exists. It was preserved; no file was changed.");
        }

        var catalogStorage = new ProviderCatalogStorage(providerDataRoot);
        var catalogLoad = catalogStorage.Load();
        if (catalogLoad.Status is not (
                ProviderCatalogLoadStatus.Loaded or
                ProviderCatalogLoadStatus.MissingDefaults))
        {
            return Result(
                InstructionConfigurationStatus.Unavailable,
                instructions,
                null,
                "Usage Guard provider settings are unavailable. Repair them before configuring Claude; no Claude file was changed.");
        }

        if (section == SectionState.Exact &&
            skillState == ManagedAssetState.Exact &&
            statusScriptState == ManagedAssetState.Exact &&
            sessionSettingsState == ManagedAssetState.Exact &&
            catalogLoad.Settings.Providers.Any(item =>
                item.ProviderId == AiProviderId.ClaudeCode))
        {
            return Result(
                InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                instructions,
                null,
                $"Usage Guard's Claude assets are already configured without reading Claude's user settings. Start the official CLI with: {launchCommand} Then complete one ordinary response. The tested Desktop Code tab does not invoke this bridge by itself.");
        }

        var suffix = utcNow().ToString("yyyy-MM-dd-HHmmssfff");
        var instructionsBackup = File.Exists(instructions)
            ? UniqueBackupPath(instructions, suffix)
            : null;
        var skillCreated = !Directory.Exists(skillDirectory);
        var scriptCreated = !File.Exists(statusScript);
        var sessionSettingsCreated = !File.Exists(sessionSettingsPath);
        var originalStatusScript = scriptCreated
            ? []
            : File.ReadAllBytes(statusScript);
        var originalSessionSettings = sessionSettingsCreated
            ? []
            : File.ReadAllBytes(sessionSettingsPath);
        var statusScriptBackup = statusScriptState == ManagedAssetState.SupportedPrior
            ? UniqueBackupPath(statusScript, suffix)
            : null;
        var priorSkillBackup = skillState != ManagedAssetState.SupportedPrior
            ? null
            : UniqueBackupDirectoryPath(skillDirectory, suffix);
        var skillStage = skillDirectory + ".usage-guard-new";
        var skillSwapped = false;
        try
        {
            Directory.CreateDirectory(claudeRoot);
            if (instructionsBackup is not null) File.Copy(instructions, instructionsBackup, false);
            if (skillCreated)
            {
                foreach (var asset in expectedSkill)
                {
                    var path = Path.Combine(skillDirectory, asset.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, asset.Value);
                }
            }
            else if (priorSkillBackup is not null)
            {
                if (Directory.Exists(skillStage))
                {
                    throw new IOException(
                        "A previous Claude skill upgrade stage is incomplete.");
                }
                foreach (var asset in expectedSkill)
                {
                    var path = Path.Combine(skillStage, asset.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, asset.Value);
                }
                Directory.Move(skillDirectory, priorSkillBackup);
                try
                {
                    Directory.Move(skillStage, skillDirectory);
                    skillSwapped = true;
                }
                catch
                {
                    Directory.Move(priorSkillBackup, skillDirectory);
                    throw;
                }
            }
            if (scriptCreated)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statusScript)!);
                File.WriteAllBytes(statusScript, assets["claude-statusline.ps1"]);
            }
            else if (statusScriptBackup is not null)
            {
                File.Copy(statusScript, statusScriptBackup, false);
                WriteAtomic(statusScript, assets["claude-statusline.ps1"]);
            }
            if (sessionSettingsCreated)
            {
                Directory.CreateDirectory(integrationRoot);
                WriteAtomic(sessionSettingsPath, sessionSettingsBytes);
            }
            if (section == SectionState.Absent)
            {
                var newline = instructionText.Contains("\r\n", StringComparison.Ordinal)
                    ? "\r\n"
                    : "\n";
                var separator = instructionText.Length == 0
                    ? string.Empty
                    : instructionText.EndsWith(newline, StringComparison.Ordinal)
                        ? newline
                        : newline + newline;
                var suffixBytes = Utf8.GetBytes(
                    separator + agreement.Replace("\r\n", newline, StringComparison.Ordinal) + newline);
                var combined = new byte[originalInstructions.Length + suffixBytes.Length];
                originalInstructions.CopyTo(combined, 0);
                suffixBytes.CopyTo(combined, originalInstructions.Length);
                WriteAtomic(instructions, combined);
            }

            if (catalogLoad.Settings.Providers.All(item =>
                    item.ProviderId != AiProviderId.ClaudeCode))
            {
                catalogStorage.Save(new ProviderCatalogSettings(
                    ProviderCatalogSettings.CurrentSchemaVersion,
                    [.. catalogLoad.Settings.Providers, ProviderCatalogSettings.DefaultClaudeCode]));
            }
            return Result(
                InstructionConfigurationStatus.AutomaticIntegrationUnavailable,
                instructions,
                priorSkillBackup ?? statusScriptBackup ?? instructionsBackup,
                $"Usage Guard installed its Claude skill, agreement, bridge, and isolated session settings without reading or replacing Claude's user settings. Start the official CLI with: {launchCommand} Then complete one ordinary response. The tested Desktop Code tab does not invoke this bridge by itself.{(cliPath is null ? " Install the official CLI first with: winget install Anthropic.ClaudeCode" : string.Empty)}");
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException)
        {
            TryDeleteFile(sessionSettingsPath + ".usage-guard-new");
            TryDeleteFile(instructions + ".usage-guard-new");
            Restore(
                sessionSettingsPath,
                originalSessionSettings,
                !sessionSettingsCreated);
            Restore(instructions, originalInstructions, instructionsExisted);
            if (priorSkillBackup is not null)
            {
                TryDeleteDirectory(skillStage);
                if (skillSwapped)
                {
                    TryDeleteDirectory(skillDirectory);
                    TryRestoreDirectory(priorSkillBackup, skillDirectory);
                }
            }
            if (skillCreated) TryDeleteDirectory(skillDirectory);
            Restore(statusScript, originalStatusScript, !scriptCreated);
            if (statusScriptBackup is not null) TryDeleteFile(statusScriptBackup);
            return Result(
                InstructionConfigurationStatus.Unavailable,
                instructions,
                null,
                "Claude configuration failed and was rolled back safely.");
        }
    }

    private static ManagedAssetState InspectSkillDirectory(
        string directory,
        IReadOnlyDictionary<string, byte[]> expected)
    {
        try
        {
            if (!Directory.Exists(directory)) return ManagedAssetState.Absent;
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            var supportedPrior = false;
            foreach (var item in expected)
            {
                var path = Path.Combine(directory, item.Key);
                if (!File.Exists(path))
                {
                    return ManagedAssetState.Conflicting;
                }
                var bytes = File.ReadAllBytes(path);
                if (bytes.AsSpan().SequenceEqual(item.Value))
                {
                    continue;
                }
                if (IsSupportedPriorSkillAsset(item.Key, bytes))
                {
                    supportedPrior = true;
                    continue;
                }
                return ManagedAssetState.Conflicting;
            }
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(directory, file);
                if (expected.ContainsKey(relative))
                {
                    continue;
                }
                if (relative.StartsWith(
                        "scripts\\invoke_guard_process.ps1.backup-UsageGuard-",
                        StringComparison.Ordinal) &&
                    IsSupportedPriorWrapper(File.ReadAllBytes(file)))
                {
                    supportedPrior = true;
                    continue;
                }
                return ManagedAssetState.Conflicting;
            }
            return supportedPrior
                ? ManagedAssetState.SupportedPrior
                : ManagedAssetState.Exact;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            return ManagedAssetState.Conflicting;
        }
    }

    private static bool IsSupportedPriorWrapper(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return hash.Equals(
                SupportedPriorInvokeWrapperSha256,
                StringComparison.Ordinal) ||
            hash.Equals(
                SupportedIntermediateInvokeWrapperSha256,
                StringComparison.Ordinal) ||
            hash.Equals(
                SupportedPrePs5CleanupInvokeWrapperSha256,
                StringComparison.Ordinal);
    }

    private static bool IsSupportedPriorSkillAsset(string key, byte[] bytes)
    {
        if (key.Equals(
                "scripts\\invoke_guard_process.ps1",
                StringComparison.Ordinal))
        {
            return IsSupportedPriorWrapper(bytes);
        }
        if (!key.Equals("scripts\\check_usage.ps1", StringComparison.Ordinal))
        {
            return false;
        }
        return Convert.ToHexString(SHA256.HashData(bytes)).Equals(
            SupportedPriorCheckWrapperSha256,
            StringComparison.Ordinal);
    }

    private static ManagedAssetState InspectExactOwnedFile(
        string path,
        byte[] expected)
    {
        try
        {
            if (!File.Exists(path)) return ManagedAssetState.Absent;
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(expected)
                ? ManagedAssetState.Exact
                : ManagedAssetState.Conflicting;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            return ManagedAssetState.Conflicting;
        }
    }

    private static ManagedAssetState InspectStatusScript(
        string path,
        byte[] expected)
    {
        try
        {
            if (!File.Exists(path)) return ManagedAssetState.Absent;
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan().SequenceEqual(expected))
            {
                return ManagedAssetState.Exact;
            }
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return hash.Equals(
                    SupportedPriorStatusLineSha256,
                    StringComparison.Ordinal) ||
                hash.Equals(
                    SupportedIntermediateStatusLineSha256,
                    StringComparison.Ordinal) ||
                hash.Equals(
                    SupportedRepairedStatusLineSha256,
                    StringComparison.Ordinal) ||
                hash.Equals(
                    SupportedPreColdStartFixStatusLineSha256,
                    StringComparison.Ordinal) ||
                hash.Equals(
                    SupportedPrePs5CleanupStatusLineSha256,
                    StringComparison.Ordinal)
                ? ManagedAssetState.SupportedPrior
                : ManagedAssetState.Conflicting;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            return ManagedAssetState.Conflicting;
        }
    }

    private static string UniqueBackupPath(string sourcePath, string suffix)
    {
        var stem = sourcePath + ".backup-UsageGuard-" + suffix;
        if (!File.Exists(stem))
        {
            return stem;
        }
        for (var index = 2; index <= 999; index++)
        {
            var candidate = stem + "-" + index;
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("No unique Claude configuration backup name is available.");
    }

    private static string UniqueBackupDirectoryPath(
        string sourcePath,
        string suffix)
    {
        var stem = sourcePath + ".backup-UsageGuard-" + suffix;
        if (!Directory.Exists(stem) && !File.Exists(stem))
        {
            return stem;
        }
        for (var index = 2; index <= 999; index++)
        {
            var candidate = stem + "-" + index;
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("No unique Claude skill backup name is available.");
    }

    private static SectionState Inspect(string text, string agreement)
    {
        var start = agreement[..agreement.IndexOf("\r\n", StringComparison.Ordinal)];
        var end = agreement[(agreement.LastIndexOf("\r\n", StringComparison.Ordinal) + 2)..];
        if (!text.Contains(start, StringComparison.Ordinal) &&
            !text.Contains(end, StringComparison.Ordinal)) return SectionState.Absent;
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(
            agreement.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal)
            ? SectionState.Exact
            : SectionState.Conflicting;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".usage-guard-new";
        if (File.Exists(temporary))
        {
            throw new IOException("A previous atomic write is incomplete.");
        }
        using (var stream = new FileStream(
            temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    private static ReadOnlySpan<byte> StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;

    private static void Restore(string path, byte[] original, bool existed)
    {
        try
        {
            if (!existed) File.Delete(path);
            else File.WriteAllBytes(path, original);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryRestoreDirectory(string backupPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(backupPath) && !Directory.Exists(targetPath))
            {
                Directory.Move(backupPath, targetPath);
            }
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static InstructionConfigurationResult Result(
        InstructionConfigurationStatus status,
        string path,
        string? backup,
        string message) => new(status, path, backup, message);

    private enum SectionState { Absent, Exact, Conflicting }
    private enum ManagedAssetState { Absent, Exact, SupportedPrior, Conflicting }
}
