# FieldCure.Ai.Providers

AI provider clients for Claude, OpenAI, Gemini, Ollama, and Groq with shared models and streaming support.

## Providers

| Provider | Transport | Streaming |
|----------|-----------|-----------|
| Claude (Anthropic) | HTTP | SSE |
| OpenAI / Groq | HTTP | SSE |
| Gemini (Google) | HTTP | SSE |
| Ollama | HTTP | NDJSON |
| Mock | In-memory | Sync |

## Quick Start

```csharp
using FieldCure.Ai.Providers.Models;
using FieldCure.Ai.Providers;

var preset = new ProviderPreset
{
    ProviderType = "Claude",
    ApiKey = "sk-...",
    ModelId = "claude-sonnet-4-20250514",
};

var provider = ProviderFactory.Create(preset);

var request = new AiRequest
{
    Messages = [new ChatMessage(ChatRole.User, "Hello!")],
};

var response = await provider.CompleteAsync(request);
Console.WriteLine(response.Content);
```

## Models

- `ChatMessage`, `ChatRole` — conversation messages
- `AiRequest`, `AiResponse` — LLM request/response
- `ProviderPreset` — provider configuration
- `ToolCall`, `IAssistTool` — function calling
- `StreamEvent` — streaming response events
- `McpToolAdapter` — MCP tool wrapper

## License

MIT
