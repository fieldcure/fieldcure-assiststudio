using FieldCure.AssistStudio.Models;
using AssistStudio.Modules.Helpers;
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
    /// Handles the add profile button click to create a new custom profile.
    /// </summary>
    private void OnAddProfileClicked(object sender, RoutedEventArgs e)
    {
        var newProfile = new Profile
        {
            Name = "New Profile",
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
        ProfileNameBox.Focus(FocusState.Programmatic);
        ProfileNameBox.SelectAll();
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
    /// Loads the currently selected profile's data into the name and prompt editor fields.
    /// </summary>
    private void LoadSelectedProfile()
    {
        if (ProfileListView.SelectedItem is not Profile profile) return;

        _suppressEvents = true;
        ProfileNameBox.Text = profile.Name;
        ProfileNameBox.IsEnabled = !profile.IsBuiltIn;
        SystemPromptBox.Text = profile.Text;
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
public class InverseBoolToVisibilityConverter : IValueConverter
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
