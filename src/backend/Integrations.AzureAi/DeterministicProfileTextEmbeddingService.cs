using Cip.Application.Features.CipMvp;
using System.Text.RegularExpressions;

namespace Integrations.AzureAi;

public sealed partial class DeterministicProfileTextEmbeddingService : IProfileTextEmbeddingService
{
    private const int Dimensions = 128;

    public Task<IReadOnlyCollection<float>> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var vector = new float[Dimensions];
        foreach (var token in TokenRegex().Matches(text ?? string.Empty).Select(match => match.Value.Trim().ToLowerInvariant()))
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            AddWeightedToken(vector, token, 1f);

            if (token.Length < 3)
            {
                continue;
            }

            for (var index = 0; index <= token.Length - 3; index++)
            {
                AddWeightedToken(vector, token.Substring(index, 3), 0.25f);
            }
        }

        Normalize(vector);
        return Task.FromResult<IReadOnlyCollection<float>>(vector);
    }

    private static void AddWeightedToken(float[] vector, string token, float weight)
    {
        var hash = ComputeHash(token);
        var index = (int)(hash % Dimensions);
        vector[index] += weight;
    }

    private static uint ComputeHash(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static void Normalize(float[] vector)
    {
        double magnitude = 0;
        for (var index = 0; index < vector.Length; index++)
        {
            magnitude += vector[index] * vector[index];
        }

        if (magnitude == 0)
        {
            return;
        }

        var scale = (float)(1d / Math.Sqrt(magnitude));
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] *= scale;
        }
    }

    [GeneratedRegex("[A-Za-z0-9@._:-]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
