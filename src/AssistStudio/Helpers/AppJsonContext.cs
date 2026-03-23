using System.Text.Json.Serialization;
using AssistStudio.Models;
using FieldCure.AssistStudio.Models;

namespace AssistStudio.Helpers;

/// <summary>
/// Source-generated JSON serializer context for trim-safe serialization.
/// Covers all model types used in <see cref="ConversationManager"/> and AppSettings.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<Profile>))]
[JsonSerializable(typeof(List<ProviderPreset>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ConversationData))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IDictionary<string, string>))]
[JsonSerializable(typeof(List<McpServerConfig>))]
[JsonSerializable(typeof(Dictionary<string, BuiltInServerConfig>))]
public partial class AppJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Source-generated JSON serializer context with indented formatting for conversation files.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(JsonStringEnumConverter<ChatRole>)])]
[JsonSerializable(typeof(ConversationData))]
public partial class IndentedJsonContext : JsonSerializerContext
{
}
