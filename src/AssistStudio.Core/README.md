# FieldCure.AssistStudio.Core

**App-level helpers and models for AssistStudio** — tool orchestration, specialist agents, workspace context, and MCP server management. AI providers live in [`FieldCure.Ai.Providers`](https://www.nuget.org/packages/FieldCure.Ai.Providers).

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **Tool Orchestration** — `ToolCallExecutor` with confirmation handler and parallel execution, `ToolResolver` for built-in/MCP tool merge
- **Specialist Agents** — `ISpecialist` interface for domain-specific sub-agent routing (e.g., Web Search Specialist)
- **Knowledge Base** — `KnowledgeBase` model for multi-KB metadata persistence
- **Workspace Context** — `IWorkspaceContext` for dynamic system prompt injection
- **MCP Server Management** — Built-in server lifecycle (install, update, connect) via `BuiltInServerHelper`
- **RAG Support** — `IContextProvider` retrieves `ContextChunk`s for queries
- **Structured Logging** — `DiagnosticLogger` with pluggable callbacks
- **Hardware Probing** — `HardwareProbe` for GPU/CPU detection (Ollama compatibility)
- **Platform-Agnostic** — Targets `net8.0`

> **Note:** AI providers (Claude, OpenAI, Gemini, Ollama, Groq), streaming, and shared models (`ChatMessage`, `AiRequest`, `IAssistTool`, etc.) are in [`FieldCure.Ai.Providers`](https://www.nuget.org/packages/FieldCure.Ai.Providers). See [Supported Providers](https://www.nuget.org/packages/FieldCure.Ai.Providers#readme-body-tab).

## Install

```bash
dotnet add package FieldCure.AssistStudio.Core
```

## Tool Execution

`ToolCallExecutor` runs tool calls with optional user confirmation and parallel execution:

```csharp
using FieldCure.AssistStudio.Helpers;

var executor = new ToolCallExecutor([weatherTool, fileTool, searchTool]);

// User confirmation — returns (Approved, UserNote)
executor.ConfirmationHandler = async (toolName, argsJson) =>
{
    var approved = await ShowConfirmationDialog(toolName, argsJson);
    return (approved, userNote: null);
};

// Execute a single tool call
var result = await executor.ExecuteAsync(toolCall);
Console.WriteLine(result.Text);

// Multimedia results (IMultiContentTool)
foreach (var media in result.MediaItems)
    Console.WriteLine($"  {media.MimeType}: {media.FileName}");
```

### Tool Resolution

`ToolResolver` merges built-in tools with MCP tools, prefixing names on conflict:

```csharp
using FieldCure.AssistStudio.Helpers;

// Built-in "read_file" + MCP "read_file" → MCP tool becomes "filesystem_read_file"
var tools = ToolResolver.Resolve(builtInTools, mcpTools, conversationState);
```

## Specialist Agents

Define domain-specific specialists for sub-agent routing via `ISpecialist`:

```csharp
using FieldCure.AssistStudio;

public class WebSearchSpecialist : ISpecialist
{
    public string Name => "web_search_specialist";
    public string DisplayName => "Web Search Specialist";
    public string? Icon => null;

    public IReadOnlyList<string> AllowedTools { get; } =
        ["web_search", "web_fetch", "run_javascript"];

    public IReadOnlyList<string> FallbackServers { get; } = ["builtin_essentials"];
    public int MaxRounds => 15;
    public TimeSpan Timeout => TimeSpan.FromMinutes(2);

    public string BuildSystemPrompt(
        string userQuery, IReadOnlyDictionary<string, string>? contextHints = null)
    {
        return $"""
            You are a web research specialist.
            Search the web, read relevant pages, and produce a concise report.

            ## Task
            {userQuery}
            """;
    }
}
```

### Routing hints for the parent

`ISpecialist` itself only defines the sub-agent's own contract.
Implementations can additionally expose a plain `const string` (by
convention named `RoutingGuideline`) that the host appends to the
**parent** conversation's system prompt. This is how the host steers
the parent on when to delegate and how to handle the specialist's
result — e.g. forward the returned `report` verbatim, re-invoke on
`status: "truncated"`, or preserve the `specialist` parameter on retry
instead of falling back to generic `delegate_task` arguments.

`WebSearchSpecialist.RoutingGuideline` and
`JudgmentSpecialist.RoutingGuideline` in AssistStudio are reference
patterns. The constant is not part of the `ISpecialist` interface —
the host that knows about a specialist appends its guideline
explicitly when building the parent system prompt.

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

## Diagnostic Logging

Wire up `DiagnosticLogger` to capture internal events from providers and helpers:

```csharp
DiagnosticLogger.OnException = ex => logger.LogError(ex, "AssistStudio error");
DiagnosticLogger.OnWarning = msg => logger.LogWarning(msg);
DiagnosticLogger.OnInfo = msg => logger.LogInformation(msg);
```

## Key Types

### Core Types

| Type | Description |
|------|-------------|
| `ToolCallExecutor` | Executes tool calls with confirmation handler and parallel execution. Supports `IMultiContentTool` for multimedia results |
| `ToolResolver` | Merges built-in and MCP tools with conflict resolution |
| `ISpecialist` | Specialist agent interface — `Name`, `DisplayName`, `AllowedTools`, `BuildSystemPrompt` |
| `KnowledgeBase` | KB metadata — Id, Name, SourcePaths, Embedding/Contextualizer config |
| `IWorkspaceContext` | Dynamic system prompt injection interface |
| `IContextProvider` | RAG retrieval interface — returns `ContextChunk`s for a query |
| `BuiltInServerConfig` | Configuration for built-in MCP servers (enabled, folders, search engine) |
| `Profile` | System prompt (`SystemPrompt`) + tool/server selection preset |
| `McpServerConfig` | MCP server connection configuration (command, args, transport) |
| `DiagnosticLogger` | Structured logging with `OnException`/`OnWarning`/`OnInfo` callbacks |
| `HardwareProbe` | GPU/CPU detection for Ollama model compatibility |

### From [FieldCure.Ai.Providers](https://www.nuget.org/packages/FieldCure.Ai.Providers) (transitive dependency)

| Type | Description |
|------|-------------|
| `IAiProvider` | Provider interface — completion, streaming, model listing, thinking support |
| `StreamEvent` | Discriminated union — `TextDelta`, `ThinkingDelta`, `ToolCallStart`, `Usage`, `StreamCompleted` |
| `IAssistTool` | Tool/function calling interface with optional confirmation |
| `AiRequest` / `AiResponse` | Request and response models |
| `ChatMessage` | Conversation message with role, content, attachments, and tree branching |
| `ProviderPreset` | Saved provider configuration — model, temperature, thinking, PDF capability |
| `McpToolAdapter` | Bridges MCP tools to `IAssistTool` (zero MCP SDK dependency) |

## Related Packages

- [FieldCure.Ai.Providers](https://www.nuget.org/packages/FieldCure.Ai.Providers) — AI provider clients (Claude, OpenAI, Gemini, Ollama, Groq) and shared models. Core depends on this.
- [FieldCure.Ai.Execution](https://www.nuget.org/packages/FieldCure.Ai.Execution) — Agent loop and sub-agent execution engine built on Providers.
- [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) — WinUI 3 chat UI controls built on this library.

## License

[MIT](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
