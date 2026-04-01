namespace Integrations.Storage.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "AzureResources:Storage";

    public string AccountName { get; init; } = string.Empty;
    public string RawArtifactsContainer { get; init; } = "artifacts-raw";
    public string RenderedArtifactsContainer { get; init; } = "artifacts-rendered";
}
