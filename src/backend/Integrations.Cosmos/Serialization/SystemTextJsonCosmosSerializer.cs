using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Integrations.Cosmos.Serialization;

public sealed class SystemTextJsonCosmosSerializer : CosmosSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override T FromStream<T>(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (stream.Length == 0)
        {
            return default!;
        }

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (stream)
        {
            return JsonSerializer.Deserialize<T>(stream, SerializerOptions)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, input, SerializerOptions);
        stream.Position = 0;
        return stream;
    }
}
