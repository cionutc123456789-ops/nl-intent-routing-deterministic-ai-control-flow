namespace IntentRoutingAgent.Tools;

public sealed record ToolExecutionResult(bool Ok, string? Output, string SafeMessage)
{
    public static ToolExecutionResult Success(string output) => new(true, output, "");
    public static ToolExecutionResult Fail(string safeMessage) => new(false, null, safeMessage);
}
