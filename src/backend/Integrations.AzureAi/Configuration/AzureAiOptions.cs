namespace Integrations.AzureAi.Configuration;

public sealed class AzureAiOptions
{
    public const string SectionName = "AzureResources:AI";

    public string Endpoint { get; init; } = string.Empty;
    public string EmbeddingsDeployment { get; init; } = "text-embedding-3-large";
    public string ChatDeployment { get; init; } = "gpt-4.1-mini";
}
