using Cip.Application.Features.CipMvp;
using Cip.Application.Features.Health;
using Cip.Infrastructure.Persistence;
using Integrations.AzureAi;
using Integrations.AzureAi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cip.IntegrationTests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void ApplicationAndInfrastructureServices_CanBeResolved()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var healthService = provider.GetService<IApplicationHealthService>();
        var cipMvpService = provider.GetService<ICipMvpService>();
        var profileTextEmbeddingService = provider.GetService<IProfileTextEmbeddingService>();
        var processingStatusService = provider.GetService<IProcessingStatusService>();

        Assert.NotNull(healthService);
        Assert.NotNull(cipMvpService);
        Assert.NotNull(profileTextEmbeddingService);
        Assert.NotNull(processingStatusService);
    }

    [Fact]
    public void Infrastructure_RegistersAdaptiveEmbeddingService_WithDeterministicFallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var embeddingService = provider.GetRequiredService<IProfileTextEmbeddingService>();
        var fallbackService = provider.GetRequiredService<DeterministicProfileTextEmbeddingService>();

        Assert.IsType<AdaptiveProfileTextEmbeddingService>(embeddingService);
        Assert.NotNull(fallbackService);
    }

    [Fact]
    public void Infrastructure_UsesInMemoryRuntimeStore_WhenCosmosIsNotConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var runtimeStore = provider.GetRequiredService<ICipRuntimeStore>();

        Assert.IsType<InMemoryCipRuntimeStore>(runtimeStore);
    }

    [Fact]
    public void Infrastructure_BindsAzureAiOptions_FromNeutralAiSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureResources:AI:Endpoint"] = "https://contoso.openai.azure.com/",
                ["AzureResources:AI:EmbeddingsDeployment"] = "embeddings-deployment",
                ["AzureResources:AI:ChatDeployment"] = "chat-deployment"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AzureAiOptions>>().Value;

        Assert.Equal("https://contoso.openai.azure.com/", options.Endpoint);
        Assert.Equal("embeddings-deployment", options.EmbeddingsDeployment);
        Assert.Equal(128, options.EmbeddingsDimensions);
        Assert.Equal("chat-deployment", options.ChatDeployment);
    }

    [Fact]
    public void Infrastructure_UsesCosmosRuntimeStore_WhenCosmosIsConfiguredInAutoMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureResources:Cosmos:RuntimeMode"] = "Auto",
                ["AzureResources:Cosmos:AccountEndpoint"] = "https://contoso.documents.azure.com:443/",
                ["AzureResources:Cosmos:DatabaseName"] = "cip",
                ["AzureResources:Cosmos:EventsContainer"] = "events",
                ["AzureResources:Cosmos:OperationalContainer"] = "operational"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var runtimeStore = provider.GetRequiredService<ICipRuntimeStore>();

        Assert.Equal("CosmosCipRuntimeStore", runtimeStore.GetType().Name);
    }

    [Fact]
    public void Infrastructure_UsesInMemoryRuntimeStore_WhenModeForcesInMemory()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureResources:Cosmos:RuntimeMode"] = "InMemory",
                ["AzureResources:Cosmos:AccountEndpoint"] = "https://contoso.documents.azure.com:443/",
                ["AzureResources:Cosmos:DatabaseName"] = "cip",
                ["AzureResources:Cosmos:EventsContainer"] = "events",
                ["AzureResources:Cosmos:OperationalContainer"] = "operational"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var runtimeStore = provider.GetRequiredService<ICipRuntimeStore>();

        Assert.IsType<InMemoryCipRuntimeStore>(runtimeStore);
    }
}
