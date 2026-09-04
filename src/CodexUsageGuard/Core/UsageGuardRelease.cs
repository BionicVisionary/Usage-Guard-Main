using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexUsageGuard.Core;

public static class UsageGuardRelease
{
    public const string ProductName = "Usage Guard";
    public const string DisplayVersion = "0.004";
    public const string RepositorySlug = "BionicVisionary/Usage-Guard-Main";
    public static readonly Uri ReleasePagePrefix = new(
        $"https://github.com/{RepositorySlug}/releases/");
    public static string ProductNameWithVersion =>
        $"{ProductName} v.{DisplayVersion}";
}
public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    ChannelNotConfigured,
    Unavailable
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? AvailableVersion,
    string Message,
    Uri? ReleasePage,
    Uri? InstallerAsset = null,
    Uri? ChecksumAsset = null,
    bool IsImmutableRelease = false,
    string? InstallerSha256 = null,
    string? ChecksumSha256 = null);

public interface IUsageGuardUpdateService
{
    Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

public static class UpdateNotificationPolicy
{
    public static string KeyFor(string version) => $"Update:{version}";

    public static bool ShouldNotify(
        UpdateCheckResult result,
        IReadOnlyDictionary<string, DateTimeOffset>? notificationLedger) =>
        result.Status == UpdateCheckStatus.UpdateAvailable &&
        result.AvailableVersion is { Length: > 0 } version &&
        result.ReleasePage is not null &&
        result.InstallerAsset is not null &&
        result.ChecksumAsset is not null &&
        result.IsImmutableRelease &&
        result.InstallerSha256 is { Length: 64 } &&
        result.ChecksumSha256 is { Length: 64 } &&
        notificationLedger?.ContainsKey(KeyFor(version)) != true;
}

public sealed class UnconfiguredUpdateService : IUsageGuardUpdateService
{
    public Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new UpdateCheckResult(
            UpdateCheckStatus.ChannelNotConfigured,
            UsageGuardRelease.DisplayVersion,
            null,
            "No verified update channel is configured. Install only a package whose published SHA-256 and release notes you trust.",
            null));
    }
}

public sealed class GitHubReleaseUpdateService(
    HttpMessageHandler? testHandler = null) : IUsageGuardUpdateService
{
    public static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/BionicVisionary/Usage-Guard-Main/releases/latest");
    private const int MaximumResponseBytes = 65_536;

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = testHandler is null
                ? new HttpClient(new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(5)
                })
                : new HttpClient(testHandler, disposeHandler: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                LatestReleaseEndpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(
                "UsageGuard",
                UsageGuardRelease.DisplayVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.ChannelNotConfigured,
                    UsageGuardRelease.DisplayVersion,
                    null,
                    "No approved Usage Guard release has been published yet.",
                    null);
            }

            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                return Unavailable();
            }

            var bytes = await ReadBoundedAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token),
                timeout.Token);
            return ParseLatestRelease(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("The update check timed out safely.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or
            InvalidOperationException)
        {
            return Unavailable();
        }
    }

    internal static UpdateCheckResult ParseLatestRelease(
        ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tag_name", out var tagElement) ||
            tagElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("html_url", out var pageElement) ||
            pageElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("draft", out var draftElement) ||
            draftElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("prerelease", out var prereleaseElement) ||
            prereleaseElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("immutable", out var immutableElement) ||
            immutableElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            draftElement.GetBoolean() || prereleaseElement.GetBoolean() ||
            !immutableElement.GetBoolean())
        {
            return Unavailable();
        }

        var tag = tagElement.GetString()?.Trim();
        var page = pageElement.GetString();
        if (tag is null || page is null ||
            !Uri.TryCreate(page, UriKind.Absolute, out var pageUri) ||
            !pageUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !pageUri.AbsoluteUri.StartsWith(
                UsageGuardRelease.ReleasePagePrefix.AbsoluteUri,
                StringComparison.Ordinal) ||
            !TryParseDisplayVersion(tag.TrimStart('v', 'V'), out var available) ||
            !TryParseDisplayVersion(UsageGuardRelease.DisplayVersion, out var current))
        {
            return Unavailable();
        }

        var isNewer = CompareVersions(available, current) > 0;
        Uri? installerAsset = null;
        Uri? checksumAsset = null;
        string? installerSha256 = null;
        string? checksumSha256 = null;
        if (isNewer && (!TryFindReleaseAssets(
                root,
                tag.TrimStart('v', 'V'),
                out installerAsset,
                out checksumAsset,
                out installerSha256,
                out checksumSha256)))
        {
            return Unavailable(
                "The approved release does not contain one unambiguous installer and matching SHA-256 file.");
        }
        return new UpdateCheckResult(
            isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
            UsageGuardRelease.DisplayVersion,
            tag.TrimStart('v', 'V'),
            isNewer
                ? $"Usage Guard v.{tag.TrimStart('v', 'V')} is available. Select Yes to download its installer and matching SHA-256, verify them locally, and install from Usage Guard."
                : $"{UsageGuardRelease.ProductNameWithVersion} is current.",
            pageUri,
            installerAsset,
            checksumAsset,
            IsImmutableRelease: true,
            installerSha256,
            checksumSha256);
    }

    private static bool TryFindReleaseAssets(
        JsonElement root,
        string version,
        out Uri? installer,
        out Uri? checksum,
        out string? installerSha256,
        out string? checksumSha256)
    {
        installer = null;
        checksum = null;
        installerSha256 = null;
        checksumSha256 = null;
        if (!root.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var installerName = $"UsageGuard-Setup-{version}.exe";
        var checksumName = installerName + ".sha256";
        var installers = FindAssets(assets, installerName).ToArray();
        var checksums = FindAssets(assets, checksumName).ToArray();
        if (installers.Length != 1 || checksums.Length != 1)
        {
            return false;
        }
        installer = installers[0].Uri;
        checksum = checksums[0].Uri;
        installerSha256 = installers[0].Sha256;
        checksumSha256 = checksums[0].Sha256;
        return true;
    }

    private static IEnumerable<ReleaseAsset> FindAssets(
        JsonElement assets,
        string expectedName)
    {
        var prefix = $"https://github.com/BionicVisionary/Usage-Guard-Main/releases/download/";
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !asset.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), expectedName, StringComparison.Ordinal) ||
                !asset.TryGetProperty("browser_download_url", out var url) ||
                url.ValueKind != JsonValueKind.String ||
                !asset.TryGetProperty("digest", out var digest) ||
                digest.ValueKind != JsonValueKind.String ||
                !TryParseSha256Digest(digest.GetString(), out var sha256) ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            yield return new ReleaseAsset(uri, sha256);
        }
    }

    private static bool TryParseSha256Digest(string? value, out string sha256)
    {
        const string prefix = "sha256:";
        if (value is not null &&
            value.StartsWith(prefix, StringComparison.Ordinal) &&
            Regex.IsMatch(
                value[prefix.Length..],
                "^[A-Fa-f0-9]{64}$",
                RegexOptions.CultureInvariant))
        {
            sha256 = value[prefix.Length..].ToUpperInvariant();
            return true;
        }
        sha256 = string.Empty;
        return false;
    }

    private sealed record ReleaseAsset(Uri Uri, string Sha256);

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return memory.ToArray();
            }
            if (memory.Length + read > MaximumResponseBytes)
            {
                throw new InvalidOperationException("Update response exceeded limit.");
            }
            memory.Write(buffer, 0, read);
        }
    }

    private static bool TryParseDisplayVersion(
        string value,
        out int[] parts)
    {
        var segments = value.Split('.', StringSplitOptions.None);
        if (segments.Length is < 2 or > 4 || segments.Any(segment =>
                segment.Length is < 1 or > 6 ||
                !int.TryParse(segment, out _)))
        {
            parts = [];
            return false;
        }

        parts = segments.Select(int.Parse).ToArray();
        return true;
    }

    private static int CompareVersions(int[] left, int[] right)
    {
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            var leftValue = index < left.Length ? left[index] : 0;
            var rightValue = index < right.Length ? right[index] : 0;
            if (leftValue != rightValue)
            {
                return leftValue.CompareTo(rightValue);
            }
        }
        return 0;
    }

    private static UpdateCheckResult Unavailable(
        string message = "The verified GitHub release channel is unavailable or returned invalid data.") => new(
            UpdateCheckStatus.Unavailable,
            UsageGuardRelease.DisplayVersion,
            null,
            message,
            null);
}

public enum UpdatePreparationStatus
{
    Ready,
    Unavailable,
    VerificationFailed
}

public sealed record UpdatePreparationResult(
    UpdatePreparationStatus Status,
    string Message,
    string? InstallerPath);

public interface IUsageGuardUpdateInstaller
{
    Task<UpdatePreparationResult> DownloadAndVerifyAsync(
        UpdateCheckResult release,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubReleaseUpdateInstaller(
    HttpMessageHandler? testHandler = null) : IUsageGuardUpdateInstaller
{
    private const int MaximumChecksumBytes = 4_096;
    private const long MaximumInstallerBytes = 200L * 1024 * 1024;

    public async Task<UpdatePreparationResult> DownloadAndVerifyAsync(
        UpdateCheckResult release,
        CancellationToken cancellationToken = default)
    {
        if (release.Status != UpdateCheckStatus.UpdateAvailable ||
            release.AvailableVersion is not { Length: > 0 } version ||
            release.InstallerAsset is null || release.ChecksumAsset is null ||
            !release.IsImmutableRelease ||
            !TryParseExpectedHash(
                release.InstallerSha256,
                out var expectedInstallerDigest) ||
            !TryParseExpectedHash(
                release.ChecksumSha256,
                out var expectedChecksumDigest))
        {
            return Unavailable("No verified installable update is available.");
        }

        var installerName = $"UsageGuard-Setup-{version}.exe";
        var work = Path.Combine(
            Path.GetTempPath(),
            "UsageGuard-Update-" + Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(work, installerName);
        try
        {
            Directory.CreateDirectory(work);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            using var client = testHandler is null
                ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
                : new HttpClient(testHandler, disposeHandler: false);
            var checksumBytes = await DownloadBytesAsync(
                client,
                release.ChecksumAsset,
                MaximumChecksumBytes,
                timeout.Token);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(checksumBytes),
                    expectedChecksumDigest))
            {
                return VerificationFailed(
                    work,
                    "The checksum file did not match GitHub's immutable release digest and was deleted.");
            }
            await DownloadInstallerAsync(
                client,
                release.InstallerAsset,
                installerPath,
                timeout.Token);
            var checksumText = System.Text.Encoding.ASCII.GetString(checksumBytes).Trim();
            var match = Regex.Match(
                checksumText,
                $"^(?<hash>[A-Fa-f0-9]{{64}})\\s+\\*?{Regex.Escape(installerName)}$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return VerificationFailed(work);
            }

            var actualHash = await HashInstallerAsync(installerPath, timeout.Token);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(match.Groups["hash"].Value),
                    Convert.FromHexString(actualHash)) ||
                !CryptographicOperations.FixedTimeEquals(
                    expectedInstallerDigest,
                    Convert.FromHexString(actualHash)))
            {
                return VerificationFailed(work);
            }

            return new UpdatePreparationResult(
                UpdatePreparationStatus.Ready,
                "The installer matched both its published checksum and GitHub's immutable release digest.",
                installerPath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(work);
            return Unavailable("The update download timed out safely.");
        }
        catch (Exception exception) when (exception is
            HttpRequestException or IOException or InvalidDataException or
            UnauthorizedAccessException or CryptographicException)
        {
            TryDelete(work);
            return Unavailable("The update could not be downloaded and verified safely.");
        }
    }

    private static bool TryParseExpectedHash(string? value, out byte[] hash)
    {
        if (value is not null &&
            Regex.IsMatch(
                value,
                "^[A-Fa-f0-9]{64}$",
                RegexOptions.CultureInvariant))
        {
            hash = Convert.FromHexString(value);
            return true;
        }
        hash = [];
        return false;
    }

    private static async Task<string> HashInstallerAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            file.Length is <= 0 or > MaximumInstallerBytes)
        {
            throw new InvalidDataException("Installer exceeded its size limit.");
        }
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task<byte[]> DownloadBytesAsync(
        HttpClient client,
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await SendAssetRequestAsync(client, uri, cancellationToken);
        if (response.Content.Headers.ContentLength is { } length &&
            length > maximumBytes)
        {
            throw new InvalidDataException("Update asset exceeded its size limit.");
        }
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return memory.ToArray();
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException("Update asset exceeded its size limit.");
            memory.Write(buffer, 0, read);
        }
    }

    private static async Task DownloadInstallerAsync(
        HttpClient client,
        Uri uri,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await SendAssetRequestAsync(client, uri, cancellationToken);
        if (response.Content.Headers.ContentLength is > MaximumInstallerBytes)
            throw new InvalidDataException("Installer exceeded its size limit.");
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.WriteThrough | FileOptions.Asynchronous);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumInstallerBytes)
                throw new InvalidDataException("Installer exceeded its size limit.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await target.FlushAsync(cancellationToken);
        target.Flush(flushToDisk: true);
    }

    private static async Task<HttpResponseMessage> SendAssetRequestAsync(
        HttpClient client,
        Uri original,
        CancellationToken cancellationToken)
    {
        if (!IsApprovedInitialAsset(original))
            throw new InvalidDataException("Update asset URL was not approved.");
        var current = original;
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("UsageGuard", UsageGuardRelease.DisplayVersion));
            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null || !location.IsAbsoluteUri ||
                    !IsApprovedRedirect(location))
                    throw new InvalidDataException("Update redirect was not approved.");
                current = location;
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.Dispose();
                throw new HttpRequestException("Update asset was unavailable.");
            }
            return response;
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    private static bool IsApprovedInitialAsset(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith(
            "/BionicVisionary/Usage-Guard-Main/releases/download/",
            StringComparison.Ordinal);

    private static bool IsApprovedRedirect(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static UpdatePreparationResult VerificationFailed(
        string work,
        string message = "The downloaded installer did not match its published SHA-256 and was deleted.")
    {
        TryDelete(work);
        return new UpdatePreparationResult(
            UpdatePreparationStatus.VerificationFailed,
            message,
            null);
    }

    private static UpdatePreparationResult Unavailable(string message) => new(
        UpdatePreparationStatus.Unavailable,
        message,
        null);

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed best-effort cleanup must not convert an unsafe update into success.
        }
    }
}
