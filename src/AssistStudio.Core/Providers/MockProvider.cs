using System.Runtime.CompilerServices;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// A mock AI provider that returns a static Markdown response, useful for UI testing and development.
/// </summary>
public class MockProvider : IAiProvider
{
    #region Properties

    /// <inheritdoc/>
    public string ProviderName => "Mock";

    /// <inheritdoc/>
    public string ModelId => "mock-markdown-1.0";

    /// <inheritdoc/>
    public TokenUsage? LastUsage => null;

    /// <inheritdoc/>
    public bool IsTruncated => false;

    /// <inheritdoc/>
    public string? LastRequestBody => null;

    /// <inheritdoc/>
    public string? LastRawResponse => null;

    /// <inheritdoc/>
    public PdfCapability PdfCapability => PdfCapability.TextExtraction;

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
    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Stream the markdown response token-by-token (word-level)
        var words = MarkdownResponse.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var token = (i > 0 ? " " : "") + words[i];
            yield return token;
            await Task.Delay(30, ct);
        }
    }

    #endregion
}
