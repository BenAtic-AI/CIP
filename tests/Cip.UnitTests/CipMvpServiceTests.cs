using Cip.Application.Features.CipMvp;
using Cip.Contracts.ChangeSets;
using Cip.Contracts.Events;
using Cip.Contracts.Profiles;
using Cip.Contracts.Shared;
using Cip.Domain.Documents;
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
        Assert.True(first.Any(value => value != 0f));
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
        var service = new CipMvpService(store, new StubEmbeddingService([1f, 0f]));

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
