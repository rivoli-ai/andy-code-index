using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace Andy.CodeIndex.Infrastructure.Data.Converters;

/// <summary>
/// Stores a pgvector <see cref="Vector"/> as a little-endian float32 BLOB so the
/// SQLite backend can persist embeddings without the Postgres <c>vector</c> type.
/// Vector similarity is computed in-process for SQLite (see SearchService); this
/// converter only handles persistence.
/// </summary>
public sealed class VectorBlobConverter : ValueConverter<Vector, byte[]>
{
    public VectorBlobConverter()
        : base(v => ToBytes(v), b => ToVector(b))
    {
    }

    public static byte[] ToBytes(Vector vector)
    {
        var floats = vector.ToArray();
        var bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] ToFloats(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, floats.Length * sizeof(float));
        return floats;
    }

    public static Vector ToVector(byte[] bytes) => new(ToFloats(bytes));
}
