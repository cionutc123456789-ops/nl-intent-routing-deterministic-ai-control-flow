using IntentRoutingAgent.App;
using IntentRoutingAgent.Llm;
using IntentRoutingAgent.Tools;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace IntentRoutingAgent.Intents;

public sealed class IntentRouter
{
    private readonly IntentClassifier _classifier;
    private readonly ToolRegistry _tools;
    private readonly GroundedAnswerComposer _answerComposer;
    private readonly AppConfig _cfg;
    private readonly ILogger<IntentRouter> _log;

    public IntentRouter(
        IntentClassifier classifier,
        ToolRegistry tools,
        GroundedAnswerComposer answerComposer,
        IOptions<AppConfig> cfg,
        ILogger<IntentRouter> log)
    {
        _classifier = classifier;
        _tools = tools;
        _answerComposer = answerComposer;
        _cfg = cfg.Value;
        _log = log;
    }

    public async Task<IntentRoutingResult> RouteAndExecuteAsync(string input, CancellationToken ct)
    {
        var ruleMatches = _classifier.DetectRuleMatches(input);
        if (ruleMatches.Count >= 2)
        {
            var options = string.Join(", ", ruleMatches.OrderBy(x => x).Select(FormatIntentOption));
            _log.LogInformation("Multiple intents detected: {Intents}", string.Join(", ", ruleMatches));

            return new IntentRoutingResult(
                Intent.Unknown,
                0.40,
                RoutingPath.RulesOnly,
                $"I can only handle one request at a time. Which do you want: {options}?");
        }

        var (intent, confidence, path) = await _classifier.ClassifyAsync(input, ct);

        return intent switch
        {
            Intent.WorldTime => await HandleWorldTimeAsync(input, confidence, path, ct),
            Intent.RunbookSearch => await HandleRunbookSearchAsync(input, confidence, path, ct),
            Intent.GeneralOpsAdvice => await HandleGeneralOpsAdviceAsync(input, confidence, path, ct),
            _ => new IntentRoutingResult(Intent.Unknown, confidence, path,
                "I don't know. Try asking for the time in a city, or describe an incident (Redis, DB pool, pods restarting).")
        };
    }

    private async Task<IntentRoutingResult> HandleWorldTimeAsync(
        string input, double confidence, RoutingPath path, CancellationToken ct)
    {
        // Deterministic extraction (keep it boring & safe)
        var city = ExtractCityForTime(input);
        if (string.IsNullOrWhiteSpace(city))
        {
            return new IntentRoutingResult(Intent.WorldTime, confidence, path,
                "Which city?");
        }

        var plan = ToolPlan.Tool("WorldTime.GetCityTime", new Dictionary<string, object> { ["city"] = city });
        var toolResult = await _tools.TryExecuteAsync(plan, ct);

        if (!toolResult.Ok)
            return new IntentRoutingResult(Intent.WorldTime, confidence, path, toolResult.SafeMessage);

        // WorldTime tool already returns the final text safely
        return new IntentRoutingResult(Intent.WorldTime, confidence, path, toolResult.Output!);
    }

    private async Task<IntentRoutingResult> HandleRunbookSearchAsync(
        string input, double confidence, RoutingPath path, CancellationToken ct)
    {
        var plan = ToolPlan.Tool("Runbooks.Search", new Dictionary<string, object> { ["query"] = input });

        using var toolTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_cfg.ToolTimeoutMs));
        using var toolLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, toolTimeout.Token);

        var toolResult = await _tools.TryExecuteAsync(plan, toolLinked.Token);
        if (!toolResult.Ok)
            return new IntentRoutingResult(Intent.RunbookSearch, confidence, path, toolResult.SafeMessage);

        // Compose with a dedicated budget, then deterministically fall back to tool output.
        string final;
        using (var composeTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_cfg.ComposeTimeoutMs)))
        using (var composeLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, composeTimeout.Token))
        {
            var composed = await _answerComposer.ComposeFromToolOutputAsync(
                userQuestion: input,
                toolName: plan.ToolName!,
                toolOutput: toolResult.Output!,
                composeLinked.Token);

            final = LooksLikeUnknown(composed) ? toolResult.Output! : composed;
        }

        return new IntentRoutingResult(Intent.RunbookSearch, confidence, path, final);
    }

    private static bool LooksLikeUnknown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var normalized = text.Trim();
        return normalized.Equals("I don't know.", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("I don't know", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IntentRoutingResult> HandleGeneralOpsAdviceAsync(
        string input, double confidence, RoutingPath path, CancellationToken ct)
    {
        // No tools needed: just use a constrained local LLM response (or replace with docs later)
        var final = await _answerComposer.ComposeGeneralAnswerAsync(input, ct);
        return new IntentRoutingResult(Intent.GeneralOpsAdvice, confidence, path, final);
    }

    private static string? ExtractCityForTime(string input)
    {
        // Very small heuristic extraction:
        // "time in London" -> London
        // "current time in New York?" -> New York
        var lower = input.ToLowerInvariant();
        var idx = lower.IndexOf("time in ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var city = input[(idx + "time in ".Length)..].Trim().TrimEnd('?', '.', '!');
            return city.Length > 64 ? city[..64] : city;
        }

        // fallback: "What time is it in Tokyo"
        var idx2 = lower.IndexOf("in ", StringComparison.OrdinalIgnoreCase);
        if (lower.Contains("time") && idx2 >= 0)
        {
            var city = input[(idx2 + 3)..].Trim().TrimEnd('?', '.', '!');
            return city.Length > 64 ? city[..64] : city;
        }

        return null;
    }

    private static string FormatIntentOption(Intent intent) => intent switch
    {
        Intent.WorldTime => "time in a city",
        Intent.RunbookSearch => "runbook/incident search",
        Intent.GeneralOpsAdvice => "general ops advice",
        _ => "something else"
    };
}
