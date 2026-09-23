using System.Runtime.CompilerServices;
using System.Numerics.Tensors;

namespace SemanticStart.Core.Query;

/// <summary>
/// Vector math for the retrieval hot path.
///
/// Because every stored vector and every query vector is L2-normalized by contract, the dot
/// product *is* the cosine similarity, so no per-row magnitude work is required.
/// </summary>
public static class VectorMath
{
    /// <summary>Dot product of two equal-length spans, using the runtime's SIMD implementation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length.", nameof(b));

        return TensorPrimitives.Dot(a, b);
    }

    /// <summary>Scales a vector to unit length in place. No-op for a zero vector.</summary>
    public static void NormalizeInPlace(Span<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (norm <= 0f)
            return;

        TensorPrimitives.Multiply(vector, 1f / norm, vector);
    }
}
