namespace Cip.Domain.Documents;

public sealed record ProfileDocument(
    string Id,
    string TenantId,
    string ProfileId,
    string DocType,
    string Status,
    IReadOnlyCollection<ProfileIdentity> Identities,
    IReadOnlyCollection<ProfileTrait> Traits,
    string ProfileCard,
    string Synopsis,
    IReadOnlyCollection<float>? SynopsisVector,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProfileIdentity(string Type, string Value, string Source);

public sealed record ProfileTrait(string Name, string Value, decimal Confidence);
