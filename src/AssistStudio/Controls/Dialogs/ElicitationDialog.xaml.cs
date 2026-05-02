using AssistStudio.Mcp;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using ModelContextProtocol.Protocol;

namespace AssistStudio.Controls.Dialogs;

/// <summary>
/// Hosts an MCP elicitation prompt inside a dialog-owned submit/cancel surface.
/// </summary>
public sealed partial class ElicitationDialog : ThemedContentDialog
{
    private readonly ResourceLoader _loader = new();
    private IDictionary<string, object?>? _capturedContent;

    /// <summary>Initializes a new dialog for the given elicitation request.</summary>
    internal ElicitationDialog(ElicitationRequest request)
    {
        InitializeComponent();
        ApplyLocalizedText();

        ElicitationPanel.ToolName = request.ToolName;
        ElicitationPanel.ServerName = request.ServerName;
        ElicitationPanel.Message = request.Message;
        ElicitationPanel.Fields = request.Fields;
    }

    /// <summary>Creates the MCP accept result from the hosted panel state.</summary>
    internal ElicitResult CreateAcceptResult() =>
        McpElicitationMapper.ConvertToElicitResult("accept",
            _capturedContent ?? ElicitationPanel.GetContent());

    /// <summary>Applies localized dialog chrome text without relying on x:Uid lookup.</summary>
    private void ApplyLocalizedText()
    {
        Title = GetString("ElicitationDialog_Title", "Input required");
        PrimaryButtonText = GetString("ElicitationDialog_PrimaryButtonText", "OK");
        CloseButtonText = GetString("ElicitationDialog_CloseButtonText", "Cancel");
    }

    /// <summary>
    /// Validates and snapshots the panel content before the dialog closes; the
    /// captured copy avoids reading from the visual tree after unload, when
    /// bindings can disconnect or fire empty change events.
    /// </summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ElicitationPanel.TryValidate())
        {
            args.Cancel = true;
            return;
        }

        _capturedContent = ElicitationPanel.GetContent();
    }

    /// <summary>Returns a localized string, falling back when resources are unavailable.</summary>
    private string GetString(string key, string fallback)
    {
        try
        {
            var value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
