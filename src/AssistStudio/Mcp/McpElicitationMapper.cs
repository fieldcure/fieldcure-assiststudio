using FieldCure.AssistStudio.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// Converts MCP elicitation protocol shapes to AssistStudio UI models and back.
/// </summary>
internal static class McpElicitationMapper
{
    /// <summary>
    /// Converts an MCP elicitation schema to fields rendered by AssistStudio controls.
    /// </summary>
    public static List<ElicitationFieldInfo> ConvertSchema(ElicitRequestParams.RequestSchema? schema)
    {
        var fields = new List<ElicitationFieldInfo>();
        if (schema?.Properties is null) return fields;

        var required = schema.Required is { Count: > 0 }
            ? new HashSet<string>(schema.Required, StringComparer.Ordinal)
            : null;

        foreach (var (name, definition) in schema.Properties)
        {
            var isRequired = required?.Contains(name) ?? false;
            var field = definition switch
            {
                ElicitRequestParams.UntitledSingleSelectEnumSchema enumSchema =>
                    new ElicitationFieldInfo
                    {
                        Name = name,
                        Type = ElicitationFieldType.Enum,
                        Title = enumSchema.Title,
                        Description = enumSchema.Description,
                        Required = isRequired,
                        DefaultValue = enumSchema.Default,
                        Options = [.. enumSchema.Enum.Select(v => new ElicitationOptionInfo
                        {
                            Value = v,
                            DisplayTitle = v,
                        })],
                    },

                ElicitRequestParams.TitledSingleSelectEnumSchema titledSchema =>
                    new ElicitationFieldInfo
                    {
                        Name = name,
                        Type = ElicitationFieldType.Enum,
                        Title = titledSchema.Title,
                        Description = titledSchema.Description,
                        Required = isRequired,
                        DefaultValue = titledSchema.Default,
                        Options = [.. titledSchema.OneOf.Select(o => new ElicitationOptionInfo
                        {
                            Value = o.Const,
                            DisplayTitle = o.Title,
                        })],
                    },

                ElicitRequestParams.BooleanSchema boolSchema =>
                    new ElicitationFieldInfo
                    {
                        Name = name,
                        Type = ElicitationFieldType.Boolean,
                        Title = boolSchema.Title,
                        Description = boolSchema.Description,
                        Required = isRequired,
                        DefaultValue = boolSchema.Default?.ToString(),
                        Options =
                        [
                            new() { Value = "true", DisplayTitle = GetString("Elicitation_Yes", "Yes") },
                            new() { Value = "false", DisplayTitle = GetString("Elicitation_No", "No") },
                        ],
                    },

                _ => new ElicitationFieldInfo
                {
                    Name = name,
                    Type = ElicitationFieldType.String,
                    Title = definition.Title,
                    Description = definition.Description,
                    Required = isRequired,
                },
            };

            fields.Add(field);
        }

        return fields;
    }

    /// <summary>
    /// Converts a UI elicitation result to the MCP protocol result shape.
    /// </summary>
    public static ElicitResult ConvertToElicitResult(
        string action,
        IDictionary<string, object?>? content)
    {
        if (action != "accept" || content is null)
            return new ElicitResult { Action = action };

        var jsonContent = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in content)
        {
            jsonContent[key] = value switch
            {
                "true" => JsonSerializer.SerializeToElement(true),
                "false" => JsonSerializer.SerializeToElement(false),
                string s => JsonSerializer.SerializeToElement(s),
                _ => JsonSerializer.SerializeToElement(value?.ToString() ?? string.Empty),
            };
        }

        return new ElicitResult
        {
            Action = "accept",
            Content = jsonContent,
        };
    }

    /// <summary>Loads a localized string with a safe fallback for non-UI test contexts.</summary>
    private static string GetString(string key, string fallback)
    {
        try
        {
            return new ResourceLoader().GetString(key) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
