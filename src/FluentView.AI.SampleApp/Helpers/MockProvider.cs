using System.Runtime.CompilerServices;
using FluentView.AI.Models;
using FluentView.AI.Providers;

namespace FluentView.AI.SampleApp.Helpers;

public class MockProvider : IAiProvider
{
    public string ProviderName => "Mock";
    public string ModelId => "mock-markdown-1.0";
    public TokenUsage? LastUsage => null;
    public bool IsTruncated => false;

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
                Console.WriteLine("Hello, FluentView.AI!");
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

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        return MarkdownResponse;
    }

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AiModel> models = [new AiModel("mock-markdown-1.0", "Mock Markdown", "mock")];
        return Task.FromResult(models);
    }

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ConnectionInfo(true, null, null, null));
    }

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
}
