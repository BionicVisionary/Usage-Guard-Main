using System.IO;
using System.Text;

namespace CodexUsageGuard.Core;

public enum InstructionProvider
{
    Codex,
    ClaudeCode
}

public enum InstructionConfigurationStatus
{
    Configured,
    AlreadyConfigured,
    AutomaticIntegrationUnavailable,
    MissingProvider,
    Shadowed,
    ConflictingIntegration,
    ConflictingOwnedSection,
    UnsupportedEncoding,
    Unavailable
}

public sealed record InstructionConfigurationResult(
    InstructionConfigurationStatus Status,
    string TargetPath,
    string? BackupPath,
    string Message);

public sealed class ProviderInstructionConfigurator
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly string _userProfile;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly string _providerDataRoot;
    private readonly Func<string?>? _claudeExecutableResolver;

    public ProviderInstructionConfigurator(
        string? userProfile = null,
        Func<DateTimeOffset>? utcNow = null,
        string? providerDataRoot = null,
        Func<string?>? claudeExecutableResolver = null)
    {
        _userProfile = Path.GetFullPath(userProfile ??
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _providerDataRoot = Path.GetFullPath(providerDataRoot ??
            GuardDataPaths.RootDirectory);
        _claudeExecutableResolver = claudeExecutableResolver;
    }

    public InstructionConfigurationResult Configure(InstructionProvider provider)
    {
        var target = TargetPath(provider);
        if (provider == InstructionProvider.ClaudeCode)
        {
            try
            {
                return new ClaudeIntegrationConfigurator(
                    _userProfile,
                    _utcNow,
                    _providerDataRoot,
                    _claudeExecutableResolver)
                    .Configure();
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
            {
                return Result(
                    InstructionConfigurationStatus.Unavailable,
                    target,
                    null,
                    "Claude could not be configured safely. Existing files were preserved where possible, and no broader permissions were requested.");
            }
        }

        var installedSkillNow = false;
        try
        {
            var shadow = Path.Combine(_userProfile, ".codex", "AGENTS.override.md");
            if (File.Exists(shadow))
            {
                return Result(
                    InstructionConfigurationStatus.Shadowed,
                    target,
                    null,
                    "AGENTS.override.md may shadow the global agreement. Resolve it manually; no file was changed.");
            }

            var agreement = UsageIntegrationInstructions.CodexAgreement;
            var existingBytes = File.Exists(target)
                ? File.ReadAllBytes(target)
                : [];
            string existing;
            try
            {
                existing = StrictUtf8.GetString(StripUtf8Bom(existingBytes));
            }
            catch (DecoderFallbackException)
            {
                return Result(
                    InstructionConfigurationStatus.UnsupportedEncoding,
                    target,
                    null,
                    "The existing instruction file is not valid UTF-8. Review it manually; no file was changed.");
            }

            var sectionState = InspectOwnedSection(existing, agreement);
            if (sectionState == OwnedSectionState.Conflicting)
            {
                return Result(
                    InstructionConfigurationStatus.ConflictingOwnedSection,
                    target,
                    null,
                    "A different or incomplete Usage Guard section already exists. Review it manually; no file was changed.");
            }

            var skillStatus = EnsureCodexSkill(out installedSkillNow);
            if (skillStatus != InstructionConfigurationStatus.Configured)
            {
                return Result(
                    skillStatus,
                    target,
                    null,
                    skillStatus == InstructionConfigurationStatus.ConflictingIntegration
                        ? "A materially different codex-usage-guard skill already exists. It was preserved; review or update it through the verified installer."
                        : "The verified Codex integration could not be installed safely. No existing instructions were replaced.");
            }

            if (sectionState == OwnedSectionState.Exact)
            {
                return Result(
                    installedSkillNow
                        ? InstructionConfigurationStatus.Configured
                        : InstructionConfigurationStatus.AlreadyConfigured,
                    target,
                    null,
                    installedSkillNow
                        ? "The verified Codex skill was installed; the Usage Guard agreement was already present. Start a new Codex task for it to load."
                        : "The verified Codex skill and Usage Guard agreement are already configured; no file was changed.");
            }

            var directory = Path.GetDirectoryName(target) ??
                throw new InvalidOperationException("Instruction directory is unavailable.");
            Directory.CreateDirectory(directory);
            var newline = existing.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : "\n";
            var platformAgreement = agreement.Replace("\r\n", newline, StringComparison.Ordinal);
            var separator = existing.Length == 0
                ? string.Empty
                : existing.EndsWith(newline, StringComparison.Ordinal)
                    ? newline
                    : newline + newline;
            var suffix = StrictUtf8.GetBytes(separator + platformAgreement + newline);
            var appendedBytes = new byte[existingBytes.Length + suffix.Length];
            existingBytes.CopyTo(appendedBytes, 0);
            suffix.CopyTo(appendedBytes, existingBytes.Length);
            var temporary = target + ".usage-guard-new";
            if (File.Exists(temporary))
            {
                RollBackNewSkill(installedSkillNow);
                return Result(
                    InstructionConfigurationStatus.Unavailable,
                    target,
                    null,
                    "A prior temporary configuration file exists. Review it manually; no file was changed.");
            }

            string? backup = null;
            if (File.Exists(target))
            {
                backup = target + ".backup-UsageGuard-" +
                    _utcNow().ToString("yyyy-MM-dd-HHmmssfff");
                File.Copy(target, backup, overwrite: false);
            }

            try
            {
                WriteBytesDurably(temporary, appendedBytes);
                File.Move(temporary, target, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                RollBackNewSkill(installedSkillNow);
                throw;
            }

            return Result(
                InstructionConfigurationStatus.Configured,
                target,
                backup,
                "The verified Codex skill was installed and the Usage Guard section was appended without replacing existing instructions. Start a new Codex task for it to load.");
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RollBackNewSkill(installedSkillNow);
            return Result(
                InstructionConfigurationStatus.Unavailable,
                target,
                null,
                "Codex could not be configured safely. No existing instructions were intentionally replaced.");
        }
    }

    private InstructionConfigurationStatus EnsureCodexSkill(out bool installedNow)
    {
        installedNow = false;
        var assets = EmbeddedCodexIntegration.ReadVerifiedAssets();
        var skillDirectory = CodexSkillDirectory();
        if (Directory.Exists(skillDirectory))
        {
            var files = Directory.GetFiles(
                    skillDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(skillDirectory, path),
                    path => File.ReadAllBytes(path),
                    StringComparer.OrdinalIgnoreCase);
            if (files.Count != assets.Count || assets.Any(asset =>
                    !files.TryGetValue(asset.Key, out var bytes) ||
                    !bytes.AsSpan().SequenceEqual(asset.Value)))
            {
                return InstructionConfigurationStatus.ConflictingIntegration;
            }
            return InstructionConfigurationStatus.Configured;
        }

        var parent = Path.GetDirectoryName(skillDirectory) ??
            throw new InvalidOperationException("Codex skill directory is unavailable.");
        Directory.CreateDirectory(parent);
        var stage = skillDirectory + ".usage-guard-new";
        if (Directory.Exists(stage))
        {
            return InstructionConfigurationStatus.Unavailable;
        }

        try
        {
            Directory.CreateDirectory(stage);
            foreach (var asset in assets)
            {
                var path = Path.Combine(stage, asset.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                WriteBytesDurably(path, asset.Value);
            }
            Directory.Move(stage, skillDirectory);
            installedNow = true;
            return InstructionConfigurationStatus.Configured;
        }
        catch
        {
            TryDeleteDirectory(stage);
            throw;
        }
    }

    private void RollBackNewSkill(bool installedNow)
    {
        if (installedNow)
        {
            TryDeleteDirectory(CodexSkillDirectory());
        }
    }

    private string CodexSkillDirectory() => Path.Combine(
        _userProfile,
        ".codex",
        "skills",
        "codex-usage-guard");

    private string TargetPath(InstructionProvider provider) => provider switch
    {
        InstructionProvider.Codex => Path.Combine(_userProfile, ".codex", "AGENTS.md"),
        InstructionProvider.ClaudeCode => Path.Combine(_userProfile, ".claude", "CLAUDE.md"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static OwnedSectionState InspectOwnedSection(
        string existing,
        string agreement)
    {
        var startMarker = agreement[..agreement.IndexOf("\r\n", StringComparison.Ordinal)];
        var endMarker = agreement[(agreement.LastIndexOf("\r\n", StringComparison.Ordinal) + 2)..];
        var start = existing.IndexOf(startMarker, StringComparison.Ordinal);
        var end = existing.IndexOf(endMarker, StringComparison.Ordinal);
        if (start < 0 && end < 0)
        {
            return OwnedSectionState.Absent;
        }
        var normalizedExisting = existing.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedAgreement = agreement.Replace("\r\n", "\n", StringComparison.Ordinal);
        return start >= 0 && end > start &&
            normalizedExisting.Contains(normalizedAgreement, StringComparison.Ordinal)
                ? OwnedSectionState.Exact
                : OwnedSectionState.Conflicting;
    }

    private static ReadOnlySpan<byte> StripUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;

    private static void WriteBytesDurably(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static InstructionConfigurationResult Result(
        InstructionConfigurationStatus status,
        string target,
        string? backup,
        string message) => new(status, target, backup, message);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private enum OwnedSectionState
    {
        Absent,
        Exact,
        Conflicting
    }
}
