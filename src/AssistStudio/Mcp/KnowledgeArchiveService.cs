using System.Text.Json;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Mcp;

/// <summary>
/// Orchestrates Knowledge Archive (지식보관소) MCP server lifecycle and document indexing.
/// Reports progress via <see cref="NotificationCenter"/>.
/// </summary>
public sealed class KnowledgeArchiveService
{
    #region Fields

    private readonly McpServerRegistry _registry;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeArchiveService"/> class.
    /// </summary>
    public KnowledgeArchiveService(McpServerRegistry registry)
    {
        _registry = registry;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Runs <c>index_documents</c> on the connected RAG server and posts progress notifications.
    /// </summary>
    /// <param name="force">If <see langword="true"/>, re-indexes all files regardless of hash.</param>
    public async Task IndexAsync(bool force = false)
    {
        var connection = _registry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection is null || !connection.IsConnected)
        {
            LoggingService.LogWarning("[KnowledgeArchive] Cannot index — RAG server not connected");
            return;
        }

        var tool = connection.Tools.FirstOrDefault(t => t.Name == "index_documents");
        if (tool is null)
        {
            LoggingService.LogWarning("[KnowledgeArchive] index_documents tool not found");
            return;
        }

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Informational,
            "Knowledge Archive",
            "Indexing documents…");

        try
        {
            var args = JsonSerializer.SerializeToElement(new { force });
            var result = await tool.ExecuteAsync(args);

            NotificationCenter.Instance.Post(
                InfoBarSeverity.Success,
                "Knowledge Archive — Ready",
                result ?? "Indexing complete.",
                5000);

            LoggingService.LogInfo($"[KnowledgeArchive] Indexing complete: {result}");
        }
        catch (Exception ex)
        {
            NotificationCenter.Instance.Post(
                InfoBarSeverity.Error,
                "Knowledge Archive — Failed",
                $"{ex.Message}\nMake sure an embedding model is loaded:\n  ollama pull nomic-embed-text",
                8000);

            LoggingService.LogError($"[KnowledgeArchive] Indexing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Connects the RAG server and runs initial indexing.
    /// Syncs embedding environment variables from AppSettings before connecting.
    /// </summary>
    public async Task ConnectAndIndexAsync()
    {
        var builtIn = AppSettings.BuiltInServers;
        var ragConfig = builtIn.GetValueOrDefault(BuiltInServerHelper.RagKey);
        if (ragConfig is null || !ragConfig.IsEnabled || ragConfig.Folders.Count == 0)
            return;

        // Sync embedding + contextualizer env vars
        SyncRagEnvVars(ragConfig);

        builtIn[BuiltInServerHelper.RagKey] = ragConfig;
        AppSettings.BuiltInServers = builtIn;

        await _registry.ConnectBuiltInAsync(BuiltInServerHelper.RagKey, ragConfig);

        var connection = _registry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected == true)
        {
            await IndexAsync();
        }
    }

    /// <summary>
    /// Syncs embedding and contextualizer settings from <see cref="AppSettings"/>
    /// to PasswordVault and sets the environment variable keys on the config.
    /// </summary>
    private static void SyncRagEnvVars(FieldCure.AssistStudio.Models.BuiltInServerConfig ragConfig)
    {
        const string id = "builtin_rag";

        // Embedding
        PasswordVaultHelper.SaveMcpEnvVar(id, "EMBEDDING_BASE_URL", AppSettings.EmbeddingBaseUrl);
        PasswordVaultHelper.SaveMcpEnvVar(id, "EMBEDDING_MODEL", AppSettings.EmbeddingModel);
        PasswordVaultHelper.SaveMcpEnvVar(id, "EMBEDDING_DIMENSION", "0");
        // EMBEDDING_API_KEY is saved by AppTasksPage handlers

        // Contextualizer
        PasswordVaultHelper.SaveMcpEnvVar(id, "CONTEXTUALIZER_PROVIDER", AppSettings.ContextualizerProvider);
        PasswordVaultHelper.SaveMcpEnvVar(id, "CONTEXTUALIZER_BASE_URL", AppSettings.ContextualizerBaseUrl);
        PasswordVaultHelper.SaveMcpEnvVar(id, "CONTEXTUALIZER_MODEL", AppSettings.ContextualizerModel);
        // CONTEXTUALIZER_API_KEY is saved by AppTasksPage handlers

        ragConfig.EnvironmentVariableKeys =
        [
            "EMBEDDING_BASE_URL", "EMBEDDING_API_KEY",
            "EMBEDDING_MODEL", "EMBEDDING_DIMENSION",
            "CONTEXTUALIZER_PROVIDER", "CONTEXTUALIZER_BASE_URL",
            "CONTEXTUALIZER_API_KEY", "CONTEXTUALIZER_MODEL",
        ];
    }

    #endregion
}
