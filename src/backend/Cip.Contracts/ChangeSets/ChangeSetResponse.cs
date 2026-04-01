using Cip.Contracts.Shared;

namespace Cip.Contracts.ChangeSets;

public sealed record ChangeSetResponse(
    string TenantId,
    string ChangeSetId,
    string TargetProfileId,
    string Type,
    string Status,
    IReadOnlyCollection<string> ProposedOperations,
    IReadOnlyCollection<IdentityDto> ProposedIdentities,
    IReadOnlyCollection<TraitDto> ProposedTraits,
    IReadOnlyCollection<string> EvidenceReferences,
    DateTimeOffset ProposedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? ReviewComment,
    string SourceEventId);
