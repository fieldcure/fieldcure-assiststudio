using System.Text.Json;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Tools;

/// <summary>
/// Saves information to persistent memory for use across conversations.
/// Called when the user asks to remember preferences, facts, or context.
/// </summary>
public class RememberTool : IAssistTool
{
    #region Fields

    private readonly MemoryStore _memoryStore;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of <see cref="RememberTool"/>.
    /// </summary>
    public RememberTool(MemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    #endregion

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "remember";

    /// <inheritdoc/>
    public string DisplayName => "Remember";

    /// <inheritdoc/>
    public string Description =>
        "Saves information to memory for use across conversations. " +
        "Use when the user asks you to remember preferences, facts, or context. " +
        "Write the content as a concise third-person statement.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "content": {
              "type": "string",
              "description": "Concise factual statement to remember, written in third person (e.g., 'User prefers dark theme.')"
            }
          },
          "required": ["content"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var content = parameters.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(JsonSerializer.Serialize(new { error = "Missing required parameter: content" }));

        var (success, warning) = _memoryStore.Add(content);

        if (!success)
            return Task.FromResult(JsonSerializer.Serialize(new { error = "Failed to save memory." }));

        var result = warning is not null
            ? JsonSerializer.Serialize(new { success = true, message = $"Remembered: {content}", warning })
            : JsonSerializer.Serialize(new { success = true, message = $"Remembered: {content}" });

        return Task.FromResult(result);
    }

    #endregion
}
