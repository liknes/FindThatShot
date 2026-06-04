using System.Buffers.Binary;

namespace VideoArchiveManager.Core.Services.Ai;

// Small, allocation-conscious helpers for working with embedding vectors and
// their on-disk (little-endian float32 BLOB) representation. All embeddings the
// AI subsystem stores are L2-normalized, so cosine similarity collapses to a
// plain dot product.
public static class VectorMath
{
    public static void L2NormalizeInPlace(float[] vector)
    {
        double sumSq = 0;
        for (var i = 0; i < vector.Length; i++) sumSq += (double)vector[i] * vector[i];
        var norm = Math.Sqrt(sumSq);
        if (norm <= 1e-12) return;
        var inv = (float)(1.0 / norm);
        for (var i = 0; i < vector.Length; i++) vector[i] *= inv;
    }

    // Dot product == cosine similarity for unit-length vectors.
    public static float Dot(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        float sum = 0;
        for (var i = 0; i < len; i++) sum += a[i] * b[i];
        return sum;
    }

    // Mean of several vectors, then re-normalized — the "pooled" clip vector.
    public static float[] MeanNormalized(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count == 0) return Array.Empty<float>();
        var dim = vectors[0].Length;
        var acc = new float[dim];
        foreach (var v in vectors)
        {
            var n = Math.Min(dim, v.Length);
            for (var i = 0; i < n; i++) acc[i] += v[i];
        }
        var inv = 1f / vectors.Count;
        for (var i = 0; i < dim; i++) acc[i] *= inv;
        L2NormalizeInPlace(acc);
        return acc;
    }

    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), vector[i]);
        }
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var count = bytes.Length / sizeof(float);
        var vector = new float[count];
        for (var i = 0; i < count; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        }
        return vector;
    }
}
