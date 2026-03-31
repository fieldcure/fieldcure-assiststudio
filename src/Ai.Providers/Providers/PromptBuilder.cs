using System.Text;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers;

/// <summary>
/// Assembles a final system prompt from base prompt, memory, workspace context, and retrieved RAG chunks.
/// Providers call this by default; they can bypass it to implement provider-specific context handling.
/// </summary>
internal static class PromptBuilder
{
    /// <summary>
    /// Builds a composite system prompt in priority order:
    /// basePrompt (system instructions) → memory (user context) → workspace → RAG chunks.
    /// Returns <c>null</c> only when all inputs are null or empty.
    /// </summary>
    internal static string? Build(
        string? basePrompt,
        string? workspaceText,
        IReadOnlyList<ContextChunk>? chunks,
        string? memoryText = null)
    {
        var hasMemory = !string.IsNullOrWhiteSpace(memoryText);
        var hasWorkspace = !string.IsNullOrWhiteSpace(workspaceText);
        var hasChunks = chunks is { Count: > 0 };

        if (!hasMemory && !hasWorkspace && !hasChunks)
            return basePrompt;

        var sb = new StringBuilder();

        // 1. Base prompt (system instructions — most stable)
        if (!string.IsNullOrWhiteSpace(basePrompt))
        {
            sb.Append(basePrompt);
            sb.AppendLine();
            sb.AppendLine();
        }

        // 2. Memory (user context — shared across all conversations)
        if (hasMemory)
        {
            sb.AppendLine(memoryText);
            sb.AppendLine();
        }

        // 3. Workspace context (per-conversation)
        if (hasWorkspace)
        {
            sb.AppendLine("[Workspace Context]");
            sb.AppendLine(workspaceText);
            sb.AppendLine();
        }

        // 4. Retrieved RAG chunks (per-query)
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

        return sb.ToString().TrimEnd();
    }
}
