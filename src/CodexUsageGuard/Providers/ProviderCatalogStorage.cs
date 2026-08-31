using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexUsageGuard.Providers;

public enum ProviderCatalogLoadStatus
{
    Loaded,
    MissingDefaults,
    Corrupt,
    UnsupportedVersion,
    Inaccessible
}

public sealed record ProviderCatalogLoadResult(
    ProviderCatalogSettings Settings,
    ProviderCatalogLoadStatus Status,
    ProviderCatalogValidationError ValidationError);

public sealed class ProviderCatalogStorage(string rootDirectory)
{
    private const string FileName = "providers.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public ProviderCatalogLoadResult Load()
    {
        var path = Path.Combine(rootDirectory, FileName);
        if (!File.Exists(path))
        {
            return new ProviderCatalogLoadResult(
                ProviderCatalogSettings.Default,
                ProviderCatalogLoadStatus.MissingDefaults,
                ProviderCatalogValidationError.None);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ProviderCatalogSettings>(
                File.ReadAllText(path),
                JsonOptions);
            if (settings is null)
            {
                return Corrupt();
            }

            settings = AddMissingCodexFiveHourDefaults(settings);

            var validation = ProviderCatalogValidator.Validate(settings);
            return validation switch
            {
                ProviderCatalogValidationError.None => new(
                    settings,
                    ProviderCatalogLoadStatus.Loaded,
                    validation),
                ProviderCatalogValidationError.UnsupportedSchema => new(
                    ProviderCatalogSettings.Default,
                    ProviderCatalogLoadStatus.UnsupportedVersion,
                    validation),
                _ => new(
                    ProviderCatalogSettings.Default,
                    ProviderCatalogLoadStatus.Corrupt,
                    validation)
            };
        }
        catch (UnauthorizedAccessException)
        {
            return Inaccessible();
        }
        catch (IOException)
        {
            return Inaccessible();
        }
        catch (JsonException)
        {
            return Corrupt();
        }
    }

    public void Save(ProviderCatalogSettings settings)
    {
        var validation = ProviderCatalogValidator.Validate(settings);
        if (validation != ProviderCatalogValidationError.None)
        {
            throw new InvalidDataException($"Invalid provider settings: {validation}.");
        }

        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, FileName);
        var temporary = path + ".new";
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static ProviderCatalogLoadResult Corrupt() => new(
        ProviderCatalogSettings.Default,
        ProviderCatalogLoadStatus.Corrupt,
        ProviderCatalogValidationError.MissingProvider);

    private static ProviderCatalogLoadResult Inaccessible() => new(
        ProviderCatalogSettings.Default,
        ProviderCatalogLoadStatus.Inaccessible,
        ProviderCatalogValidationError.None);

    private static ProviderCatalogSettings AddMissingCodexFiveHourDefaults(
        ProviderCatalogSettings settings)
    {
        var changed = false;
        var providers = settings.Providers.Select(provider =>
        {
            if (provider.ProviderId != AiProviderId.Codex ||
                provider.QuotaWindows.Any(window =>
                    window.Kind == QuotaWindowKind.RollingFiveHour))
            {
                return provider;
            }

            changed = true;
            var defaults = ProviderCatalogSettings.DefaultCodex.QuotaWindows.Single(
                window => window.Kind == QuotaWindowKind.RollingFiveHour);
            return provider with
            {
                QuotaWindows = [defaults, .. provider.QuotaWindows]
            };
        }).ToArray();
        return changed ? settings with { Providers = providers } : settings;
    }
}
