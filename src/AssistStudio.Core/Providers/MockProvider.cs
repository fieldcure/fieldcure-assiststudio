using System.Runtime.CompilerServices;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// A mock AI provider that returns configurable responses, useful for UI testing and development.
/// Supports simulating the full streaming pipeline: thinking, text, tool calls, and usage events.
/// </summary>
public class MockProvider : IAiProvider
{
    #region Properties

    /// <inheritdoc/>
    public string ProviderName => "Mock";

    /// <inheritdoc/>
    public string ModelId => "mock-markdown-1.0";

    /// <inheritdoc/>
    public TokenUsage? LastUsage { get; private set; }

    /// <inheritdoc/>
    public bool IsTruncated { get; private set; }

    /// <inheritdoc/>
    public string? LastRequestBody => null;

    /// <inheritdoc/>
    public string? LastRawResponse => null;

    /// <inheritdoc/>
    public PdfCapability PdfCapability => PdfCapability.TextExtraction;

    #endregion

    #region Configuration

    /// <summary>
    /// The sequence of <see cref="StreamEvent"/> instances to yield during <see cref="StreamAsync"/>.
    /// When set, overrides default behavior and yields these events verbatim.
    /// A <see cref="StreamEvent.StreamCompleted"/> is appended automatically if the sequence does not end with one.
    /// </summary>
    public IReadOnlyList<StreamEvent>? ScriptedEvents { get; set; }

    /// <summary>
    /// Delay in milliseconds between each yielded event. Default is 30ms.
    /// Set to 0 for instant streaming (useful in tests).
    /// </summary>
    public int EventDelayMs { get; set; } = 30;

    /// <summary>
    /// Simulated token usage to emit as a <see cref="StreamEvent.Usage"/> event.
    /// Only used when <see cref="ScriptedEvents"/> is null (default streaming mode).
    /// </summary>
    public TokenUsage? SimulatedUsage { get; set; }

    /// <summary>
    /// Whether to simulate a truncated response.
    /// Only used when <see cref="ScriptedEvents"/> is null (default streaming mode).
    /// </summary>
    public bool SimulateTruncated { get; set; }

    #endregion

    #region Constants

    /// <summary>The static Markdown response returned by the mock provider.</summary>
    private const string MarkdownResponse = """
        Here's a **Markdown** demo response!

        ## Features

        - **Bold text** and *italic text*
        - Inline `code` formatting
        - [Hyperlinks](https://example.com)

        ### Code Block (C#)

        ```csharp
        public class HelloWorld
        {
            public static void Main(string[] args)
            {
                Console.WriteLine("Hello, AssistStudio!");
            }
        }
        ```

        ### Code Block (Python)

        ```python
        def fibonacci(n):
            if n <= 1:
                return n
            return fibonacci(n - 1) + fibonacci(n - 2)

        for i in range(10):
            print(fibonacci(i))
        ```

        ### Table

        | Language | Typing | Use Case |
        |----------|--------|----------|
        | C# | Static | WinUI, .NET |
        | Python | Dynamic | ML, Scripts |
        | JavaScript | Dynamic | Web, Node.js |

        > This is a blockquote. It demonstrates the styling of quoted text.

        ---

        That's the end of the demo!
        """;

    #endregion

    #region IAiProvider Implementation

    /// <inheritdoc/>
    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        return new AiResponse { Content = MarkdownResponse };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AiModel> models = [new AiModel("mock-markdown-1.0", "Mock Markdown", "mock")];
        return Task.FromResult(models);
    }

    /// <inheritdoc/>
    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ConnectionInfo(true, null, null, null));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        IsTruncated = false;
        LastUsage = null;

        if (ScriptedEvents is not null)
        {
            // Scripted mode: yield each event from the configured sequence
            var hasCompleted = false;
            foreach (var evt in ScriptedEvents)
            {
                ct.ThrowIfCancellationRequested();
                if (EventDelayMs > 0)
                    await Task.Delay(EventDelayMs, ct);

                if (evt is StreamEvent.Usage u)
                    LastUsage = u.TokenUsage;
                else if (evt is StreamEvent.StreamCompleted c)
                {
                    IsTruncated = c.IsTruncated;
                    hasCompleted = true;
                }

                yield return evt;
            }

            if (!hasCompleted)
                yield return new StreamEvent.StreamCompleted(IsTruncated);
        }
        else
        {
            // Default mode: stream the built-in markdown response word-by-word
            var words = MarkdownResponse.Split(' ');
            for (var i = 0; i < words.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var token = (i > 0 ? " " : "") + words[i];
                yield return new StreamEvent.TextDelta(token);
                if (EventDelayMs > 0)
                    await Task.Delay(EventDelayMs, ct);
            }

            IsTruncated = SimulateTruncated;

            if (SimulatedUsage is not null)
            {
                LastUsage = SimulatedUsage;
                yield return new StreamEvent.Usage(SimulatedUsage);
            }

            yield return new StreamEvent.StreamCompleted(IsTruncated);
        }
    }

    #endregion
}
