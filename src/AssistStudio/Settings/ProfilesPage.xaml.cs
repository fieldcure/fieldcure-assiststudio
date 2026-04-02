using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
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

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfilesPage"/> class.
    /// </summary>
    public ProfilesPage()
    {
        InitializeComponent();

        NavigationCacheMode = NavigationCacheMode.Enabled;
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

        // Set reset button tooltip (localized)
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        ToolTipService.SetToolTip(ResetProfileButton, loader.GetString("Profiles_ResetProfile"));
        ToolTipService.SetPlacement(ResetProfileButton, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);

        LoadSelectedProfile();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        UnsubscribeToolsPanelEvents();
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
            var desired = ProfileNameBox.Text.Trim();
            profile.Name = MakeUniqueName(desired, excludeName: profile.Name);
        }
        profile.SystemPrompt = SystemPromptBox.Text;

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
            Name = MakeUniqueName(name),
            SystemPrompt = "",
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
    private void OnResetProfileClicked(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not Profile profile) return;
        if (!profile.IsBuiltIn) return;

        // Find the static default for this built-in profile
        var defaults = AppSettings.GetBuiltInDefaults(profile.Name);
        if (defaults is null) return;

        profile.SystemPrompt = defaults.SystemPrompt;
        profile.ToolNames = [.. defaults.ToolNames];
        profile.UseSearchTools = defaults.UseSearchTools;
        profile.EnabledServers = [.. defaults.EnabledServers];
        profile.PreferredProviderType = defaults.PreferredProviderType;
        profile.PreferredModelId = defaults.PreferredModelId;

        LoadSelectedProfile();
        SaveAll();
    }

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
    /// Unsubscribes Checked/Unchecked handlers from all CheckBoxes in the tools panel
    /// to prevent leaked subscriptions when the panel is rebuilt or the page is left.
    /// </summary>
    private void UnsubscribeToolsPanelEvents()
    {
        foreach (var child in ToolsPanel.Children)
        {
            var cb = child as CheckBox
                ?? (child as StackPanel)?.Children.OfType<CheckBox>().FirstOrDefault();
            if (cb is null) continue;

            cb.Checked -= OnServerChecked;
            cb.Unchecked -= OnServerChecked;
        }
    }

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

        // Show reset for built-in, delete for custom
        ResetProfileButton.Visibility = profile.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
        DeleteProfileButton.Visibility = profile.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        SystemPromptBox.Text = profile.SystemPrompt;

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
            AppSettings.SystemPrompt = selected.SystemPrompt;
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
        UnsubscribeToolsPanelEvents();
        ToolsPanel.Children.Clear();
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();

        // Build server list: always include built-in servers, plus user-configured servers
        var filesystemId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var userServers = App.McpRegistry.Connections
            .Where(c => !c.Config.IsBuiltIn)
            .ToList();
        var hasServers = userServers.Count > 0;
        var enabledSet = profile.EnabledServers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // --- Built-in servers (order: Essentials → Filesystem → RAG → Memory → Outbox → Runner) ---

        // Essentials
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
                Content = loader.GetString("Profiles_EssentialsLabel") is { Length: > 0 } l
                    ? l : BuiltInServerHelper.EssentialsDisplayName,
                Tag = $"builtin_{BuiltInServerHelper.EssentialsKey}",
                IsChecked = profile.EnabledServers.Contains($"builtin_{BuiltInServerHelper.EssentialsKey}"),
                MinWidth = 0,
            };
            cb.Checked += OnServerChecked;
            cb.Unchecked += OnServerChecked;
            row.Children.Add(cb);

            ToolsPanel.Children.Add(row);

            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_EssentialsHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(28, 0, 0, 4),
            });
        }

        // Workspace (Filesystem)
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

            var wsHint = enabledSet.Contains(filesystemId)
                ? loader.GetString("Profiles_WorkspaceSuppressHint")
                : loader.GetString("Profiles_WorkspaceHint");
            ToolsPanel.Children.Add(new TextBlock
            {
                Text = wsHint,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(28, 0, 0, 4),
            });
        }

        // Knowledge Archive (RAG)
        {
            var ragId = $"builtin_{BuiltInServerHelper.RagKey}";
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
                Content = BuiltInServerHelper.RagDisplayName,
                Tag = ragId,
                IsChecked = enabledSet.Contains(ragId),
                MinWidth = 0,
            };
            cb.Checked += OnServerChecked;
            cb.Unchecked += OnServerChecked;
            row.Children.Add(cb);

            ToolsPanel.Children.Add(row);

            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_KnowledgeHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(28, 0, 0, 4),
            });
        }

        // Memory — now part of Essentials MCP server (no separate toggle)

        // Outbox
        {
            var outboxId = $"builtin_{BuiltInServerHelper.OutboxKey}";
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
                Content = BuiltInServerHelper.OutboxDisplayName,
                Tag = outboxId,
                IsChecked = enabledSet.Contains(outboxId),
                MinWidth = 0,
            };
            cb.Checked += OnServerChecked;
            cb.Unchecked += OnServerChecked;
            row.Children.Add(cb);

            ToolsPanel.Children.Add(row);

            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_OutboxHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(28, 0, 0, 4),
            });
        }

        // Runner
        {
            var runnerId = $"builtin_{BuiltInServerHelper.RunnerKey}";
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
                Content = BuiltInServerHelper.RunnerDisplayName,
                Tag = runnerId,
                IsChecked = enabledSet.Contains(runnerId),
                MinWidth = 0,
            };
            cb.Checked += OnServerChecked;
            cb.Unchecked += OnServerChecked;
            row.Children.Add(cb);

            ToolsPanel.Children.Add(row);

            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_RunnerHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5,
                Margin = new Thickness(28, 0, 0, 4),
            });
        }

        // Separator between built-in and user-configured servers
        if (hasServers)
        {
            ToolsPanel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Height = 1,
                Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Opacity = 0.3,
                Margin = new Thickness(0, 4, 0, 4),
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

    }

    /// <summary>
    /// Persists the currently selected profile as the active profile.
    /// </summary>
    private void SaveActiveProfile()
    {
        if (ProfileCombo.SelectedItem is Profile selected)
        {
            AppSettings.ActiveProfile = selected.Name;
            AppSettings.SystemPrompt = selected.SystemPrompt;
        }
    }

    /// <summary>
    /// Returns a unique profile name by appending (2), (3), etc. if the name
    /// collides with built-in profiles or other existing profiles.
    /// </summary>
    private string MakeUniqueName(string desiredName, string? excludeName = null)
    {
        var existing = _profiles
            .Where(p => excludeName is null || !p.Name.Equals(excludeName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(desiredName))
            return desiredName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{desiredName} ({i})";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    #endregion
}
