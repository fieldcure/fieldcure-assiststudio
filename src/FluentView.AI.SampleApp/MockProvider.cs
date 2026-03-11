using System.Runtime.CompilerServices;
using FluentView.AI.Models;
using FluentView.AI.Providers;

namespace FluentView.AI.SampleApp;

public class MockProvider : IAiProvider
{
    public string ProviderName => "Mock";
    public string ModelId => "echo-1.0";

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
        var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUserMessage?.Content ?? "(no message)";
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastUserMessage = request.Messages.LastOrDefault(m => m.Role == ChatRole.User);
        var text = lastUserMessage?.Content ?? "(no message)";
        var response = $"Echo: {text}";

        // Simulate token-by-token streaming
        var words = response.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var token = (i > 0 ? " " : "") + words[i];
            yield return token;
            await Task.Delay(50, ct);
        }
    }
}
