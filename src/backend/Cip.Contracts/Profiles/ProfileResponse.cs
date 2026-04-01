using Cip.Contracts.Shared;

namespace Cip.Contracts.Profiles;

public sealed record ProfileResponse(
    string TenantId,
    string ProfileId,
    string Status,
    string ProfileCard,
    string Synopsis,
    IReadOnlyCollection<IdentityDto> Identities,
    IReadOnlyCollection<TraitDto> Traits,
    int PendingChangeSetCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
