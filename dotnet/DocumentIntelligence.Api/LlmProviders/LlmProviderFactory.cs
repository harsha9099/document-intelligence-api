namespace DocumentIntelligence.Api.LlmProviders;

public interface ILlmProviderFactory
{
    ILlmProvider Create(string? providerName = null, string? model = null, string? filenameHint = null);
}

public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public LlmProviderFactory(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    public ILlmProvider Create(string? providerName = null, string? model = null, string? filenameHint = null)
    {
        var name = (providerName ?? _config["LlmSettings:DefaultProvider"] ?? "anthropic").ToLowerInvariant();

        // Auto-fallback to mock in Development when no credentials are configured
        if (_env.IsDevelopment() && name != "mock" && !HasAnyCredentials())
            name = "mock";

        return name switch
        {
            "anthropic" => new AnthropicProvider(_config, model),
            "aitrium" => new AitriumProvider(_config, model),
            "bedrock" => new BedrockProvider(_config, model),
            "openai" => new OpenAiProvider(_config, model),
            "mock" => new MockProvider(filenameHint),
            _ => throw new ArgumentException($"Unknown LLM provider: {name}. Available: anthropic, aitrium, bedrock, openai, mock")
        };
    }

    private bool HasAnyCredentials() =>
        !string.IsNullOrEmpty(_config["LlmSettings:AnthropicApiKey"]) ||
        !string.IsNullOrEmpty(_config["LlmSettings:OpenAiApiKey"]) ||
        !string.IsNullOrEmpty(_config["LlmSettings:AitriumAuthToken"]) ||
        !string.IsNullOrEmpty(_config["LlmSettings:BedrockAccessKey"]);
}
