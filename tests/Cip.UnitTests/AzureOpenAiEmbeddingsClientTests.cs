using Azure.Core;
using Integrations.AzureAi;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Cip.UnitTests;

public sealed class AzureOpenAiEmbeddingsClientTests
{
    [Fact]
    public async Task EmbedAsync_SendsConfiguredDimensionsInRequest()
    {
        var handler = new StubHttpMessageHandler(_ => CreateEmbeddingResponse(128));
        using var httpClient = new HttpClient(handler);
        var client = new AzureOpenAiEmbeddingsClient(
            httpClient,
            new TestTokenCredential(),
            Options.Create(new AzureAiOptions
            {
                Endpoint = "https://contoso.openai.azure.com/",
                EmbeddingsDeployment = "embeddings",
                EmbeddingsDimensions = 128
            }));

        var embedding = await client.EmbedAsync("finance analyst", CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        using var payload = JsonDocument.Parse(handler.LastRequestBody);
        Assert.Equal("finance analyst", payload.RootElement.GetProperty("input").GetString());
        Assert.Equal(128, payload.RootElement.GetProperty("dimensions").GetInt32());
        Assert.Equal(128, embedding.Count);
    }

    [Fact]
    public async Task EmbedAsync_ThrowsWhenResponseDimensionsDoNotMatchConfiguration()
    {
        var handler = new StubHttpMessageHandler(_ => CreateEmbeddingResponse(3));
        using var httpClient = new HttpClient(handler);
        var client = new AzureOpenAiEmbeddingsClient(
            httpClient,
            new TestTokenCredential(),
            Options.Create(new AzureAiOptions
            {
                Endpoint = "https://contoso.openai.azure.com/",
                EmbeddingsDeployment = "embeddings",
                EmbeddingsDimensions = 128
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmbedAsync("finance analyst", CancellationToken.None));

        Assert.Contains("did not match configured dimensions 128", exception.Message);
    }

    private static HttpResponseMessage CreateEmbeddingResponse(int dimensions)
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new
                {
                    embedding = Enumerable.Repeat(0.1f, dimensions).ToArray()
                }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return responseFactory(request);
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
