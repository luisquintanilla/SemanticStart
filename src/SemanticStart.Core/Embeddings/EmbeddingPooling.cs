using System.Numerics.Tensors;

namespace SemanticStart.Core.Embeddings;

internal static class EmbeddingPooling
{
    public static IReadOnlyList<float[]> MeanPoolAndNormalize(
        HiddenStateBatch hiddenStates,
        TokenizedBatch tokens)
    {
        if (hiddenStates.BatchSize != tokens.BatchSize
            || hiddenStates.SequenceLength != tokens.SequenceLength)
        {
            throw new ArgumentException("Hidden states and token batch shapes must match.", nameof(tokens));
        }

        var vectors = new float[hiddenStates.BatchSize][];
        for (var batch = 0; batch < hiddenStates.BatchSize; batch++)
        {
            var vector = new float[hiddenStates.Dimensions];
            var tokenCount = 0;

            for (var token = 0; token < hiddenStates.SequenceLength; token++)
            {
                if (tokens.AttentionMask[batch * hiddenStates.SequenceLength + token] == 0)
                    continue;

                tokenCount++;
                var hiddenOffset = (batch * hiddenStates.SequenceLength + token) * hiddenStates.Dimensions;
                for (var dimension = 0; dimension < hiddenStates.Dimensions; dimension++)
                    vector[dimension] += hiddenStates.Values[hiddenOffset + dimension];
            }

            var scale = 1f / Math.Max(tokenCount, 1);
            TensorPrimitives.Multiply(vector, scale, vector);

            var norm = TensorPrimitives.Norm(vector);
            if (norm > 0)
                TensorPrimitives.Multiply(vector, 1f / norm, vector);

            vectors[batch] = vector;
        }

        return vectors;
    }
}
