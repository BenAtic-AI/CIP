using Cip.Contracts.Constants;

namespace Integrations.Cosmos.Configuration;

public sealed class CosmosOptions
{
    public const string SectionName = "AzureResources:Cosmos";

    public string RuntimeMode { get; init; } = Constants.Runtime.Auto;
    public string AccountEndpoint { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = "cip";
    public string EventsContainer { get; init; } = "events";
    public string OperationalContainer { get; init; } = "operational";
    public string LeasesContainer { get; init; } = "leases";

    public bool IsConfigured()
        => !string.IsNullOrWhiteSpace(AccountEndpoint)
            && !string.IsNullOrWhiteSpace(DatabaseName)
            && !string.IsNullOrWhiteSpace(EventsContainer)
            && !string.IsNullOrWhiteSpace(OperationalContainer);

    public string GetRuntimeMode()
    {
        if (string.Equals(RuntimeMode, Constants.Runtime.Cosmos, StringComparison.OrdinalIgnoreCase))
        {
            return Constants.Runtime.Cosmos;
        }

        if (string.Equals(RuntimeMode, Constants.Runtime.InMemory, StringComparison.OrdinalIgnoreCase))
        {
            return Constants.Runtime.InMemory;
        }

        return Constants.Runtime.Auto;
    }

    public bool ShouldUseCosmos()
        => !string.Equals(GetRuntimeMode(), Constants.Runtime.InMemory, StringComparison.OrdinalIgnoreCase)
            && IsConfigured();
}
