using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Models;
using System.Text.Json.Serialization;

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

    /// <summary>The name of the ProviderModel used for this conversation.</summary>
    [JsonPropertyName("ProviderModelName")]
    public string? ProviderModelName { get; set; }

    /// <summary>Legacy alias for older .astx files. Read-only; never serialized (no getter).</summary>
    [JsonPropertyName("ProviderPresetName")]
    public string? LegacyProviderPresetName
    {
        set => ProviderModelName ??= value;
    }

    /// <summary>The UTC timestamp when the conversation was saved.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>The list of messages in the conversation.</summary>
    public List<SavedMessage> Messages { get; set; } = [];

    /// <summary>The ID of the active root-level child message. Used when the first message has branches.</summary>
    public string? ActiveRootChildId { get; set; }

    /// <summary>
    /// A stable unique identifier for this conversation, used as the media storage folder key.
    /// Generated on first save if null. Legacy files will have this as null.
    /// </summary>
    public string? ConversationId { get; set; }

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

    /// <summary>Thinking/reasoning content from the AI model (e.g., Claude extended thinking).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThinkingContent { get; set; }

    /// <summary>Media files associated with this message (images, audio, video).</summary>
    public List<MediaReference>? Media { get; set; }

    /// <summary>
    /// Elapsed time in seconds for generating this assistant response.
    /// Null for non-assistant messages or when not measured.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ElapsedSeconds { get; set; }

    /// <summary>
    /// Total token count for this assistant response (input + output).
    /// Null for non-assistant messages or when not reported by the provider.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TokenCount { get; set; }

    /// <summary>
    /// Summary metadata. Non-null indicates this message is a conversation summary.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SummaryMeta? Summary { get; set; }

    /// <summary>
    /// True for the synthetic "Continue writing…" user turn that the host
    /// inserts when the user clicks the Continue button on a truncated
    /// assistant response. Persisted so the conversation tree round-trips
    /// correctly; the renderer skips it on load. Mirrors
    /// <see cref="ChatMessage.IsHidden"/>. Default <c>false</c> omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsHidden { get; set; }

    /// <summary>
    /// True for assistant messages that resume a prior truncated response.
    /// Persisted so the renderer can restore the "↪ continued" chip on load.
    /// Mirrors <see cref="ChatMessage.IsContinuation"/>. Default <c>false</c>
    /// omitted.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsContinuation { get; set; }

    #endregion
}

/// <summary>
/// Reference to a media file stored in the media directory.
/// </summary>
public class MediaReference
{
    /// <summary>Unique identifier for matching in rendered content.</summary>
    public required string Id { get; set; }

    /// <summary>File name in the media directory (no path — directory determined by conversation ID).</summary>
    public required string FileName { get; set; }

    /// <summary>MIME type of the media file.</summary>
    public required string MimeType { get; set; }

    /// <summary>Origin of the media: "user_upload", "mcp_tool", "assistant_generated".</summary>
    public required string Source { get; set; }

    /// <summary>Associated tool call ID (for mcp_tool source).</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Original user-facing filename before content-addressed storage (e.g., "Pasted-20260410-153022.txt").</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalFileName { get; set; }

    /// <summary>Cached character count for pasted text attachments. Null for non-pasted media.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CharCount { get; set; }

    /// <summary>Cached line count for pasted text attachments. Null for non-pasted media.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LineCount { get; set; }

    /// <summary>Cached playback duration in seconds for audio attachments. Null when unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DurationSeconds { get; set; }
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

/// <summary>
/// Metadata stored in manifest.json inside an .astx archive.
/// Used to identify the format version and provide summary info without
/// parsing the full conversation data.
/// </summary>
public class ManifestData
{
    /// <summary>Format version number. Current version is 2.</summary>
    public int FormatVersion { get; set; } = 2;

    /// <summary>The application version that created this archive.</summary>
    public string AppVersion { get; set; } = "";

    /// <summary>UTC timestamp when the conversation was first created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp when the archive was last modified.</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>Number of media entries in the archive.</summary>
    public int MediaCount { get; set; }

    /// <summary>Number of messages in the conversation.</summary>
    public int MessageCount { get; set; }
}

/// <summary>
/// Result of loading an .astx conversation archive, containing the
/// conversation data and all media bytes resolved from the archive's
/// media/ directory.
/// </summary>
public sealed class LoadConversationResult
{
    /// <summary>The deserialized conversation data.</summary>
    public ConversationData Conversation { get; init; } = null!;

    /// <summary>
    /// Media bytes keyed by zip entry path (e.g., "media/a1b2c3d4e5f67890.png").
    /// Empty if the conversation has no media.
    /// </summary>
    public Dictionary<string, byte[]> Media { get; init; } = new();

    /// <summary>The archive manifest metadata.</summary>
    public ManifestData Manifest { get; init; } = null!;
}
