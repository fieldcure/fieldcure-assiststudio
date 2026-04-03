# FieldCure.Ai.Execution

Agent loop and sub-agent execution engine for autonomous LLM tool-use workflows.

## Components

| Class | Description |
|-------|-------------|
| `IAgentLoop` / `AgentLoop` | Prompt - model call - tool execution - repeat loop |
| `ISubAgentExecutor` / `SubAgentExecutor` | Isolated sub-agent sessions with context propagation |
| `AgentLoopContext` | Loop input: provider, system prompt, tools, guards |
| `AgentLoopResult` | Loop output: status, summary, tool call count |
| `SubAgentRequest` | Sub-agent task definition with ContextHints |
| `SubAgentResult` | Sub-agent report, status, duration |
| `ContextHintKeys` | Well-known keys for context propagation (kb_id, etc.) |
| `SystemPromptHints` | Converts ContextHints to system prompt fragments |

## Quick Start

### AgentLoop (standalone)

```csharp
using FieldCure.Ai.Execution;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

var loop = new AgentLoop();

var result = await loop.RunAsync(new AgentLoopContext
{
    Provider = myProvider,         // IAiProvider instance
    SystemPrompt = "You are helpful.",
    UserPrompt = "Summarize this document.",
    Tools = [searchTool, readTool], // IAssistTool list
    MaxRounds = 10,
});

Console.WriteLine(result.Summary);  // Last assistant message
Console.WriteLine(result.Status);   // Completed, MaxRoundsReached, Failed
```

### SubAgentExecutor

```csharp
using FieldCure.Ai.Execution;
using FieldCure.Ai.Execution.Models;

var executor = new SubAgentExecutor(
    agentLoop: new AgentLoop(),
    resolveProvider: preset => ProviderFactory.Create(preset),
    resolveTools: (servers, allowed, ct) => BootstrapMcpTools(servers, allowed, ct),
    defaultPresetName: "Claude"
);

var result = await executor.ExecuteAsync(new SubAgentRequest
{
    Prompt = "Research competitor news and write a report.",
    McpServers = ["builtin_essentials"],
    AllowedTools = ["http_request"],
    MaxRounds = 10,
    Timeout = TimeSpan.FromMinutes(2),
    ContextHints = new Dictionary<string, string>
    {
        [ContextHintKeys.KbId] = "my-kb-uuid",  // RAG kb_id propagation
    },
});

Console.WriteLine(result.Report);   // Sub-agent's final report
Console.WriteLine(result.Status);   // Completed, TimedOut, MaxRoundsReached, Failed
```

## Design Principles

- **AgentLoop is pure** - no timeout, no retry, no MCP. Just CancellationToken.
- **Timeout** is the caller's responsibility (CancelAfter + catch OperationCanceledException).
- **Retry** is the caller's responsibility (wrap IAiProvider or handle externally).
- **MCP bootstrapping** stays in the consumer (Runner, AssistStudio Core).
- **ContextHints** are resolved by SubAgentExecutor into system prompt text. AgentLoop never sees them.

## Dependencies

- `FieldCure.Ai.Providers` (IAiProvider, IAssistTool, AiRequest, AiResponse, ChatMessage)

## License

MIT
