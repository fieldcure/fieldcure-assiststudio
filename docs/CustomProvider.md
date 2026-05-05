# Implementing a custom `IAiProvider`

Two paths exist for adding a provider that AssistStudio doesn't ship with built-in:

1. **OpenAI-compatible endpoint** — register a `CustomProviderConfig` and reuse the built-in `OpenAiProvider`. No code beyond the registration call.
2. **Arbitrary AI service** — implement `IAiProvider` yourself and assign your instance directly to `ChatPanel.Provider`. `ProviderFactory.Create` does not have a generic-implementation registration hook, so the host owns the lifetime of your provider.

Pick path 1 whenever the upstream API mimics OpenAI's `/v1/chat/completions` shape. Path 2 is for genuinely different protocols (Anthropic-style content blocks, custom transport, on-device runtime, etc.).

---

## Path 1 — OpenAI-compatible endpoint

```csharp
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

ProviderFactory.RegisterCustomProvider(new CustomProviderConfig
{
    Id          = "minimax",          // becomes ProviderType "Custom_minimax"
    DisplayName = "MiniMax",
    BaseUrl     = "https://api.minimax.chat/v1",
});

// Build a model that points at the registered provider …
var model = new ProviderModel
{
    ProviderType = "Custom_minimax",
    ApiKey       = "sk-…",
    ModelId      = "abab6.5-chat",
};

// … and let the factory route to OpenAiProvider with the custom BaseUrl.
Chat.Provider = ProviderFactory.Create(model);
```

The registration is in-memory; persist your `CustomProviderConfig` somewhere host-side (the workspace app stores them under Settings → Models) and call `RegisterCustomProvider` again on each startup.

---

## Path 2 — implementing `IAiProvider`

The interface is small but every member must be supplied. The skeleton below covers all of it; replace the bodies with calls to your service.

```csharp
using System.Runtime.CompilerServices;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

public sealed class MyCustomProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly string _modelId;

    public MyCustomProvider(string apiKey, string modelId)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.example.com/") };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _modelId = modelId;
    }

    // --- Identity ---------------------------------------------------------

    public string ProviderName => "Example";
    public string ModelId      => _modelId;

    // --- Diagnostics (populated after each call) --------------------------

    public TokenUsage? LastUsage      { get; private set; }
    public bool        IsTruncated    { get; private set; }
    public string?     LastRequestBody { get; private set; }
    public string?     LastRawResponse { get; private set; }

    // --- Capability advertisement ----------------------------------------

    public PdfCapability      PdfCapability      => PdfCapability.TextExtraction;
    public AudioCapability    AudioCapability    => AudioCapability.NotSupported;
    public ToolCallingSupport ToolCallingSupport => ToolCallingSupport.NotSupported;

    // --- Calls ------------------------------------------------------------

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        // 1. Serialize `request.Messages` (and optional tool manifest) into your wire format.
        // 2. POST to your endpoint.
        // 3. Capture LastRequestBody / LastRawResponse for the debug pane.
        // 4. Update LastUsage from the API's usage block.
        // 5. Return an AiResponse whose Content carries the assistant text or tool calls.
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Yield TextDelta as chunks arrive; yield ThinkingDelta if the upstream
        // exposes reasoning content; yield ToolCallStart/ToolCallDelta to drive
        // the tool-approval UX; finish with Usage and StreamCompleted.
        yield return new StreamEvent.TextDelta("Hello ");
        yield return new StreamEvent.TextDelta("from Example!");
        yield return new StreamEvent.StreamCompleted(IsTruncated: false);
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModel>>(
            [new AiModel(_modelId, _modelId, "example")]);

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new ConnectionInfo(IsValid: true, ErrorMessage: null,
                                              ResponseTimeMs: null, ModelCount: null));

    public ThinkingSupport GetThinkingSupport(string modelId)
        => ThinkingSupport.NotSupported;
}
```

Wire it up directly:

```csharp
Chat.Provider = new MyCustomProvider(apiKey: "…", modelId: "example-pro");
```

### Streaming notes

`StreamEvent` is a discriminated union; emit the right variant for each upstream chunk:

| Variant | When to emit |
|---------|--------------|
| `TextDelta`        | Visible assistant text chunk. |
| `ThinkingDelta`    | Reasoning/scratch-pad content (renders inside a collapsed block). |
| `ToolCallStart`    | The model wants to call a function. Carries `CallId`, `FunctionName`. |
| `ToolCallDelta`    | Streaming the JSON arguments for a previously-started call. |
| `Usage`            | Final token counts. Emit just before `StreamCompleted`. |
| `StreamCompleted`  | Last event. `IsTruncated=true` when the response was cut off at `max_tokens`. |

Streaming consumers depend on the order: a `ToolCallStart` must precede its `ToolCallDelta`s, and `Usage` should arrive before `StreamCompleted`.

### Tool calling

If your service supports function calling, set `ToolCallingSupport => ToolCallingSupport.Supported` and read `request.RegisteredTools` in `CompleteAsync` / `StreamAsync`. Translate each `IAssistTool` into your wire-format function declaration; on a tool-call response, emit `ToolCallStart` + `ToolCallDelta` events instead of `TextDelta`.

`ToolCallExecutor` (in `FieldCure.AssistStudio.Core`) handles the rest — confirmation, parallel execution, and feeding tool results back into the next turn.

### Persisting choices

Provider instances live alongside the user's selected `ProviderModel` (API key, model id, temperature, etc.). Hosts typically:

1. Persist a `ProviderModel` row for the custom provider (the workspace app uses `provider-models.json`).
2. Re-instantiate `MyCustomProvider` on each app launch using the saved fields.
3. Assign it to the active `ChatPanel`.
