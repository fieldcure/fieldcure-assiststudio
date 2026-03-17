# FieldCure.AssistStudio.Core

**Platform-agnostic AI provider client library for .NET** — Claude, OpenAI, Gemini, Ollama, and any OpenAI-compatible endpoint.

[![NuGet](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Core)](https://www.nuget.org/packages/FieldCure.AssistStudio.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **Multi-Provider** — Claude, OpenAI, Gemini, and Ollama built-in. Implement `IAiProvider` to add your own.
- **Streaming** — Real-time token-by-token responses via `IAsyncEnumerable<string>`.
- **Vision & Documents** — Attach images, PDFs, and DOCX files to conversations.
- **Tool / Function Calling** — Define tools with `IAssistTool` for provider-invoked function calls.
- **Token Tracking** — Input/output token counts exposed after every request.
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

// Streaming
await foreach (var token in provider.StreamAsync(request))
{
    Console.Write(token);
}
```

## Supported Providers

| Provider | Streaming | Vision | Documents | Tool Calling |
|----------|:---------:|:------:|:---------:|:------------:|
| **Claude** (Anthropic) | Yes | Yes | Yes | Yes |
| **OpenAI** (+ compatible) | Yes | Yes | Yes | Yes |
| **Gemini** (Google) | Yes | Yes | Yes | Yes |
| **Ollama** (local) | Yes | Model-dependent | Model-dependent | Model-dependent |

> OpenAI provider works with any OpenAI-compatible API (Groq, Azure OpenAI, etc.) by setting a custom `baseUrl`.

## Custom Provider

Implement `IAiProvider` to integrate any AI service:

```csharp
public class MyProvider : IAiProvider
{
    public string ProviderName => "MyService";
    public string ModelId => "my-model-v1";
    public TokenUsage? LastUsage { get; private set; }
    // ... implement CompleteAsync, StreamAsync, ListModelsAsync, ValidateConnectionAsync
}
```

## Key Types

| Type | Description |
|------|-------------|
| `IAiProvider` | Provider interface — completion, streaming, model listing |
| `IAssistTool` | Tool/function calling interface |
| `AiRequest` / `AiResponse` | Request and response models |
| `ChatMessage` | Conversation message with role, content, and attachments |
| `ProviderPreset` | Saved provider configuration (model, temperature, etc.) |
| `Profile` | System prompt + tool selection preset |
| `ConversationManager` | Save/load conversations in `.astx` format |
| `ProviderFactory` | Create `IAiProvider` from a `ProviderPreset` |

## Related Packages

- [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) — WinUI 3 chat UI controls built on this library.

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
