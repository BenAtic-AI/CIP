namespace Cip.Contracts.Profiles;

public sealed record ProfileSearchRequest(
    string TenantId,
    string QueryText,
    int? Limit);
