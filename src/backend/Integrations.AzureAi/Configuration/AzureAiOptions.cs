namespace Integrations.AzureAi.Configuration;

public sealed class AzureAiOptions
{
    public const string SectionName = "AzureResources:AI";

    public string Endpoint { get; init; } = string.Empty;
    public string EmbeddingsDeployment { get; init; } = "text-embedding-3-large";
    public int EmbeddingsDimensions { get; init; } = 128;
    public string ChatDeployment { get; init; } = "gpt-4.1-mini";
    public bool UseLiveEmbeddings { get; init; }
    public bool UseLiveProfileCards { get; init; }

    public bool HasEmbeddingsConfiguration()
        => Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(EmbeddingsDeployment);

    public bool ShouldUseLiveEmbeddings()
        => UseLiveEmbeddings && HasEmbeddingsConfiguration();

    public bool HasChatConfiguration()
        => Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(ChatDeployment);

    public bool ShouldUseLiveProfileCards()
        => UseLiveProfileCards && HasChatConfiguration();
}
