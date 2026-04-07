using Cip.Application.Features.CipMvp;
using Cip.Domain.Documents;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integrations.AzureAi;

public sealed class AdaptiveProfileCardGenerationService(
    DeterministicProfileCardGenerationService fallbackService,
    AzureOpenAiProfileCardClient liveProfileCardClient,
    IOptions<AzureAiOptions> options,
    ILogger<AdaptiveProfileCardGenerationService> logger) : IProfileCardGenerationService
{
    public async Task<string> GenerateAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        if (!options.Value.ShouldUseLiveProfileCards())
        {
            return await fallbackService.GenerateAsync(profile, cancellationToken);
        }

        try
        {
            var markdown = await liveProfileCardClient.GenerateAsync(profile, cancellationToken);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return await fallbackService.GenerateAsync(profile, cancellationToken);
            }

            return markdown;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Falling back to deterministic profile cards.");
            return await fallbackService.GenerateAsync(profile, cancellationToken);
        }
    }
}
