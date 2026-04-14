namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Lightweight server descriptor for the tool flyout UI.
/// Passed from the App layer via dependency properties.
/// </summary>
/// <param name="Id">Server ID (e.g., "builtin_filesystem", "github_abc123").</param>
/// <param name="DisplayName">Display name (e.g., "github", "Workspace Folders").</param>
/// <param name="IsConnected">Whether the server is currently connected.</param>
/// <param name="IsBuiltIn">Whether this is a built-in server.</param>
public record ServerInfo(string Id, string DisplayName, bool IsConnected, bool IsBuiltIn);
