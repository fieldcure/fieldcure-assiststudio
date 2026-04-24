using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Helpers;
using System.Text.Json;

namespace FieldCure.AssistStudio.Core.Tools;

/// <summary>
/// Removes information from persistent memory.
/// Called when the user asks to forget or delete previously remembered information.
/// </summary>
public class ForgetTool : IAssistTool
{
    #region Fields

    private readonly MemoryStore _memoryStore;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of <see cref="ForgetTool"/>.
    /// </summary>
    public ForgetTool(MemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    #endregion

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "forget";

    /// <inheritdoc/>
    public string DisplayName => "Forget";

    /// <inheritdoc/>
    public string Description =>
        "Removes information from memory. " +
        "Use when the user asks you to forget or delete previously remembered information.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search term to find the memory to remove (matched by substring)"
            }
          },
          "required": ["query"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var query = parameters.TryGetProperty("query", out var queryEl)
            ? queryEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(JsonSerializer.Serialize(new { error = "Missing required parameter: query" }));

        var removed = _memoryStore.RemoveByQuery(query);

        var result = removed
            ? JsonSerializer.Serialize(new { success = true, message = $"Forgot: {query}" })
            : JsonSerializer.Serialize(new { success = false, message = $"No matching memory found for: {query}" });

        return Task.FromResult(result);
    }

    #endregion
}
