namespace Cip.Contracts.Triggers;

public sealed record TriggerDefinitionResponse(
    string TenantId,
    string TriggerId,
    string Name,
    string? Description,
    string Status,
    IReadOnlyCollection<TriggerConditionResponse> Conditions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRunAt);

public sealed record TriggerConditionResponse(string Operator, string Attribute, string Value);
