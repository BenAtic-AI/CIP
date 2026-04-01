namespace Cip.Contracts.Health;

public sealed record HealthResponse(
    string Service,
    string Environment,
    DateTimeOffset UtcTime,
    IReadOnlyCollection<string> Modules);
