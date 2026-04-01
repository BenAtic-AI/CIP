namespace Cip.Domain.Documents;

public sealed record TriggerDefinitionDocument(
    string Id,
    string TenantId,
    string Name,
    string? Description,
    string Status,
    IReadOnlyCollection<TriggerConditionDocument> Conditions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRunAt);

public sealed record TriggerConditionDocument(string Operator, string Attribute, string Value);
