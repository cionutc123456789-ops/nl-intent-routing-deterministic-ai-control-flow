using IntentRoutingAgent.App;
using IntentRoutingAgent.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntentRoutingAgent.Intents;

public sealed class IntentClassifier
{
    private readonly OllamaEmbeddingClient _embeddings;
    private readonly AppConfig _cfg;
    private readonly ILogger<IntentClassifier> _log;

    // Prototypes for embedding disambiguation (keep short and stable)
    private static readonly (Intent Intent, string Prototype)[] Prototypes =
    [
        (Intent.WorldTime, "user asks for current time in a specific city"),
        (Intent.RunbookSearch, "user describes an incident and wants a runbook or troubleshooting steps"),
        (Intent.GeneralOpsAdvice, "user asks general production operations advice for AI systems")
    ];

    private readonly Dictionary<Intent, float[]> _prototypeVectors = new();

    public IntentClassifier(
        OllamaEmbeddingClient embeddings,
        IOptions<AppConfig> cfg,
        ILogger<IntentClassifier> log)
    {
        _embeddings = embeddings;
        _cfg = cfg.Value;
        _log = log;
    }

    public async Task<(Intent Intent, double Confidence, RoutingPath Path)> ClassifyAsync(
        string input,
        CancellationToken ct)
    {
        // 1) Deterministic rules first
        var rules = ClassifyWithRules(input);
        if (rules.Intent != Intent.Unknown && rules.Confidence >= 0.80)
        {
            _log.LogInformation("Rules-only routing selected: {Intent} ({Confidence:F2})", rules.Intent, rules.Confidence);
            return (rules.Intent, rules.Confidence, RoutingPath.RulesOnly);
        }

        // If embeddings disabled, return the rules result (even if uncertain)
        if (!_cfg.IntentRouting.UseEmbeddingDisambiguation)
        {
            _log.LogInformation("Embeddings disabled; using rules: {Intent} ({Confidence:F2})", rules.Intent, rules.Confidence);
            return (rules.Intent, rules.Confidence, RoutingPath.RulesOnly);
        }

        // 2) Embedding disambiguation for ambiguous cases
        try
        {
            await EnsurePrototypeVectorsAsync(ct);

            var q = await _embeddings.EmbedAsync(input, ct);

            // Score similarity to each prototype
            var scored = _prototypeVectors
                .Select(kv => (Intent: kv.Key, Score: CosineSimilarity(kv.Value, q)))
                .OrderByDescending(x => x.Score)
                .ToArray();

            if (scored.Length == 0)
                return (rules.Intent, rules.Confidence, RoutingPath.RulesOnly);

            var top = scored[0];
            var second = scored.Length > 1 ? scored[1] : (Intent: Intent.Unknown, Score: -1.0);

            // Confidence model: cosine [-1..1] -> [0..1] and margin requirement
            var topConf = (top.Score + 1.0) / 2.0;
            var secondConf = (second.Score + 1.0) / 2.0;

            var margin = topConf - secondConf;

            if (topConf < _cfg.IntentRouting.EmbeddingConfidenceThreshold)
            {
                _log.LogInformation("Embedding confidence too low: {Top:F2} < {Threshold:F2}", topConf, _cfg.IntentRouting.EmbeddingConfidenceThreshold);
                // Too weak: fall back to rule classification
                return (rules.Intent, rules.Confidence, RoutingPath.RulesPlusEmbeddings);
            }

            if (margin < _cfg.IntentRouting.AmbiguityMargin)
            {
                _log.LogInformation("Embedding ambiguity too high: margin {Margin:F2} < {Threshold:F2}", margin, _cfg.IntentRouting.AmbiguityMargin);
                // Too ambiguous: fall back to safest non-action intent
                return (Intent.GeneralOpsAdvice, 0.50, RoutingPath.RulesPlusEmbeddings);
            }

            // Merge rules + embeddings: prefer strong rules if they disagree
            if (rules.Intent != Intent.Unknown && rules.Intent != top.Intent && rules.Confidence >= 0.65)
            {
                _log.LogInformation("Rules override embeddings: rules={RulesIntent} ({RulesConfidence:F2}) embed={EmbedIntent} ({EmbedConfidence:F2})",
                    rules.Intent, rules.Confidence, top.Intent, topConf);
                return (rules.Intent, rules.Confidence, RoutingPath.RulesPlusEmbeddings);
            }

            _log.LogInformation("Embedding routing selected: {Intent} ({Confidence:F2})", top.Intent, topConf);
            return (top.Intent, topConf, RoutingPath.RulesPlusEmbeddings);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Embedding-based routing failed; falling back to rules.");
            return (rules.Intent, rules.Confidence, RoutingPath.RulesOnly);
        }
    }

    private (Intent Intent, double Confidence) ClassifyWithRules(string input)
    {
        var s = input.Trim();
        var lower = s.ToLowerInvariant();

        // Time intent
        if (lower.Contains("time in ") || lower.Contains("current time") || lower.StartsWith("what time", StringComparison.OrdinalIgnoreCase))
            return (Intent.WorldTime, 0.90);

        // Runbook / incident intent
        if (lower.Contains("redis") || lower.Contains("outage") || lower.Contains("incident") ||
            lower.Contains("kubernetes") || lower.Contains("pod restart") ||
            lower.Contains("connection pool") || lower.Contains("high cpu"))
            return (Intent.RunbookSearch, 0.85);

        // General ops / AI reliability intent
        if (lower.Contains("observability") || lower.Contains("evaluation") || lower.Contains("guardrail") ||
            lower.Contains("hallucination") || lower.Contains("feedback loop") || lower.Contains("production ai"))
            return (Intent.GeneralOpsAdvice, 0.70);

        return (Intent.Unknown, 0.30);
    }

    public HashSet<Intent> DetectRuleMatches(string input)
    {
        var s = input.Trim();
        var lower = s.ToLowerInvariant();
        var matches = new HashSet<Intent>();

        if (lower.Contains("time in ") || lower.Contains("current time") || lower.StartsWith("what time", StringComparison.OrdinalIgnoreCase))
            matches.Add(Intent.WorldTime);

        if (lower.Contains("redis") || lower.Contains("outage") || lower.Contains("incident") ||
            lower.Contains("kubernetes") || lower.Contains("pod restart") ||
            lower.Contains("connection pool") || lower.Contains("high cpu"))
            matches.Add(Intent.RunbookSearch);

        if (lower.Contains("observability") || lower.Contains("evaluation") || lower.Contains("guardrail") ||
            lower.Contains("hallucination") || lower.Contains("feedback loop") || lower.Contains("production ai"))
            matches.Add(Intent.GeneralOpsAdvice);

        return matches;
    }

    private async Task EnsurePrototypeVectorsAsync(CancellationToken ct)
    {
        if (_prototypeVectors.Count == Prototypes.Length)
            return;

        foreach (var (intent, text) in Prototypes)
        {
            if (_prototypeVectors.ContainsKey(intent))
                continue;

            var vec = await _embeddings.EmbedAsync(text, ct);
            _prototypeVectors[intent] = vec;
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
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

        return dot / denom;
    }
}
