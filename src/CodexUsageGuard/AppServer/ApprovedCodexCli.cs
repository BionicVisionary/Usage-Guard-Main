using System.IO;
using System.Security.Cryptography;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.AppServer;

public static class ApprovedCodexCli
{
    // Reviewed against openai/codex rust-v0.156.1 release asset digest and
    // a valid OpenAI OpCo, LLC Authenticode signature on 2026-09-27.
    public const string Version = "0.156.1";
    public const string Distribution = "official_user_scoped_windows";
    public const string ExecutableSha256 =
        "70bcb05f9bf1a4e7306edd0cd1b57d02af3267ad02a34b26f45c8c4bb20a3301";

    public static string ExecutablePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "OpenAI",
        "Codex",
        "bin",
        "codex.exe");

    public static ApprovedCodexCliValidation Validate()
    {
        var executablePath = ExecutablePath;
        try
        {
            if (!File.Exists(executablePath))
            {
                return new ApprovedCodexCliValidation(
                    executablePath,
                    AppServerUsageError.ExecutableNotFound);
            }

            using var stream = File.OpenRead(executablePath);
            var actualHash = Convert.ToHexString(
                SHA256.HashData(stream)).ToLowerInvariant();
            return actualHash.Equals(
                ExecutableSha256,
                StringComparison.OrdinalIgnoreCase)
                ? new ApprovedCodexCliValidation(executablePath, null)
                : new ApprovedCodexCliValidation(
                    executablePath,
                    AppServerUsageError.ExecutableNotApproved);
        }
        catch (UnauthorizedAccessException)
        {
            return new ApprovedCodexCliValidation(
                executablePath,
                AppServerUsageError.ExecutableInaccessible);
        }
        catch (IOException)
        {
            return new ApprovedCodexCliValidation(
                executablePath,
                AppServerUsageError.ExecutableInaccessible);
        }
        catch
        {
            return new ApprovedCodexCliValidation(
                executablePath,
                AppServerUsageError.LaunchFailed);
        }
    }
}

public sealed record ApprovedCodexCliValidation(
    string ExecutablePath,
    AppServerUsageError? Error);
