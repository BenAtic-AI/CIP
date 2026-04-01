namespace Cip.Contracts.Triggers;

public sealed record TriggerDefinitionRequest(
    string TenantId,
    string Name,
    string? Description,
    IReadOnlyCollection<TriggerConditionRequest> Conditions);

public sealed record TriggerConditionRequest(string Operator, string Attribute, string Value);
