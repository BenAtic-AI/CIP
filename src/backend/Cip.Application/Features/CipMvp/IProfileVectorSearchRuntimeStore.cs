using Cip.Domain.Documents;

namespace Cip.Application.Features.CipMvp;

public interface IProfileVectorSearchRuntimeStore
{
    Task<IReadOnlyCollection<ProfileVectorSearchMatch>> SearchProfilesBySynopsisVectorAsync(
        string tenantId,
        IReadOnlyCollection<float> queryVector,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record ProfileVectorSearchMatch(ProfileDocument Profile, double SimilarityScore);

public sealed class ProfileVectorSearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
