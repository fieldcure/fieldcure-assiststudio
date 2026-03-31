using System.Text.Json;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace AssistStudio.Mcp;

/// <summary>
/// Detected MCP configuration source from another application.
/// </summary>
/// <param name="AppName">Human-readable app name.</param>
/// <param name="FilePath">Absolute path to the config file.</param>
/// <param name="ServerCount">Number of MCP servers found in the file.</param>
public record ImportSource(string AppName, string FilePath, int ServerCount);

/// <summary>
/// Imports MCP server configurations from other applications
/// (Claude Desktop, VS Code, Claude Code).
/// </summary>
public static class McpConfigImporter
{
    #region Public Methods

    /// <summary>
    /// Detects which applications have MCP server configurations available.
    /// Only returns sources where the file exists and contains at least one server.
    /// </summary>
    public static IReadOnlyList<ImportSource> DetectSources()
    {
        var sources = new List<ImportSource>();

        foreach (var (appName, filePath, parser) in KnownSources)
        {
            if (!File.Exists(filePath)) continue;

            try
            {
                var json = File.ReadAllText(filePath);
                var doc = JsonDocument.Parse(json);
                var count = CountServers(doc.RootElement, parser);
                if (count > 0)
                    sources.Add(new ImportSource(appName, filePath, count));
            }
            catch
            {
                // Skip files we can't parse
            }
        }

        return sources;
    }

    /// <summary>
    /// Parses MCP server configurations from the specified source file.
    /// Environment variables are included in the returned configs
    /// (caller should persist them to PasswordVault).
    /// </summary>
    public static IReadOnlyList<McpServerConfig> ParseFrom(ImportSource source)
    {
        var json = File.ReadAllText(source.FilePath);
        var doc = JsonDocument.Parse(json);

        var parser = KnownSources
            .Where(s => s.FilePath == source.FilePath)
            .Select(s => s.Parser)
            .FirstOrDefault();

        return parser switch
        {
            ParserKind.ClaudeDesktop or ParserKind.ClaudeCode => ParseClaudeFormat(doc.RootElement),
            ParserKind.VSCode => ParseVSCodeFormat(doc.RootElement),
            _ => []
        };
    }

    #endregion

    #region Private

    private enum ParserKind { ClaudeDesktop, ClaudeCode, VSCode }

    private static readonly (string AppName, string FilePath, ParserKind Parser)[] KnownSources =
    [
        ("Claude Desktop",
         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"),
         ParserKind.ClaudeDesktop),

        ("Claude Code",
         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"),
         ParserKind.ClaudeCode),

        ("VS Code",
         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "settings.json"),
         ParserKind.VSCode),
    ];

    private static int CountServers(JsonElement root, ParserKind parser)
    {
        return parser switch
        {
            ParserKind.ClaudeDesktop or ParserKind.ClaudeCode =>
                root.TryGetProperty("mcpServers", out var servers) && servers.ValueKind == JsonValueKind.Object
                    ? servers.EnumerateObject().Count()
                    : 0,

            ParserKind.VSCode =>
                TryGetVSCodeServers(root, out var vsServers)
                    ? vsServers.EnumerateObject().Count()
                    : 0,

            _ => 0
        };
    }

    /// <summary>
    /// Parses Claude Desktop / Claude Code format:
    /// <code>{ "mcpServers": { "name": { "command": "...", "args": [...], "env": {...} } } }</code>
    /// </summary>
    private static List<McpServerConfig> ParseClaudeFormat(JsonElement root)
    {
        var configs = new List<McpServerConfig>();

        if (!root.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object)
            return configs;

        foreach (var entry in servers.EnumerateObject())
        {
            var config = new McpServerConfig
            {
                Name = entry.Name,
                TransportType = McpTransportType.Stdio,
            };

            var server = entry.Value;

            if (server.TryGetProperty("command", out var cmd))
                config.Command = cmd.GetString();

            if (server.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                config.Arguments = args.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.String)
                    .Select(a => a.GetString()!)
                    .ToList();

            if (server.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                config.EnvironmentVariables = [];
                foreach (var envProp in env.EnumerateObject())
                {
                    if (envProp.Value.ValueKind == JsonValueKind.String)
                        config.EnvironmentVariables[envProp.Name] = envProp.Value.GetString()!;
                }
            }

            if (!string.IsNullOrEmpty(config.Command))
                configs.Add(config);
        }

        return configs;
    }

    /// <summary>
    /// Parses VS Code format. Tries both:
    /// <list type="bullet">
    /// <item><c>settings.json: { "mcp": { "servers": { ... } } }</c></item>
    /// <item><c>settings.json: { "mcp.servers": { ... } }</c></item>
    /// </list>
    /// </summary>
    private static List<McpServerConfig> ParseVSCodeFormat(JsonElement root)
    {
        if (!TryGetVSCodeServers(root, out var servers))
            return [];

        var configs = new List<McpServerConfig>();

        foreach (var entry in servers.EnumerateObject())
        {
            var config = new McpServerConfig
            {
                Name = entry.Name,
                TransportType = McpTransportType.Stdio,
            };

            var server = entry.Value;

            // VS Code MCP uses "type" to indicate transport
            if (server.TryGetProperty("type", out var type) &&
                type.GetString()?.Equals("http", StringComparison.OrdinalIgnoreCase) == true)
            {
                config.TransportType = McpTransportType.Http;
                if (server.TryGetProperty("url", out var url))
                    config.Url = url.GetString();
            }
            else
            {
                if (server.TryGetProperty("command", out var cmd))
                    config.Command = cmd.GetString();

                if (server.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                    config.Arguments = args.EnumerateArray()
                        .Where(a => a.ValueKind == JsonValueKind.String)
                        .Select(a => a.GetString()!)
                        .ToList();
            }

            if (server.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                config.EnvironmentVariables = [];
                foreach (var envProp in env.EnumerateObject())
                {
                    if (envProp.Value.ValueKind == JsonValueKind.String)
                        config.EnvironmentVariables[envProp.Name] = envProp.Value.GetString()!;
                }
            }

            if (!string.IsNullOrEmpty(config.Command) || !string.IsNullOrEmpty(config.Url))
                configs.Add(config);
        }

        return configs;
    }

    private static bool TryGetVSCodeServers(JsonElement root, out JsonElement servers)
    {
        // Format 1: { "mcp": { "servers": { ... } } }
        if (root.TryGetProperty("mcp", out var mcp) &&
            mcp.ValueKind == JsonValueKind.Object &&
            mcp.TryGetProperty("servers", out servers) &&
            servers.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        // Format 2: { "mcp.servers": { ... } }
        if (root.TryGetProperty("mcp.servers", out servers) &&
            servers.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        servers = default;
        return false;
    }

    #endregion
}
