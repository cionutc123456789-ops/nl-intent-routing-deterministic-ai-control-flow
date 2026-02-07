using System.Text;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace IntentRoutingAgent.Llm;

public sealed class OllamaChatClient
{
    private readonly OllamaApiClient _client;
    private readonly string _model;

    public OllamaChatClient(Uri baseUri, string model)
    {
        _model = model;
        _client = new OllamaApiClient(baseUri)
        {
            SelectedModel = model
        };
    }

    public async Task<string> ChatAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        var chatMessages = messages.Select(m =>
            new Message(m.Role.Equals("system", StringComparison.OrdinalIgnoreCase) ? ChatRole.System : ChatRole.User, m.Content))
            .ToList();

        var request = new ChatRequest
        {
            Model = _model,
            Messages = chatMessages
        };

        var sb = new StringBuilder();
        await foreach (var chunk in _client.ChatAsync(request, ct))
        {
            if (chunk?.Message?.Content is not null)
                sb.Append(chunk.Message.Content);
        }

        return sb.ToString();
    }
}

public sealed record ChatMessage(string Role, string Content);
