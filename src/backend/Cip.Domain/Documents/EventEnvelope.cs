namespace Cip.Domain.Documents;

public sealed record EventEnvelope(
    string Id,
    string TenantId,
    string EventType,
    string Source,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    IReadOnlyCollection<ProfileIdentity> Identities,
    IReadOnlyCollection<ProfileTrait> Traits,
    string ProfileId,
    string ChangeSetId,
    string ProcessingState,
    int SchemaVersion);
