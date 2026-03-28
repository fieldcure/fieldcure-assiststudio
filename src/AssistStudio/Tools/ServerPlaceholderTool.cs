using System.Text.Json;
using FieldCure.AssistStudio.Models;

namespace AssistStudio.Tools;

/// <summary>
/// Placeholder tool entry representing an MCP server in the tools flyout.
/// Not sent in API requests — serves as a UI toggle for server-level tool control.
/// At send time, <see cref="Modules.ViewModels.ChatTabViewModel.PrepareToolsForSendAsync"/>
/// replaces these with actual tools from connected servers.
/// </summary>
internal sealed class ServerPlaceholderTool : IAssistTool
{
    /// <summary>The server ID (e.g., "builtin_filesystem", "github").</summary>
    public required string Name { get; init; }

    /// <summary>Localized display name for the flyout (e.g., "Workspace Folders").</summary>
    public required string DisplayName { get; init; }

    public string Description => "";
    public string ParameterSchema => """{"type":"object"}""";
    public bool IsServerPlaceholder => true;

    public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
        => throw new NotSupportedException("ServerPlaceholderTool is for display only.");
}
