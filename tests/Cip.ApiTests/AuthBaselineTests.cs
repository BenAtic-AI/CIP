using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cip.ApiTests;

public sealed class AuthBaselineTests
{
    [Fact]
    public async Task GetHealth_AllowsAnonymous_WhenAuthIsEnabled()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetProfiles_ReturnsUnauthorized_WhenAuthIsEnabledAndNoBearerTokenIsProvided()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/profiles?tenantId=tenant-auth");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfiles_Succeeds_WhenAuthIsEnabledAndTestSchemeAuthenticatesRequest()
    {
        using var factory = new AuthWebApplicationFactory(useTestAuthentication: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "integration-test");

        var response = await client.GetAsync("/api/profiles?tenantId=tenant-auth");

        response.EnsureSuccessStatusCode();
    }

    private sealed class AuthWebApplicationFactory(bool useTestAuthentication = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Entra:Enabled"] = bool.TrueString,
                    ["Entra:Authority"] = "https://login.microsoftonline.com/test-tenant-id/v2.0",
                    ["Entra:Audience"] = "api://cip-api-tests",
                    ["Entra:RequireHttpsMetadata"] = bool.FalseString
                });
            });

            if (!useTestAuthentication)
            {
                return;
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorizationHeader) ||
                authorizationHeader.Scheme != SchemeName ||
                authorizationHeader.Parameter != "integration-test")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "Integration Test User")
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
