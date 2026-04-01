using Integrations.Cosmos.Configuration;
using Integrations.Cosmos.Persistence;
using Integrations.Cosmos.Serialization;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddCosmosIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CosmosOptions>(configuration.GetSection(CosmosOptions.SectionName));

        var options = configuration.GetSection(CosmosOptions.SectionName).Get<CosmosOptions>() ?? new CosmosOptions();
        if (!options.ShouldUseCosmos())
        {
            return services;
        }

        services.AddSingleton(_ => new CosmosClient(
            options.AccountEndpoint,
            new DefaultAzureCredential(),
            new CosmosClientOptions
            {
                Serializer = new SystemTextJsonCosmosSerializer()
            }));

        services.AddSingleton<CosmosCipRuntimeStore>();
        return services;
    }
}
