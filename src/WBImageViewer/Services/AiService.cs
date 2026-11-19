using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace WBImageViewer.Services;

/// <summary>
/// OpenAI-compatible multimodal API client. Used by the screenshot / region tools for
/// translation, printed &amp; handwriting OCR. Endpoint, key and model are user-configurable
/// so any provider exposing the chat/completions contract works (OpenAI, Azure, local, etc.).
/// </summary>
internal static class AiService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(App.Settings.AiEndpoint) &&
        !string.IsNullOrWhiteSpace(App.Settings.AiModel);

    /// <summary>Ask the model to transcribe all visible text (printed and handwritten).</summary>
    public static Task<string> OcrAsync(BitmapSource image) =>
        CompleteAsync(
            "请识别图片中的所有文字，包括印刷体和手写体。按阅读顺序只输出文字内容本身，不要解释、不要加引号、不要 Markdown。若没有文字则输出空字符串。",
            image);

    /// <summary>Translate the text visible in the image into the configured target language.</summary>
    public static Task<string> TranslateImageAsync(BitmapSource image) =>
        CompleteAsync(
            $"请把图片中的所有文字翻译成 {App.Settings.AiTargetLanguage}。只输出译文，保持原有段落结构，不要解释或加引号。",
            image);

    /// <summary>Translate a plain text snippet.</summary>
    public static async Task<string> TranslateTextAsync(string text)
    {
        var content = BuildTextContent(
            $"把下面的文字翻译成 {App.Settings.AiTargetLanguage}，只输出译文，不要解释：\n\n{text}");
        return await RequestAsync(content).ConfigureAwait(false);
    }

    private static Task<string> CompleteAsync(string instruction, BitmapSource image)
    {
        var dataUrl = ToDataUrl(image);
        var content = new
        {
            model = App.Settings.AiModel,
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
        return RequestAsync(content);
    }

    private static object BuildTextContent(string instruction) => new
    {
        model = App.Settings.AiModel,
        messages = new[] { new { role = "user", content = instruction } },
        max_tokens = 1024,
        temperature = 0.2
    };

    private static async Task<string> RequestAsync(object payload)
    {
        var endpoint = App.Settings.AiEndpoint.TrimEnd('/');
        if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint += "/v1/chat/completions";

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var key = App.Settings.AiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await Client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {(int)response.StatusCode}: {Truncate(body, 400)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");
            if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                return c.GetString() ?? string.Empty;
        }
        throw new InvalidOperationException("API 返回格式无法识别：" + Truncate(body, 300));
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
