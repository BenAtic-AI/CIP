using Azure.Core;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integrations.AzureAi;

public sealed class AzureOpenAiEmbeddingsClient(HttpClient httpClient, TokenCredential credential, IOptions<AzureAiOptions> options)
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";
    private const string EmbeddingsApiVersion = "2024-10-21";

    public async Task<IReadOnlyCollection<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var azureAiOptions = options.Value;
        if (!azureAiOptions.HasEmbeddingsConfiguration())
        {
            throw new InvalidOperationException("Live Azure AI embeddings are not fully configured.");
        }

        if (azureAiOptions.EmbeddingsDimensions <= 0)
        {
            throw new InvalidOperationException("Azure AI embeddings dimensions must be greater than zero.");
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext([CognitiveServicesScope]), cancellationToken);
        var requestUri = BuildEmbeddingsUri(azureAiOptions.Endpoint, azureAiOptions.EmbeddingsDeployment);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new EmbeddingsRequest(text, azureAiOptions.EmbeddingsDimensions));
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Content.Headers.ContentType.CharSet = Encoding.UTF8.WebName;

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Azure OpenAI embeddings request failed with status code {(int)response.StatusCode}. Response: {body}",
                inner: null,
                response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var responsePayload = await JsonSerializer.DeserializeAsync<EmbeddingsResponse>(responseStream, cancellationToken: cancellationToken);
        var embedding = responsePayload?.Data?.FirstOrDefault()?.Embedding;

        if (embedding is null || embedding.Length == 0)
        {
            throw new InvalidOperationException("Azure OpenAI embeddings response did not include an embedding vector.");
        }

        if (embedding.Length != azureAiOptions.EmbeddingsDimensions)
        {
            throw new InvalidOperationException(
                $"Azure OpenAI embeddings response length {embedding.Length} did not match configured dimensions {azureAiOptions.EmbeddingsDimensions}.");
        }

        return embedding;
    }

    private static Uri BuildEmbeddingsUri(string endpoint, string deployment)
        => new(new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute), $"openai/deployments/{Uri.EscapeDataString(deployment)}/embeddings?api-version={EmbeddingsApiVersion}");

    private sealed record EmbeddingsRequest(
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("dimensions")] int Dimensions);

    private sealed record EmbeddingsResponse([property: JsonPropertyName("data")] EmbeddingsResponseItem[] Data);

    private sealed record EmbeddingsResponseItem([property: JsonPropertyName("embedding")] float[] Embedding);
}
