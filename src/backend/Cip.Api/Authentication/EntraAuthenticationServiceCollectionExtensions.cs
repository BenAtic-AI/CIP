using Cip.Api.Configuration;
using Cip.Contracts.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Cip.Api.Authentication;

public static class EntraAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddEntraAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(Constants.Entra.SectionName).Get<EntraOptions>() ?? new EntraOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtBearerOptions =>
            {
                jwtBearerOptions.Authority = options.Authority;
                jwtBearerOptions.Audience = options.Audience;
                jwtBearerOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;
            });

        services.AddAuthorization();

        return services;
    }
}
