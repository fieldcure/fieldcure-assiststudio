using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Dialogs;

/// <summary>
/// Dialog for adding or editing a custom OpenAI-compatible provider.
/// </summary>
public sealed partial class AddCustomProviderDialog : ThemedContentDialog
{
    #region Properties

    /// <summary>
    /// The resulting configuration. Non-null after the dialog is confirmed.
    /// </summary>
    public CustomProviderConfig? Result { get; private set; }

    /// <summary>
    /// When editing, the existing config to populate fields from.
    /// </summary>
    public CustomProviderConfig? EditingConfig { get; set; }

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="AddCustomProviderDialog"/>.</summary>
    public AddCustomProviderDialog()
    {
        InitializeComponent();
    }

    #endregion

    #region Lifecycle

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (EditingConfig is not null)
        {
            Title = Loader.GetString("CustomProviderDialog_EditTitle");
            DisplayNameBox.Text = EditingConfig.DisplayName;
            BaseUrlBox.Text = EditingConfig.BaseUrl;
        }

        ValidateInput();
    }

    #endregion

    #region Event Handlers

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        ValidateInput();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var baseUrl = BaseUrlBox.Text.Trim().TrimEnd('/');

        Result = new CustomProviderConfig
        {
            Id = EditingConfig?.Id ?? Guid.NewGuid().ToString("N"),
            DisplayName = DisplayNameBox.Text.Trim(),
            BaseUrl = baseUrl,
        };
    }

    #endregion

    #region Private Methods

    private void ValidateInput()
    {
        var hasName = !string.IsNullOrWhiteSpace(DisplayNameBox.Text);
        var hasUrl = !string.IsNullOrWhiteSpace(BaseUrlBox.Text);
        IsPrimaryButtonEnabled = hasName && hasUrl;
    }

    #endregion
}
