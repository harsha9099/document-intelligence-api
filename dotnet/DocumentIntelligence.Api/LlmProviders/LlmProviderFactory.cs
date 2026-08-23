namespace DocumentIntelligence.Api.LlmProviders;

public interface ILlmProviderFactory
{
    ILlmProvider Create(string? providerName = null, string? model = null);
}

public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IConfiguration _config;

    public LlmProviderFactory(IConfiguration config)
    {
        _config = config;
    }

    public ILlmProvider Create(string? providerName = null, string? model = null)
    {
        var name = (providerName ?? _config["LlmSettings:DefaultProvider"] ?? "anthropic").ToLowerInvariant();

        return name switch
        {
            "anthropic" => new AnthropicProvider(_config, model),
            "aitrium" => new AitriumProvider(_config, model),
            "bedrock" => new BedrockProvider(_config, model),
            "openai" => new OpenAiProvider(_config, model),
            _ => throw new ArgumentException($"Unknown LLM provider: {name}. Available: anthropic, aitrium, bedrock, openai")
        };
    }
}
