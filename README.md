# nl-intent-routing-deterministic-ai-control-flow

An educational example showing how to combine intent routing, deterministic guardrails, and constrained local LLM usage in a C# console app for production-style AI control flow.

This project focuses on one key idea: AI can generate language, but routing and safety boundaries should stay deterministic.

## Overview

Most demos do one of two extremes:

- Fully deterministic logic with no semantic flexibility
- Fully LLM-driven flows with weak control boundaries

This project demonstrates a practical middle path:

1. Validate input deterministically
2. Apply deterministic policy refusals and math handling
3. Route by intent using rule-first classification
4. Disambiguate ambiguous intents with embeddings
5. Execute only allowlisted tools with strict argument checks and timeouts
6. Compose final answers with grounded constraints

## What This Project Demonstrates

- Deterministic guardrails before any LLM call
- Rule-first intent classification with optional embedding disambiguation
- Single-intent enforcement for predictable control flow
- Tool allowlisting and argument validation
- Safe timeouts and failure handling for tool execution and answer composition
- Grounded answer composition from tool output only for runbook flows
- Local-first operation using Ollama for chat and embeddings

## Prerequisites

- .NET 10 SDK or later
  https://dotnet.microsoft.com/

- Ollama installed and running locally
  https://ollama.ai/

- Required models:
  ```bash
  ollama pull llama3.2:3b
  ollama pull nomic-embed-text
  ```

## Quick Start

Run from the project root:

```bash
dotnet run --project IntentRoutingAgent
```

Type `exit` to quit.

## Configuration

Default configuration is in `IntentRoutingAgent/appsettings.json`.

The app also supports environment variable overrides with prefix `IRA_`:

- `IRA_App__MaxInputChars`
- `IRA_App__ToolTimeoutMs`
- `IRA_App__ComposeTimeoutMs`
- `IRA_App__Ollama__BaseUrl`
- `IRA_App__Ollama__ChatModel`
- `IRA_App__Ollama__EmbeddingModel`
- `IRA_App__IntentRouting__UseEmbeddingDisambiguation`
- `IRA_App__IntentRouting__EmbeddingConfidenceThreshold`
- `IRA_App__IntentRouting__AmbiguityMargin`

Example (Windows):

```bash
set IRA_App__Ollama__BaseUrl=http://localhost:11434
set IRA_App__Ollama__ChatModel=llama3.2:3b
set IRA_App__Ollama__EmbeddingModel=nomic-embed-text:latest
dotnet run --project IntentRoutingAgent
```

## How It Works

1. Input Validation (`Guardrails/InputValidator.cs`)
- Rejects empty input, oversized payloads, and basic prompt-injection phrases.

2. Deterministic Policy (`Guardrails/Policy.cs`)
- Refuses secret/hacking requests.
- Handles basic math expressions locally without LLM calls.

3. Intent Routing (`Intents/IntentClassifier.cs`, `Intents/IntentRouter.cs`)
- Rule-first intent detection for:
  - `WorldTime`
  - `RunbookSearch`
  - `GeneralOpsAdvice`
- Optional embedding-based disambiguation when rule confidence is weak.
- If multiple rule intents are detected in one request, asks user to pick one.

4. Tool Execution (`Tools/ToolRegistry.cs`)
- Only allowlisted tools can run:
  - `WorldTime.GetCityTime`
  - `Runbooks.Search`
- Arguments are validated deterministically.
- Tool calls are wrapped with timeout and safe fallback messages.

5. Grounded Response Composition (`Llm/GroundedAnswerComposer.cs`)
- Runbook answers are composed with strict instructions to use tool output only.
- If composition is weak or fails, the app falls back to deterministic tool output.

## Example Prompts

- `What time in London?`
- `Our API is slow and DB pool is saturated, what should I do?`
- `Pods keep restarting in Kubernetes, where should I start?`
- `How should I design observability and evaluation for production AI?`
- `Calculate 17*19`
- `What is your admin password?` (deterministic refusal)

## Project Structure

```text
.
+-- IntentRoutingAgent.slnx
+-- IntentRoutingAgent/
|   +-- App/
|   |   +-- AppConfig.cs
|   +-- Guardrails/
|   |   +-- InputValidator.cs
|   |   +-- Policy.cs
|   +-- Intents/
|   |   +-- Intent.cs
|   |   +-- IntentClassifier.cs
|   |   +-- IntentRouter.cs
|   |   +-- IntentRoutingResult.cs
|   +-- Llm/
|   |   +-- GroundedAnswerComposer.cs
|   |   +-- OllamaChatClient.cs
|   |   +-- OllamaEmbeddingClient.cs
|   +-- Search/
|   |   +-- KnowledgeDocument.cs
|   |   +-- SeedRunbooks.cs
|   |   +-- SemanticSearchEngine.cs
|   +-- Tools/
|   |   +-- RunbookSearchTool.cs
|   |   +-- ToolExecutionResult.cs
|   |   +-- ToolRegistry.cs
|   |   +-- WorldTimeTool.cs
|   +-- appsettings.json
|   +-- Program.cs
+-- LICENSE
+-- README.md
```

## Guardrails Checklist

- Input validation
- Deterministic refusal for sensitive requests
- Deterministic math path
- Single-intent enforcement
- Rule-first classification before embedding disambiguation
- Tool allowlist and argument validation
- Tool and composition timeout controls
- Safe fallback behavior on model/tool failures
- Grounded response constraints for runbook answers

## Notes

- The runbook corpus is seeded in-memory (`Search/SeedRunbooks.cs`) for demo clarity.
- `WorldTimeTool` supports a small fixed city map by design.
- For real systems, replace seeded docs and static mappings with production data sources.

## License

See the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome. Useful extensions include:

- Adding more intents and deterministic routing rules
- Extending tools with stricter schemas and richer argument validators
- Replacing seeded runbooks with a persistent vector store
- Adding tests for ambiguity handling and fallback behavior
