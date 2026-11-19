using System.Text.Json.Serialization;

namespace WBImageViewer.Models;

/// <summary>Upstream wire protocol used by a provider.</summary>
public enum AiApiProtocol
{
    ChatCompletions,
    Responses
}

public sealed class AiModelEntry
{
    public string Id { get; set; } = "";
    public string? DisplayName { get; set; }

    /// <summary>UI-only check state; deliberately not persisted.</summary>
    [JsonIgnore]
    public bool Selected { get; set; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Id ? Id : $"{DisplayName} ({Id})";
}

/// <summary>
/// One supplier configuration, modelled after Codex++ supplier management:
/// name / protocol / base URL / key / a selectable model catalogue.
/// </summary>
public sealed class AiProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新供应商";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AiApiProtocol Protocol { get; set; } = AiApiProtocol.ChatCompletions;

    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public List<AiModelEntry> Models { get; set; } = new();
    public bool Enabled { get; set; } = true;

    public AiProvider Clone()
    {
        return new AiProvider
        {
            Id = Id,
            Name = Name,
            Protocol = Protocol,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            Enabled = Enabled,
            Models = Models.Select(m => new AiModelEntry { Id = m.Id, DisplayName = m.DisplayName }).ToList()
        };
    }
}

/// <summary>
/// All suppliers plus the two independent default bindings. Translation and OCR are
/// deliberately separate: they are different capabilities and may use different models.
/// </summary>
public sealed class AiProviderConfig
{
    public List<AiProvider> Providers { get; set; } = new();
    public string TranslateProviderId { get; set; } = "";
    public string TranslateModel { get; set; } = "";
    public string OcrProviderId { get; set; } = "";
    public string OcrModel { get; set; } = "";
    public string TargetLanguage { get; set; } = "简体中文";

    public AiProviderConfig Clone()
    {
        return new AiProviderConfig
        {
            Providers = Providers.Select(p => p.Clone()).ToList(),
            TranslateProviderId = TranslateProviderId,
            TranslateModel = TranslateModel,
            OcrProviderId = OcrProviderId,
            OcrModel = OcrModel,
            TargetLanguage = TargetLanguage
        };
    }

    public AiProvider? FindProvider(string id) =>
        Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public (AiProvider Provider, string Model)? Resolve(bool forTranslation)
    {
        var id = forTranslation ? TranslateProviderId : OcrProviderId;
        var model = forTranslation ? TranslateModel : OcrModel;
        var provider = FindProvider(id);
        if (provider == null || !provider.Enabled || string.IsNullOrWhiteSpace(provider.BaseUrl) ||
            string.IsNullOrWhiteSpace(model)) return null;
        return (provider, model);
    }
}
