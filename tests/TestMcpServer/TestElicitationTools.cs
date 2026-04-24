using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using static ModelContextProtocol.Protocol.ElicitRequestParams;

namespace TestMcpServer;

/// <summary>
/// MCP tools that exercise all elicitation field types for ToolElicitationPanel verification.
/// Each tool deliberately omits a required parameter, then uses <c>ElicitAsync</c>
/// to request it from the user at runtime.
/// </summary>
[McpServerToolType]
public static class TestElicitationTools
{
    /// <summary>
    /// Demonstrates enum elicitation — presents a list of choices for the user to pick from.
    /// Simulates: "Which channel should I send this message to?"
    /// </summary>
    [McpServerTool(Name = "test_elicit_enum")]
    [Description("Send a greeting message. Asks the user to choose a delivery channel.")]
    public static async Task<string> SendGreeting(
        McpServer server,
        string message,
        CancellationToken ct)
    {
        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = "Which channel should this message be sent to?",
            RequestedSchema = new RequestSchema
            {
                Properties =
                {
                    ["channel"] = new UntitledSingleSelectEnumSchema
                    {
                        Description = "Select a delivery channel",
                        Enum = ["Slack", "Telegram", "KakaoTalk", "Email"]
                    }
                },
                Required = ["channel"]
            }
        }, ct);

        if (result.Action != "accept" || result.Content is null)
            return $"Message not sent. User action: {result.Action}";

        var channel = result.Content["channel"].GetString();
        return $"Message \"{message}\" sent via {channel}!";
    }

    /// <summary>
    /// Demonstrates boolean elicitation — asks a simple yes/no confirmation.
    /// Simulates: "This action is destructive. Proceed?"
    /// </summary>
    [McpServerTool(Name = "test_elicit_bool")]
    [Description("Delete temporary files. Asks the user for confirmation before proceeding.")]
    public static async Task<string> CleanupTempFiles(
        McpServer server,
        string directory,
        CancellationToken ct)
    {
        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"This will delete all temporary files in '{directory}'. Are you sure?",
            RequestedSchema = new RequestSchema
            {
                Properties =
                {
                    ["confirm"] = new BooleanSchema
                    {
                        Description = "Confirm deletion"
                    }
                },
                Required = ["confirm"]
            }
        }, ct);

        if (result.Action != "accept")
            return "Cleanup cancelled by user.";

        var confirmed = result.Content?["confirm"].ValueKind == JsonValueKind.True;
        return confirmed
            ? $"Cleaned up temporary files in '{directory}'."
            : "User declined. No files were deleted.";
    }

    /// <summary>
    /// Demonstrates string elicitation — requests free-text input from the user.
    /// Simulates: "Search returned too many results. Please refine your query."
    /// </summary>
    [McpServerTool(Name = "test_elicit_string")]
    [Description("Search documents. May ask the user to refine the query if results are too broad.")]
    public static async Task<string> SearchDocuments(
        McpServer server,
        string query,
        CancellationToken ct)
    {
        // Simulate a broad query that returns too many results
        if (query.Length < 5)
        {
            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = $"The query \"{query}\" is too broad (500+ results). Please provide a more specific search term.",
                RequestedSchema = new RequestSchema
                {
                    Properties =
                    {
                        ["refined_query"] = new StringSchema
                        {
                            Description = "Enter a more specific search term"
                        }
                    },
                    Required = ["refined_query"]
                }
            }, ct);

            if (result.Action == "accept" && result.Content is not null)
                query = result.Content["refined_query"].GetString() ?? query;
            else
                return $"Search cancelled. User action: {result.Action}";
        }

        return $"Found 12 documents matching \"{query}\".";
    }

    /// <summary>
    /// Demonstrates multi-field elicitation — requests multiple values at once.
    /// Tests how the panel handles a complex schema with mixed types.
    /// Note: Panel behavior for multi-field is TBD (may show all at once with Submit button).
    /// </summary>
    [McpServerTool(Name = "test_elicit_multi")]
    [Description("Create a new task. Asks the user for task details (priority, notification, notes).")]
    public static async Task<string> CreateTask(
        McpServer server,
        string title,
        CancellationToken ct)
    {
        var result = await server.ElicitAsync(new ElicitRequestParams
        {
            Message = $"Provide details for task \"{title}\":",
            RequestedSchema = new RequestSchema
            {
                Properties =
                {
                    ["priority"] = new UntitledSingleSelectEnumSchema
                    {
                        Description = "Task priority",
                        Enum = ["Low", "Medium", "High", "Critical"]
                    },
                    ["notify"] = new BooleanSchema
                    {
                        Description = "Send notification to team?"
                    },
                    ["notes"] = new StringSchema
                    {
                        Description = "Additional notes (optional)"
                    }
                },
                Required = ["priority"]
            }
        }, ct);

        if (result.Action != "accept" || result.Content is null)
            return "Task creation cancelled.";

        var priority = result.Content["priority"].GetString();
        var notify = result.Content.TryGetValue("notify", out var n) && n.ValueKind == JsonValueKind.True;
        var notes = result.Content.TryGetValue("notes", out var notesVal) ? notesVal.GetString() : null;

        var summary = $"Task \"{title}\" created with priority={priority}";
        if (notify) summary += ", team notified";
        if (!string.IsNullOrEmpty(notes)) summary += $", notes: {notes}";

        return summary + ".";
    }
}
