using AssistStudio.Helpers;

namespace AssistStudio.Mcp;

/// <summary>
/// Orchestrates the shared RAG MCP server lifecycle.
/// Connects the multi-KB serve process on app startup when KBs exist.
/// </summary>
public sealed class KnowledgeArchiveService
{
    #region Fields

    private readonly McpServerRegistry _registry;

    #endregion

    #region Constructor

    public KnowledgeArchiveService(McpServerRegistry registry)
    {
        _registry = registry;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Connects the shared RAG serve if knowledge bases exist and the server is enabled.
    /// </summary>
    public async Task ConnectIfNeededAsync()
    {
        var builtIn = AppSettings.BuiltInServers;
        var ragConfig = builtIn.GetValueOrDefault(BuiltInServerHelper.RagKey);
        if (ragConfig is null || !ragConfig.IsEnabled)
        {
            LoggingService.LogInfo("[KnowledgeArchive] Skipped — server disabled");
            return;
        }

        if (!KnowledgeBaseStore.AnyExists())
        {
            LoggingService.LogInfo("[KnowledgeArchive] Skipped — no knowledge bases exist");
            return;
        }

        LoggingService.LogInfo("[KnowledgeArchive] Connecting shared RAG serve…");
        await _registry.ConnectBuiltInAsync(BuiltInServerHelper.RagKey, ragConfig);

        var connection = _registry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected == true)
        {
            LoggingService.LogInfo($"[KnowledgeArchive] Connected — tools: {string.Join(", ", connection.Tools.Select(t => t.Name))}");
        }
        else
        {
            LoggingService.LogWarning("[KnowledgeArchive] Connection failed");
        }
    }

    /// <summary>
    /// Ensures the shared RAG serve is running. Call after creating the first KB.
    /// </summary>
    public async Task EnsureConnectedAsync()
    {
        var connection = _registry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected == true)
            return;

        await ConnectIfNeededAsync();
    }

    #endregion
}
