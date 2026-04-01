using Cip.Contracts.ChangeSets;
using Cip.Contracts.Constants;
using Cip.Contracts.Events;
using Cip.Contracts.Profiles;
using Cip.Contracts.Shared;
using Cip.Contracts.Triggers;
using Cip.Domain.Documents;

namespace Cip.Application.Features.CipMvp;

public interface ICipMvpService
{
    Task<IngestEventResponse> IngestEventAsync(IngestEventRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProfileResponse>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken);
    Task<ProfileResponse?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ChangeSetResponse>> ListChangeSetsAsync(string tenantId, string? status, CancellationToken cancellationToken);
    Task<ChangeSetResponse?> GetChangeSetAsync(string tenantId, string changeSetId, CancellationToken cancellationToken);
    Task<ChangeSetResponse?> ApproveChangeSetAsync(string tenantId, string changeSetId, string reviewedBy, string? comment, CancellationToken cancellationToken);
    Task<ChangeSetResponse?> RejectChangeSetAsync(string tenantId, string changeSetId, string reviewedBy, string? comment, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<TriggerDefinitionResponse>> ListTriggersAsync(string tenantId, CancellationToken cancellationToken);
    Task<TriggerDefinitionResponse> CreateTriggerAsync(TriggerDefinitionRequest request, CancellationToken cancellationToken);
    Task<RunTriggerResponse?> RunTriggerAsync(string tenantId, string triggerId, CancellationToken cancellationToken);
}

public interface IProcessingStatusService
{
    Task<ProcessingStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed class CipMvpService(ICipRuntimeStore store) : ICipMvpService, IProcessingStatusService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IngestEventResponse> IngestEventAsync(IngestEventRequest request, CancellationToken cancellationToken)
    {
        ValidateEventRequest(request);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var duplicateEvent = await store.GetEventAsync(request.TenantId, request.EventId, cancellationToken);
            if (duplicateEvent is not null)
            {
                return new IngestEventResponse(
                    request.TenantId,
                    request.EventId,
                    duplicateEvent.ProfileId,
                    duplicateEvent.ChangeSetId,
                    Accepted: true,
                    Duplicate: true,
                    duplicateEvent.ProcessingState);
            }

            var receivedAt = DateTimeOffset.UtcNow;
            var identities = request.Identities.Select(MapIdentity).ToArray();
            var traits = request.Traits.Select(MapTrait).ToArray();

            var existingProfile = await store.FindProfileByIdentityAsync(request.TenantId, identities, cancellationToken);
            var profile = existingProfile ?? CreateProfileShell(request.TenantId, request.EventType, identities, receivedAt);
            var proposedIdentities = identities
                .Where(identity => !profile.Identities.Any(existing => IdentityMatches(existing, identity)))
                .ToArray();

            var proposedTraits = traits
                .Where(trait => !profile.Traits.Any(existing => TraitMatches(existing, trait)))
                .ToArray();

            var changeSetId = $"cs_{Guid.NewGuid():N}";
            var operations = BuildOperations(proposedIdentities, proposedTraits);
            string[] evidenceReferences = [request.EventId, request.Source];
            var effectiveProfile = profile with
            {
                Status = Constants.Profiles.PendingReview,
                UpdatedAt = receivedAt,
                Synopsis = BuildSynopsis(request.EventType, request.Source, proposedIdentities.Length, proposedTraits.Length)
            };

            var changeSet = new ChangeSetDocument(
                Id: changeSetId,
                TenantId: request.TenantId,
                TargetProfileId: effectiveProfile.ProfileId,
                Type: Constants.ChangeSets.EventMaterialization,
                Status: Constants.ChangeSets.Pending,
                BaseProfileEtag: effectiveProfile.UpdatedAt.ToUnixTimeMilliseconds().ToString(),
                ProposedOperations: operations,
                ProposedIdentities: proposedIdentities,
                ProposedTraits: proposedTraits,
                EvidenceReferences: evidenceReferences,
                ProposedAt: receivedAt,
                ReviewedAt: null,
                ReviewedBy: null,
                ReviewComment: null,
                SourceEventId: request.EventId);

            var eventEnvelope = new EventEnvelope(
                Id: request.EventId,
                TenantId: request.TenantId,
                EventType: request.EventType,
                Source: request.Source,
                OccurredAt: request.OccurredAt,
                ReceivedAt: receivedAt,
                Identities: identities,
                Traits: traits,
                ProfileId: effectiveProfile.ProfileId,
                ChangeSetId: changeSetId,
                ProcessingState: Constants.Events.PendingApproval,
                SchemaVersion: request.SchemaVersion);

            if (existingProfile is null)
            {
                await store.SaveProfileAsync(effectiveProfile, cancellationToken);
            }
            else
            {
                await store.UpdateProfileAsync(effectiveProfile, cancellationToken);
            }

            await store.SaveChangeSetAsync(changeSet, cancellationToken);
            await store.SaveEventAsync(eventEnvelope, cancellationToken);

            return new IngestEventResponse(
                request.TenantId,
                request.EventId,
                effectiveProfile.ProfileId,
                changeSetId,
                Accepted: true,
                Duplicate: false,
                eventEnvelope.ProcessingState);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<ProfileResponse>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        var profiles = await store.ListProfilesAsync(tenantId, cancellationToken);
        return await MapProfilesAsync(tenantId, profiles, cancellationToken);
    }

    public async Task<ProfileResponse?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        ValidateRequired(profileId, nameof(profileId));

        var profile = await store.GetProfileAsync(tenantId, profileId, cancellationToken);
        return profile is null ? null : await MapProfileAsync(profile, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ChangeSetResponse>> ListChangeSetsAsync(string tenantId, string? status, CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);

        var changeSets = await store.ListChangeSetsAsync(tenantId, status, cancellationToken);
        return changeSets.Select(MapChangeSet).ToArray();
    }

    public async Task<ChangeSetResponse?> GetChangeSetAsync(string tenantId, string changeSetId, CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        ValidateRequired(changeSetId, nameof(changeSetId));

        var changeSet = await store.GetChangeSetAsync(tenantId, changeSetId, cancellationToken);
        return changeSet is null ? null : MapChangeSet(changeSet);
    }

    public async Task<ChangeSetResponse?> ApproveChangeSetAsync(string tenantId, string changeSetId, string reviewedBy, string? comment, CancellationToken cancellationToken)
    {
        ValidateReviewRequest(tenantId, reviewedBy, changeSetId);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var changeSet = await store.GetChangeSetAsync(tenantId, changeSetId, cancellationToken);
            if (changeSet is null)
            {
                return null;
            }

            EnsurePending(changeSet);

            var profile = await store.GetProfileAsync(tenantId, changeSet.TargetProfileId, cancellationToken)
                ?? throw new InvalidOperationException($"Profile '{changeSet.TargetProfileId}' was not found.");

            var updatedProfile = ApplyApprovedChanges(profile, changeSet);
            var now = DateTimeOffset.UtcNow;

            var reviewedChangeSet = changeSet with
            {
                Status = Constants.ChangeSets.Approved,
                ReviewedAt = now,
                ReviewedBy = reviewedBy.Trim(),
                ReviewComment = comment?.Trim()
            };

            await store.UpdateProfileAsync(updatedProfile with { UpdatedAt = now }, cancellationToken);
            await store.UpdateChangeSetAsync(reviewedChangeSet, cancellationToken);

            var sourceEvent = await store.GetEventAsync(tenantId, changeSet.SourceEventId, cancellationToken);
            if (sourceEvent is not null)
            {
                await store.UpdateEventAsync(sourceEvent with { ProcessingState = Constants.Events.Applied }, cancellationToken);
            }

            var pending = await store.ListPendingChangeSetsForProfileAsync(tenantId, profile.ProfileId, cancellationToken);
            if (pending.Count == 0)
            {
                await store.UpdateProfileAsync(updatedProfile with { Status = Constants.Profiles.Ready, UpdatedAt = now }, cancellationToken);
            }

            var reloaded = await store.GetChangeSetAsync(tenantId, changeSetId, cancellationToken);
            return reloaded is null ? null : MapChangeSet(reloaded);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChangeSetResponse?> RejectChangeSetAsync(string tenantId, string changeSetId, string reviewedBy, string? comment, CancellationToken cancellationToken)
    {
        ValidateReviewRequest(tenantId, reviewedBy, changeSetId);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var changeSet = await store.GetChangeSetAsync(tenantId, changeSetId, cancellationToken);
            if (changeSet is null)
            {
                return null;
            }

            EnsurePending(changeSet);

            var now = DateTimeOffset.UtcNow;
            var reviewedChangeSet = changeSet with
            {
                Status = Constants.ChangeSets.Rejected,
                ReviewedAt = now,
                ReviewedBy = reviewedBy.Trim(),
                ReviewComment = comment?.Trim()
            };

            await store.UpdateChangeSetAsync(reviewedChangeSet, cancellationToken);

            var sourceEvent = await store.GetEventAsync(tenantId, changeSet.SourceEventId, cancellationToken);
            if (sourceEvent is not null)
            {
                await store.UpdateEventAsync(sourceEvent with { ProcessingState = Constants.Events.Rejected }, cancellationToken);
            }

            var profile = await store.GetProfileAsync(tenantId, changeSet.TargetProfileId, cancellationToken);
            if (profile is not null)
            {
                var pending = await store.ListPendingChangeSetsForProfileAsync(tenantId, profile.ProfileId, cancellationToken);
                var status = pending.Count == 0 && profile.Identities.Count + profile.Traits.Count > 0
                    ? Constants.Profiles.Ready
                    : Constants.Profiles.PendingReview;

                await store.UpdateProfileAsync(profile with { Status = status, UpdatedAt = now }, cancellationToken);
            }

            return MapChangeSet(reviewedChangeSet);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<TriggerDefinitionResponse>> ListTriggersAsync(string tenantId, CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        var triggers = await store.ListTriggersAsync(tenantId, cancellationToken);
        return triggers.Select(MapTrigger).ToArray();
    }

    public async Task<TriggerDefinitionResponse> CreateTriggerAsync(TriggerDefinitionRequest request, CancellationToken cancellationToken)
    {
        ValidateTriggerRequest(request);

        var trigger = new TriggerDefinitionDocument(
            Id: $"trg_{Guid.NewGuid():N}",
            TenantId: request.TenantId,
            Name: request.Name.Trim(),
            Description: request.Description?.Trim(),
            Status: Constants.Triggers.Active,
            Conditions: request.Conditions.Select(condition => new TriggerConditionDocument(
                condition.Operator.Trim(),
                condition.Attribute.Trim(),
                condition.Value.Trim())).ToArray(),
            CreatedAt: DateTimeOffset.UtcNow,
            LastRunAt: null);

        await store.SaveTriggerAsync(trigger, cancellationToken);
        return MapTrigger(trigger);
    }

    public async Task<RunTriggerResponse?> RunTriggerAsync(string tenantId, string triggerId, CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        ValidateRequired(triggerId, nameof(triggerId));

        var trigger = await store.GetTriggerAsync(tenantId, triggerId, cancellationToken);
        if (trigger is null)
        {
            return null;
        }

        var profiles = await store.ListProfilesAsync(tenantId, cancellationToken);
        var matchedProfiles = new List<ProfileResponse>();

        foreach (var profile in profiles)
        {
            if (MatchesTrigger(profile, trigger))
            {
                matchedProfiles.Add(await MapProfileAsync(profile, cancellationToken));
            }
        }

        var executedAt = DateTimeOffset.UtcNow;
        await store.UpdateTriggerAsync(trigger with { LastRunAt = executedAt }, cancellationToken);

        return new RunTriggerResponse(
            tenantId,
            triggerId,
            matchedProfiles.Count,
            matchedProfiles,
            executedAt);
    }

    public Task<ProcessingStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken)
        => store.GetProcessingStatusAsync(cancellationToken);

    private static ProfileDocument CreateProfileShell(string tenantId, string eventType, IReadOnlyCollection<ProfileIdentity> identities, DateTimeOffset now)
    {
        var profileId = $"pro_{Guid.NewGuid():N}";
        var profileCard = identities.FirstOrDefault() is { } firstIdentity
            ? $"{firstIdentity.Type}:{firstIdentity.Value}"
            : $"Shell created from {eventType}";

        return new ProfileDocument(
            Id: profileId,
            TenantId: tenantId,
            ProfileId: profileId,
            DocType: Constants.Profiles.Shell,
            Status: Constants.Profiles.PendingReview,
            Identities: [],
            Traits: [],
            ProfileCard: profileCard,
            Synopsis: $"Shell created from {eventType} event pending approval.",
            SynopsisVector: null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static string BuildSynopsis(string eventType, string source, int identityCount, int traitCount)
        => $"Pending {eventType} materialization from {source} with {identityCount} identities and {traitCount} traits.";

    private static string[] BuildOperations(IReadOnlyCollection<ProfileIdentity> identities, IReadOnlyCollection<ProfileTrait> traits)
    {
        var operations = new List<string>(identities.Count + traits.Count);
        operations.AddRange(identities.Select(identity => $"Upsert identity {identity.Type}:{identity.Value}"));
        operations.AddRange(traits.Select(trait => $"Upsert trait {trait.Name}={trait.Value}"));

        if (operations.Count == 0)
        {
            operations.Add("No-op review required");
        }

        return operations.ToArray();
    }

    private static ProfileDocument ApplyApprovedChanges(ProfileDocument profile, ChangeSetDocument changeSet)
    {
        var identities = profile.Identities.ToList();
        foreach (var identity in changeSet.ProposedIdentities)
        {
            if (!identities.Any(existing => IdentityMatches(existing, identity)))
            {
                identities.Add(identity);
            }
        }

        var traits = profile.Traits.ToDictionary(trait => trait.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var trait in changeSet.ProposedTraits)
        {
            traits[trait.Name] = trait;
        }

        return profile with
        {
            Status = Constants.Profiles.Ready,
            Identities = identities,
            Traits = traits.Values.OrderBy(trait => trait.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            Synopsis = $"Approved profile with {identities.Count} identities and {traits.Count} traits."
        };
    }

    private async Task<IReadOnlyCollection<ProfileResponse>> MapProfilesAsync(string tenantId, IReadOnlyCollection<ProfileDocument> profiles, CancellationToken cancellationToken)
    {
        var results = new List<ProfileResponse>(profiles.Count);
        foreach (var profile in profiles.OrderByDescending(item => item.UpdatedAt))
        {
            results.Add(await MapProfileAsync(profile, cancellationToken));
        }

        return results;
    }

    private async Task<ProfileResponse> MapProfileAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        var pending = await store.ListPendingChangeSetsForProfileAsync(profile.TenantId, profile.ProfileId, cancellationToken);

        return new ProfileResponse(
            profile.TenantId,
            profile.ProfileId,
            profile.Status,
            profile.ProfileCard,
            profile.Synopsis,
            profile.Identities.Select(item => new IdentityDto(item.Type, item.Value, item.Source)).ToArray(),
            profile.Traits.Select(item => new TraitDto(item.Name, item.Value, item.Confidence)).ToArray(),
            pending.Count,
            profile.CreatedAt,
            profile.UpdatedAt);
    }

    private static ChangeSetResponse MapChangeSet(ChangeSetDocument changeSet)
        => new(
            changeSet.TenantId,
            changeSet.Id,
            changeSet.TargetProfileId,
            changeSet.Type,
            changeSet.Status,
            changeSet.ProposedOperations,
            changeSet.ProposedIdentities.Select(item => new IdentityDto(item.Type, item.Value, item.Source)).ToArray(),
            changeSet.ProposedTraits.Select(item => new TraitDto(item.Name, item.Value, item.Confidence)).ToArray(),
            changeSet.EvidenceReferences,
            changeSet.ProposedAt,
            changeSet.ReviewedAt,
            changeSet.ReviewedBy,
            changeSet.ReviewComment,
            changeSet.SourceEventId);

    private static TriggerDefinitionResponse MapTrigger(TriggerDefinitionDocument trigger)
        => new(
            trigger.TenantId,
            trigger.Id,
            trigger.Name,
            trigger.Description,
            trigger.Status,
            trigger.Conditions.Select(item => new TriggerConditionResponse(item.Operator, item.Attribute, item.Value)).ToArray(),
            trigger.CreatedAt,
            trigger.LastRunAt);

    private static bool MatchesTrigger(ProfileDocument profile, TriggerDefinitionDocument trigger)
        => trigger.Conditions.All(condition => condition.Operator switch
        {
            Constants.Triggers.TraitEquals => profile.Traits.Any(trait =>
                string.Equals(trait.Name, condition.Attribute, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(trait.Value, condition.Value, StringComparison.OrdinalIgnoreCase)),
            Constants.Triggers.IdentityEquals => profile.Identities.Any(identity =>
                string.Equals(identity.Type, condition.Attribute, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(identity.Value, condition.Value, StringComparison.OrdinalIgnoreCase)),
            Constants.Triggers.IdentityContains => profile.Identities.Any(identity =>
                string.Equals(identity.Type, condition.Attribute, StringComparison.OrdinalIgnoreCase) &&
                identity.Value.Contains(condition.Value, StringComparison.OrdinalIgnoreCase)),
            _ => false
        });

    private static ProfileIdentity MapIdentity(IdentityDto identity)
        => new(identity.Type.Trim(), identity.Value.Trim(), identity.Source.Trim());

    private static ProfileTrait MapTrait(TraitDto trait)
        => new(trait.Name.Trim(), trait.Value.Trim(), trait.Confidence);

    private static bool IdentityMatches(ProfileIdentity left, ProfileIdentity right)
        => string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static bool TraitMatches(ProfileTrait left, ProfileTrait right)
        => string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);

    private static void EnsurePending(ChangeSetDocument changeSet)
    {
        if (!string.Equals(changeSet.Status, Constants.ChangeSets.Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Change set '{changeSet.Id}' is already {changeSet.Status}.");
        }
    }

    private static void ValidateEventRequest(IngestEventRequest request)
    {
        ValidateTenantId(request.TenantId);
        ValidateRequired(request.EventId, nameof(request.EventId));
        ValidateRequired(request.EventType, nameof(request.EventType));
        ValidateRequired(request.Source, nameof(request.Source));

        if ((request.Identities?.Count ?? 0) == 0 && (request.Traits?.Count ?? 0) == 0)
        {
            throw new ArgumentException("At least one identity or trait is required.");
        }

        if (request.Identities is not null)
        {
            foreach (var identity in request.Identities)
            {
                ValidateRequired(identity.Type, $"{nameof(request.Identities)}.{nameof(identity.Type)}");
                ValidateRequired(identity.Value, $"{nameof(request.Identities)}.{nameof(identity.Value)}");
                ValidateRequired(identity.Source, $"{nameof(request.Identities)}.{nameof(identity.Source)}");
            }
        }

        if (request.Traits is not null)
        {
            foreach (var trait in request.Traits)
            {
                ValidateRequired(trait.Name, $"{nameof(request.Traits)}.{nameof(trait.Name)}");
                ValidateRequired(trait.Value, $"{nameof(request.Traits)}.{nameof(trait.Value)}");
            }
        }
    }

    private static void ValidateTriggerRequest(TriggerDefinitionRequest request)
    {
        ValidateTenantId(request.TenantId);
        ValidateRequired(request.Name, nameof(request.Name));

        if (request.Conditions is null || request.Conditions.Count == 0)
        {
            throw new ArgumentException("At least one trigger condition is required.");
        }

        foreach (var condition in request.Conditions)
        {
            ValidateRequired(condition.Operator, nameof(condition.Operator));
            ValidateRequired(condition.Attribute, nameof(condition.Attribute));
            ValidateRequired(condition.Value, nameof(condition.Value));

            if (condition.Operator is not Constants.Triggers.TraitEquals
                and not Constants.Triggers.IdentityEquals
                and not Constants.Triggers.IdentityContains)
            {
                throw new ArgumentException($"Unsupported trigger operator '{condition.Operator}'.");
            }
        }
    }

    private static void ValidateReviewRequest(string tenantId, string reviewedBy, string changeSetId)
    {
        ValidateTenantId(tenantId);
        ValidateRequired(reviewedBy, nameof(reviewedBy));
        ValidateRequired(changeSetId, nameof(changeSetId));
    }

    private static void ValidateTenantId(string tenantId)
        => ValidateRequired(tenantId, nameof(tenantId));

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.");
        }
    }
}
