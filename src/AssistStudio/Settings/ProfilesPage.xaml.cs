using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using AssistStudio.Tools;
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
public sealed partial class ProfilesPage : Page
{
    #region Fields

    /// <summary>
    /// The list of all profiles (built-in and custom).
    /// </summary>
    private List<Profile> _profiles = [];

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    /// <summary>
    /// Built-in tool checkboxes for suppress logic (file tools grayed out when Workspace active).
    /// </summary>
    private readonly List<CheckBox> _builtInToolCheckBoxes = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilesPage"/> class.
    /// </summary>
    public ProfilesPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _suppressEvents = true;
        _profiles = AppSettings.LoadProfiles();
        ProfileCombo.ItemsSource = _profiles;

        // Select active profile
        var activeName = AppSettings.ActiveProfile;
        var activeIndex = _profiles.FindIndex(p => p.Name == activeName);
        if (activeIndex >= 0)
        {
            ProfileCombo.SelectedIndex = activeIndex;
        }
        else if (_profiles.Count > 0)
        {
            ProfileCombo.SelectedIndex = 0;
        }
        _suppressEvents = false;

        LoadSelectedProfile();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles profile combo box selection changes to load the selected profile into the editor.
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
        if (ProfileCombo.SelectedItem is not Profile profile) return;

        if (!profile.IsBuiltIn)
        {
            profile.Name = ProfileNameBox.Text.Trim();
        }
        profile.Text = SystemPromptBox.Text;

        // Refresh combo display
        _suppressEvents = true;
        var idx = ProfileCombo.SelectedIndex;
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = _profiles;
        ProfileCombo.SelectedIndex = idx;
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
        var dialog = new ThemedContentDialog
        {
            Title = loader.GetString("Profiles_NewProfileDialog"),
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
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = _profiles;
        ProfileCombo.SelectedIndex = _profiles.Count - 1;
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
        if (ProfileCombo.SelectedItem is not Profile profile) return;

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
        if (ProfileCombo.SelectedItem is not Profile profile) return;

        profile.PreferredModelId = ModelCombo.SelectedItem as string;
        SaveAll();
    }

    /// <summary>
    /// Handles built-in tool checkbox checked/unchecked events.
    /// </summary>
    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ProfileCombo.SelectedItem is not Profile profile) return;
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
    /// Handles server checkbox checked/unchecked events with Workspace suppress logic.
    /// </summary>
    private void OnServerChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (ProfileCombo.SelectedItem is not Profile profile) return;
        if (sender is not CheckBox cb || cb.Tag is not string serverId) return;

        if (cb.IsChecked == true)
        {
            if (!profile.EnabledServers.Contains(serverId))
                profile.EnabledServers.Add(serverId);
        }
        else
        {
            profile.EnabledServers.Remove(serverId);
        }

        // Workspace toggled: rebuild panel to update suppress state and hints
        if (serverId == $"builtin_{BuiltInServerHelper.FilesystemKey}")
        {
            SaveAll();
            PopulateToolsPanel(profile);
            return;
        }

        SaveAll();
    }

    /// <summary>
    /// Handles the delete profile button click to remove the currently selected custom profile.
    /// </summary>
    private void OnDeleteProfileClicked(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not Profile profile) return;
        if (profile.IsBuiltIn) return;

        var idx = _profiles.IndexOf(profile);
        _profiles.Remove(profile);

        _suppressEvents = true;
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = _profiles;
        ProfileCombo.SelectedIndex = Math.Min(idx, _profiles.Count - 1);
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
        if (ProfileCombo.SelectedItem is not Profile profile) return;

        _suppressEvents = true;

        // Show name field only for custom profiles
        ProfileNameBox.Visibility = profile.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        ProfileNameBox.Text = profile.Name;

        // Show delete button only for custom profiles
        DeleteProfileButton.Visibility = profile.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

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
        if (ProfileCombo.SelectedItem is Profile selected)
        {
            AppSettings.ActiveProfile = selected.Name;
            AppSettings.SystemPrompt = selected.Text;
            AppSettings.NotifyProfilesChanged();
            System.Diagnostics.Debug.WriteLine($"[SaveAll] NotifyProfilesChanged fired for {selected.Name}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[SaveAll] No profile selected, NotifyProfilesChanged NOT fired");
        }
    }

    /// <summary>
    /// Provider type key to display name mappings.
    /// Index 0 is the "Any provider" entry with null key.
    /// </summary>
    private static readonly KeyValuePair<string?, string>[] ProviderDisplayNames =
    [
        new(null, "Any provider"),
        new("Claude", "Anthropic Claude"),
        new("OpenAI", "OpenAI"),
        new("Gemini", "Google Gemini"),
        new("Groq", "Groq"),
        new("Ollama", "Ollama"),
        new("Mock", "Mock"),
    ];

    /// <summary>
    /// Populates the provider combo box with available provider types.
    /// The first item uses a localized "Any provider" label from resources.
    /// </summary>
    private void PopulateProviderCombo()
    {
        ProviderCombo.Items.Clear();
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var anyProviderLabel = loader.GetString("Profiles_AnyProvider");

        for (var i = 0; i < ProviderDisplayNames.Length; i++)
        {
            var label = i == 0 && !string.IsNullOrEmpty(anyProviderLabel)
                ? anyProviderLabel
                : ProviderDisplayNames[i].Value;
            ProviderCombo.Items.Add(label);
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
    /// Populates the tools panel with built-in tool checkboxes and server toggle checkboxes.
    /// </summary>
    private void PopulateToolsPanel(Profile profile)
    {
        ToolsPanel.Children.Clear();
        _builtInToolCheckBoxes.Clear();
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();

        // --- Built-in tools (exclude search_tools) ---
        var builtInTools = ToolRegistry.All.Where(t => t.Name != "search_tools").ToList();
        foreach (var tool in builtInTools)
        {
            var localizedName = loader.GetString($"Tool_{tool.Name}");
            var cb = new CheckBox
            {
                Content = string.IsNullOrEmpty(localizedName) ? tool.DisplayName : localizedName,
                Tag = tool.Name,
                IsChecked = profile.ToolNames.Contains(tool.Name),
                MinWidth = 0,
            };
            cb.Checked += OnToolChecked;
            cb.Unchecked += OnToolChecked;
            ToolsPanel.Children.Add(cb);
            _builtInToolCheckBoxes.Add(cb);
        }

        // --- Servers ---
        // Build server list: always include Workspace (filesystem), plus user-configured servers
        var filesystemId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var userServers = App.McpRegistry.Connections
            .Where(c => !c.Config.Id.StartsWith($"builtin_{BuiltInServerHelper.FilesystemKey}", StringComparison.Ordinal))
            .ToList();
        var hasServers = userServers.Count > 0;

        // Separator (always shown — filesystem is always present)
        ToolsPanel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Height = 1,
            Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
            Opacity = 0.3,
            Margin = new Thickness(0, 4, 0, 4),
        });

        var enabledSet = profile.EnabledServers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Workspace (filesystem) — always shown, not dependent on registry
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 6, Height = 6,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(dot);

            var cb = new CheckBox
            {
                Content = BuiltInServerHelper.FilesystemDisplayName,
                Tag = filesystemId,
                IsChecked = enabledSet.Contains(filesystemId),
                MinWidth = 0,
            };
            cb.Checked += OnServerChecked;
            cb.Unchecked += OnServerChecked;
            row.Children.Add(cb);

            ToolsPanel.Children.Add(row);

            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_WorkspaceHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(28, 0, 0, 4),
            });
        }

        // User-configured MCP servers
        foreach (var conn in userServers)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 6, Height = 6,
                Fill = conn.IsConnected
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(dot);

            var cb = new CheckBox
            {
                Content = conn.Config.Name,
                Tag = conn.Config.Id,
                IsChecked = enabledSet.Contains(conn.Config.Id),
                MinWidth = 0,
            };
            cb.Checked += OnServerChecked;
            cb.Unchecked += OnServerChecked;
            row.Children.Add(cb);

            ToolsPanel.Children.Add(row);
        }

        // Info hints
        if (enabledSet.Contains(filesystemId))
        {
            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_WorkspaceSuppressHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        ToolsPanel.Children.Add(new TextBlock
        {
            Text = loader.GetString("Profiles_ToolsDefaultHint"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.5,
            Margin = new Thickness(0, 4, 0, 0),
        });
        if (hasServers)
        {
            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_ServerToolsHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        // Apply suppress if Workspace (filesystem) is currently enabled
        var fsId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        if (profile.EnabledServers.Contains(fsId))
        {
            ApplyFilesystemSuppress(profile, suppress: true);
        }
    }

    /// <summary>
    /// Suppresses or restores file tool checkboxes when the Workspace (filesystem) server is toggled.
    /// When suppressed, read_file/write_file/search_files are unchecked and grayed out.
    /// </summary>
    private void ApplyFilesystemSuppress(Profile profile, bool suppress)
    {
        _suppressEvents = true;
        foreach (var cb in _builtInToolCheckBoxes)
        {
            if (cb.Tag is string toolName && BuiltInServerHelper.SuppressedBuiltInToolNames.Contains(toolName))
            {
                if (suppress)
                {
                    cb.IsChecked = false;
                    cb.IsEnabled = false;
                }
                else
                {
                    cb.IsEnabled = true;
                    cb.IsChecked = profile.ToolNames.Contains(toolName);
                }
            }
        }
        _suppressEvents = false;
    }

    /// <summary>
    /// Persists the currently selected profile as the active profile.
    /// </summary>
    private void SaveActiveProfile()
    {
        if (ProfileCombo.SelectedItem is Profile selected)
        {
            AppSettings.ActiveProfile = selected.Name;
            AppSettings.SystemPrompt = selected.Text;
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
