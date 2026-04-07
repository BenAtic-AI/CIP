using Cip.Domain.Documents;

namespace Cip.Application.Features.CipMvp;

public interface IProfileCardGenerationService
{
    Task<string> GenerateAsync(ProfileDocument profile, CancellationToken cancellationToken);
}
