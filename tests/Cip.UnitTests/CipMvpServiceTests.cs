using Cip.Application.Features.CipMvp;
using Cip.Contracts.ChangeSets;
using Cip.Contracts.Events;
using Cip.Contracts.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cip.UnitTests;

public sealed class CipMvpServiceTests
{
    [Fact]
    public async Task RejectChangeSet_PreservesApprovedProfileState()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddCipApplication();
        services.AddCipInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
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
}
