using Cip.Application.Features.CipMvp;
using Azure.Core;
using Azure.Identity;
using Integrations.AzureAi;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddAzureAiIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureAiOptions>(configuration.GetSection(AzureAiOptions.SectionName));
        services.TryAddSingleton<DeterministicProfileCardGenerationService>();
        services.TryAddSingleton<DeterministicProfileTextEmbeddingService>();
        services.TryAddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddHttpClient<AzureOpenAiProfileCardClient>();
        services.AddHttpClient<AzureOpenAiEmbeddingsClient>();
        services.TryAddSingleton<IProfileCardGenerationService, AdaptiveProfileCardGenerationService>();
        services.TryAddSingleton<IProfileTextEmbeddingService, AdaptiveProfileTextEmbeddingService>();
        return services;
    }
}
