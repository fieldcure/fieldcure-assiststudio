# FieldCure.Ai.Providers

AI provider clients for Claude, OpenAI, Gemini, Ollama, and Groq with shared models and streaming support.

## Providers

| Provider | Transport | Streaming |
|----------|-----------|-----------|
| Claude (Anthropic) | HTTP | SSE |
| OpenAI / Groq | HTTP | SSE |
| Gemini (Google) | HTTP | SSE |
| Ollama | HTTP | NDJSON |
| Custom (OpenAI-compatible) | HTTP | SSE |
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

## Features

- **Custom Providers** — Register any OpenAI-compatible endpoint via `ProviderFactory.RegisterCustomProvider` with `CustomProviderConfig` (BaseUrl, DisplayName).
- **Image Compression** — `ImageCompressor` automatically compresses and resizes large images (JPEG, via SkiaSharp) before sending to providers, reducing token usage and API costs.
- **Multimedia Tool Results** — `IMultiContentTool` interface for tools returning structured multimedia content (images, audio, video) alongside text via `ToolExecutionResult`.
- **Media Persistence** — `ChatMessage.MediaItems` stores media attachments with conversation messages for save/load in `.astd` files.
- **Thinking / Reasoning** — Structured `reasoning_details` parsing, `<think>` tag streaming extraction via `ThinkTagParser`, and Ollama native `thinking` field support.

## Models

**Core models**
- `ChatMessage`, `ChatRole` — conversation messages (with `MediaItems` for media persistence)
- `AiRequest`, `AiResponse` — LLM request/response
- `ProviderPreset` — provider configuration
- `CustomProviderConfig` — custom OpenAI-compatible provider registration (Id, DisplayName, BaseUrl)

**Tool calling**
- `ToolCall`, `IAssistTool` — function calling interface with optional confirmation
- `IMultiContentTool` — tools returning multimedia content alongside text
- `ToolExecutionResult` — combined text + media content return type

**Media**
- `MediaContent` — typed media payload (MimeType, Base64Data, FileName)
- `ImageCompressor` — JPEG compression and resize helper (via SkiaSharp)

**Streaming**
- `StreamEvent` — discriminated union: `TextDelta`, `ThinkingDelta`, `ToolCallStart`, `ToolCallDelta`, `Usage`, `StreamCompleted`

**Adapters**
- `McpToolAdapter` — bridges MCP tools to `IAssistTool` with multimedia content block support

## License

MIT
