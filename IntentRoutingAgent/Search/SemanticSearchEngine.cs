using IntentRoutingAgent.Llm;
using Microsoft.Extensions.Logging;

namespace IntentRoutingAgent.Search;

public sealed class SemanticSearchEngine
{
    private readonly List<(KnowledgeDocument Doc, float[] Vec)> _index;
    private readonly ILogger<SemanticSearchEngine> _log;

    private SemanticSearchEngine(List<(KnowledgeDocument, float[])> index, ILogger<SemanticSearchEngine> log)
    {
        _index = index;
        _log = log;
    }

    // Build at startup (precompute embeddings deterministically once)
    public static SemanticSearchEngine Build(
        IReadOnlyList<KnowledgeDocument> docs,
        OllamaEmbeddingClient embeddings,
        ILogger<SemanticSearchEngine> log)
    {
        var index = new List<(KnowledgeDocument, float[])>(docs.Count);

        log.LogInformation("Indexing {Count} runbooks...", docs.Count);

        foreach (var d in docs)
        {
            // Startup-time sync is acceptable for a small demo.
            // For larger corpora, switch to async build with progress + persistence.
            var vec = embeddings.EmbedAsync(d.Body, CancellationToken.None).GetAwaiter().GetResult();
            index.Add((d, vec));
        }

        log.LogInformation("Runbook index ready.");
        return new SemanticSearchEngine(index, log);
    }

    public async Task<List<(KnowledgeDocument Doc, float Score)>> SearchAsync(
        OllamaEmbeddingClient embeddings,
        string query,
        int topK,
        CancellationToken ct)
    {
        _log.LogInformation("Runbook search: topK={TopK}", topK);
        var q = await embeddings.EmbedAsync(query, ct);

        var results = _index
            .Select(e => (e.Doc, Score: CosineSimilarity(e.Vec, q)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        _log.LogInformation("Runbook search results: {Count}", results.Count);
        return results;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        if (denom == 0) return 0;

        return (float)(dot / denom);
    }
}
