using Cip.Application.Features.CipMvp;
using Cip.Contracts.ChangeSets;
using Cip.Contracts.Events;
using Cip.Contracts.Profiles;
using Cip.Contracts.Shared;
using Cip.Domain.Documents;
using Integrations.AzureAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cip.UnitTests;

public sealed class CipMvpServiceTests
{
    [Fact]
    public async Task ProfileTextEmbeddingService_ReturnsStableVectors()
    {
        using var provider = BuildServiceProvider();
        var embeddingService = provider.GetRequiredService<IProfileTextEmbeddingService>();

        var first = (await embeddingService.EmbedAsync("Finance analyst analyst@contoso.com", CancellationToken.None)).ToArray();
        var second = (await embeddingService.EmbedAsync("Finance analyst analyst@contoso.com", CancellationToken.None)).ToArray();
        var different = (await embeddingService.EmbedAsync("Legal reviewer legal@contoso.com", CancellationToken.None)).ToArray();

        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.Contains(first, value => value != 0f);
    }

    [Fact]
    public async Task RejectChangeSet_PreservesApprovedProfileState()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetRequiredService<ICipMvpService>();

        var ingestion = await service.IngestEventAsync(
            new IngestEventRequest(
                "tenant-unit",
                "event-reject",
                "PersonObserved",
                "unit-test",
                DateTimeOffset.UtcNow,
                [new IdentityDto("Email", "reject@contoso.com", "unit-test")],
                [new TraitDto("Department", "Legal", 0.9m)]),
            CancellationToken.None);

        var rejected = await service.RejectChangeSetAsync(
            "tenant-unit",
            ingestion.ChangeSetId,
            "reviewer@contoso.com",
            "not enough evidence",
            CancellationToken.None);

        var profile = await service.GetProfileAsync("tenant-unit", ingestion.ProfileId, CancellationToken.None);

        Assert.NotNull(rejected);
        Assert.Equal("Rejected", rejected!.Status);
        Assert.NotNull(profile);
        Assert.Empty(profile!.Identities);
        Assert.Empty(profile.Traits);
        Assert.Equal(0, profile.PendingChangeSetCount);
    }

    [Fact]
    public async Task IngestAndApproveChangeSet_GeneratesProfileCardsFromProfileContent()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetRequiredService<ICipMvpService>();

        var ingestion = await service.IngestEventAsync(
            CreateEventRequest("tenant-cards", "event-cards", "Finance", "analyst@contoso.com"),
            CancellationToken.None);

        var pendingProfile = await service.GetProfileAsync("tenant-cards", ingestion.ProfileId, CancellationToken.None);

        Assert.NotNull(pendingProfile);
        Assert.Contains("### Profile summary", pendingProfile!.ProfileCard);
        Assert.Contains("Pending review", pendingProfile.ProfileCard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("none", pendingProfile.ProfileCard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Shell created from PersonObserved", pendingProfile.ProfileCard, StringComparison.Ordinal);

        await service.ApproveChangeSetAsync(
            "tenant-cards",
            ingestion.ChangeSetId,
            "reviewer@contoso.com",
            "approved",
            CancellationToken.None);

        var approvedProfile = await service.GetProfileAsync("tenant-cards", ingestion.ProfileId, CancellationToken.None);

        Assert.NotNull(approvedProfile);
        Assert.Contains("Ready", approvedProfile!.ProfileCard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Email:analyst@contoso.com", approvedProfile.ProfileCard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Department=Finance", approvedProfile.ProfileCard, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(pendingProfile.ProfileCard, approvedProfile.ProfileCard);
    }

    [Fact]
    public async Task IngestEvent_GeneratesDeterministicExplanationAndEvidence()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetRequiredService<ICipMvpService>();
        var occurredAt = new DateTimeOffset(2026, 4, 1, 12, 30, 0, TimeSpan.Zero);
        var request = new IngestEventRequest(
            "tenant-evidence",
            "event-evidence",
            "PersonObserved",
            "unit-test",
            occurredAt,
            [new IdentityDto("Email", "evidence@contoso.com", "unit-test")],
            [new TraitDto("Department", "Finance", 0.91m)]);

        var ingestion = await service.IngestEventAsync(request, CancellationToken.None);
        var changeSet = await service.GetChangeSetAsync(request.TenantId, ingestion.ChangeSetId, CancellationToken.None);

        Assert.NotNull(changeSet);
        Assert.Equal(request.EventId, changeSet!.SourceEventId);
        Assert.Equal($"Event {request.EventId} ({request.EventType}) from {request.Source} at {request.OccurredAt:O} proposes a create for profile {ingestion.ProfileId} by adding identities Email:evidence@contoso.com and traits Department=Finance. These changes are grounded in the source event payload and require review before profile materialization.", changeSet.Explanation);
        Assert.Equal([request.EventId, request.Source], changeSet.EvidenceReferences);
        Assert.Equal(3, changeSet.EvidenceItems.Count);

        var metadata = changeSet.EvidenceItems.First();
        Assert.Equal("EventMetadata", metadata.Kind);
        Assert.Equal(request.EventId, metadata.Reference);
        Assert.Equal(request.Source, metadata.Source);
        Assert.Equal(request.EventId, metadata.EventId);
        Assert.Equal(request.EventType, metadata.EventType);
        Assert.Equal(request.OccurredAt, metadata.OccurredAt);

        Assert.Contains(changeSet.EvidenceItems, item =>
            item.Kind == "IdentityObservation"
            && item.Reference == "event-evidence#identity:Email:evidence@contoso.com"
            && item.Confidence == 1m
            && item.Source == request.Source);

        Assert.Contains(changeSet.EvidenceItems, item =>
            item.Kind == "TraitObservation"
            && item.Reference == "event-evidence#trait:Department"
            && item.Confidence == 0.91m
            && item.Source == request.Source);
    }

    [Fact]
    public async Task IngestEvent_WithNoNewChanges_GeneratesAuditExplanation()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetRequiredService<ICipMvpService>();

        var initialRequest = CreateEventRequest("tenant-noop", "event-noop-1", "Finance", "noop@contoso.com");
        var initialIngestion = await service.IngestEventAsync(initialRequest, CancellationToken.None);

        await service.ApproveChangeSetAsync(
            initialRequest.TenantId,
            initialIngestion.ChangeSetId,
            "reviewer@contoso.com",
            "approved",
            CancellationToken.None);

        var secondRequest = new IngestEventRequest(
            initialRequest.TenantId,
            "event-noop-2",
            initialRequest.EventType,
            initialRequest.Source,
            new DateTimeOffset(2026, 4, 2, 8, 15, 0, TimeSpan.Zero),
            initialRequest.Identities,
            initialRequest.Traits,
            initialRequest.SchemaVersion);

        var secondIngestion = await service.IngestEventAsync(secondRequest, CancellationToken.None);
        var changeSet = await service.GetChangeSetAsync(secondRequest.TenantId, secondIngestion.ChangeSetId, CancellationToken.None);

        Assert.NotNull(changeSet);
        Assert.Equal(["No-op review required for unit-test event evidence"], changeSet!.ProposedOperations);
        Assert.Equal($"Event {secondRequest.EventId} ({secondRequest.EventType}) from {secondRequest.Source} at {secondRequest.OccurredAt:O} was matched to profile {secondIngestion.ProfileId} for review, but it does not introduce any new identities or traits. Approval or rejection is still required to audit the event linkage.", changeSet.Explanation);
        Assert.Single(changeSet.EvidenceItems);
        Assert.Equal("EventMetadata", changeSet.EvidenceItems.Single().Kind);
    }

    [Fact]
    public async Task SearchProfiles_RanksMostRelevantProfileAndPersistsSynopsisVector()
    {
        using var provider = BuildServiceProvider();
        var service = provider.GetRequiredService<ICipMvpService>();
        var store = provider.GetRequiredService<ICipRuntimeStore>();

        var financeIngestion = await service.IngestEventAsync(
            CreateEventRequest("tenant-search", "event-finance", "Finance", "analyst@contoso.com"),
            CancellationToken.None);

        await service.ApproveChangeSetAsync(
            "tenant-search",
            financeIngestion.ChangeSetId,
            "reviewer@contoso.com",
            "approved",
            CancellationToken.None);

        var legalIngestion = await service.IngestEventAsync(
            CreateEventRequest("tenant-search", "event-legal", "Legal", "reviewer@contoso.com"),
            CancellationToken.None);

        await service.ApproveChangeSetAsync(
            "tenant-search",
            legalIngestion.ChangeSetId,
            "reviewer@contoso.com",
            "approved",
            CancellationToken.None);

        var search = await service.SearchProfilesAsync(
            new ProfileSearchRequest("tenant-search", "finance analyst", 3),
            CancellationToken.None);

        var financeProfile = await store.GetProfileAsync("tenant-search", financeIngestion.ProfileId, CancellationToken.None);

        Assert.NotNull(financeProfile);
        Assert.NotNull(financeProfile!.SynopsisVector);
        Assert.NotEmpty(financeProfile.SynopsisVector!);

        Assert.NotEmpty(search.Results);
        var topResult = search.Results.First();

        Assert.Equal(financeIngestion.ProfileId, topResult.Profile.ProfileId);
        Assert.Contains(topResult.SharedTraits, trait => trait.Name == "Department" && trait.Value == "Finance");
        Assert.Contains(topResult.SharedIdentities, identity => identity.Type == "Email" && identity.Value == "analyst@contoso.com");
        Assert.Contains("finance", topResult.Evidence.MatchedTerms, StringComparer.OrdinalIgnoreCase);
        Assert.True(search.Results.Count <= 3);

        if (search.Results.Count > 1)
        {
            Assert.True(topResult.SimilarityScore >= search.Results.Skip(1).Max(result => result.SimilarityScore));
        }
    }

    [Fact]
    public async Task SearchProfiles_PrefersNativeVectorRuntimeStore_WhenAvailable()
    {
        var profile = new ProfileDocument(
            Id: "pro_native",
            TenantId: "tenant-native",
            ProfileId: "pro_native",
            DocType: "profile",
            Status: "Ready",
            Identities: [new ProfileIdentity("Email", "analyst@contoso.com", "unit-test")],
            Traits: [new ProfileTrait("Department", "Finance", 0.99m)],
            ProfileCard: "Email:analyst@contoso.com",
            Synopsis: "Approved finance analyst profile.",
            SynopsisVector: [1f, 0f],
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt: DateTimeOffset.UtcNow);

        var store = new NativeVectorSearchStore(profile, 0.97d);
        var service = new CipMvpService(store, new StubEmbeddingService([1f, 0f]), new StubProfileCardGenerationService("### Profile summary\n- **Status:** Ready"));

        var response = await service.SearchProfilesAsync(
            new ProfileSearchRequest("tenant-native", "finance analyst", 3),
            CancellationToken.None);

        Assert.True(store.NativeSearchCalled);
        Assert.False(store.ListProfilesCalled);
        Assert.Single(response.Results);
        var result = response.Results.First();
        Assert.Equal(profile.ProfileId, result.Profile.ProfileId);
        Assert.Equal(0.97d, result.SimilarityScore);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static IngestEventRequest CreateEventRequest(string tenantId, string eventId, string department, string email)
        => new(
            tenantId,
            eventId,
            "PersonObserved",
            "unit-test",
            DateTimeOffset.UtcNow,
            [new IdentityDto("Email", email, "unit-test")],
            [new TraitDto("Department", department, 0.99m)]);

    private sealed class StubEmbeddingService(IReadOnlyCollection<float> vector) : IProfileTextEmbeddingService
    {
        public Task<IReadOnlyCollection<float>> EmbedAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult(vector);
    }

    private sealed class StubProfileCardGenerationService(string markdown) : IProfileCardGenerationService
    {
        public Task<string> GenerateAsync(ProfileDocument profile, CancellationToken cancellationToken)
            => Task.FromResult(markdown);
    }

    private sealed class NativeVectorSearchStore(ProfileDocument profile, double similarityScore) : ICipRuntimeStore, IProfileVectorSearchRuntimeStore
    {
        public bool NativeSearchCalled { get; private set; }
        public bool ListProfilesCalled { get; private set; }

        public Task<IReadOnlyCollection<ProfileVectorSearchMatch>> SearchProfilesBySynopsisVectorAsync(string tenantId, IReadOnlyCollection<float> queryVector, int limit, CancellationToken cancellationToken)
        {
            NativeSearchCalled = true;
            IReadOnlyCollection<ProfileVectorSearchMatch> results = [new ProfileVectorSearchMatch(profile, similarityScore)];
            return Task.FromResult(results);
        }

        public Task<IReadOnlyCollection<ChangeSetDocument>> ListPendingChangeSetsForProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<ChangeSetDocument>>([]);

        public Task<IReadOnlyCollection<ProfileDocument>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken)
        {
            ListProfilesCalled = true;
            throw new InvalidOperationException("Native search path should be used instead of app-side ranking.");
        }

        public Task<EventEnvelope?> GetEventAsync(string tenantId, string eventId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProfileDocument?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProfileDocument?> FindProfileByIdentityAsync(string tenantId, IReadOnlyCollection<ProfileIdentity> identities, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveProfileAsync(ProfileDocument profile, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateProfileAsync(ProfileDocument profile, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ChangeSetDocument?> GetChangeSetAsync(string tenantId, string changeSetId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<ChangeSetDocument>> ListChangeSetsAsync(string tenantId, string? status, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TriggerDefinitionDocument?> GetTriggerAsync(string tenantId, string triggerId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyCollection<TriggerDefinitionDocument>> ListTriggersAsync(string tenantId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProcessingStatusSnapshot> GetProcessingStatusAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
