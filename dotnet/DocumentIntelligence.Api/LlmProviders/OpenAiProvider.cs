using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

namespace DocumentIntelligence.Api.LlmProviders;

public class OpenAiProvider : ILlmProvider
{
    private readonly IConfiguration _config;
    private readonly string _model;

    public string Name => "openai";

    public OpenAiProvider(IConfiguration config, string? model = null)
    {
        _config = config;
        _model = model ?? "gpt-4o";
    }

    public async Task<Dictionary<string, object>> AnalyzeDocumentAsync(
        string? text = null,
        List<byte[]>? images = null,
        byte[]? rawFileBytes = null,
        string? mimeType = null,
        string? extractionHint = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["LlmSettings:OpenAiApiKey"]
            ?? throw new InvalidOperationException("OpenAI API key not configured");

        var client = new OpenAIClient(apiKey);
        var chatClient = client.GetChatClient(_model);

        var contentParts = new List<ChatMessageContentPart>();

        if (images is { Count: > 0 })
        {
            foreach (var img in images.Take(20))
            {
                var b64 = Convert.ToBase64String(img);
                contentParts.Add(ChatMessageContentPart.CreateImagePart(
                    new BinaryData(img), "image/png", ChatImageDetailLevel.High));
            }
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            contentParts.Add(ChatMessageContentPart.CreateTextPart($"Document text content:\n\n{text}"));
        }

        if (contentParts.Count == 0)
        {
            return new Dictionary<string, object>
            {
                ["document_type"] = "unknown",
                ["title"] = "Empty Document",
                ["confidence"] = 0.0,
                ["content"] = new Dictionary<string, object>()
            };
        }

        contentParts.Add(ChatMessageContentPart.CreateTextPart("Analyze this document and return structured JSON."));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(LlmSystemPrompt.Build(extractionHint)),
            new UserChatMessage(contentParts)
        };

        var options = new ChatCompletionOptions { MaxOutputTokenCount = 8192 };
        var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        var raw = response.Value.Content[0].Text.Trim();

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
