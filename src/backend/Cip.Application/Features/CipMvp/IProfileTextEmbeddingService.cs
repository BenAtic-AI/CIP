namespace Cip.Application.Features.CipMvp;

public interface IProfileTextEmbeddingService
{
    Task<IReadOnlyCollection<float>> EmbedAsync(string text, CancellationToken cancellationToken);
}
