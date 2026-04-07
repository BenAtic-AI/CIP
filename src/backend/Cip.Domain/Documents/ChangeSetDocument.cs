namespace Cip.Domain.Documents;

public sealed record ChangeSetDocument(
    string Id,
    string TenantId,
    string TargetProfileId,
    string Type,
    string Status,
    string BaseProfileEtag,
    IReadOnlyCollection<string> ProposedOperations,
    IReadOnlyCollection<ProfileIdentity> ProposedIdentities,
    IReadOnlyCollection<ProfileTrait> ProposedTraits,
    IReadOnlyCollection<string> EvidenceReferences,
    string Explanation,
    IReadOnlyCollection<ChangeSetEvidenceItem> EvidenceItems,
    DateTimeOffset ProposedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? ReviewComment,
    string SourceEventId);
