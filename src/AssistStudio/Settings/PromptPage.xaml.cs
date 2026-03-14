using FieldCure.AssistStudio.Models;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing system prompt presets, allowing users to create, edit,
/// delete, and select prompt presets that define the AI assistant's behavior.
/// </summary>
public sealed partial class PromptPage : Page
{
    #region Fields

    /// <summary>
    /// Reference to the parent settings panel for raising change events.
    /// </summary>
    private SettingsPanel? _settings;

    /// <summary>
    /// The list of all prompt presets (built-in and custom).
    /// </summary>
    private List<PromptPreset> _presets = [];

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptPage"/> class.
    /// </summary>
    public PromptPage()
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
        _presets = AppSettings.LoadPromptPresets();
        PresetListView.ItemsSource = _presets;

        // Select active preset
        var activeName = AppSettings.ActivePromptPreset;
        var activeIndex = _presets.FindIndex(p => p.Name == activeName);
        if (activeIndex >= 0)
        {
            PresetListView.SelectedIndex = activeIndex;
        }
        else if (_presets.Count > 0)
        {
            PresetListView.SelectedIndex = 0;
        }
        _suppressEvents = false;

        LoadSelectedPreset();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles preset list selection changes to load the selected preset into the editor.
    /// </summary>
    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        LoadSelectedPreset();
        SaveActivePreset();
    }

    /// <summary>
    /// Handles text changes in the preset name or prompt editor and persists updates.
    /// </summary>
    private void OnEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (PresetListView.SelectedItem is not PromptPreset preset) return;

        if (!preset.IsBuiltIn)
        {
            preset.Name = PresetNameBox.Text.Trim();
        }
        preset.Text = SystemPromptBox.Text;

        // Refresh list display
        _suppressEvents = true;
        var idx = PresetListView.SelectedIndex;
        PresetListView.ItemsSource = null;
        PresetListView.ItemsSource = _presets;
        PresetListView.SelectedIndex = idx;
        _suppressEvents = false;

        SaveAll();
    }

    /// <summary>
    /// Handles the add preset button click to create a new custom preset.
    /// </summary>
    private void OnAddPresetClicked(object sender, RoutedEventArgs e)
    {
        var newPreset = new PromptPreset
        {
            Name = "New Preset",
            Text = "",
            IsBuiltIn = false
        };
        _presets.Add(newPreset);

        _suppressEvents = true;
        PresetListView.ItemsSource = null;
        PresetListView.ItemsSource = _presets;
        PresetListView.SelectedIndex = _presets.Count - 1;
        _suppressEvents = false;

        LoadSelectedPreset();
        SaveAll();
        PresetNameBox.Focus(FocusState.Programmatic);
        PresetNameBox.SelectAll();
    }

    /// <summary>
    /// Handles the delete preset button click to remove a custom preset.
    /// </summary>
    private void OnDeletePresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not PromptPreset preset) return;
        if (preset.IsBuiltIn) return;

        var idx = _presets.IndexOf(preset);
        _presets.Remove(preset);

        _suppressEvents = true;
        PresetListView.ItemsSource = null;
        PresetListView.ItemsSource = _presets;
        PresetListView.SelectedIndex = Math.Min(idx, _presets.Count - 1);
        _suppressEvents = false;

        LoadSelectedPreset();
        SaveAll();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads the currently selected preset's data into the name and prompt editor fields.
    /// </summary>
    private void LoadSelectedPreset()
    {
        if (PresetListView.SelectedItem is not PromptPreset preset) return;

        _suppressEvents = true;
        PresetNameBox.Text = preset.Name;
        PresetNameBox.IsEnabled = !preset.IsBuiltIn;
        SystemPromptBox.Text = preset.Text;
        _suppressEvents = false;
    }

    /// <summary>
    /// Saves all custom presets and notifies the settings panel of system prompt and preset changes.
    /// </summary>
    private void SaveAll()
    {
        AppSettings.SaveCustomPromptPresets(_presets);

        // Update current system prompt
        if (PresetListView.SelectedItem is PromptPreset selected)
        {
            AppSettings.ActivePromptPreset = selected.Name;
            _settings?.RaiseSystemPromptChanged(selected.Text);
            _settings?.RaisePromptPresetsChanged();
        }
    }

    /// <summary>
    /// Persists the currently selected preset as the active prompt preset.
    /// </summary>
    private void SaveActivePreset()
    {
        if (PresetListView.SelectedItem is PromptPreset selected)
        {
            AppSettings.ActivePromptPreset = selected.Name;
            AppSettings.SystemPrompt = selected.Text;
            _settings?.RaiseSystemPromptChanged(selected.Text);
        }
    }

    #endregion
}

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/>: <c>true</c> maps to Collapsed, <c>false</c> maps to Visible.
/// Used to hide delete buttons on built-in presets.
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
