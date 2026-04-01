using Cip.Application.Features.CipMvp;
using Cip.Infrastructure.Persistence;
using Integrations.Cosmos.Configuration;
using Integrations.Cosmos.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddCipInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCosmosIntegration(configuration);
        services.AddStorageIntegration(configuration);
        services.AddAzureAiIntegration(configuration);

        var cosmosOptions = configuration.GetSection(CosmosOptions.SectionName).Get<CosmosOptions>() ?? new CosmosOptions();
        if (cosmosOptions.ShouldUseCosmos())
        {
            services.AddSingleton<ICipRuntimeStore>(provider => provider.GetRequiredService<CosmosCipRuntimeStore>());
        }
        else
        {
            services.AddSingleton<ICipRuntimeStore, InMemoryCipRuntimeStore>();
        }

        return services;
    }
}
