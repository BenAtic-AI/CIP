using Cip.Contracts.Health;

namespace Cip.Application.Features.Health;

public interface IApplicationHealthService
{
    HealthResponse GetStatus(string environmentName);
}

public sealed class ApplicationHealthService : IApplicationHealthService
{
    private static readonly string[] Modules =
    [
        "Ingestion",
        "Profiles",
        "IdentityResolution",
        "ChangeGovernance",
        "Triggers",
        "Artifacts",
        "PlatformAdmin"
    ];

    public HealthResponse GetStatus(string environmentName)
        => new(
            Service: "cip-api",
            Environment: environmentName,
            UtcTime: DateTimeOffset.UtcNow,
            Modules: Modules);
}
