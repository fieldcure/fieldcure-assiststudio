# FieldCure.AssistStudio.Controls.WinUI.Anthropic

**WinUI 3 ChatPanel integration for the Anthropic SDK** — Stream Claude responses directly into a production-ready chat UI with minimal code.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **`BeginAnthropicTurn`** — Creates an assistant message bubble and starts streaming with one call.
- **`StreamAnthropicAsync`** — Pipes `RawMessageStreamEvent` into ChatPanel rendering (text, thinking blocks, usage tracking).
- **`GetConversationAsAnthropicMessages`** — Converts the current conversation to Anthropic SDK `MessageParam` format for the next API call.
- **Automatic lifecycle** — `AssistantTurnHandle` is `IAsyncDisposable`. Use `await using` for guaranteed finalization (message rendering, input restoration).

> **Scope note.** This package is a thin streaming bridge: raw Anthropic SDK events → ChatPanel rendering. Tool-use dispatch, sub-agent `delegate_task` fan-out, parallel pulse placeholders, and truncation-aware recovery are features of ChatPanel's built-in send flow (when `Provider` is set directly). When you drive Claude through this adapter with `DisableInternalSendFlow="True"`, those behaviors are the caller's responsibility — you handle `tool_use` blocks and decide whether to retry on `stop_reason=max_tokens`.

## Install

```bash
dotnet add package FieldCure.AssistStudio.Controls.WinUI.Anthropic
```

This transitively brings in `FieldCure.AssistStudio.Anthropic` (mapper + converter) and `FieldCure.AssistStudio.Controls.WinUI` (ChatPanel).

## Quick Start

**XAML:**
```xml
<controls:ChatPanel x:Name="ChatPanel" DisableInternalSendFlow="True" />
```

**Code-behind:**
```csharp
using FieldCure.AssistStudio.Controls.Anthropic;

var client = new AnthropicClient { ApiKey = "sk-..." };

ChatPanel.UserMessageSubmitted += async (s, e) =>
{
    await using var handle = ChatPanel.BeginAnthropicTurn("Claude", "claude-sonnet-4-6");
    var conv = ChatPanel.GetConversationAsAnthropicMessages();

    var stream = client.Messages.CreateStreaming(new()
    {
        Model = "claude-sonnet-4-6",
        System = conv.SystemPrompt is not null ? new(conv.SystemPrompt) : null,
        Messages = conv.Messages,
        MaxTokens = 4096,
    });

    await handle.StreamAnthropicAsync(stream);
};
```

## Dependencies

| Package | Version |
|---------|---------|
| [FieldCure.AssistStudio.Anthropic](https://www.nuget.org/packages/FieldCure.AssistStudio.Anthropic) | >= 0.1.0 |
| [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) | (transitive) |
| [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK) | >= 1.7 |

## Related Packages

| Package | Description |
|---------|-------------|
| [FieldCure.AssistStudio.Anthropic](https://www.nuget.org/packages/FieldCure.AssistStudio.Anthropic) | Platform-agnostic mapper + converter (no WinUI dependency) |
| [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) | The ChatPanel control |

## License

MIT
