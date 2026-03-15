using AssistStudio.Modules.Helpers;
using AssistStudio.Modules.Tools;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing profiles, allowing users to create, edit,
/// delete, and select profiles that define the AI assistant's behavior.
/// </summary>
public sealed partial class ProfilePage : Page
{
    #region Fields

    /// <summary>
    /// Reference to the parent settings panel for raising change events.
    /// </summary>
    private SettingsPanel? _settings;

    /// <summary>
    /// The list of all profiles (built-in and custom).
    /// </summary>
    private List<Profile> _profiles = [];

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilePage"/> class.
    /// </summary>
    public ProfilePage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SettingsPanel settings)
        {
            _settings = settings;
        }

        _suppressEvents = true;
        _profiles = AppSettings.LoadProfiles();
        ProfileListView.ItemsSource = _profiles;

        // Select active profile
        var activeName = AppSettings.ActiveProfile;
        var activeIndex = _profiles.FindIndex(p => p.Name == activeName);
        if (activeIndex >= 0)
        {
            ProfileListView.SelectedIndex = activeIndex;
        }
        else if (_profiles.Count > 0)
        {
            ProfileListView.SelectedIndex = 0;
        }
        _suppressEvents = false;

        LoadSelectedProfile();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles profile list selection changes to load the selected profile into the editor.
    /// </summary>
    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        LoadSelectedProfile();
        SaveActiveProfile();
    }

    /// <summary>
    /// Handles text changes in the profile name or prompt editor and persists updates.
    /// </summary>
    private void OnEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ProfileListView.SelectedItem is not Profile profile) return;

        if (!profile.IsBuiltIn)
        {
            profile.Name = ProfileNameBox.Text.Trim();
        }
        profile.Text = SystemPromptBox.Text;

        // Refresh list display
        _suppressEvents = true;
        var idx = ProfileListView.SelectedIndex;
        ProfileListView.ItemsSource = null;
        ProfileListView.ItemsSource = _profiles;
        ProfileListView.SelectedIndex = idx;
        _suppressEvents = false;

        SaveAll();
    }

    /// <summary>
    /// Handles the add profile button click by showing a dialog to name the new profile.
    /// </summary>
    private async void OnAddProfileClicked(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            PlaceholderText = "Profile name",
            Text = "New Profile",
            SelectionStart = 0,
            SelectionLength = "New Profile".Length
        };

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var dialog = new ContentDialog
        {
            Title = loader.GetString("Profile_NewProfileDialog"),
            Content = input,
            PrimaryButtonText = loader.GetString("Dialog_OK"),
            CloseButtonText = loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var name = input.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var newProfile = new Profile
        {
            Name = name,
            Text = "",
            IsBuiltIn = false
        };
        _profiles.Add(newProfile);

        _suppressEvents = true;
        ProfileListView.ItemsSource = null;
        ProfileListView.ItemsSource = _profiles;
        ProfileListView.SelectedIndex = _profiles.Count - 1;
        _suppressEvents = false;

        LoadSelectedProfile();
        SaveAll();
    }

    /// <summary>
    /// Handles preferred provider combo box selection changes.
    /// </summary>
    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ProfileListView.SelectedItem is not Profile profile) return;

        var providerType = ProviderCombo.SelectedIndex <= 0
            ? null
            : ProviderDisplayNames[ProviderCombo.SelectedIndex].Key;

        profile.PreferredProviderType = providerType;

        _suppressEvents = true;
        PopulateModelCombo(providerType);
        profile.PreferredModelId = null;
        _suppressEvents = false;

        SaveAll();
    }

    /// <summary>
    /// Handles preferred model combo box selection changes.
    /// </summary>
    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ProfileListView.SelectedItem is not Profile profile) return;

        profile.PreferredModelId = ModelCombo.SelectedItem as string;
        SaveAll();
    }

    /// <summary>
    /// Handles tool checkbox checked/unchecked events.
    /// </summary>
    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ProfileListView.SelectedItem is not Profile profile) return;
        if (sender is not CheckBox cb || cb.Tag is not string toolName) return;

        if (cb.IsChecked == true)
        {
            if (!profile.ToolNames.Contains(toolName))
                profile.ToolNames.Add(toolName);
        }
        else
        {
            profile.ToolNames.Remove(toolName);
        }

        SaveAll();
    }

    /// <summary>
    /// Handles the delete profile button click to remove a custom profile.
    /// </summary>
    private void OnDeleteProfileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not Profile profile) return;
        if (profile.IsBuiltIn) return;

        var idx = _profiles.IndexOf(profile);
        _profiles.Remove(profile);

        _suppressEvents = true;
        ProfileListView.ItemsSource = null;
        ProfileListView.ItemsSource = _profiles;
        ProfileListView.SelectedIndex = Math.Min(idx, _profiles.Count - 1);
        _suppressEvents = false;

        LoadSelectedProfile();
        SaveAll();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads the currently selected profile's data into all editor fields.
    /// </summary>
    private void LoadSelectedProfile()
    {
        if (ProfileListView.SelectedItem is not Profile profile) return;

        _suppressEvents = true;
        ProfileNameBox.Text = profile.Name;
        ProfileNameBox.IsEnabled = !profile.IsBuiltIn;
        SystemPromptBox.Text = profile.Text;

        // Provider
        PopulateProviderCombo();
        SelectProviderItem(profile.PreferredProviderType);

        // Model
        PopulateModelCombo(profile.PreferredProviderType);
        SelectModelItem(profile.PreferredModelId);

        // Tools
        PopulateToolsPanel(profile);

        _suppressEvents = false;
    }

    /// <summary>
    /// Saves all custom profiles and notifies the settings panel of system prompt and profile changes.
    /// </summary>
    private void SaveAll()
    {
        AppSettings.SaveCustomProfiles(_profiles);

        // Update current system prompt
        if (ProfileListView.SelectedItem is Profile selected)
        {
            AppSettings.ActiveProfile = selected.Name;
            _settings?.RaiseSystemPromptChanged(selected.Text);
            _settings?.RaiseProfilesChanged();
        }
    }

    /// <summary>
    /// Provider type key to display name mappings.
    /// Index 0 is the "(None)" entry with null key.
    /// </summary>
    private static readonly KeyValuePair<string?, string>[] ProviderDisplayNames =
    [
        new(null, "(None)"),
        new("Claude", "Anthropic Claude"),
        new("OpenAI", "OpenAI"),
        new("Gemini", "Google Gemini"),
        new("Groq", "Groq"),
        new("Ollama", "Ollama"),
        new("Mock", "Mock"),
    ];

    /// <summary>
    /// Populates the provider combo box with available provider types.
    /// </summary>
    private void PopulateProviderCombo()
    {
        ProviderCombo.Items.Clear();
        foreach (var kvp in ProviderDisplayNames)
        {
            ProviderCombo.Items.Add(kvp.Value);
        }
    }

    /// <summary>
    /// Selects the matching provider item in the combo box.
    /// </summary>
    private void SelectProviderItem(string? providerType)
    {
        if (providerType is null)
        {
            ProviderCombo.SelectedIndex = 0;
            return;
        }

        for (var i = 0; i < ProviderDisplayNames.Length; i++)
        {
            if (ProviderDisplayNames[i].Key == providerType)
            {
                ProviderCombo.SelectedIndex = i;
                return;
            }
        }
        ProviderCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Populates the model combo box based on the selected provider type.
    /// </summary>
    private void PopulateModelCombo(string? providerType)
    {
        ModelCombo.Items.Clear();

        if (providerType is null)
        {
            ModelCombo.IsEnabled = false;
            return;
        }

        var models = AppSettings.GetCachedModels(providerType);
        if (models is not null)
        {
            foreach (var m in models)
                ModelCombo.Items.Add(m);
        }

        ModelCombo.IsEnabled = ModelCombo.Items.Count > 0;
    }

    /// <summary>
    /// Selects the matching model item in the combo box.
    /// </summary>
    private void SelectModelItem(string? modelId)
    {
        if (modelId is null || ModelCombo.Items.Count == 0)
        {
            if (ModelCombo.Items.Count > 0)
                ModelCombo.SelectedIndex = 0;
            return;
        }

        for (var i = 0; i < ModelCombo.Items.Count; i++)
        {
            if (ModelCombo.Items[i] is string s && s == modelId)
            {
                ModelCombo.SelectedIndex = i;
                return;
            }
        }
        ModelCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Populates the tools panel with checkboxes for each registered tool.
    /// </summary>
    private void PopulateToolsPanel(Profile profile)
    {
        ToolsPanel.Children.Clear();
        var tools = ToolRegistry.All;

        foreach (var tool in tools)
        {
            var cb = new CheckBox
            {
                Content = tool.Name,
                Tag = tool.Name,
                IsChecked = profile.ToolNames.Contains(tool.Name),
            };
            cb.Checked += OnToolChecked;
            cb.Unchecked += OnToolChecked;
            ToolsPanel.Children.Add(cb);
        }

        if (tools.Count == 0)
        {
            ToolsPanel.Children.Add(new TextBlock
            {
                Text = "No tools registered.",
                Opacity = 0.5,
                FontSize = 12,
            });
        }
    }

    /// <summary>
    /// Persists the currently selected profile as the active profile.
    /// </summary>
    private void SaveActiveProfile()
    {
        if (ProfileListView.SelectedItem is Profile selected)
        {
            AppSettings.ActiveProfile = selected.Name;
            AppSettings.SystemPrompt = selected.Text;
            _settings?.RaiseSystemPromptChanged(selected.Text);
        }
    }

    #endregion
}

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/>: <c>true</c> maps to Collapsed, <c>false</c> maps to Visible.
/// Used to hide delete buttons on built-in profiles.
/// </summary>
public partial class InverseBoolToVisibilityConverter : IValueConverter
{
    #region Public Methods

    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();

    #endregion
}
