using Cip.Contracts.Shared;

namespace Cip.Contracts.Profiles;

public sealed record ProfileSearchResponse(
    string TenantId,
    string QueryText,
    int Limit,
    IReadOnlyCollection<ProfileSearchResult> Results);

public sealed record ProfileSearchResult(
    ProfileResponse Profile,
    double SimilarityScore,
    IReadOnlyCollection<IdentityDto> SharedIdentities,
    IReadOnlyCollection<TraitDto> SharedTraits,
    ProfileSearchEvidenceResponse Evidence);

public sealed record ProfileSearchEvidenceResponse(
    string QueryText,
    string Synopsis,
    IReadOnlyCollection<string> MatchedTerms);
