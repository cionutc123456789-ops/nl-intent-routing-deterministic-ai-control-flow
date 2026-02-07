using Microsoft.Extensions.AI;
using OllamaSharp;

namespace IntentRoutingAgent.Llm;

public sealed class OllamaEmbeddingClient
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public OllamaEmbeddingClient(Uri baseUri, string model)
    {
        _generator = new OllamaApiClient(baseUri, model);
    }

    public async Task<float[]> EmbedAsync(string input, CancellationToken ct = default)
    {
        var embeddings = await _generator.GenerateAsync(new[] { input }, cancellationToken: ct);
        var first = embeddings.FirstOrDefault();
        return first is null ? Array.Empty<float>() : first.Vector.ToArray();
    }
}
