using System.Text.Json;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;

namespace DocumentIntelligence.Api.LlmProviders;

public class BedrockProvider : ILlmProvider
{
    private readonly IConfiguration _config;
    private readonly string _model;

    public string Name => "bedrock";

    public BedrockProvider(IConfiguration config, string? model = null)
    {
        _config = config;
        _model = model
            ?? _config["LlmSettings:BedrockModel"]
            ?? "anthropic.claude-sonnet-4-20250514-v1:0";
    }

    public async Task<Dictionary<string, object>> AnalyzeDocumentAsync(
        string? text = null,
        List<byte[]>? images = null,
        byte[]? rawFileBytes = null,
        string? mimeType = null,
        string? extractionHint = null,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();

        var content = new List<ContentBlock>();

        if (images is { Count: > 0 })
        {
            foreach (var img in images.Take(20))
            {
                content.Add(new ContentBlock
                {
                    Image = new ImageBlock
                    {
                        Format = ImageFormat.Png,
                        Source = new ImageSource
                        {
                            Bytes = new MemoryStream(img)
                        }
                    }
                });
            }
        }

        if (rawFileBytes != null && mimeType == "application/pdf")
        {
            content.Add(new ContentBlock
            {
                Document = new DocumentBlock
                {
                    Format = DocumentFormat.Pdf,
                    Name = "document",
                    Source = new DocumentSource
                    {
                        Bytes = new MemoryStream(rawFileBytes)
                    }
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            content.Add(new ContentBlock { Text = $"Document text content:\n\n{text}" });
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

        content.Add(new ContentBlock { Text = "Analyze this document and return structured JSON." });

        var request = new ConverseRequest
        {
            ModelId = _model,
            Messages =
            [
                new Message
                {
                    Role = ConversationRole.User,
                    Content = content
                }
            ],
            System =
            [
                new SystemContentBlock { Text = LlmSystemPrompt.Build(extractionHint) }
            ],
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 8192
            }
        };

        var response = await client.ConverseAsync(request, cancellationToken);

        var raw = response.Output.Message.Content
            .First(c => c.Text != null).Text.Trim();

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

    private AmazonBedrockRuntimeClient CreateClient()
    {
        var region = _config["LlmSettings:BedrockRegion"] ?? "eu-central-1";
        var accessKey = _config["LlmSettings:BedrockAccessKey"];
        var secretKey = _config["LlmSettings:BedrockSecretKey"];
        var sessionToken = _config["LlmSettings:BedrockSessionToken"];
        var endpoint = _config["LlmSettings:BedrockEndpoint"];

        var regionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);

        AmazonBedrockRuntimeConfig clientConfig = new()
        {
            RegionEndpoint = regionEndpoint
        };

        if (!string.IsNullOrEmpty(endpoint))
        {
            clientConfig.ServiceURL = endpoint;
        }

        if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
        {
            var credentials = string.IsNullOrEmpty(sessionToken)
                ? (AWSCredentials)new BasicAWSCredentials(accessKey, secretKey)
                : new SessionAWSCredentials(accessKey, secretKey, sessionToken);

            return new AmazonBedrockRuntimeClient(credentials, clientConfig);
        }

        // Falls back to default credential chain (env vars, IAM role, ~/.aws/credentials)
        return new AmazonBedrockRuntimeClient(clientConfig);
    }
}
