using Cip.Domain.Documents;

namespace Cip.Application.Features.CipMvp;

public interface ICipRuntimeStore
{
    Task<EventEnvelope?> GetEventAsync(string tenantId, string eventId, CancellationToken cancellationToken);
    Task SaveEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken);
    Task UpdateEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken);
    Task<ProfileDocument?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken);
    Task<ProfileDocument?> FindProfileByIdentityAsync(string tenantId, IReadOnlyCollection<ProfileIdentity> identities, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProfileDocument>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken);
    Task SaveProfileAsync(ProfileDocument profile, CancellationToken cancellationToken);
    Task UpdateProfileAsync(ProfileDocument profile, CancellationToken cancellationToken);
    Task<ChangeSetDocument?> GetChangeSetAsync(string tenantId, string changeSetId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ChangeSetDocument>> ListChangeSetsAsync(string tenantId, string? status, CancellationToken cancellationToken);
    Task SaveChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken);
    Task UpdateChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ChangeSetDocument>> ListPendingChangeSetsForProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken);
    Task<TriggerDefinitionDocument?> GetTriggerAsync(string tenantId, string triggerId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TriggerDefinitionDocument>> ListTriggersAsync(string tenantId, CancellationToken cancellationToken);
    Task SaveTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken);
    Task UpdateTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken);
    Task<ProcessingStatusSnapshot> GetProcessingStatusAsync(CancellationToken cancellationToken);
}

public sealed record ProcessingStatusSnapshot(
    string RuntimeStore,
    int EventCount,
    int ProfileCount,
    int PendingChangeSetCount,
    int TriggerCount,
    DateTimeOffset UtcTime);
