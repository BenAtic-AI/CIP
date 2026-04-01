using Cip.Application.Features.CipMvp;
using Cip.Contracts.Constants;
using Cip.Domain.Documents;

namespace Cip.Infrastructure.Persistence;

public sealed class InMemoryCipRuntimeStore : ICipRuntimeStore
{
    private readonly Lock _syncRoot = new();
    private readonly Dictionary<string, EventEnvelope> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProfileDocument> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ChangeSetDocument> _changeSets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TriggerDefinitionDocument> _triggers = new(StringComparer.OrdinalIgnoreCase);

    public Task<EventEnvelope?> GetEventAsync(string tenantId, string eventId, CancellationToken cancellationToken)
        => Task.FromResult(_events.GetValueOrDefault(BuildKey(tenantId, eventId)));

    public Task SaveEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _events[BuildKey(eventEnvelope.TenantId, eventEnvelope.Id)] = eventEnvelope;
        }

        return Task.CompletedTask;
    }

    public Task UpdateEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken)
        => SaveEventAsync(eventEnvelope, cancellationToken);

    public Task<ProfileDocument?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken)
        => Task.FromResult(_profiles.GetValueOrDefault(BuildKey(tenantId, profileId)));

    public Task<ProfileDocument?> FindProfileByIdentityAsync(string tenantId, IReadOnlyCollection<ProfileIdentity> identities, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            var match = _profiles.Values
                .Where(profile => string.Equals(profile.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(profile => profile.Identities.Any(existing => identities.Any(candidate =>
                    string.Equals(existing.Type, candidate.Type, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.Value, candidate.Value, StringComparison.OrdinalIgnoreCase))));

            return Task.FromResult(match);
        }
    }

    public Task<IReadOnlyCollection<ProfileDocument>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            IReadOnlyCollection<ProfileDocument> profiles = _profiles.Values
                .Where(profile => string.Equals(profile.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return Task.FromResult(profiles);
        }
    }

    public Task SaveProfileAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _profiles[BuildKey(profile.TenantId, profile.ProfileId)] = profile;
        }

        return Task.CompletedTask;
    }

    public Task UpdateProfileAsync(ProfileDocument profile, CancellationToken cancellationToken)
        => SaveProfileAsync(profile, cancellationToken);

    public Task<ChangeSetDocument?> GetChangeSetAsync(string tenantId, string changeSetId, CancellationToken cancellationToken)
        => Task.FromResult(_changeSets.GetValueOrDefault(BuildKey(tenantId, changeSetId)));

    public Task<IReadOnlyCollection<ChangeSetDocument>> ListChangeSetsAsync(string tenantId, string? status, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            var query = _changeSets.Values.Where(changeSet => string.Equals(changeSet.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(changeSet => string.Equals(changeSet.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            IReadOnlyCollection<ChangeSetDocument> result = query.ToArray();
            return Task.FromResult(result);
        }
    }

    public Task SaveChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _changeSets[BuildKey(changeSet.TenantId, changeSet.Id)] = changeSet;
        }

        return Task.CompletedTask;
    }

    public Task UpdateChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken)
        => SaveChangeSetAsync(changeSet, cancellationToken);

    public Task<IReadOnlyCollection<ChangeSetDocument>> ListPendingChangeSetsForProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            IReadOnlyCollection<ChangeSetDocument> result = _changeSets.Values
                .Where(changeSet => string.Equals(changeSet.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(changeSet.TargetProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(changeSet.Status, Constants.ChangeSets.Pending, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return Task.FromResult(result);
        }
    }

    public Task<TriggerDefinitionDocument?> GetTriggerAsync(string tenantId, string triggerId, CancellationToken cancellationToken)
        => Task.FromResult(_triggers.GetValueOrDefault(BuildKey(tenantId, triggerId)));

    public Task<IReadOnlyCollection<TriggerDefinitionDocument>> ListTriggersAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            IReadOnlyCollection<TriggerDefinitionDocument> result = _triggers.Values
                .Where(trigger => string.Equals(trigger.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return Task.FromResult(result);
        }
    }

    public Task SaveTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            _triggers[BuildKey(trigger.TenantId, trigger.Id)] = trigger;
        }

        return Task.CompletedTask;
    }

    public Task UpdateTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken)
        => SaveTriggerAsync(trigger, cancellationToken);

    public Task<ProcessingStatusSnapshot> GetProcessingStatusAsync(CancellationToken cancellationToken)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(new ProcessingStatusSnapshot(
                Constants.Runtime.InMemory,
                _events.Count,
                _profiles.Count,
                _changeSets.Values.Count(changeSet => string.Equals(changeSet.Status, Constants.ChangeSets.Pending, StringComparison.OrdinalIgnoreCase)),
                _triggers.Count,
                DateTimeOffset.UtcNow));
        }
    }

    private static string BuildKey(string tenantId, string id) => $"{tenantId}::{id}";
}
