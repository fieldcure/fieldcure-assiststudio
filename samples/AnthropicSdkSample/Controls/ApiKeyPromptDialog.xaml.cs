using Microsoft.UI.Xaml.Controls;

namespace AnthropicSdkSample.Controls;

/// <summary>
/// Dialog that prompts the user for their Anthropic API key on first launch or after a reset.
/// </summary>
public sealed partial class ApiKeyPromptDialog : ContentDialog
{
    /// <summary>Initializes a new <see cref="ApiKeyPromptDialog"/>.</summary>
    public ApiKeyPromptDialog()
    {
        InitializeComponent();
    }

    /// <summary>The API key text entered by the user.</summary>
    public string ApiKey => ApiKeyInput.Password;
}
