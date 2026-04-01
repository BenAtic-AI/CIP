using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddAzureAiIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureAiOptions>(configuration.GetSection(AzureAiOptions.SectionName));
        return services;
    }
}
