using System.Text;
using IntentRoutingAgent.Llm;
using IntentRoutingAgent.Search;

namespace IntentRoutingAgent.Tools;

public sealed class RunbookSearchTool
{
    private readonly SemanticSearchEngine _engine;
    private readonly OllamaEmbeddingClient _embeddings;

    public RunbookSearchTool(SemanticSearchEngine engine, OllamaEmbeddingClient embeddings)
    {
        _engine = engine;
        _embeddings = embeddings;
    }

    public async Task<string> SearchAsync(string query, CancellationToken ct)
    {
        var results = await _engine.SearchAsync(_embeddings, query, topK: 3, ct);

        if (results.Count == 0)
            return "No relevant runbook documents found.";

        var sb = new StringBuilder();
        sb.AppendLine("Relevant documents:");
        foreach (var r in results)
        {
            sb.AppendLine($"- {r.Doc.Id}: {r.Doc.Title} (score: {r.Score:F3})");
            sb.AppendLine($"  Excerpt: {Truncate(r.Doc.Body, 180)}");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
