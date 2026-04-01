using Cip.Application.Features.CipMvp;
using Cip.Contracts.Constants;
using Cip.Domain.Documents;
using Integrations.Cosmos.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Integrations.Cosmos.Persistence;

public sealed class CosmosCipRuntimeStore : ICipRuntimeStore
{
    private const string EventDocumentType = "event";
    private const string ProfileDocumentType = "profile";
    private const string ChangeSetDocumentType = "changeSet";
    private const string TriggerDocumentType = "trigger";

    private readonly Container _eventsContainer;
    private readonly Container _operationalContainer;

    public CosmosCipRuntimeStore(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var cosmosOptions = options.Value;
        var database = client.GetDatabase(cosmosOptions.DatabaseName);
        _eventsContainer = database.GetContainer(cosmosOptions.EventsContainer);
        _operationalContainer = database.GetContainer(cosmosOptions.OperationalContainer);
    }

    public Task<EventEnvelope?> GetEventAsync(string tenantId, string eventId, CancellationToken cancellationToken)
        => ReadAsync<EventEnvelope>(_eventsContainer, BuildStoredId(EventDocumentType, eventId), tenantId, cancellationToken);

    public Task SaveEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken)
        => UpsertAsync(_eventsContainer, ToStoredDocument(EventDocumentType, eventEnvelope.Id, eventEnvelope.TenantId, eventEnvelope), cancellationToken);

    public Task UpdateEventAsync(EventEnvelope eventEnvelope, CancellationToken cancellationToken)
        => SaveEventAsync(eventEnvelope, cancellationToken);

    public Task<ProfileDocument?> GetProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken)
        => ReadAsync<ProfileDocument>(_operationalContainer, BuildStoredId(ProfileDocumentType, profileId), tenantId, cancellationToken);

    public async Task<ProfileDocument?> FindProfileByIdentityAsync(string tenantId, IReadOnlyCollection<ProfileIdentity> identities, CancellationToken cancellationToken)
    {
        var profiles = await ListProfilesAsync(tenantId, cancellationToken);
        return profiles.FirstOrDefault(profile => profile.Identities.Any(existing => identities.Any(candidate =>
            string.Equals(existing.Type, candidate.Type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Value, candidate.Value, StringComparison.OrdinalIgnoreCase))));
    }

    public Task<IReadOnlyCollection<ProfileDocument>> ListProfilesAsync(string tenantId, CancellationToken cancellationToken)
        => QueryPayloadsAsync<ProfileDocument>(
            _operationalContainer,
            new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId AND c.documentType = @documentType")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@documentType", ProfileDocumentType),
            cancellationToken);

    public Task SaveProfileAsync(ProfileDocument profile, CancellationToken cancellationToken)
        => UpsertAsync(_operationalContainer, ToStoredDocument(ProfileDocumentType, profile.ProfileId, profile.TenantId, profile), cancellationToken);

    public Task UpdateProfileAsync(ProfileDocument profile, CancellationToken cancellationToken)
        => SaveProfileAsync(profile, cancellationToken);

    public Task<ChangeSetDocument?> GetChangeSetAsync(string tenantId, string changeSetId, CancellationToken cancellationToken)
        => ReadAsync<ChangeSetDocument>(_operationalContainer, BuildStoredId(ChangeSetDocumentType, changeSetId), tenantId, cancellationToken);

    public Task<IReadOnlyCollection<ChangeSetDocument>> ListChangeSetsAsync(string tenantId, string? status, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.tenantId = @tenantId AND c.documentType = @documentType"
            + (string.IsNullOrWhiteSpace(status) ? string.Empty : " AND c.payload.status = @status"))
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@documentType", ChangeSetDocumentType);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.WithParameter("@status", status);
        }

        return QueryPayloadsAsync<ChangeSetDocument>(_operationalContainer, query, cancellationToken);
    }

    public Task SaveChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken)
        => UpsertAsync(_operationalContainer, ToStoredDocument(ChangeSetDocumentType, changeSet.Id, changeSet.TenantId, changeSet), cancellationToken);

    public Task UpdateChangeSetAsync(ChangeSetDocument changeSet, CancellationToken cancellationToken)
        => SaveChangeSetAsync(changeSet, cancellationToken);

    public Task<IReadOnlyCollection<ChangeSetDocument>> ListPendingChangeSetsForProfileAsync(string tenantId, string profileId, CancellationToken cancellationToken)
        => QueryPayloadsAsync<ChangeSetDocument>(
            _operationalContainer,
            new QueryDefinition(
                "SELECT * FROM c WHERE c.tenantId = @tenantId AND c.documentType = @documentType AND c.payload.targetProfileId = @profileId AND c.payload.status = @status")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@documentType", ChangeSetDocumentType)
                .WithParameter("@profileId", profileId)
                .WithParameter("@status", Constants.ChangeSets.Pending),
            cancellationToken);

    public Task<TriggerDefinitionDocument?> GetTriggerAsync(string tenantId, string triggerId, CancellationToken cancellationToken)
        => ReadAsync<TriggerDefinitionDocument>(_operationalContainer, BuildStoredId(TriggerDocumentType, triggerId), tenantId, cancellationToken);

    public Task<IReadOnlyCollection<TriggerDefinitionDocument>> ListTriggersAsync(string tenantId, CancellationToken cancellationToken)
        => QueryPayloadsAsync<TriggerDefinitionDocument>(
            _operationalContainer,
            new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId AND c.documentType = @documentType")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@documentType", TriggerDocumentType),
            cancellationToken);

    public Task SaveTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken)
        => UpsertAsync(_operationalContainer, ToStoredDocument(TriggerDocumentType, trigger.Id, trigger.TenantId, trigger), cancellationToken);

    public Task UpdateTriggerAsync(TriggerDefinitionDocument trigger, CancellationToken cancellationToken)
        => SaveTriggerAsync(trigger, cancellationToken);

    public async Task<ProcessingStatusSnapshot> GetProcessingStatusAsync(CancellationToken cancellationToken)
    {
        var eventCountTask = CountAsync(
            _eventsContainer,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.documentType = @documentType")
                .WithParameter("@documentType", EventDocumentType),
            cancellationToken);

        var profileCountTask = CountAsync(
            _operationalContainer,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.documentType = @documentType")
                .WithParameter("@documentType", ProfileDocumentType),
            cancellationToken);

        var pendingChangeSetCountTask = CountAsync(
            _operationalContainer,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.documentType = @documentType AND c.payload.status = @status")
                .WithParameter("@documentType", ChangeSetDocumentType)
                .WithParameter("@status", Constants.ChangeSets.Pending),
            cancellationToken);

        var triggerCountTask = CountAsync(
            _operationalContainer,
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.documentType = @documentType")
                .WithParameter("@documentType", TriggerDocumentType),
            cancellationToken);

        await Task.WhenAll(eventCountTask, profileCountTask, pendingChangeSetCountTask, triggerCountTask);

        return new ProcessingStatusSnapshot(
            Constants.Runtime.Cosmos,
            eventCountTask.Result,
            profileCountTask.Result,
            pendingChangeSetCountTask.Result,
            triggerCountTask.Result,
            DateTimeOffset.UtcNow);
    }

    private static async Task<TDocument?> ReadAsync<TDocument>(Container container, string id, string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<CosmosStoredDocument<TDocument>>(
                id,
                new PartitionKey(tenantId),
                cancellationToken: cancellationToken);

            return response.Resource.Payload;
        }
        catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private static async Task UpsertAsync<TDocument>(Container container, CosmosStoredDocument<TDocument> document, CancellationToken cancellationToken)
        => await container.UpsertItemAsync(document, new PartitionKey(document.TenantId), cancellationToken: cancellationToken);

    private static async Task<IReadOnlyCollection<TDocument>> QueryPayloadsAsync<TDocument>(Container container, QueryDefinition query, CancellationToken cancellationToken)
    {
        var results = new List<TDocument>();
        var iterator = container.GetItemQueryIterator<CosmosStoredDocument<TDocument>>(query);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response.Select(item => item.Payload));
        }

        return results;
    }

    private static async Task<int> CountAsync(Container container, QueryDefinition query, CancellationToken cancellationToken)
    {
        var iterator = container.GetItemQueryIterator<int>(query);
        var count = 0;

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            count += response.FirstOrDefault();
        }

        return count;
    }

    private static CosmosStoredDocument<TDocument> ToStoredDocument<TDocument>(string documentType, string documentId, string tenantId, TDocument payload)
        => new(BuildStoredId(documentType, documentId), tenantId, documentType, payload);

    private static string BuildStoredId(string documentType, string documentId) => $"{documentType}::{documentId}";

    private sealed record CosmosStoredDocument<TDocument>(string Id, string TenantId, string DocumentType, TDocument Payload);
}
