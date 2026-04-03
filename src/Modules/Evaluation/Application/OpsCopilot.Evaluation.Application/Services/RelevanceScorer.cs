using Microsoft.Extensions.AI;

namespace OpsCopilot.Evaluation.Application.Services;

public sealed class RelevanceScorer
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public RelevanceScorer(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<float> ScoreAsync(
        string query,
        string candidate,
        CancellationToken cancellationToken = default)
    {
        var embeddings = await _embeddingGenerator.GenerateAsync(
            [query, candidate], cancellationToken: cancellationToken);

        return GroundednessScorer.CosineSimilarity(embeddings[0].Vector, embeddings[1].Vector);
    }
}
