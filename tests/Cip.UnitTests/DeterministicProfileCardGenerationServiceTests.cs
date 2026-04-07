using Cip.Contracts.Constants;
using Cip.Domain.Documents;
using Integrations.AzureAi;
using Xunit;

namespace Cip.UnitTests;

public sealed class DeterministicProfileCardGenerationServiceTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsStableMarkdownFromProfileFields()
    {
        var service = new DeterministicProfileCardGenerationService();
        var profile = new ProfileDocument(
            Id: "pro_card",
            TenantId: "tenant-card",
            ProfileId: "pro_card",
            DocType: Constants.Profiles.Shell,
            Status: Constants.Profiles.Ready,
            Identities: [new ProfileIdentity("Email", "analyst@contoso.com", "unit-test")],
            Traits: [new ProfileTrait("Department", "Finance", 0.98m)],
            ProfileCard: string.Empty,
            Synopsis: "Approved profile. Identities: Email:analyst@contoso.com. Traits: Department=Finance.",
            SynopsisVector: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var markdown = await service.GenerateAsync(profile, CancellationToken.None);

        Assert.Equal(
            "### Profile summary\n"
            + "- **Status:** Ready\n"
            + "- **Identities:** Email:analyst@contoso.com\n"
            + "- **Traits:** Department=Finance\n"
            + "- **Synopsis:** Approved profile. Identities: Email:analyst@contoso.com. Traits: Department=Finance.\n"
            + "> AI-generated from stable profile fields; verify against source evidence.",
            markdown);
    }
}
