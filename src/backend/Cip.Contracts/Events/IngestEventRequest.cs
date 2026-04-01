using Cip.Contracts.Shared;

namespace Cip.Contracts.Events;

public sealed record IngestEventRequest(
    string TenantId,
    string EventId,
    string EventType,
    string Source,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<IdentityDto> Identities,
    IReadOnlyCollection<TraitDto> Traits,
    int SchemaVersion = 1);
