using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WBImageViewer.Models;

namespace WBImageViewer.Services;

internal static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WBImageViewer");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options)
                           ?? new AppSettings();
            MigrateAiConfiguration(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        MigrateAiConfiguration(settings);
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
    }

    /// <summary>
    /// Lift the legacy single-provider fields into the multi-provider catalogue exactly once.
    /// Old settings.json files keep working; translation and OCR both point at the old model.
    /// </summary>
    private static void MigrateAiConfiguration(AppSettings settings)
    {
        settings.AiConfig ??= new AiProviderConfig();
        var config = settings.AiConfig;
        config.Providers ??= new List<AiProvider>();

        var hasLegacy = !string.IsNullOrWhiteSpace(settings.AiEndpoint) ||
                        !string.IsNullOrWhiteSpace(settings.AiModel) ||
                        !string.IsNullOrWhiteSpace(settings.AiKey);
        if (config.Providers.Count == 0 && hasLegacy)
        {
            var provider = new AiProvider
            {
                Name = "默认供应商",
                BaseUrl = settings.AiEndpoint,
                ApiKey = settings.AiKey,
                Models = string.IsNullOrWhiteSpace(settings.AiModel)
                    ? new List<AiModelEntry>()
                    : new List<AiModelEntry> { new() { Id = settings.AiModel, DisplayName = settings.AiModel } }
            };
            config.Providers.Add(provider);
            config.TranslateProviderId = provider.Id;
            config.TranslateModel = settings.AiModel;
            config.OcrProviderId = provider.Id;
            config.OcrModel = settings.AiModel;
            config.TargetLanguage = string.IsNullOrWhiteSpace(config.TargetLanguage)
                ? settings.AiTargetLanguage
                : config.TargetLanguage;
        }

        if (string.IsNullOrWhiteSpace(config.TargetLanguage))
            config.TargetLanguage = "简体中文";
    }
}

internal static class ImageFile
{
    private static readonly HashSet<string> Extensions =
        new(SupportedFormats.All, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string path)
    {
        try
        {
            return File.Exists(path) && Extensions.Contains(Path.GetExtension(path));
        }
        catch
        {
            return false;
        }
    }
}
