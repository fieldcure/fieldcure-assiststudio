using AssistStudio.Helpers;

namespace AssistStudio.Mcp;

/// <summary>
/// Orchestrates the shared RAG MCP server lifecycle.
/// Connects the multi-KB serve process on app startup when KBs exist.
/// </summary>
/// <remarks>
/// Credential rotation (e.g., the user changing a provider API key in the Models page)
/// is <b>not</b> handled by restarting the RAG process. Per ADR-001 Principle 5, the
/// server itself invalidates its cached key on 401/403 and issues an Elicitation back
/// to the host, which surfaces a prompt through the active ChatPanel. That path keeps
/// the RAG process cross-platform and alive across rotations — no AssistStudio-side
/// reconnect hook is required.
/// </remarks>
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
    /// Ensures the shared RAG serve is running. Call after creating the first KB
    /// or on KB page entry so a subsequent <c>search_documents</c> / <c>get_index_info</c>
    /// call has a live connection to hit.
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
