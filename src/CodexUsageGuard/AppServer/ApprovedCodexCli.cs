using System.IO;
using System.Security.Cryptography;
using CodexUsageGuard.Core;

namespace CodexUsageGuard.AppServer;

public static class ApprovedCodexCli
{
    public const string Version = "0.149.1";
    public const string Distribution = "official_user_scoped_windows";
    public const string ExecutableSha256 =
        "a395030b56b126f608f2403036dddb654a9c063213e9c2b5f85d954cf490ebe6";

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
