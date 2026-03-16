using System.Text;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// Assembles a final system prompt from base prompt, workspace context, and retrieved RAG chunks.
/// Providers call this by default; they can bypass it to implement provider-specific context handling.
/// </summary>
internal static class PromptBuilder
{
    /// <summary>
    /// Builds a composite system prompt by prepending workspace text and context chunks to the base prompt.
    /// Returns <c>null</c> only when all inputs are null or empty.
    /// </summary>
    internal static string? Build(string? basePrompt, string? workspaceText, IReadOnlyList<ContextChunk>? chunks)
    {
        var hasWorkspace = !string.IsNullOrWhiteSpace(workspaceText);
        var hasChunks = chunks is { Count: > 0 };

        if (!hasWorkspace && !hasChunks)
            return basePrompt;

        var sb = new StringBuilder();

        if (hasWorkspace)
        {
            sb.AppendLine("[Workspace Context]");
            sb.AppendLine(workspaceText);
            sb.AppendLine();
        }

        if (hasChunks)
        {
            sb.AppendLine("[Retrieved Context]");
            foreach (var chunk in chunks!)
            {
                if (chunk.Source is not null)
                    sb.AppendLine($"<source: {chunk.Source}>");
                sb.AppendLine(chunk.Text);
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(basePrompt))
            sb.Append(basePrompt);

        return sb.ToString().TrimEnd();
    }
}
