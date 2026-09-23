using Microsoft.Extensions.AI;
using SemanticStart.Core.Embeddings;

namespace SemanticStart.Tests;

public sealed class EmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateVectorsAsync_CopiesNativeEmbeddings()
    {
        using var generator = new FakeEmbeddingGenerator(text => [text.Length, 1f, 0f, 0f]);

        var vectors = await generator.GenerateVectorsAsync(["a", "long"], 4);

        Assert.Equal(2, vectors.Count);
        Assert.Equal([1f, 1f, 0f, 0f], vectors[0]);
        Assert.Equal([4f, 1f, 0f, 0f], vectors[1]);
    }

    [Fact]
    public async Task GenerateVectorsAsync_RejectsUnexpectedDimensions()
    {
        using var generator = new FakeEmbeddingGenerator(_ => [1f, 2f]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.GenerateVectorsAsync(["text"], 4));

        Assert.Contains("expected 4", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRequiredMetadata_RejectsProvidersWithoutMetadata()
    {
        using var generator = new MetadataLessEmbeddingGenerator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => generator.GetRequiredMetadata());

        Assert.Contains(nameof(EmbeddingGeneratorMetadata), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MeanPoolAndNormalize_UsesOnlyAttentionTokens()
    {
        var hiddenStates = new HiddenStateBatch(
            Values:
            [
                1f, 0f,
                100f, 100f,
                3f, 4f,
            ],
            BatchSize: 1,
            SequenceLength: 3,
            Dimensions: 2);
        var tokens = new TokenizedBatch(
            InputIds: [1, 2, 3],
            AttentionMask: [1, 0, 1],
            TokenTypeIds: null,
            BatchSize: 1,
            SequenceLength: 3);

        var vectors = EmbeddingPooling.MeanPoolAndNormalize(hiddenStates, tokens);

        var vector = Assert.Single(vectors);
        Assert.InRange(vector[0], 0.7071f, 0.7072f);
        Assert.InRange(vector[1], 0.7071f, 0.7072f);
    }

    private sealed class FakeEmbeddingGenerator(Func<string, float[]> createVector) : TestEmbeddingGenerator(createVector);

    private sealed class MetadataLessEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var generated = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var value in values)
                generated.Add(new Embedding<float>(new[] { value.Length, 1f }.AsMemory()));

            return Task.FromResult(generated);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
