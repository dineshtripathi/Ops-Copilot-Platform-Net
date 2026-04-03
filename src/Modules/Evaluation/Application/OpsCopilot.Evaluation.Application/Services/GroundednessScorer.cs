using Microsoft.Extensions.AI;

namespace OpsCopilot.Evaluation.Application.Services;

public sealed class GroundednessScorer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public GroundednessScorer(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<float> ScoreAsync(
        string reference,
        string candidate,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync(
            [reference, candidate], cancellationToken: cancellationToken);

        return CosineSimilarity(embeddings[0].Vector, embeddings[1].Vector);
    }

    internal static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;

        float dot = 0f, magA = 0f, magB = 0f;
        for (int i = 0; i < spanA.Length; i++)
        {
            dot  += spanA[i] * spanB[i];
            magA += spanA[i] * spanA[i];
            magB += spanB[i] * spanB[i];
        }

        // Zero vectors (NullEmbeddingGenerator) → neutral 0.5
        if (magA == 0f || magB == 0f) return 0.5f;

        return Math.Clamp(dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB)), 0f, 1f);
    }
}
