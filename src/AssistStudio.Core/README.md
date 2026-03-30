# FieldCure.AssistStudio.Core

**Platform-agnostic AI provider client library for .NET** — Claude, OpenAI, Gemini, Ollama, Groq, and any OpenAI-compatible endpoint.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **Multi-Provider** — Claude, OpenAI, Gemini, Ollama, and Groq built-in. Implement `IAiProvider` to add your own.
- **Streaming** — Real-time structured event streaming via `IAsyncEnumerable<StreamEvent>`. The `StreamEvent` discriminated union covers text, thinking, tool calls, usage, and completion.
- **Extended Thinking** — Per-provider thinking/reasoning support. `ThinkingSupport` detection, `ThinkingOverride` (Auto / ForceOn / ForceOff), and `ThinkingBudget` on `AiRequest`.
- **Vision & Documents** — Attach images (PNG, JPG, WebP, GIF), PDFs, and DOCX files. `PdfCapability` (Auto / TextExtraction / NativePdf / PageAsImage) per provider.
- **Tool / Function Calling** — Define tools with `IAssistTool`. `ToolCallExecutor` orchestrates execution with an optional confirmation callback. `ToolResolver` merges built-in and MCP tools with name-conflict resolution.
- **MCP Tool Adapter** — `McpToolAdapter` bridges external MCP tools to the `IAssistTool` pipeline with zero MCP SDK dependency in Core.
- **Workspace Context** — `IWorkspaceContext` for dynamic system prompt injection based on host app state.
- **RAG Support** — `IContextProvider` retrieves relevant `ContextChunk`s for a query. Chunks are injected into the system prompt alongside workspace context. Built-in Knowledge Archive server provides document indexing and search via MCP.
- **Token Tracking** — `TokenUsage` (input/output counts) exposed after every request.
- **Structured Logging** — `DiagnosticLogger` with pluggable `OnException`, `OnWarning`, `OnInfo` callbacks.
- **Hardware Probing** — `HardwareProbe` for GPU/CPU detection to evaluate Ollama model compatibility.
- **Platform-Agnostic** — Targets `net8.0` with no Windows dependency. Use from console apps, servers, or any .NET project.

## Install

```bash
dotnet add package FieldCure.AssistStudio.Core
```

## Quick Start

```csharp
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;

// Create a provider
var provider = new ClaudeProvider(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-20250514");

// Simple completion
var request = new AiRequest("What is the capital of France?");
var response = await provider.CompleteAsync(request);
Console.WriteLine(response.Content);

// Streaming with StreamEvent pattern matching
await foreach (var evt in provider.StreamAsync(request))
{
    switch (evt)
    {
        case StreamEvent.TextDelta delta:
            Console.Write(delta.Text);
            break;
        case StreamEvent.Usage usage:
            Console.WriteLine($"\nTokens: {usage.TokenUsage.TotalTokens}");
            break;
    }
}
```

## Extended Thinking

Models that support extended thinking (Claude, OpenAI o-series, Ollama think tags) can reason step-by-step before responding:

```csharp
var request = new AiRequest("Prove that √2 is irrational.")
{
    ThinkingEnabled = true,
    ThinkingBudget = 8192
};

await foreach (var evt in provider.StreamAsync(request))
{
    switch (evt)
    {
        case StreamEvent.ThinkingDelta t:
            Console.Write($"[think] {t.Text}");
            break;
        case StreamEvent.TextDelta d:
            Console.Write(d.Text);
            break;
    }
}
```

Use `IAiProvider.GetThinkingSupport(modelId)` to check per-model capability at runtime.

## Tool Calling

Define tools by implementing `IAssistTool`, then pass them in `AiRequest.Tools`:

```csharp
public class WeatherTool : IAssistTool
{
    public string Name => "get_weather";
    public string Description => "Get current weather for a city";
    public string ParameterSchema => """{"type":"object","properties":{"city":{"type":"string"}}}""";
    public bool RequiresConfirmation => false;

    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var city = parameters.GetProperty("city").GetString();
        return $"22°C, sunny in {city}";
    }
}
```

`ToolCallExecutor` handles the execution loop with optional user confirmation:

```csharp
var executor = new ToolCallExecutor([new WeatherTool()]);
executor.ConfirmationHandler = async (name, args) =>
{
    // Show UI confirmation — return true to allow
    return true;
};

var result = await executor.ExecuteAsync(toolCall);
```

`ToolResolver.Resolve()` merges built-in tools with MCP tools, prefixing MCP tool names with the server name on conflict.

## Workspace Context

Inject dynamic context into the system prompt based on app state:

```csharp
public class MyWorkspace : IWorkspaceContext
{
    public string? ActiveLabel => "Project: MyApp";
    public Task<string?> GetContextAsync(CancellationToken ct = default)
        => Task.FromResult<string?>($"Current file: {_currentFile}");
}
```

## Supported Providers

| Provider | Streaming | Vision | Documents | Tool Calling | Thinking |
|----------|:---------:|:------:|:---------:|:------------:|:--------:|
| **Claude** (Anthropic) | Yes | Yes | Yes | Yes | Yes |
| **OpenAI** (+ compatible) | Yes | Yes | Yes | Yes | o-series |
| **Gemini** (Google) | Yes | Yes | Yes | Yes | No |
| **Ollama** (local) | Yes | Dep. | Dep. | Dep. | think tags |
| **Groq** | Yes | Yes | Yes | Yes | Dep. |

> OpenAI provider works with any OpenAI-compatible API (Groq, Azure OpenAI, etc.) by setting a custom `baseUrl`.

## Custom Provider

Implement `IAiProvider` to integrate any AI service:

```csharp
public class MyProvider : IAiProvider
{
    public string ProviderName => "MyService";
    public string ModelId => "my-model-v1";
    public TokenUsage? LastUsage { get; private set; }
    public bool IsTruncated { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastRawResponse { get; private set; }
    public PdfCapability PdfCapability => PdfCapability.TextExtraction;

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        // Call your API, return an AiResponse
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamEvent.TextDelta("Hello from MyService!");
        yield return new StreamEvent.StreamCompleted(false);
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModel>>(
            [new AiModel("my-model-v1", "My Model", "myservice")]);

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new ConnectionInfo(true, null, null, null));

    public ThinkingSupport GetThinkingSupport(string modelId)
        => ThinkingSupport.NotSupported;
}
```

## Key Types

| Type | Description |
|------|-------------|
| `IAiProvider` | Provider interface — completion, streaming, model listing, thinking support |
| `StreamEvent` | Discriminated union — `TextDelta`, `ThinkingDelta`, `ToolCallStart`, `ToolCallDelta`, `Usage`, `StreamCompleted` |
| `IAssistTool` | Tool/function calling interface with optional confirmation |
| `AiRequest` / `AiResponse` | Request (messages, system prompt, tools, thinking) and response models |
| `ChatMessage` | Conversation message with role, content, attachments, and tree branching (`ParentId`) |
| `ProviderPreset` | Saved provider configuration — model, temperature, thinking, PDF capability |
| `Profile` | System prompt (`SystemPrompt`) + tool selection preset |
| `ToolCallExecutor` | Executes tool calls with confirmation handler |
| `ToolResolver` | Merges built-in and MCP tools with conflict resolution |
| `McpToolAdapter` | Bridges MCP tools to `IAssistTool` (zero MCP SDK dependency) |
| `IWorkspaceContext` | Dynamic system prompt injection interface |
| `IContextProvider` | RAG retrieval interface — returns `ContextChunk`s for a query |
| `ContextChunk` | Retrieved context record — `Text`, `Source`, `Score` |
| `BuiltInServerConfig` | Configuration for built-in MCP servers (enabled, folders) |
| `ProviderFactory` | Create `IAiProvider` from a `ProviderPreset` |
| `DiagnosticLogger` | Structured logging with `OnException`/`OnWarning`/`OnInfo` callbacks |
| `ThinkingSupport` | Enum — `NotSupported`, `Optional`, `Required` |
| `ThinkingOverride` | Enum — `Auto`, `ForceOn`, `ForceOff` |
| `PdfCapability` | Enum — `Auto`, `TextExtraction`, `NativePdf`, `PageAsImage` |
| `HardwareProbe` | GPU/CPU detection for Ollama model compatibility |

## Diagnostic Logging

Wire up `DiagnosticLogger` to capture internal events from providers and helpers:

```csharp
DiagnosticLogger.OnException = ex => logger.LogError(ex, "AssistStudio error");
DiagnosticLogger.OnWarning = msg => logger.LogWarning(msg);
DiagnosticLogger.OnInfo = msg => logger.LogInformation(msg);
```

## Related Packages

- [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) — WinUI 3 chat UI controls built on this library.

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
