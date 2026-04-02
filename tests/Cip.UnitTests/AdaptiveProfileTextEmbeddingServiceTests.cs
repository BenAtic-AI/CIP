using Azure;
using Azure.Core;
using Integrations.AzureAi;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using Xunit;

namespace Cip.UnitTests;

public sealed class AdaptiveProfileTextEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_FallsBackToDeterministic_WhenLiveEmbeddingsAreDisabled()
    {
        var fallback = new DeterministicProfileTextEmbeddingService();
        var handler = new CountingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AdaptiveProfileTextEmbeddingService(
            fallback,
            new AzureOpenAiEmbeddingsClient(httpClient, new TestTokenCredential(), Options.Create(new AzureAiOptions
            {
                Endpoint = "https://contoso.openai.azure.com/",
                EmbeddingsDeployment = "embeddings",
                UseLiveEmbeddings = false
            })),
            Options.Create(new AzureAiOptions
            {
                Endpoint = "https://contoso.openai.azure.com/",
                EmbeddingsDeployment = "embeddings",
                UseLiveEmbeddings = false
            }),
            NullLogger<AdaptiveProfileTextEmbeddingService>.Instance);

        var expected = await fallback.EmbedAsync("finance analyst", CancellationToken.None);
        var actual = await service.EmbedAsync("finance analyst", CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task EmbedAsync_FallsBackToDeterministic_WhenLiveEmbeddingsConfigurationIsIncomplete()
    {
        var fallback = new DeterministicProfileTextEmbeddingService();
        var handler = new CountingHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AdaptiveProfileTextEmbeddingService(
            fallback,
            new AzureOpenAiEmbeddingsClient(httpClient, new TestTokenCredential(), Options.Create(new AzureAiOptions
            {
                Endpoint = "https://contoso.openai.azure.com/",
                EmbeddingsDeployment = string.Empty,
                UseLiveEmbeddings = true
            })),
            Options.Create(new AzureAiOptions
            {
                Endpoint = "https://contoso.openai.azure.com/",
                EmbeddingsDeployment = string.Empty,
                UseLiveEmbeddings = true
            }),
            NullLogger<AdaptiveProfileTextEmbeddingService>.Instance);

        var expected = await fallback.EmbedAsync("finance analyst", CancellationToken.None);
        var actual = await service.EmbedAsync("finance analyst", CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.Equal(0, handler.CallCount);
    }

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
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
