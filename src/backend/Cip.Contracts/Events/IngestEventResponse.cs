namespace Cip.Contracts.Events;

public sealed record IngestEventResponse(
    string TenantId,
    string EventId,
    string ProfileId,
    string ChangeSetId,
    bool Accepted,
    bool Duplicate,
    string ProcessingState);
