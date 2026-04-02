using Cip.Api.Configuration;
using Cip.Contracts.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Cip.Api.Authentication;

public static class EntraAuthenticationServiceCollectionExtensions
{
    private const string ApiAudiencePrefix = "api://";

    public static IServiceCollection AddEntraAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(Constants.Entra.SectionName).Get<EntraOptions>() ?? new EntraOptions();
        var validAudiences = GetValidAudiences(options.Audience);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtBearerOptions =>
            {
                jwtBearerOptions.Authority = options.Authority;
                jwtBearerOptions.Audience = options.Audience;
                jwtBearerOptions.TokenValidationParameters.ValidAudiences = validAudiences;
                jwtBearerOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;
            });

        services.AddAuthorization();

        return services;
    }

    private static string[] GetValidAudiences(string? audience)
    {
        if (string.IsNullOrWhiteSpace(audience))
        {
            return [];
        }

        var normalizedAudience = audience.Trim();

        if (!normalizedAudience.StartsWith(ApiAudiencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return [normalizedAudience];
        }

        var bareClientId = normalizedAudience[ApiAudiencePrefix.Length..];

        if (string.IsNullOrWhiteSpace(bareClientId))
        {
            return [normalizedAudience];
        }

        return [normalizedAudience, bareClientId];
    }
}
