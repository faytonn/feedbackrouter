using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FeedbackRouter.Infra.Data;

/// <summary>Stores a float[] as its raw little-endian bytes in a varbinary column.</summary>
public static class EmbeddingSerializer
{
    public static readonly ValueConverter<float[]?, byte[]?> Converter = new(
        v => v == null ? null : ToBytes(v),
        v => v == null ? null : FromBytes(v));

    public static byte[] ToBytes(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    public static float[] FromBytes(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes).ToArray();
}
