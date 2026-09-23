using Microsoft.Extensions.AI;

namespace SemanticStart.Core.Embeddings;

/// <summary>
/// Microsoft.Extensions.AI embedding provider backed by a local ONNX sentence-transformer model.
/// The tokenizer, ONNX scorer, and pooling stages stay replaceable inside this facade while the
/// rest of the application depends only on IEmbeddingGenerator.
/// </summary>
public sealed class OnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const string DefaultModelId = "all-MiniLM-L6-v2";
    public const int DefaultDimensions = 384;

    private readonly BertTokenBatcher _tokenizer;
    private readonly OnnxTextScorer _scorer;
    private readonly EmbeddingGeneratorMetadata _metadata;
    private readonly string _modelId;
    private readonly int _dimensions;
    private bool _disposed;

    public OnnxEmbeddingGenerator(
        string modelPath,
        string vocabPath,
        string modelId = DefaultModelId,
        int dimensions = DefaultDimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        if (!File.Exists(vocabPath))
            throw new FileNotFoundException("Embedding model vocabulary was not found.", vocabPath);

        _modelId = modelId;
        _dimensions = dimensions;
        _tokenizer = new BertTokenBatcher(vocabPath);
        _scorer = new OnnxTextScorer(modelPath);

        _metadata = new EmbeddingGeneratorMetadata(
            providerName: "ONNX Runtime",
            defaultModelId: _modelId,
            defaultModelDimensions: _dimensions);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(values);
        ValidateOptions(options);

        var texts = values as IReadOnlyList<string> ?? values.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (texts.Count == 0)
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>());

        return Task.Run(() => GenerateCore(texts, cancellationToken), cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata : null;
    }

    private GeneratedEmbeddings<Embedding<float>> GenerateCore(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var tokenized = _tokenizer.Tokenize(texts, _scorer.HasTokenTypeIds, cancellationToken);
        var hiddenStates = _scorer.Score(tokenized, cancellationToken);
        var vectors = EmbeddingPooling.MeanPoolAndNormalize(hiddenStates, tokenized);

        var generated = new GeneratedEmbeddings<Embedding<float>>(vectors.Count);
        foreach (var vector in vectors)
        {
            if (vector.Length != _dimensions)
            {
                throw new InvalidOperationException(
                    $"ONNX embedding model returned {vector.Length} dimensions; expected {_dimensions}.");
            }

            generated.Add(new Embedding<float>(vector.AsMemory())
            {
                ModelId = _modelId,
            });
        }

        return generated;
    }

    private void ValidateOptions(EmbeddingGenerationOptions? options)
    {
        if (options?.ModelId is { } modelId
            && !string.Equals(modelId, _modelId, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"This embedding generator serves model '{_modelId}', not '{modelId}'.");
        }

        if (options?.Dimensions is { } dimensions && dimensions != _dimensions)
        {
            throw new NotSupportedException(
                $"This embedding generator serves {_dimensions} dimensions, not {dimensions}.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _scorer.Dispose();
        _disposed = true;
    }
}
