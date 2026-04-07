using Azure.Core;
using Cip.Domain.Documents;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integrations.AzureAi;

public sealed class AzureOpenAiProfileCardClient(HttpClient httpClient, TokenCredential credential, IOptions<AzureAiOptions> options)
{
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";
    private const string ChatApiVersion = "2024-10-21";

    public async Task<string> GenerateAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        var azureAiOptions = options.Value;
        if (!azureAiOptions.HasChatConfiguration())
        {
            throw new InvalidOperationException("Live Azure AI profile cards are not fully configured.");
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext([CognitiveServicesScope]), cancellationToken);
        var requestUri = BuildChatUri(azureAiOptions.Endpoint, azureAiOptions.ChatDeployment);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new ChatCompletionsRequest(
            [
                new ChatMessage("system", "You generate concise Markdown profile cards for CIP. Use only supplied stable fields, keep the card non-authoritative, and do not invent facts."),
                new ChatMessage("user", ProfileCardMarkdown.BuildPrompt(profile))
            ],
            Temperature: 0.2,
            MaxTokens: 180));

        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Content.Headers.ContentType.CharSet = Encoding.UTF8.WebName;

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Azure OpenAI profile card request failed with status code {(int)response.StatusCode}. Response: {body}",
                inner: null,
                response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var responsePayload = await JsonSerializer.DeserializeAsync<ChatCompletionsResponse>(responseStream, cancellationToken: cancellationToken);
        var content = responsePayload?.Choices?.FirstOrDefault()?.Message?.Content;
        var markdown = ExtractContent(content);

        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Azure OpenAI profile card response did not include content.");
        }

        return ProfileCardMarkdown.Normalize(markdown);
    }

    private static Uri BuildChatUri(string endpoint, string deployment)
        => new(new Uri(endpoint.TrimEnd('/') + "/", UriKind.Absolute), $"openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={ChatApiVersion}");

    private static string ExtractContent(JsonElement? content)
    {
        if (content is not { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } jsonContent)
        {
            return string.Empty;
        }

        if (jsonContent.ValueKind == JsonValueKind.String)
        {
            return jsonContent.GetString() ?? string.Empty;
        }

        if (jsonContent.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Concat(jsonContent.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private sealed record ChatCompletionsRequest(
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatCompletionsResponse([property: JsonPropertyName("choices")] ChatCompletionChoice[]? Choices);

    private sealed record ChatCompletionChoice([property: JsonPropertyName("message")] ChatCompletionMessage? Message);

    private sealed record ChatCompletionMessage([property: JsonPropertyName("content")] JsonElement Content);
}
