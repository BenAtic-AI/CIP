using Azure.Core;
using Cip.Contracts.Constants;
using Cip.Domain.Documents;
using Integrations.AzureAi;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Xunit;

namespace Cip.UnitTests;

public sealed class AdaptiveProfileCardGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_FallsBackToDeterministic_WhenLiveCardsAreDisabled()
    {
        var fallback = new DeterministicProfileCardGenerationService();
        var handler = new CountingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new AzureAiOptions
        {
            Endpoint = "https://contoso.openai.azure.com/",
            ChatDeployment = "chat",
            UseLiveProfileCards = false
        });

        var service = new AdaptiveProfileCardGenerationService(
            fallback,
            new AzureOpenAiProfileCardClient(httpClient, new TestTokenCredential(), options),
            options,
            NullLogger<AdaptiveProfileCardGenerationService>.Instance);

        var profile = CreateProfile();
        var expected = await fallback.GenerateAsync(profile, CancellationToken.None);
        var actual = await service.GenerateAsync(profile, CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FallsBackToDeterministic_WhenLiveCardsConfigurationIsIncomplete()
    {
        var fallback = new DeterministicProfileCardGenerationService();
        var handler = new CountingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new AzureAiOptions
        {
            Endpoint = "https://contoso.openai.azure.com/",
            ChatDeployment = string.Empty,
            UseLiveProfileCards = true
        });

        var service = new AdaptiveProfileCardGenerationService(
            fallback,
            new AzureOpenAiProfileCardClient(httpClient, new TestTokenCredential(), options),
            options,
            NullLogger<AdaptiveProfileCardGenerationService>.Instance);

        var profile = CreateProfile();
        var expected = await fallback.GenerateAsync(profile, CancellationToken.None);
        var actual = await service.GenerateAsync(profile, CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FallsBackToDeterministic_WhenLiveCardsFail()
    {
        var fallback = new DeterministicProfileCardGenerationService();
        var handler = new CountingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom", Encoding.UTF8, "text/plain")
        });
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new AzureAiOptions
        {
            Endpoint = "https://contoso.openai.azure.com/",
            ChatDeployment = "chat",
            UseLiveProfileCards = true
        });

        var service = new AdaptiveProfileCardGenerationService(
            fallback,
            new AzureOpenAiProfileCardClient(httpClient, new TestTokenCredential(), options),
            options,
            NullLogger<AdaptiveProfileCardGenerationService>.Instance);

        var profile = CreateProfile();
        var expected = await fallback.GenerateAsync(profile, CancellationToken.None);
        var actual = await service.GenerateAsync(profile, CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(1, handler.CallCount);
    }

    private static ProfileDocument CreateProfile()
        => new(
            Id: "pro_card",
            TenantId: "tenant-card",
            ProfileId: "pro_card",
            DocType: Constants.Profiles.Shell,
            Status: Constants.Profiles.PendingReview,
            Identities: [new ProfileIdentity("Email", "analyst@contoso.com", "unit-test")],
            Traits: [new ProfileTrait("Department", "Finance", 0.98m)],
            ProfileCard: string.Empty,
            Synopsis: "Pending PersonObserved materialization from unit-test.",
            SynopsisVector: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    private sealed class CountingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TestTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("token", DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(5)));
    }
}
