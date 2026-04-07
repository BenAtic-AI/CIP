using Cip.Application.Features.CipMvp;
using Cip.Domain.Documents;

namespace Integrations.AzureAi;

public sealed class DeterministicProfileCardGenerationService : IProfileCardGenerationService
{
    public Task<string> GenerateAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ProfileCardMarkdown.CreateDeterministic(profile));
    }
}
