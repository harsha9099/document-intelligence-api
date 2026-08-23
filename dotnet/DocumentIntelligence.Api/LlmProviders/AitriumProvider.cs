using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DocumentIntelligence.Api.LlmProviders;

public class AitriumProvider : ILlmProvider
{
    private readonly IConfiguration _config;
    private readonly string _model;

    public string Name => "aitrium";

    public AitriumProvider(IConfiguration config, string? model = null)
    {
        _config = config;
        _model = model
            ?? _config["LlmSettings:AitriumModel"]
            ?? throw new InvalidOperationException("Aitrium model not configured");
    }

    public async Task<Dictionary<string, object>> AnalyzeDocumentAsync(
        string? text = null,
        List<byte[]>? images = null,
        byte[]? rawFileBytes = null,
        string? mimeType = null,
        string? extractionHint = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _config["LlmSettings:AitriumBaseUrl"]
            ?? throw new InvalidOperationException("Aitrium base URL not configured");
        var authToken = _config["LlmSettings:AitriumAuthToken"]
            ?? throw new InvalidOperationException("Aitrium auth token not configured");

        var content = new List<object>();

        if (images is { Count: > 0 })
        {
            foreach (var img in images.Take(20))
            {
                var b64 = Convert.ToBase64String(img);
                content.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = "image/png",
                        data = b64
                    }
                });
            }
        }

        if (rawFileBytes != null && mimeType == "application/pdf")
        {
            var b64 = Convert.ToBase64String(rawFileBytes);
            content.Add(new
            {
                type = "document",
                source = new
                {
                    type = "base64",
                    media_type = "application/pdf",
                    data = b64
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            content.Add(new { type = "text", text = $"Document text content:\n\n{text}" });
        }

        if (content.Count == 0)
        {
            return new Dictionary<string, object>
            {
                ["document_type"] = "unknown",
                ["title"] = "Empty Document",
                ["confidence"] = 0.0,
                ["content"] = new Dictionary<string, object>()
            };
        }

        content.Add(new { type = "text", text = "Analyze this document and return structured JSON." });

        var requestBody = new
        {
            model = _model,
            max_tokens = 8192,
            system = LlmSystemPrompt.Build(extractionHint),
            messages = new[]
            {
                new { role = "user", content }
            }
        };

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", authToken);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var messagesUrl = baseUrl.Contains("/chat/completions")
            ? baseUrl.Replace("/chat/completions", "/messages")
            : baseUrl.TrimEnd('/') + "/messages";

        var response = await httpClient.PostAsync(messagesUrl, jsonContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);

        var raw = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!
            .Trim();

        if (raw.StartsWith("```"))
        {
            raw = raw.Split('\n', 2)[1];
            var lastFence = raw.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                raw = raw[..lastFence].Trim();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object>>(raw)
            ?? throw new InvalidOperationException("LLM returned invalid JSON");
    }
}
