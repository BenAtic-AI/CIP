namespace Cip.Domain.Documents;

public sealed record ChangeSetEvidenceItem(
    string Kind,
    string Reference,
    string Summary,
    decimal Confidence,
    string Source,
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt);
