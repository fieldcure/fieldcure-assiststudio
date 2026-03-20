using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Tools;
using AssistStudio.Controls;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using System.Globalization;

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
    /// Maps each tool CollapsibleSection to its list of CheckBoxes for count updates.
    /// </summary>
    private readonly Dictionary<CollapsibleSection, List<CheckBox>> _toolSections = [];

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
    /// Handles tool checkbox checked/unchecked events and updates section subheaders.
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

        // Update the parent section's subheader count
        foreach (var (section, checkBoxes) in _toolSections)
        {
            if (checkBoxes.Contains(cb))
            {
                var selected = checkBoxes.Count(c => c.IsChecked == true);
                section.SubHeader = $"{selected}/{checkBoxes.Count}";
                break;
            }
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
    /// Populates the tools panel with checkboxes for each registered tool.
    /// </summary>
    private void PopulateToolsPanel(Profile profile)
    {
        ToolsPanel.Children.Clear();
        _toolSections.Clear();
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var hasAnyTool = false;

        // --- Built-in tools (exclude search_tools, shown in Extended section) ---
        var builtInTools = ToolRegistry.All.Where(t => t.Name != "search_tools").ToList();
        if (builtInTools.Count > 0)
        {
            hasAnyTool = true;
            var builtInPanel = new StackPanel { Spacing = 2 };
            var builtInCheckBoxes = new List<CheckBox>();
            foreach (var tool in builtInTools)
            {
                var localizedName = loader.GetString($"Tool_{tool.Name}");
                var cb = new CheckBox
                {
                    Content = string.IsNullOrEmpty(localizedName) ? tool.DisplayName : localizedName,
                    Tag = tool.Name,
                    IsChecked = profile.ToolNames.Contains(tool.Name),
                };
                cb.Checked += OnToolChecked;
                cb.Unchecked += OnToolChecked;
                builtInPanel.Children.Add(cb);
                builtInCheckBoxes.Add(cb);
            }
            var selectedCount = builtInCheckBoxes.Count(c => c.IsChecked == true);
            var builtInSection = new CollapsibleSection
            {
                Header = loader.GetString("Profiles_BuiltInTools"),
                SubHeader = $"{selectedCount}/{builtInTools.Count}",
                Body = builtInPanel,
                ContentSpacing = 4,
                IsExpanded = true,
            };
            _toolSections[builtInSection] = builtInCheckBoxes;
            ToolsPanel.Children.Add(builtInSection);
        }

        // --- Extended tools (Search Tools toggle) ---
        var extPanel = new StackPanel { Spacing = 4 };
        var searchToolsCb = new CheckBox
        {
            Content = loader.GetString("Profiles_SearchToolsName"),
            IsChecked = profile.UseSearchTools,
            MinWidth = 0,
        };
        extPanel.Children.Add(searchToolsCb);
        extPanel.Children.Add(new TextBlock
        {
            Text = loader.GetString("Profiles_SearchToolsDesc"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });
        var extSection = new CollapsibleSection
        {
            Header = loader.GetString("Profiles_ExtendedTools"),
            SubHeader = profile.UseSearchTools ? "1/1" : "0/1",
            Body = extPanel,
            ContentSpacing = 4,
            IsExpanded = true,
        };
        ToolsPanel.Children.Add(extSection);

        // --- MCP tools grouped by server ---
        var mcpSections = new List<CollapsibleSection>();
        var filterPlaceholder = loader.GetString("Profiles_FilterToolsPlaceholder");
        var mcpToolsByServer = App.McpRegistry.GetToolsByServer();
        foreach (var (serverName, serverTools) in mcpToolsByServer)
        {
            if (serverTools.Count == 0) continue;
            hasAnyTool = true;
            var toolsPanel = new StackPanel { Spacing = 2 };
            var mcpCheckBoxes = new List<CheckBox>();
            foreach (var tool in serverTools)
            {
                var cb = new CheckBox
                {
                    Content = HumanizeName(tool.Name),
                    Tag = tool.Name,
                    IsChecked = profile.ToolNames.Contains(tool.Name),
                };
                cb.Checked += OnToolChecked;
                cb.Unchecked += OnToolChecked;
                toolsPanel.Children.Add(cb);
                mcpCheckBoxes.Add(cb);
            }

            // Wrap with filter box + tools list
            var filterBox = new AutoSuggestBox
            {
                PlaceholderText = filterPlaceholder,
                QueryIcon = new SymbolIcon(Symbol.Find),
                Margin = new Thickness(0, 0, 0, 4),
            };
            filterBox.TextChanged += (sender, _) =>
            {
                var query = sender.Text.Trim();
                foreach (var child in toolsPanel.Children)
                {
                    if (child is CheckBox cb)
                    {
                        cb.Visibility = string.IsNullOrEmpty(query)
                            || cb.Content?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                    }
                }
            };

            var bodyPanel = new StackPanel { Spacing = 4 };
            bodyPanel.Children.Add(filterBox);
            bodyPanel.Children.Add(toolsPanel);

            var mcpSelectedCount = mcpCheckBoxes.Count(c => c.IsChecked == true);
            var section = new CollapsibleSection
            {
                Header = serverName,
                SubHeader = $"{mcpSelectedCount}/{serverTools.Count}",
                Body = bodyPanel,
                ContentSpacing = 4,
                IsExpanded = false,
                Visibility = profile.UseSearchTools ? Visibility.Collapsed : Visibility.Visible,
            };
            _toolSections[section] = mcpCheckBoxes;
            mcpSections.Add(section);
            ToolsPanel.Children.Add(section);
        }

        // Toggle MCP sections visibility when Search Tools changes
        searchToolsCb.Checked += (_, _) =>
        {
            profile.UseSearchTools = true;
            extSection.SubHeader = "1/1";
            foreach (var s in mcpSections) s.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine($"[SearchTools] Checked: profile={profile.Name}, UseSearchTools={profile.UseSearchTools}, IsBuiltIn={profile.IsBuiltIn}");
            SaveAll();
            // Verify save round-trip
            var reloaded = AppSettings.LoadProfiles().FirstOrDefault(p => p.Name == profile.Name);
            System.Diagnostics.Debug.WriteLine($"[SearchTools] After reload: UseSearchTools={reloaded?.UseSearchTools}");
        };
        searchToolsCb.Unchecked += (_, _) =>
        {
            profile.UseSearchTools = false;
            extSection.SubHeader = "0/1";
            foreach (var s in mcpSections) s.Visibility = Visibility.Visible;
            SaveAll();
        };

        if (!hasAnyTool)
        {
            ToolsPanel.Children.Add(new TextBlock
            {
                Text = loader.GetString("Profiles_NoToolsRegistered"),
                Opacity = 0.5,
                FontSize = 12,
            });
        }
    }

    /// <summary>
    /// Converts a snake_case or kebab-case tool name to a human-readable Title Case string.
    /// Example: "create_document" → "Create Document", "list-repos" → "List Repos".
    /// </summary>
    private static string HumanizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Remove server prefix if present (e.g., "github/list_repos" → "list_repos")
        var slashIndex = name.IndexOf('/');
        if (slashIndex >= 0 && slashIndex < name.Length - 1)
            name = name[(slashIndex + 1)..];
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
            name.Replace('_', ' ').Replace('-', ' '));
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
