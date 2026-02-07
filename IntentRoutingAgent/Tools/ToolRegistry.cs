using IntentRoutingAgent.App;
using IntentRoutingAgent.Llm;
using IntentRoutingAgent.Search;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace IntentRoutingAgent.Tools;

public sealed class ToolRegistry
{
    private readonly AppConfig _cfg;
    private readonly WorldTimeTool _time;
    private readonly RunbookSearchTool _runbooks;
    private readonly ILogger<ToolRegistry> _log;

    private static readonly HashSet<string> AllowedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorldTime.GetCityTime",
        "Runbooks.Search"
    };

    public ToolRegistry(
        IOptions<AppConfig> cfg,
        SemanticSearchEngine search,
        OllamaEmbeddingClient embeddings,
        ILogger<ToolRegistry> log)
    {
        _cfg = cfg.Value;
        _time = new WorldTimeTool();
        _runbooks = new RunbookSearchTool(search, embeddings);
        _log = log;
    }

    public async Task<ToolExecutionResult> TryExecuteAsync(ToolPlan plan, CancellationToken ct)
    {
        if (plan.Action != ToolPlanAction.Tool)
            return ToolExecutionResult.Fail("Tool execution requested incorrectly.");

        if (string.IsNullOrWhiteSpace(plan.ToolName))
            return ToolExecutionResult.Fail("Missing tool name.");

        if (!AllowedTools.Contains(plan.ToolName))
            return ToolExecutionResult.Fail("Tool not allowed.");

        _log.LogInformation("Tool execution requested: {ToolName}", plan.ToolName);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_cfg.ToolTimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            // Note: Tool name only, no args.
            return plan.ToolName switch
            {
                "WorldTime.GetCityTime" => ExecWorldTime(plan.Arguments),
                "Runbooks.Search" => await ExecRunbookSearchAsync(plan.Arguments, linked.Token),
                _ => ToolExecutionResult.Fail("Tool not allowed.")
            };
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("Tool timed out: {ToolName}", plan.ToolName);
            return ToolExecutionResult.Fail("Tool timed out. Try again with a simpler request.");
        }
        catch
        {
            _log.LogWarning("Tool failed safely: {ToolName}", plan.ToolName);
            return ToolExecutionResult.Fail("Tool failed safely. Try again.");
        }
    }

    private ToolExecutionResult ExecWorldTime(IReadOnlyDictionary<string, object>? args)
    {
        if (args is null || !args.TryGetValue("city", out var cityObj))
            return ToolExecutionResult.Fail("Missing required argument: city");

        var city = cityObj?.ToString();
        if (string.IsNullOrWhiteSpace(city) || city.Length > 64)
            return ToolExecutionResult.Fail("Invalid city.");

        return ToolExecutionResult.Success(_time.GetCityTime(city));
    }

    private async Task<ToolExecutionResult> ExecRunbookSearchAsync(
        IReadOnlyDictionary<string, object>? args,
        CancellationToken ct)
    {
        if (args is null || !args.TryGetValue("query", out var qObj))
            return ToolExecutionResult.Fail("Missing required argument: query");

        var q = qObj?.ToString();
        if (string.IsNullOrWhiteSpace(q) || q.Length > 500)
            return ToolExecutionResult.Fail("Invalid query.");

        var output = await _runbooks.SearchAsync(q, ct);
        return ToolExecutionResult.Success(output);
    }
}

public enum ToolPlanAction { Tool, Answer, Refuse }

public sealed record ToolPlan(
    ToolPlanAction Action,
    string? ToolName,
    IReadOnlyDictionary<string, object>? Arguments)
{
    public static ToolPlan Tool(string toolName, IReadOnlyDictionary<string, object> args)
        => new(ToolPlanAction.Tool, toolName, args);
}
