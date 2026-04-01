using Cip.Contracts.Profiles;

namespace Cip.Contracts.Triggers;

public sealed record RunTriggerRequest(string TenantId);

public sealed record RunTriggerResponse(
    string TenantId,
    string TriggerId,
    int MatchedProfileCount,
    IReadOnlyCollection<ProfileResponse> MatchedProfiles,
    DateTimeOffset ExecutedAt);
