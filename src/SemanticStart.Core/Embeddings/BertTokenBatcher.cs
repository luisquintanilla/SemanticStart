using Microsoft.ML.Tokenizers;

namespace SemanticStart.Core.Embeddings;

internal sealed class BertTokenBatcher
{
    private const int MaxSequenceLength = 256;
    private readonly BertTokenizer _tokenizer;

    public BertTokenBatcher(string vocabPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabPath);

        _tokenizer = BertTokenizer.Create(
            vocabPath,
            new BertOptions
            {
                LowerCaseBeforeTokenization = true,
                ApplyBasicTokenization = true,
                SplitOnSpecialTokens = true,
                SeparatorToken = "[SEP]",
                PaddingToken = "[PAD]",
                ClassificationToken = "[CLS]",
                MaskingToken = "[MASK]",
                IndividuallyTokenizeCjk = true,
                RemoveNonSpacingMarks = true,
            });
    }

    public TokenizedBatch Tokenize(
        IReadOnlyList<string> texts,
        bool includeTokenTypeIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        cancellationToken.ThrowIfCancellationRequested();

        if (texts.Count == 0)
            return TokenizedBatch.Empty;

        var encoded = new long[texts.Count][];
        var sequenceLength = 0;
        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            encoded[i] = Encode(texts[i] ?? string.Empty);
            sequenceLength = Math.Max(sequenceLength, encoded[i].Length);
        }

        sequenceLength = Math.Max(sequenceLength, 1);
        var elementCount = checked(texts.Count * sequenceLength);
        var inputIds = new long[elementCount];
        var attentionMask = new long[elementCount];
        var tokenTypeIds = includeTokenTypeIds ? new long[elementCount] : null;

        for (var batch = 0; batch < encoded.Length; batch++)
        {
            var ids = encoded[batch];
            var offset = batch * sequenceLength;
            ids.AsSpan().CopyTo(inputIds.AsSpan(offset));
            attentionMask.AsSpan(offset, ids.Length).Fill(1);
        }

        return new TokenizedBatch(
            inputIds,
            attentionMask,
            tokenTypeIds,
            texts.Count,
            sequenceLength);
    }

    private long[] Encode(string text)
    {
        IReadOnlyList<int> ids = _tokenizer.EncodeToIds(
            text,
            MaxSequenceLength,
            addSpecialTokens: true,
            out _,
            out _,
            considerPreTokenization: true,
            considerNormalization: true);

        var result = new long[ids.Count];
        for (var i = 0; i < ids.Count; i++)
            result[i] = ids[i];

        return result;
    }
}

internal readonly record struct TokenizedBatch(
    long[] InputIds,
    long[] AttentionMask,
    long[]? TokenTypeIds,
    int BatchSize,
    int SequenceLength)
{
    public static TokenizedBatch Empty => new([], [], null, 0, 0);
}
