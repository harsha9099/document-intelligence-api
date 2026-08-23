using DocumentIntelligence.Api.LlmProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace DocumentIntelligence.Tests;

public class LlmProviderFactoryTests
{
    private static LlmProviderFactory CreateFactory(
        Dictionary<string, string?>? config = null,
        bool isDevelopment = true)
    {
        var configBuilder = new ConfigurationBuilder();
        if (config != null)
            configBuilder.AddInMemoryCollection(config);
        var configuration = configBuilder.Build();

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName)
            .Returns(isDevelopment ? Environments.Development : Environments.Production);

        return new LlmProviderFactory(configuration, env.Object);
    }

    [Fact]
    public void Create_ReturnsMockProvider_WhenNamedExplicitly()
    {
        var factory = CreateFactory();
        var provider = factory.Create("mock");
        Assert.IsType<MockProvider>(provider);
    }

    [Fact]
    public void Create_ReturnsMockProvider_InDevelopment_WhenNoCredentials()
    {
        var factory = CreateFactory(isDevelopment: true);
        var provider = factory.Create("anthropic");
        Assert.IsType<MockProvider>(provider);
    }

    [Fact]
    public void Create_ReturnsAnthropicProvider_WhenKeyPresent()
    {
        var factory = CreateFactory(
            new Dictionary<string, string?> { ["LlmSettings:AnthropicApiKey"] = "sk-test" },
            isDevelopment: true);
        var provider = factory.Create("anthropic");
        Assert.IsType<AnthropicProvider>(provider);
    }

    [Fact]
    public void Create_ThrowsArgumentException_ForUnknownProvider()
    {
        var factory = CreateFactory(
            new Dictionary<string, string?> { ["LlmSettings:AnthropicApiKey"] = "sk-test" });
        Assert.Throws<ArgumentException>(() => factory.Create("banana"));
    }

    [Fact]
    public void Create_UsesMockByDefault_InDevelopment_WithNoConfig()
    {
        var factory = CreateFactory(isDevelopment: true);
        var provider = factory.Create();
        Assert.IsType<MockProvider>(provider);
    }
}
