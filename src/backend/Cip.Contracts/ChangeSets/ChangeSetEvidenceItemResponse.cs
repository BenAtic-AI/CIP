namespace Cip.Contracts.ChangeSets;

public sealed record ChangeSetEvidenceItemResponse(
    string Kind,
    string Reference,
    string Summary,
    decimal Confidence,
    string Source,
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt);
