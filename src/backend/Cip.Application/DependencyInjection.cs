using Cip.Application.Features.Health;
using Cip.Application.Features.CipMvp;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddCipApplication(this IServiceCollection services)
    {
        services.AddSingleton<IApplicationHealthService, ApplicationHealthService>();
        services.AddSingleton<ICipMvpService, CipMvpService>();
        services.AddSingleton<IProcessingStatusService>(provider => (CipMvpService)provider.GetRequiredService<ICipMvpService>());
        return services;
    }
}
