using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenExCards.Generation;

namespace TenExCards.Tests;

public class GeminiConfigurationTests(TenExCardsWebApplicationFactory factory)
    : IClassFixture<TenExCardsWebApplicationFactory>
{
    private GeminiOptions Options()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<GeminiOptions>>().Value;
    }

    [Fact]
    public void Models_AreConfiguredAndDistinct()
    {
        var models = Options().Models;

        models.Should().NotBeEmpty("the generator throws at construction on an empty rotation");
        models.Should().OnlyHaveUniqueItems("a repeated model shares one quota bucket, so the "
            + "duplicate slot falls through immediately and buys nothing");
        models.Should().AllSatisfy(m => m.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Models_LeadWithTheHighestQualityModel()
    {
        // The rotation degrades on quota, so order is a quality decision: the 20/day model is
        // preferred and the 500/day ones are the fallback, never the reverse.
        Options().Models[0].Should().Be("gemini-3.8-flash");
    }

    [Fact]
    public void Endpoint_IsGeminisOpenAiCompatibleBaseUrl()
    {
        Options().Endpoint.Should().Be("https://generativelanguage.googleapis.com/v1beta/openai/");
    }
}
