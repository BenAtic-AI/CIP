using Cip.Api.Configuration;
using Cip.Contracts.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Cip.Api.Authentication;

public static class EntraAuthenticationServiceCollectionExtensions
{
    private const string ApiAudiencePrefix = "api://";
    private const string MicrosoftOnlineHost = "login.microsoftonline.com";
    private const string LegacyStsIssuerFormat = "https://sts.windows.net/{0}/";
    private static readonly string[] MultiTenantAuthorityAliases = ["common", "organizations", "consumers"];

    public static IServiceCollection AddEntraAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(Constants.Entra.SectionName).Get<EntraOptions>() ?? new EntraOptions();
        var validAudiences = GetValidAudiences(options.Audience);
        var validIssuers = GetValidIssuers(options.Authority);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtBearerOptions =>
            {
                jwtBearerOptions.Authority = options.Authority;
                jwtBearerOptions.Audience = options.Audience;
                jwtBearerOptions.TokenValidationParameters.ValidAudiences = validAudiences;
                jwtBearerOptions.TokenValidationParameters.ValidIssuers = validIssuers;
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

    private static string[] GetValidIssuers(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return [];
        }

        var validIssuers = new List<string>();
        var normalizedAuthority = authority.Trim();
        AddIssuer(validIssuers, normalizedAuthority);
        AddIssuer(validIssuers, normalizedAuthority.TrimEnd('/'));

        if (!Uri.TryCreate(normalizedAuthority, UriKind.Absolute, out var authorityUri) ||
            !authorityUri.Host.Equals(MicrosoftOnlineHost, StringComparison.OrdinalIgnoreCase))
        {
            return [.. validIssuers];
        }

        var pathSegments = authorityUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathSegments.Length < 2 ||
            !pathSegments[1].Equals("v2.0", StringComparison.OrdinalIgnoreCase) ||
            MultiTenantAuthorityAliases.Contains(pathSegments[0], StringComparer.OrdinalIgnoreCase))
        {
            return [.. validIssuers];
        }

        AddIssuer(validIssuers, string.Format(LegacyStsIssuerFormat, pathSegments[0]));

        return [.. validIssuers];
    }

    private static void AddIssuer(List<string> validIssuers, string issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer) || validIssuers.Contains(issuer, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        validIssuers.Add(issuer);
    }
}
