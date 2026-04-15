# FieldCure.AssistStudio.Anthropic

**Platform-agnostic Anthropic SDK adapter** for the AssistStudio ecosystem. Maps Anthropic streaming events to `StreamEvent` and converts `ChatMessage` to SDK `MessageParam`.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/fieldcure/fieldcure-assiststudio/blob/main/LICENSE)

## Features

- **AnthropicStreamEventMapper** — Stateful, per-stream mapper that converts `RawMessageStreamEvent` to `IAsyncEnumerable<StreamEvent>`. Handles text deltas, thinking deltas, usage, truncation, and unknown block types gracefully.
- **AnthropicMessageConverter** — Converts `ChatMessage` lists to Anthropic `MessageParam` format. Extracts system prompts, handles image/text attachments, and groups messages by role.
- **Platform-agnostic** — Targets `net8.0` and `net9.0`. No WinUI, no Windows dependency. Use in console apps, ASP.NET, MAUI, or any .NET host.

## Install

```bash
dotnet add package FieldCure.AssistStudio.Anthropic
```

## Usage

### Stream mapping

```csharp
using FieldCure.AssistStudio.Anthropic;

var mapper = new AnthropicStreamEventMapper();
var sdkStream = client.Messages.CreateStreaming(params);

await foreach (var evt in mapper.MapAsync(sdkStream))
{
    switch (evt)
    {
        case StreamEvent.TextDelta td:
            Console.Write(td.Text);
            break;
        case StreamEvent.ThinkingDelta tk:
            // extended thinking content
            break;
        case StreamEvent.Usage u:
            Console.WriteLine($"Tokens: {u.TokenUsage.TotalTokens}");
            break;
        case StreamEvent.StreamCompleted sc:
            Console.WriteLine(sc.IsTruncated ? "(truncated)" : "(done)");
            break;
    }
}
```

### Message conversion

```csharp
var result = AnthropicMessageConverter.Convert(messages);

var response = await client.Messages.Create(new()
{
    Model = "claude-sonnet-4-6",
    System = result.SystemPrompt is not null
        ? new(result.SystemPrompt) : null,
    Messages = result.Messages,
    MaxTokens = 4096,
});
```

## Dependencies

| Package | Version |
|---------|---------|
| [Anthropic](https://www.nuget.org/packages/Anthropic) | >= 12.13.0 |
| [FieldCure.Ai.Providers](https://www.nuget.org/packages/FieldCure.Ai.Providers) | (transitive) |

## Related Packages

| Package | Description |
|---------|-------------|
| [FieldCure.AssistStudio.Controls.WinUI.Anthropic](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI.Anthropic) | WinUI 3 ChatPanel integration — extension methods to stream Anthropic responses directly into the chat UI |
| [FieldCure.AssistStudio.Controls.WinUI](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI) | The ChatPanel control itself |

## License

MIT
