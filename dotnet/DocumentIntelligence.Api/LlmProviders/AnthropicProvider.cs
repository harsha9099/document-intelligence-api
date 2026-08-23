using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace DocumentIntelligence.Api.LlmProviders;

public class AnthropicProvider : ILlmProvider
{
    private readonly IConfiguration _config;
    private readonly string _model;

    public string Name => "anthropic";
    public string ModelUsed => _model;

    public AnthropicProvider(IConfiguration config, string? model = null)
    {
        _config = config;
        _model = model ?? "claude-sonnet-4-20250514";
    }

    public async Task<Dictionary<string, object>> AnalyzeDocumentAsync(
        string? text = null,
        List<byte[]>? images = null,
        byte[]? rawFileBytes = null,
        string? mimeType = null,
        string? extractionHint = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["LlmSettings:AnthropicApiKey"]
            ?? throw new InvalidOperationException("Anthropic API key not configured");

        var client = new AnthropicClient(apiKey);
        var content = new List<ContentBase>();

        if (images is { Count: > 0 })
        {
            foreach (var img in images.Take(20))
            {
                var b64 = Convert.ToBase64String(img);
                content.Add(new ImageContent
                {
                    Source = new ImageSource
                    {
                        MediaType = "image/png",
                        Data = b64
                    }
                });
            }
        }

        if (rawFileBytes != null && mimeType == "application/pdf")
        {
            var b64 = Convert.ToBase64String(rawFileBytes);
            content.Add(new ImageContent
            {
                Source = new ImageSource
                {
                    MediaType = "application/pdf",
                    Data = b64
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            content.Add(new TextContent { Text = $"Document text content:\n\n{text}" });
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

        content.Add(new TextContent { Text = "Analyze this document and return structured JSON." });

        var parameters = new MessageParameters
        {
            Model = _model,
            MaxTokens = 8192,
            System = [new SystemMessage(LlmSystemPrompt.Build(extractionHint))],
            Messages = [new Message(RoleType.User, content)]
        };

        var response = await client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
        var raw = response.Content.OfType<TextContent>().First().Text.Trim();

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
