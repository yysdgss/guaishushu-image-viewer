using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using WBImageViewer.Models;

namespace WBImageViewer.Services;

internal sealed record AiTestResult(bool Ok, long ElapsedMs, string Message);

/// <summary>
/// OpenAI-compatible multimodal client. Every request resolves its provider and model
/// by purpose (translation vs OCR), so the two capabilities can use different suppliers.
/// Also exposes supplier-style helpers used by the configuration centre: fetching the
/// upstream model catalogue, connection timing and Codex++ local-route discovery.
/// </summary>
internal static class AiService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromMilliseconds(1200) };

    public static bool IsConfigured =>
        App.Settings.AiConfig.Resolve(forTranslation: true) != null ||
        App.Settings.AiConfig.Resolve(forTranslation: false) != null;

    public static bool HasTranslation => App.Settings.AiConfig.Resolve(true) != null;
    public static bool HasOcr => App.Settings.AiConfig.Resolve(false) != null;

    /// <summary>Ask the model to transcribe all visible text (printed and handwritten).</summary>
    public static Task<string> OcrAsync(BitmapSource image) =>
        CompleteAsync(
            "请识别图片中的所有文字，包括印刷体和手写体。按阅读顺序只输出文字内容本身，不要解释、不要加引号、不要 Markdown。若没有文字则输出空字符串。",
            image, forTranslation: false);

    /// <summary>Translate the text visible in the image into the configured target language.</summary>
    public static Task<string> TranslateImageAsync(BitmapSource image) =>
        CompleteAsync(
            $"请把图片中的所有文字翻译成 {App.Settings.AiConfig.TargetLanguage}。只输出译文，保持原有段落结构，不要解释或加引号。",
            image, forTranslation: true);

    /// <summary>Translate a plain text snippet.</summary>
    public static async Task<string> TranslateTextAsync(string text)
    {
        var instruction = $"把下面的文字翻译成 {App.Settings.AiConfig.TargetLanguage}，只输出译文，不要解释：\n\n{text}";
        var target = App.Settings.AiConfig.Resolve(true)
                     ?? throw new InvalidOperationException("尚未配置翻译默认模型。请先在后台设置 → 大模型中配置。");
        return await RequestAsync(target.Provider, target.Model,
            BuildTextContent(instruction)).ConfigureAwait(false);
    }

    private static Task<string> CompleteAsync(string instruction, BitmapSource image, bool forTranslation)
    {
        var target = App.Settings.AiConfig.Resolve(forTranslation)
                     ?? throw new InvalidOperationException(
                         (forTranslation ? "尚未配置翻译默认模型" : "尚未配置识别默认模型") +
                         "。请先在后台设置 → 大模型中配置。");
        var dataUrl = ToDataUrl(image);
        var content = new
        {
            model = target.Model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = instruction },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            },
            max_tokens = 1024,
            temperature = 0.2
        };
        return RequestAsync(target.Provider, target.Model, content);
    }

    private static object BuildTextContent(string instruction) => new
    {
        messages = new[] { new { role = "user", content = instruction } },
        max_tokens = 1024,
        temperature = 0.2
    };

    // ---------------- endpoints ----------------

    /// <summary>Normalise a user-entered Base URL to the /v1 root (Codex++ style).</summary>
    public static string NormalizeBaseUrl(string input)
    {
        var url = (input ?? string.Empty).Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/chat/completions".Length].TrimEnd('/');
        if (url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/responses".Length].TrimEnd('/');
        if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/models".Length].TrimEnd('/');
        return url;
    }

    private static string ChatEndpoint(AiProvider provider)
    {
        var baseUrl = NormalizeBaseUrl(provider.BaseUrl);
        var suffix = provider.Protocol == AiApiProtocol.Responses ? "/responses" : "/chat/completions";
        // A URL that already ends in /v1 (or any explicit version segment) is used verbatim.
        return baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? baseUrl + suffix
            : baseUrl + "/v1" + suffix;
    }

    private static string ModelsEndpoint(string baseUrl) =>
        NormalizeBaseUrl(baseUrl).EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? NormalizeBaseUrl(baseUrl) + "/models"
            : NormalizeBaseUrl(baseUrl) + "/v1/models";

    private static void ApplyAuth(HttpRequestMessage request, string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
    }

    // ---------------- chat ----------------

    private static async Task<string> RequestAsync(AiProvider provider, string model, object payload)
    {
        var endpoint = ChatEndpoint(provider);
        var json = JsonSerializer.Serialize(WithModel(payload, model));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        ApplyAuth(request, provider.ApiKey);

        using var response = await Client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)response.StatusCode}: {Truncate(body, 400)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (provider.Protocol == AiApiProtocol.Responses)
        {
            if (root.TryGetProperty("output", out var output))
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content)) continue;
                    foreach (var part in content.EnumerateArray())
                        if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                            return text.GetString() ?? string.Empty;
                }
            }
            throw new InvalidOperationException("Responses API 返回格式无法识别：" + Truncate(body, 300));
        }

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");
            if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                return c.GetString() ?? string.Empty;
        }
        throw new InvalidOperationException("API 返回格式无法识别：" + Truncate(body, 300));
    }

    private static string WithModel(object payload, string model)
    {
        // Anonymous payloads do not include `model` (added in the caller helpers);
        // inject it via a dictionary so both protocol families stay uniform.
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, object?>();
        foreach (var property in doc.RootElement.EnumerateObject())
            map[property.Name] = JsonSerializer.Deserialize<JsonElement>(property.Value.GetRawText());
        map["model"] = model;
        return JsonSerializer.Serialize(map);
    }

    // ---------------- supplier helpers ----------------

    /// <summary>Fetch the upstream catalogue. Tries /v1/models first, then the raw /models path.</summary>
    public static async Task<List<AiModelEntry>> FetchModelsAsync(
        string baseUrl, string apiKey, CancellationToken ct = default)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        var urls = new List<string> { ModelsEndpoint(normalized) };
        if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            urls.Add(normalized + "/models");

        var errors = new List<string>();
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyAuth(request, apiKey);
                using var response = await ProbeClient.SendAsync(request, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{(int)response.StatusCode} {Truncate(body, 120)}");
                    continue;
                }
                using var doc = JsonDocument.Parse(body);
                var models = new List<AiModelEntry>();
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString()
                            : item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                                ? nameProp.GetString()
                                : null;
                        if (!string.IsNullOrWhiteSpace(id))
                            models.Add(new AiModelEntry { Id = id!, DisplayName = id });
                    }
                }
                if (models.Count > 0) return models;
                errors.Add("模型列表为空");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        throw new InvalidOperationException("从上游获取模型失败：" + string.Join("；", errors));
    }

    /// <summary>Send a minimal request and measure round-trip latency.</summary>
    public static async Task<AiTestResult> TestAsync(AiProvider provider, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl) || string.IsNullOrWhiteSpace(model))
            return new AiTestResult(false, 0, "请先填写接口地址与测试模型。");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            object payload;
            if (provider.Protocol == AiApiProtocol.Responses)
                payload = new { input = "ping", max_output_tokens = 1 };
            else
                payload = new { messages = new[] { new { role = "user", content = "ping" } }, max_tokens = 1 };
            _ = await RequestWithClientAsync(Client, provider, model, payload, ct).ConfigureAwait(false);
            stopwatch.Stop();
            return new AiTestResult(true, stopwatch.ElapsedMilliseconds,
                $"连接成功 · {model} · {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new AiTestResult(false, stopwatch.ElapsedMilliseconds, "测试已取消");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new AiTestResult(false, stopwatch.ElapsedMilliseconds,
                $"连接失败（{stopwatch.ElapsedMilliseconds} ms）：{Truncate(ex.Message, 240)}");
        }
    }

    private static async Task<string> RequestWithClientAsync(HttpClient client, AiProvider provider,
        string model, object payload, CancellationToken ct)
    {
        var json = WithModel(payload, model);
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint(provider))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        ApplyAuth(request, provider.ApiKey);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        return body;
    }

    /// <summary>
    /// One-click Codex++ local-route discovery. Probes the usual local gateway ports
    /// (Codex++/CliProxyApi 8317, LM Studio, Ollama, common OpenAI-compatible gateways)
    /// and returns every reachable Base URL together with its catalogue.
    /// </summary>
    public static async Task<List<(string BaseUrl, List<AiModelEntry> Models)>> ProbeLocalRoutesAsync(
        CancellationToken ct = default)
    {
        string[] candidates =
        {
            "http://127.0.0.1:8317/v1",   // Codex++ / CliProxyApi local route
            "http://127.0.0.1:8318/v1",
            "http://127.0.0.1:1234/v1",   // LM Studio
            "http://127.0.0.1:11434/v1",  // Ollama
            "http://127.0.0.1:8080/v1",
            "http://127.0.0.1:8000/v1",
            "http://127.0.0.1:3000/v1"
        };

        var found = new List<(string, List<AiModelEntry>)>();
        var tasks = candidates.Select(async url =>
        {
            try
            {
                var models = await FetchModelsAsync(url, "", ct).ConfigureAwait(false);
                return (Url: url, Models: models, Ok: models.Count > 0);
            }
            catch { return (Url: url, Models: new List<AiModelEntry>(), Ok: false); }
        });
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var r in results.Where(r => r.Ok)) found.Add((r.Url, r.Models));
        return found;
    }

    private static string ToDataUrl(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
