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

    [Fact]
    public void AddEntraAuthentication_AcceptsV2IssuerAndMatchingLegacyStsIssuer()
    {
        const string authority = "https://login.microsoftonline.com/test-tenant-id/v2.0";

        var options = ConfigureJwtBearerOptions("api://b38edfd4-87c1-4577-a207-6187247d7e5f", authority);

        Assert.Equal(authority, options.Authority);
        Assert.Equal(
            [
                authority,
                "https://sts.windows.net/test-tenant-id/"
            ],
            options.TokenValidationParameters.ValidIssuers);
    }

    [Fact]
    public void AddEntraAuthentication_NormalizesAuthorityTrailingSlashWhenDerivingValidIssuers()
    {
        var options = ConfigureJwtBearerOptions(
            "api://b38edfd4-87c1-4577-a207-6187247d7e5f",
            "https://login.microsoftonline.com/test-tenant-id/v2.0/");

        Assert.Contains("https://login.microsoftonline.com/test-tenant-id/v2.0", options.TokenValidationParameters.ValidIssuers);
        Assert.Contains("https://sts.windows.net/test-tenant-id/", options.TokenValidationParameters.ValidIssuers);
    }

    [Fact]
    public void AddEntraAuthentication_LeavesValidIssuersEmptyWhenAuthorityIsMissing()
    {
        var options = ConfigureJwtBearerOptions("api://b38edfd4-87c1-4577-a207-6187247d7e5f", authority: null);

        Assert.Null(options.Authority);
        Assert.Empty(options.TokenValidationParameters.ValidIssuers ?? []);
    }

    private static JwtBearerOptions ConfigureJwtBearerOptions(string audience, string? authority = "https://login.microsoftonline.com/test-tenant-id/v2.0")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Entra:Audience"] = audience,
            ["Entra:RequireHttpsMetadata"] = bool.FalseString
        };

        if (authority is not null)
        {
            settings["Entra:Authority"] = authority;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddEntraAuthentication(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        return serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
