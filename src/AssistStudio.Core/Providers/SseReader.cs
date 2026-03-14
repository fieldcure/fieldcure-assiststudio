using System.Runtime.CompilerServices;

namespace FieldCure.AssistStudio.Providers;

internal record SseEvent(string EventType, string Data);

internal static class SseReader
{
    public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);

        var eventType = "message";
        var dataLines = new List<string>();

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);

            if (line is null)
                break;

            if (line.Length == 0)
            {
                // Empty line = event boundary
                if (dataLines.Count > 0)
                {
                    var data = string.Join("\n", dataLines);
                    yield return new SseEvent(eventType, data);
                    eventType = "message";
                    dataLines.Clear();
                }
                continue;
            }

            if (line.StartsWith("event:"))
            {
                eventType = line["event:".Length..].TrimStart();
            }
            else if (line.StartsWith("data:"))
            {
                dataLines.Add(line["data:".Length..].TrimStart());
            }
            // Ignore "id:", "retry:", and comment lines starting with ":"
        }

        // Flush remaining data if stream ends without trailing blank line
        if (dataLines.Count > 0)
        {
            yield return new SseEvent(eventType, string.Join("\n", dataLines));
        }
    }
}
