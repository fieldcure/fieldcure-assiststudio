using AssistStudio.Helpers;

namespace AssistStudio.Mcp;

/// <summary>
/// Orchestrates the shared RAG MCP server lifecycle.
/// Connects the multi-KB serve process on app startup when KBs exist.
/// </summary>
public sealed class KnowledgeBaseService
{
    #region Fields

    private readonly McpServerRegistry _registry;

    #endregion

    #region Constructor

    public KnowledgeBaseService(McpServerRegistry registry)
    {
        _registry = registry;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Connects the shared RAG serve if knowledge bases exist.
    /// Auto-enables the RAG server when KBs are present.
    /// </summary>
    public async Task ConnectIfNeededAsync()
    {
        if (!KnowledgeBaseStore.AnyExists())
        {
            LoggingService.LogInfo("[KB] Skipped — no knowledge bases exist");
            return;
        }

        // Auto-enable RAG server when KBs exist
        var builtIn = AppSettings.BuiltInServers;
        var ragConfig = builtIn.GetValueOrDefault(BuiltInServerHelper.RagKey)
                        ?? new FieldCure.AssistStudio.Models.BuiltInServerConfig();
        if (!ragConfig.IsEnabled)
        {
            ragConfig.IsEnabled = true;
            builtIn[BuiltInServerHelper.RagKey] = ragConfig;
            AppSettings.BuiltInServers = builtIn;
            LoggingService.LogInfo("[KB] Auto-enabled RAG server (KBs exist)");
        }

        LoggingService.LogInfo("[KB] Connecting shared RAG serve…");
        await _registry.ConnectBuiltInAsync(BuiltInServerHelper.RagKey, ragConfig);

        var connection = _registry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected == true)
        {
            LoggingService.LogInfo($"[KB] Connected — tools: {string.Join(", ", connection.Tools.Select(t => t.Name))}");
        }
        else
        {
            LoggingService.LogWarning("[KB] Connection failed");
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

    /// <summary>
    /// Page-entry refresh: if RAG is already connected, rebuild its config from current
    /// <see cref="AppSettings"/> / PasswordVault state (picks up rotated Provider keys)
    /// and reconnect. If not yet connected, falls through to <see cref="EnsureConnectedAsync"/>,
    /// which spawns a fresh process and therefore already reads the latest keys.
    /// "Entering the KB page" is treated as an explicit refresh intent per the
    /// snapshot + explicit policy (doc 80).
    /// </summary>
    public async Task RefreshOrConnectAsync()
    {
        var connection = _registry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected == true
            && BuiltInServerHelper.TryRebuildBuiltInConfig(connection.Config))
        {
            LoggingService.LogInfo("[KB] Rebuilding RAG config with current settings and reconnecting");
            await _registry.ReconnectAsync(connection);
            return;
        }

        await EnsureConnectedAsync();
    }

    #endregion
}
