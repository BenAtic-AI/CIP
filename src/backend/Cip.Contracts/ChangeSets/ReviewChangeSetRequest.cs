namespace Cip.Contracts.ChangeSets;

public sealed record ReviewChangeSetRequest(string TenantId, string ReviewedBy, string? Comment);
