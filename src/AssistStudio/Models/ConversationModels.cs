using System.Text.Json.Serialization;
using FieldCure.AssistStudio.Models;

namespace AssistStudio.Models;

/// <summary>
/// The serialized data structure for a saved conversation file.
/// </summary>
public class ConversationData
{
    #region Properties

    /// <summary>The JSON schema URI for the conversation format.</summary>
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "https://assiststudio.dev/schema/conversation/v1";

    /// <summary>The type discriminator for the conversation format.</summary>
    [JsonPropertyName("$type")]
    public string Type { get; set; } = "AssistStudio.Conversation";

    /// <summary>The schema version of the conversation format.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>The display name of the tab this conversation was saved from.</summary>
    public string TabName { get; set; } = "";

    /// <summary>The name of the provider preset used for this conversation.</summary>
    public string? ProviderPresetName { get; set; }

    /// <summary>The UTC timestamp when the conversation was saved.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>The list of messages in the conversation.</summary>
    public List<SavedMessage> Messages { get; set; } = [];

    /// <summary>The ID of the active root-level child message. Used when the first message has branches.</summary>
    public string? ActiveRootChildId { get; set; }

    /// <summary>
    /// Gets or sets the built-in MCP server configurations for this conversation.
    /// Keys: "filesystem", "rag", etc.
    /// Null means use defaults from App Settings.
    /// </summary>
    public Dictionary<string, BuiltInServerConfig>? BuiltInServers { get; set; }

    #endregion
}

/// <summary>
/// Represents a single message within a saved conversation.
/// </summary>
public class SavedMessage
{
    #region Properties

    /// <summary>The unique message identifier. Null for legacy files (a new ID will be generated on load).</summary>
    public string? Id { get; set; }

    /// <summary>The role of the message sender.</summary>
    public ChatRole Role { get; set; }

    /// <summary>The text content of the message.</summary>
    public string Content { get; set; } = "";

    /// <summary>The provider name that generated this message, if it was an assistant response.</summary>
    public string? ProviderName { get; set; }

    /// <summary>The model identifier used to generate this message, if it was an assistant response.</summary>
    public string? ProviderModelId { get; set; }

    /// <summary>The UTC timestamp when the message was created.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Tool calls requested by the assistant in this message.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>The ID of the tool call this message is a result for (tool role messages only).</summary>
    public string? ToolCallId { get; set; }

    /// <summary>The parent message ID in the conversation tree. Null for the first message.</summary>
    public string? ParentId { get; set; }

    /// <summary>The ID of the active child at this branch point. Null if linear or last child is active.</summary>
    public string? ActiveChildId { get; set; }

    #endregion
}

/// <summary>
/// Provides metadata about a saved conversation file on disk.
/// </summary>
public class ConversationFileInfo
{
    #region Properties

    /// <summary>The full file path of the conversation file.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>The file name without extension.</summary>
    public string FileName { get; set; } = "";

    /// <summary>The last modified timestamp of the file in UTC.</summary>
    public DateTime ModifiedAt { get; set; }

    #endregion
}
