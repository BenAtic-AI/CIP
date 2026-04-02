using Cip.Api.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cip.ApiTests;

public sealed class EntraAuthenticationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddEntraAuthentication_AcceptsApiUriAudienceAndBareClientId()
    {
        var options = ConfigureJwtBearerOptions("api://b38edfd4-87c1-4577-a207-6187247d7e5f");

        Assert.Equal("api://b38edfd4-87c1-4577-a207-6187247d7e5f", options.Audience);
        Assert.Equal(
            [
                "api://b38edfd4-87c1-4577-a207-6187247d7e5f",
                "b38edfd4-87c1-4577-a207-6187247d7e5f"
            ],
            options.TokenValidationParameters.ValidAudiences);
    }

    [Fact]
    public void AddEntraAuthentication_KeepsSingleAudienceWhenAudienceIsNotApiUri()
    {
        var options = ConfigureJwtBearerOptions("b38edfd4-87c1-4577-a207-6187247d7e5f");

        Assert.Equal("b38edfd4-87c1-4577-a207-6187247d7e5f", options.Audience);
        Assert.Equal(["b38edfd4-87c1-4577-a207-6187247d7e5f"], options.TokenValidationParameters.ValidAudiences);
    }

    private static JwtBearerOptions ConfigureJwtBearerOptions(string audience)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Entra:Authority"] = "https://login.microsoftonline.com/test-tenant-id/v2.0",
                ["Entra:Audience"] = audience,
                ["Entra:RequireHttpsMetadata"] = bool.FalseString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddEntraAuthentication(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
