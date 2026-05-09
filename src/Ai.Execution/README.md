# FieldCure.Ai.Execution

Agent loop and sub-agent execution engine for autonomous LLM tool-use workflows.

## Components

| Class | Description |
|-------|-------------|
| `IAgentLoop` / `AgentLoop` | Prompt - model call - tool execution - repeat loop |
| `ISubAgentExecutor` / `SubAgentExecutor` | Isolated sub-agent sessions with context propagation |
| `AgentLoopContext` | Loop input: provider, system prompt, tools, guards |
| `AgentLoopResult` | Loop output: status, summary, tool call count, `Messages` (full conversation history for audit trails) |
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

Console.WriteLine(result.Summary);   // Last assistant message
Console.WriteLine(result.Status);    // Completed, Truncated, MaxRoundsReached, Failed
Console.WriteLine(result.Messages);  // Full conversation for audit logging
```

### SubAgentExecutor

```csharp
using FieldCure.Ai.Execution;
using FieldCure.Ai.Execution.Models;

var executor = new SubAgentExecutor(
    agentLoop: new AgentLoop(),
    resolveProvider: model => ProviderFactory.Create(model),
    resolveTools: (servers, allowed, ct) => BootstrapMcpTools(servers, allowed, ct)
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
Console.WriteLine(result.Status);   // Completed, Truncated, TimedOut, MaxRoundsReached, Failed
```

## Status values

| Status | `AgentLoopStatus` | `SubAgentStatus` | Meaning |
|--------|:---:|:---:|---------|
| `Completed` | ✓ | ✓ | Model produced a response with no tool calls. `Summary` / `Report` is the final text. |
| `Truncated` | ✓ | ✓ | The terminating response had no tool calls **and** `AiResponse.IsTruncated` was set (provider hit `max_tokens`). The content is partial — do **not** treat as graceful completion. Callers typically retry with a tighter scope or surface the truncation to the user. |
| `MaxRoundsReached` | ✓ | ✓ | `context.MaxRounds` reached while the model was still emitting tool calls. |
| `TimedOut` | — | ✓ | Only `SubAgentExecutor` enforces a wall-clock timeout (`SubAgentRequest.Timeout`). AgentLoop itself is timeout-free — cancellation is the caller's responsibility via `CancellationToken`. |
| `Failed` | ✓ | ✓ | An unhandled exception (other than `OperationCanceledException`, which propagates). `ErrorMessage` (loop) / `Report` (sub-agent) carries the detail. |

Note: when the context guard forces a final summary round (see
`MaxContextChars`), the terminating response is classified as `Completed`
even if `IsTruncated` would otherwise flip it — the guard asked for a
summary on purpose, so the partial marker doesn't apply.

## Tool execution order

Two layers, two policies:

| Layer | Behavior |
|-------|----------|
| `AgentLoop` (this package) — tool calls within a single LLM round | **Sequential** (`foreach` over `response.ToolCalls`) |
| Caller-level dispatch of independent agent invocations | **Caller's responsibility** — e.g., AssistStudio's ChatPanel runs `delegate_task` sub-agent calls in parallel via `Task.WhenAll` over `SubAgentExecutor.ExecuteWithoutConfirmationAsync`. |

In short: AgentLoop is intentionally simple (one round = sequential tool
execution), and parallel fan-out across multiple sub-agents is layered on
top by the host application.

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
