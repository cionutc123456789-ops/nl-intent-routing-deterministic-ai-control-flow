using IntentRoutingAgent.App;
using IntentRoutingAgent.Guardrails;
using IntentRoutingAgent.Intents;
using IntentRoutingAgent.Llm;
using IntentRoutingAgent.Search;
using IntentRoutingAgent.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        cfg.AddEnvironmentVariables(prefix: "IRA_");
    })
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<AppConfig>(ctx.Configuration.GetSection("App"));

        services.AddSingleton<OllamaChatClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            return new OllamaChatClient(new Uri(cfg.Ollama.BaseUrl), cfg.Ollama.ChatModel);
        });

        services.AddSingleton<OllamaEmbeddingClient>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<AppConfig>>().Value;
            return new OllamaEmbeddingClient(new Uri(cfg.Ollama.BaseUrl), cfg.Ollama.EmbeddingModel);
        });

        // Seeded runbooks (replace with your own store later)
        services.AddSingleton<IReadOnlyList<KnowledgeDocument>>(_ => SeedRunbooks.Create());

        services.AddSingleton<SemanticSearchEngine>(sp =>
        {
            var docs = sp.GetRequiredService<IReadOnlyList<KnowledgeDocument>>();
            var embed = sp.GetRequiredService<OllamaEmbeddingClient>();
            var logger = sp.GetRequiredService<ILogger<SemanticSearchEngine>>();
            return SemanticSearchEngine.Build(docs, embed, logger);
        });

        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<GroundedAnswerComposer>();

        services.AddSingleton<IntentClassifier>();
        services.AddSingleton<IntentRouter>();

        services.AddSingleton<InputValidator>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntentRoutingAgent");
var config = host.Services.GetRequiredService<IOptions<AppConfig>>().Value;
var validator = host.Services.GetRequiredService<InputValidator>();
var router = host.Services.GetRequiredService<IntentRouter>();

logger.LogInformation("=== Intent Routing + Deterministic Control Flow (Local-First) ===");
logger.LogInformation("Ollama: {BaseUrl} | chat={ChatModel} | embed={EmbedModel}",
    config.Ollama.BaseUrl, config.Ollama.ChatModel, config.Ollama.EmbeddingModel);

while (true)
{
    Console.Write("\nYou: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var validation = validator.ValidateUserInput(input, config.MaxInputChars);
    if (!validation.Ok)
    {
        logger.LogInformation("Validation failed: {Reason}", validation.ErrorMessage);
        Console.WriteLine($"Assistant: {validation.ErrorMessage}");
        continue;
    }

    var deterministic = Policy.TryHandleDeterministically(input);
    if (deterministic is not null)
    {
        logger.LogInformation("Deterministic policy handled request.");
        Console.WriteLine($"Assistant: {deterministic}");
        continue;
    }

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await router.RouteAndExecuteAsync(input, cts.Token);

        // Deterministic behavior: the router returns the intent & safe answer
        Console.WriteLine($"Assistant: {result.ResponseText}");
        logger.LogInformation("Intent={Intent} Confidence={Confidence:F2} Path={Path}",
            result.Intent, result.Confidence, result.Path);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Assistant: Timed out. Try a simpler request.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error");
        Console.WriteLine("Assistant: Something failed safely. Try again.");
    }
}

await host.StopAsync();
