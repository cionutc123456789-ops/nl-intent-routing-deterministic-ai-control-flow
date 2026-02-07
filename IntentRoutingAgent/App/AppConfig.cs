namespace IntentRoutingAgent.App;

public sealed class AppConfig
{
    public int MaxInputChars { get; set; } = 2000;
    public int ToolTimeoutMs { get; set; } = 1500;
    public int ComposeTimeoutMs { get; set; } = 4000;

    public OllamaConfig Ollama { get; set; } = new();
    public IntentRoutingConfig IntentRouting { get; set; } = new();
}

public sealed class OllamaConfig
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.2:3b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text:latest";
}

public sealed class IntentRoutingConfig
{
    public bool UseEmbeddingDisambiguation { get; set; } = true;

    // Minimum confidence needed to trust embedding routing when rules are ambiguous
    public double EmbeddingConfidenceThreshold { get; set; } = 0.45;

    // If top2 are too close, treat as ambiguous
    public double AmbiguityMargin { get; set; } = 0.05;
}
