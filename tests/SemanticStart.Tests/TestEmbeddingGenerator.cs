using Microsoft.Extensions.AI;

namespace SemanticStart.Tests;

internal class TestEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Func<string, float[]> _createVector;
    private readonly EmbeddingGeneratorMetadata _metadata;

    protected TestEmbeddingGenerator(
        Func<string, float[]> createVector,
        string modelId = "test-model",
        int dimensions = 4)
    {
        _createVector = createVector ?? throw new ArgumentNullException(nameof(createVector));
        _metadata = new EmbeddingGeneratorMetadata(
            providerName: "Tests",
            defaultModelId: modelId,
            defaultModelDimensions: dimensions);
    }

    public int Batches { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        cancellationToken.ThrowIfCancellationRequested();

        var generated = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = _createVector(value);
            generated.Add(new Embedding<float>(vector.AsMemory())
            {
                ModelId = _metadata.DefaultModelId,
            });
        }

        Batches++;
        return Task.FromResult(generated);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata : null;
    }

    public void Dispose()
    {
    }
}
