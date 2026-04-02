using Cip.Application.Features.CipMvp;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integrations.AzureAi;

public sealed class AdaptiveProfileTextEmbeddingService(
    DeterministicProfileTextEmbeddingService fallbackService,
    AzureOpenAiEmbeddingsClient liveEmbeddingsClient,
    IOptions<AzureAiOptions> options,
    ILogger<AdaptiveProfileTextEmbeddingService> logger) : IProfileTextEmbeddingService
{
    public async Task<IReadOnlyCollection<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || !options.Value.ShouldUseLiveEmbeddings())
        {
            return await fallbackService.EmbedAsync(text, cancellationToken);
        }

        try
        {
            return await liveEmbeddingsClient.EmbedAsync(text, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Falling back to deterministic profile embeddings.");
            return await fallbackService.EmbedAsync(text, cancellationToken);
        }
    }
}
