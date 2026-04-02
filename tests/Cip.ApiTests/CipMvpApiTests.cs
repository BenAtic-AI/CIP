using System.Net;
using System.Net.Http.Json;
using Cip.Contracts.ChangeSets;
using Cip.Contracts.Events;
using Cip.Contracts.Profiles;
using Cip.Contracts.Shared;
using Cip.Contracts.Triggers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Cip.ApiTests;

public sealed class CipMvpApiTests
{
    [Fact]
    public async Task PostEvent_MaterializesProfileShellAndPendingChangeSet()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateEventRequest("tenant-a", "event-001", "Finance", "analyst@contoso.com");

        var postResponse = await client.PostAsJsonAsync("/api/events", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var ingestion = await postResponse.Content.ReadFromJsonAsync<IngestEventResponse>();
        Assert.NotNull(ingestion);
        Assert.False(ingestion.Duplicate);

        var profiles = await client.GetFromJsonAsync<IReadOnlyCollection<ProfileResponse>>($"/api/profiles?tenantId={request.TenantId}");
        var profile = Assert.Single(profiles!);
        Assert.Equal(ingestion.ProfileId, profile.ProfileId);
        Assert.Equal(1, profile.PendingChangeSetCount);
        Assert.Empty(profile.Identities);
        Assert.Empty(profile.Traits);

        var changeSets = await client.GetFromJsonAsync<IReadOnlyCollection<ChangeSetResponse>>($"/api/change-sets?tenantId={request.TenantId}");
        var changeSet = Assert.Single(changeSets!);
        Assert.Equal(ingestion.ChangeSetId, changeSet.ChangeSetId);
        Assert.Single(changeSet.ProposedIdentities);
        Assert.Single(changeSet.ProposedTraits);
    }

    [Fact]
    public async Task PostEvent_IsIdempotentByTenantAndEventId()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateEventRequest("tenant-a", "event-duplicate", "Finance", "analyst@contoso.com");

        var first = await client.PostAsJsonAsync("/api/events", request);
        var second = await client.PostAsJsonAsync("/api/events", request);

        var firstPayload = await first.Content.ReadFromJsonAsync<IngestEventResponse>();
        var secondPayload = await second.Content.ReadFromJsonAsync<IngestEventResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotNull(firstPayload);
        Assert.NotNull(secondPayload);
        Assert.True(secondPayload.Duplicate);
        Assert.Equal(firstPayload.ChangeSetId, secondPayload.ChangeSetId);
        Assert.Equal(firstPayload.ProfileId, secondPayload.ProfileId);

        var changeSets = await client.GetFromJsonAsync<IReadOnlyCollection<ChangeSetResponse>>($"/api/change-sets?tenantId={request.TenantId}");
        Assert.Single(changeSets!);
    }

    [Fact]
    public async Task ApproveChangeSet_UpdatesProfileAndTriggersCanMatch()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateEventRequest("tenant-b", "event-approval", "Finance", "approver@contoso.com");
        var ingestResponse = await client.PostAsJsonAsync("/api/events", request);
        var ingestion = await ingestResponse.Content.ReadFromJsonAsync<IngestEventResponse>();

        var approveResponse = await client.PostAsJsonAsync(
            $"/api/change-sets/{ingestion!.ChangeSetId}/approve",
            new ReviewChangeSetRequest(request.TenantId, "reviewer@contoso.com", "looks good"));

        approveResponse.EnsureSuccessStatusCode();

        var profile = await client.GetFromJsonAsync<ProfileResponse>($"/api/profiles/{ingestion.ProfileId}?tenantId={request.TenantId}");
        Assert.NotNull(profile);
        Assert.Equal("Ready", profile.Status);
        Assert.Single(profile.Identities);
        Assert.Single(profile.Traits);
        Assert.Equal(0, profile.PendingChangeSetCount);

        var triggerResponse = await client.PostAsJsonAsync(
            "/api/triggers",
            new TriggerDefinitionRequest(
                request.TenantId,
                "Finance profiles",
                "Matches approved finance profiles",
                [new TriggerConditionRequest("TraitEquals", "Department", "Finance")]));

        var trigger = await triggerResponse.Content.ReadFromJsonAsync<TriggerDefinitionResponse>();
        Assert.NotNull(trigger);

        var runResponse = await client.PostAsJsonAsync($"/api/triggers/{trigger!.TriggerId}/run", new RunTriggerRequest(request.TenantId));
        runResponse.EnsureSuccessStatusCode();

        var run = await runResponse.Content.ReadFromJsonAsync<RunTriggerResponse>();
        Assert.NotNull(run);
        Assert.Equal(1, run.MatchedProfileCount);
        Assert.Equal(profile.ProfileId, Assert.Single(run.MatchedProfiles).ProfileId);
    }

    [Fact]
    public async Task PostProfileSearch_ReturnsRankedResults()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var financeRequest = CreateEventRequest("tenant-search", "event-finance", "Finance", "analyst@contoso.com");
        var legalRequest = CreateEventRequest("tenant-search", "event-legal", "Legal", "reviewer@contoso.com");

        var financeIngestionResponse = await client.PostAsJsonAsync("/api/events", financeRequest);
        var financeIngestion = await financeIngestionResponse.Content.ReadFromJsonAsync<IngestEventResponse>();
        financeIngestionResponse.EnsureSuccessStatusCode();

        var legalIngestionResponse = await client.PostAsJsonAsync("/api/events", legalRequest);
        var legalIngestion = await legalIngestionResponse.Content.ReadFromJsonAsync<IngestEventResponse>();
        legalIngestionResponse.EnsureSuccessStatusCode();

        var financeApprovalResponse = await client.PostAsJsonAsync(
            $"/api/change-sets/{financeIngestion!.ChangeSetId}/approve",
            new ReviewChangeSetRequest(financeRequest.TenantId, "reviewer@contoso.com", "approved"));
        financeApprovalResponse.EnsureSuccessStatusCode();

        var legalApprovalResponse = await client.PostAsJsonAsync(
            $"/api/change-sets/{legalIngestion!.ChangeSetId}/approve",
            new ReviewChangeSetRequest(legalRequest.TenantId, "reviewer@contoso.com", "approved"));
        legalApprovalResponse.EnsureSuccessStatusCode();

        var searchResponse = await client.PostAsJsonAsync(
            "/api/profiles/search",
            new ProfileSearchRequest(financeRequest.TenantId, "finance analyst", 5));

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

        var search = await searchResponse.Content.ReadFromJsonAsync<ProfileSearchResponse>();
        Assert.NotNull(search);
        Assert.Equal("finance analyst", search!.QueryText);
        Assert.Equal(2, search.Results.Count);

        var rankedResults = search.Results.ToArray();
        Assert.Equal(financeIngestion.ProfileId, rankedResults[0].Profile.ProfileId);
        Assert.Equal(legalIngestion.ProfileId, rankedResults[1].Profile.ProfileId);
        Assert.True(rankedResults[0].SimilarityScore > rankedResults[1].SimilarityScore);
        Assert.Contains(rankedResults[0].SharedTraits, trait => trait.Name == "Department" && trait.Value == "Finance");
        Assert.Contains(rankedResults[0].SharedIdentities, identity => identity.Type == "Email" && identity.Value == "analyst@contoso.com");
    }

    private static IngestEventRequest CreateEventRequest(string tenantId, string eventId, string department, string email)
        => new(
            tenantId,
            eventId,
            "PersonObserved",
            "demo-source",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            [new IdentityDto("Email", email, "demo-source")],
            [new TraitDto("Department", department, 0.98m)]);
}
