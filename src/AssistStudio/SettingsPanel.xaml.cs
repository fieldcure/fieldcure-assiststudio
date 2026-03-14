using System.Collections.ObjectModel;
using FieldCure.AssistStudio.Models;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AssistStudio;

/// <summary>
/// Settings side panel that hosts navigation to individual settings pages and
/// relays configuration changes back to the main window via events.
/// </summary>
public sealed partial class SettingsPanel : Page
{
    #region Fields

    /// <summary>
    /// The collection of provider presets managed by this settings panel.
    /// </summary>
    private ObservableCollection<ProviderPreset> _presets;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the collection of provider presets available to the application.
    /// </summary>
    public ObservableCollection<ProviderPreset> Presets
    {
        get => _presets;
        set => _presets = value;
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user changes the application theme.
    /// </summary>
    public event EventHandler<string>? ThemeChanged;

    /// <summary>
    /// Raised when the system prompt text is changed.
    /// </summary>
    public event EventHandler<string>? SystemPromptChanged;

    /// <summary>
    /// Raised when provider presets are added, removed, or modified.
    /// </summary>
    public event EventHandler? PresetsChanged;

    /// <summary>
    /// Raised when prompt presets are added, removed, or modified.
    /// </summary>
    public event EventHandler? PromptPresetsChanged;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPanel"/> class and loads presets from storage.
    /// </summary>
    public SettingsPanel()
    {
        // Load presets early so they're available before Loaded event
        _presets = AppSettings.LoadPresets();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the Loaded event to navigate to the initial settings page and select the first nav item.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Navigate to initial page
        NavigateTo("Models");

        // Select first item
        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    /// <summary>
    /// Handles navigation item invocation in the settings NavigationView.
    /// </summary>
    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Navigates the content frame to the settings page identified by the given tag.
    /// </summary>
    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "Models" => typeof(Settings.ModelsPage),
            "Personalization" => typeof(Settings.PersonalizationPage),
            "Prompt" => typeof(Settings.PromptPage),
            "UtilityAI" => typeof(Settings.UtilityAIPage),
            "Advanced" => typeof(Settings.AdvancedPage),
            "About" => typeof(Settings.AboutPage),
            _ => typeof(Settings.ModelsPage),
        };

        ContentFrame.Navigate(pageType, this, new EntranceNavigationTransitionInfo());
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Persists the theme setting and raises the <see cref="ThemeChanged"/> event.
    /// Called by sub-pages when the user changes the theme.
    /// </summary>
    internal void RaiseThemeChanged(string theme)
    {
        AppSettings.Theme = theme;
        ThemeChanged?.Invoke(this, theme);
    }

    /// <summary>
    /// Persists the system prompt setting and raises the <see cref="SystemPromptChanged"/> event.
    /// Called by sub-pages when the user modifies the system prompt.
    /// </summary>
    internal void RaiseSystemPromptChanged(string prompt)
    {
        AppSettings.SystemPrompt = prompt;
        SystemPromptChanged?.Invoke(this, prompt);
    }

    /// <summary>
    /// Persists the provider presets and raises the <see cref="PresetsChanged"/> event.
    /// Called by sub-pages when provider presets are modified.
    /// </summary>
    internal void RaisePresetsChanged()
    {
        AppSettings.SavePresets(_presets);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises the <see cref="PromptPresetsChanged"/> event.
    /// Called by sub-pages when prompt presets are modified.
    /// </summary>
    internal void RaisePromptPresetsChanged()
    {
        PromptPresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
