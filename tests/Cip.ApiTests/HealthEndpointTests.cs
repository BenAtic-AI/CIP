using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cip.ApiTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsSuccessStatusCode()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
    }
}
