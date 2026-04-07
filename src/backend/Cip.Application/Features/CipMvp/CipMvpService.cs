using Cip.Contracts.ChangeSets;
using Cip.Contracts.Constants;
using Cip.Contracts.Events;
using Cip.Contracts.Profiles;
using Cip.Contracts.Shared;
using Cip.Contracts.Triggers;
using Cip.Domain.Documents;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Cip.Application.Features.CipMvp;

public interface ICipMvpService
{
    Task<IngestEventResponse> IngestEventAsync(IngestEventRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProfileResponse>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken);
    Task<ProfileResponse?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken);
    Task<ProfileSearchResponse> SearchProfilesAsync(ProfileSearchRequest request, CancellationToken cancellationToken);
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

public sealed class CipMvpService(
    ICipRuntimeStore store,
    IProfileTextEmbeddingService profileTextEmbeddingService,
    IProfileCardGenerationService profileCardGenerationService) : ICipMvpService, IProcessingStatusService
{
    private const int DefaultProfileSearchLimit = 5;
    private const int MaxProfileSearchLimit = 25;
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
            var profile = existingProfile ?? CreateProfileShell(request.TenantId, request.EventType, receivedAt);
            var proposedIdentities = identities
                .Where(identity => !profile.Identities.Any(existing => IdentityMatches(existing, identity)))
                .ToArray();

            var proposedTraits = traits
                .Where(trait => !profile.Traits.Any(existing => TraitMatches(existing, trait)))
                .ToArray();

            var changeSetId = $"cs_{Guid.NewGuid():N}";
            var operations = BuildOperations(request.Source, proposedIdentities, proposedTraits);
            string[] evidenceReferences = [request.EventId, request.Source];
            var explanation = BuildExplanation(
                request.EventId,
                request.EventType,
                request.Source,
                request.OccurredAt,
                profile.ProfileId,
                existingProfile is null,
                proposedIdentities,
                proposedTraits);
            var evidenceItems = BuildEvidenceItems(request, proposedIdentities, proposedTraits);
            var effectiveProfile = profile with
            {
                Status = Constants.Profiles.PendingReview,
                UpdatedAt = receivedAt,
                Synopsis = BuildPendingSynopsis(request.EventType, request.Source, profile.Identities, profile.Traits, proposedIdentities, proposedTraits)
            };
            effectiveProfile = await PopulateProfileDerivedFieldsAsync(effectiveProfile, cancellationToken);

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
                Explanation: explanation,
                EvidenceItems: evidenceItems,
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

    public async Task<ProfileSearchResponse> SearchProfilesAsync(ProfileSearchRequest request, CancellationToken cancellationToken)
    {
        ValidateSearchRequest(request);

        var queryText = request.QueryText.Trim();
        var limit = ResolveSearchLimit(request.Limit);
        var queryVector = (await profileTextEmbeddingService.EmbedAsync(queryText, cancellationToken)).ToArray();
        if (queryVector.Length == 0 || ComputeMagnitude(queryVector) == 0d)
        {
            return new ProfileSearchResponse(request.TenantId, queryText, limit, []);
        }

        var queryTerms = ExtractSearchTerms(queryText);
        var rankedCandidates = await SearchProfileCandidatesAsync(request.TenantId, queryVector, queryTerms, limit, cancellationToken);

        var results = new List<ProfileSearchResult>(rankedCandidates.Length);
        foreach (var candidate in rankedCandidates)
        {
            results.Add(new ProfileSearchResult(
                await MapProfileAsync(candidate.Profile, cancellationToken),
                candidate.Similarity,
                candidate.SharedIdentities,
                candidate.SharedTraits,
                new ProfileSearchEvidenceResponse(queryText, candidate.Profile.Synopsis, candidate.MatchedTerms)));
        }

        return new ProfileSearchResponse(request.TenantId, queryText, limit, results);
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

            var now = DateTimeOffset.UtcNow;

            var reviewedChangeSet = changeSet with
            {
                Status = Constants.ChangeSets.Approved,
                ReviewedAt = now,
                ReviewedBy = reviewedBy.Trim(),
                ReviewComment = comment?.Trim()
            };

            await store.UpdateChangeSetAsync(reviewedChangeSet, cancellationToken);

            var sourceEvent = await store.GetEventAsync(tenantId, changeSet.SourceEventId, cancellationToken);
            if (sourceEvent is not null)
            {
                await store.UpdateEventAsync(sourceEvent with { ProcessingState = Constants.Events.Applied }, cancellationToken);
            }

            var pending = await store.ListPendingChangeSetsForProfileAsync(tenantId, profile.ProfileId, cancellationToken);
            var updatedProfile = await PopulateProfileDerivedFieldsAsync(
                ApplyApprovedChanges(profile, changeSet) with
                {
                    Status = pending.Count == 0 ? Constants.Profiles.Ready : Constants.Profiles.PendingReview,
                    UpdatedAt = now
                },
                cancellationToken);

            await store.UpdateProfileAsync(updatedProfile, cancellationToken);

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

                var updatedProfile = await PopulateProfileDerivedFieldsAsync(
                    profile with { Status = status, UpdatedAt = now },
                    cancellationToken);

                await store.UpdateProfileAsync(updatedProfile, cancellationToken);
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

    private static ProfileDocument CreateProfileShell(string tenantId, string eventType, DateTimeOffset now)
    {
        var profileId = $"pro_{Guid.NewGuid():N}";
        return new ProfileDocument(
            Id: profileId,
            TenantId: tenantId,
            ProfileId: profileId,
            DocType: Constants.Profiles.Shell,
            Status: Constants.Profiles.PendingReview,
            Identities: [],
            Traits: [],
            ProfileCard: string.Empty,
            Synopsis: $"Shell created from {eventType} event pending approval.",
            SynopsisVector: null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    private static string BuildPendingSynopsis(
        string eventType,
        string source,
        IReadOnlyCollection<ProfileIdentity> currentIdentities,
        IReadOnlyCollection<ProfileTrait> currentTraits,
        IReadOnlyCollection<ProfileIdentity> proposedIdentities,
        IReadOnlyCollection<ProfileTrait> proposedTraits)
    {
        var mergedIdentities = currentIdentities
            .Concat(proposedIdentities)
            .DistinctBy(identity => $"{identity.Type}::{identity.Value}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedTraits = currentTraits.ToDictionary(trait => trait.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var trait in proposedTraits)
        {
            mergedTraits[trait.Name] = trait;
        }

        return $"Pending {eventType} materialization from {source}. "
            + $"Profile identities: {FormatIdentitySummary(mergedIdentities)}. "
            + $"Profile traits: {FormatTraitSummary(mergedTraits.Values)}. "
            + $"Proposed identities: {FormatIdentitySummary(proposedIdentities)}. "
            + $"Proposed traits: {FormatTraitSummary(proposedTraits)}.";
    }

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

    private static string[] BuildOperations(string source, IReadOnlyCollection<ProfileIdentity> identities, IReadOnlyCollection<ProfileTrait> traits)
    {
        var operations = new List<string>(identities.Count + traits.Count);
        operations.AddRange(identities.Select(identity => $"Upsert identity {identity.Type}:{identity.Value} from {source}"));
        operations.AddRange(traits.Select(trait => $"Upsert trait {trait.Name}={trait.Value} from {source} (confidence {FormatConfidence(trait.Confidence)})"));

        if (operations.Count == 0)
        {
            operations.Add($"No-op review required for {source} event evidence");
        }

        return operations.ToArray();
    }

    private static string BuildExplanation(
        string eventId,
        string eventType,
        string source,
        DateTimeOffset occurredAt,
        string targetProfileId,
        bool createsProfile,
        IReadOnlyCollection<ProfileIdentity> proposedIdentities,
        IReadOnlyCollection<ProfileTrait> proposedTraits)
    {
        var profileAction = createsProfile ? "create" : "update";
        var context = $"Event {eventId} ({eventType}) from {source} at {occurredAt:O}";

        if (proposedIdentities.Count == 0 && proposedTraits.Count == 0)
        {
            return $"{context} was matched to profile {targetProfileId} for review, but it does not introduce any new identities or traits. Approval or rejection is still required to audit the event linkage.";
        }

        return $"{context} proposes a {profileAction} for profile {targetProfileId} by adding identities {FormatIdentitySummary(proposedIdentities)} and traits {FormatTraitSummary(proposedTraits)}. These changes are grounded in the source event payload and require review before profile materialization.";
    }

    private static ChangeSetEvidenceItem[] BuildEvidenceItems(
        IngestEventRequest request,
        IReadOnlyCollection<ProfileIdentity> proposedIdentities,
        IReadOnlyCollection<ProfileTrait> proposedTraits)
    {
        var evidenceItems = new List<ChangeSetEvidenceItem>(1 + proposedIdentities.Count + proposedTraits.Count)
        {
            new(
                Kind: "EventMetadata",
                Reference: request.EventId,
                Summary: $"Source event {request.EventId} ({request.EventType}) from {request.Source} occurred at {request.OccurredAt:O} and produced {proposedIdentities.Count} identity proposal(s) and {proposedTraits.Count} trait proposal(s).",
                Confidence: 1m,
                Source: request.Source,
                EventId: request.EventId,
                EventType: request.EventType,
                OccurredAt: request.OccurredAt)
        };

        evidenceItems.AddRange(proposedIdentities.Select(identity => new ChangeSetEvidenceItem(
            Kind: "IdentityObservation",
            Reference: $"{request.EventId}#identity:{identity.Type}:{identity.Value}",
            Summary: $"Observed identity {identity.Type}:{identity.Value} from source {identity.Source} in event {request.EventId} ({request.EventType}) at {request.OccurredAt:O}.",
            Confidence: 1m,
            Source: request.Source,
            EventId: request.EventId,
            EventType: request.EventType,
            OccurredAt: request.OccurredAt)));

        evidenceItems.AddRange(proposedTraits.Select(trait => new ChangeSetEvidenceItem(
            Kind: "TraitObservation",
            Reference: $"{request.EventId}#trait:{trait.Name}",
            Summary: $"Observed trait {trait.Name}={trait.Value} with confidence {FormatConfidence(trait.Confidence)} in event {request.EventId} ({request.EventType}) at {request.OccurredAt:O}.",
            Confidence: trait.Confidence,
            Source: request.Source,
            EventId: request.EventId,
            EventType: request.EventType,
            OccurredAt: request.OccurredAt)));

        return evidenceItems.ToArray();
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
            Synopsis = BuildApprovedSynopsis(identities, traits.Values)
        };
    }

    private static string BuildApprovedSynopsis(IEnumerable<ProfileIdentity> identities, IEnumerable<ProfileTrait> traits)
        => $"Approved profile. Identities: {FormatIdentitySummary(identities)}. Traits: {FormatTraitSummary(traits)}.";

    private async Task<RankedProfileCandidate[]> SearchProfileCandidatesAsync(
        string tenantId,
        IReadOnlyList<float> queryVector,
        IReadOnlyCollection<string> queryTerms,
        int limit,
        CancellationToken cancellationToken)
    {
        if (store is IProfileVectorSearchRuntimeStore vectorSearchStore)
        {
            try
            {
                var nativeMatches = await vectorSearchStore.SearchProfilesBySynopsisVectorAsync(tenantId, queryVector, limit, cancellationToken);
                return nativeMatches
                    .Select(match => CreateRankedProfileCandidate(queryTerms, match.Profile, match.SimilarityScore))
                    .ToArray();
            }
            catch (ProfileVectorSearchUnavailableException)
            {
            }
        }

        var profiles = await store.ListProfilesAsync(tenantId, cancellationToken);
        return profiles
            .Where(profile => profile.SynopsisVector is { Count: > 0 })
            .Select(profile => CreateRankedProfileCandidate(queryTerms, profile, CalculateCosineSimilarity(queryVector, profile.SynopsisVector!)))
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenByDescending(candidate => candidate.MatchedTerms.Count)
            .ThenByDescending(candidate => candidate.Profile.UpdatedAt)
            .Take(limit)
            .ToArray();
    }

    private async Task<ProfileDocument> PopulateSynopsisVectorAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.Synopsis))
        {
            return profile with { SynopsisVector = null };
        }

        var vector = await profileTextEmbeddingService.EmbedAsync(profile.Synopsis, cancellationToken);
        return profile with
        {
            SynopsisVector = vector.Count == 0 ? null : vector.ToArray()
        };
    }

    private async Task<ProfileDocument> PopulateProfileDerivedFieldsAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        var vectorizedProfile = await PopulateSynopsisVectorAsync(profile, cancellationToken);
        var profileCard = await profileCardGenerationService.GenerateAsync(vectorizedProfile, cancellationToken);
        return vectorizedProfile with { ProfileCard = profileCard };
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
            changeSet.Explanation,
            changeSet.EvidenceItems.Select(item => new ChangeSetEvidenceItemResponse(
                item.Kind,
                item.Reference,
                item.Summary,
                item.Confidence,
                item.Source,
                item.EventId,
                item.EventType,
                item.OccurredAt)).ToArray(),
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

    private static string FormatIdentitySummary(IEnumerable<ProfileIdentity> identities)
    {
        var values = identities
            .Select(identity => $"{identity.Type}:{identity.Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static string FormatTraitSummary(IEnumerable<ProfileTrait> traits)
    {
        var values = traits
            .Select(trait => $"{trait.Name}={trait.Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? "none" : string.Join(", ", values);
    }

    private static string FormatConfidence(decimal confidence)
        => confidence.ToString("0.##", CultureInfo.InvariantCulture);

    private static IReadOnlyCollection<string> ExtractSearchTerms(string text)
        => Regex.Matches(text, "[A-Za-z0-9@._:-]+")
            .Select(match => match.Value.Trim().ToLowerInvariant())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildSearchableText(ProfileDocument profile)
        => string.Join(' ', BuildSearchSegments(profile)).ToLowerInvariant();

    private static IEnumerable<string> BuildSearchSegments(ProfileDocument profile)
    {
        yield return profile.ProfileCard;
        yield return profile.Synopsis;

        foreach (var identity in profile.Identities)
        {
            yield return identity.Type;
            yield return identity.Value;
            yield return identity.Source;
        }

        foreach (var trait in profile.Traits)
        {
            yield return trait.Name;
            yield return trait.Value;
        }
    }

    private static IReadOnlyCollection<string> GetMatchedTerms(IReadOnlyCollection<string> queryTerms, ProfileDocument profile)
    {
        if (queryTerms.Count == 0)
        {
            return [];
        }

        var searchableText = BuildSearchableText(profile);
        return queryTerms
            .Where(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static RankedProfileCandidate CreateRankedProfileCandidate(IReadOnlyCollection<string> queryTerms, ProfileDocument profile, double similarity)
    {
        var matchedTerms = GetMatchedTerms(queryTerms, profile);
        return new RankedProfileCandidate(
            profile,
            similarity,
            matchedTerms,
            GetSharedIdentities(queryTerms, profile),
            GetSharedTraits(queryTerms, profile));
    }

    private static IReadOnlyCollection<IdentityDto> GetSharedIdentities(IReadOnlyCollection<string> queryTerms, ProfileDocument profile)
        => profile.Identities
            .Where(identity => MatchesAnyTerm(queryTerms, identity.Type, identity.Value, identity.Source))
            .Select(identity => new IdentityDto(identity.Type, identity.Value, identity.Source))
            .ToArray();

    private static IReadOnlyCollection<TraitDto> GetSharedTraits(IReadOnlyCollection<string> queryTerms, ProfileDocument profile)
        => profile.Traits
            .Where(trait => MatchesAnyTerm(queryTerms, trait.Name, trait.Value))
            .Select(trait => new TraitDto(trait.Name, trait.Value, trait.Confidence))
            .ToArray();

    private static bool MatchesAnyTerm(IReadOnlyCollection<string> queryTerms, params string[] values)
    {
        if (queryTerms.Count == 0)
        {
            return false;
        }

        var searchableText = string.Join(' ', values).ToLowerInvariant();
        return queryTerms.Any(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static double CalculateCosineSimilarity(IReadOnlyList<float> left, IReadOnlyCollection<float> right)
    {
        var rightVector = right as IReadOnlyList<float> ?? right.ToArray();
        var dimensions = Math.Min(left.Count, rightVector.Count);
        if (dimensions == 0)
        {
            return 0d;
        }

        double dot = 0d;
        double leftMagnitude = 0d;
        double rightMagnitude = 0d;

        for (var index = 0; index < dimensions; index++)
        {
            dot += left[index] * rightVector[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += rightVector[index] * rightVector[index];
        }

        if (leftMagnitude == 0d || rightMagnitude == 0d)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static double ComputeMagnitude(IReadOnlyList<float> vector)
    {
        double magnitude = 0d;
        for (var index = 0; index < vector.Count; index++)
        {
            magnitude += vector[index] * vector[index];
        }

        return Math.Sqrt(magnitude);
    }

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

    private static void ValidateSearchRequest(ProfileSearchRequest request)
    {
        ValidateTenantId(request.TenantId);
        ValidateRequired(request.QueryText, nameof(request.QueryText));

        if (request.Limit is <= 0)
        {
            throw new ArgumentException("Limit must be greater than zero.");
        }
    }

    private static int ResolveSearchLimit(int? limit)
    {
        var resolvedLimit = limit ?? DefaultProfileSearchLimit;
        return Math.Min(resolvedLimit, MaxProfileSearchLimit);
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

    private sealed record RankedProfileCandidate(
        ProfileDocument Profile,
        double Similarity,
        IReadOnlyCollection<string> MatchedTerms,
        IReadOnlyCollection<IdentityDto> SharedIdentities,
        IReadOnlyCollection<TraitDto> SharedTraits);
}
