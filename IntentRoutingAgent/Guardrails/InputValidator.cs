namespace IntentRoutingAgent.Guardrails;

public sealed class InputValidator
{
    public ValidationResult ValidateUserInput(string input, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Fail("Please enter a question.");

        var trimmed = input.Trim();
        if (trimmed.Length > maxChars)
            return ValidationResult.Fail($"Input too long. Max allowed is {maxChars} characters.");

        var lower = trimmed.ToLowerInvariant();

        // Demo-grade prompt injection indicators (deterministic reject)
        if (lower.Contains("ignore previous instructions") ||
            lower.Contains("reveal system prompt") ||
            lower.Contains("developer message") ||
            lower.Contains("print the hidden"))
        {
            return ValidationResult.Fail("I can't process that request.");
        }

        return ValidationResult.Success();
    }
}

public readonly record struct ValidationResult(bool Ok, string? ErrorMessage)
{
    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Fail(string message) => new(false, message);
}
