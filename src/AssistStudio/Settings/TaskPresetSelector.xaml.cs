using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace AssistStudio.Settings;

/// <summary>
/// Reusable control for selecting a provider source (Inherit / Specific) and preset
/// for an auxiliary task (title, summary, sub-agent).
/// </summary>
public sealed partial class TaskPresetSelector : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Source"/> dependency property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(AuxiliaryTaskSource), typeof(TaskPresetSelector),
            new PropertyMetadata(AuxiliaryTaskSource.Inherit));

    /// <summary>Identifies the <see cref="PresetName"/> dependency property.</summary>
    public static readonly DependencyProperty PresetNameProperty =
        DependencyProperty.Register(nameof(PresetName), typeof(string), typeof(TaskPresetSelector),
            new PropertyMetadata(""));

    #endregion

    #region Fields

    private bool _suppressEvents;

    #endregion

    #region Events

    /// <summary>Raised when the user changes the source or preset.</summary>
    public event EventHandler? SettingsChanged;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskPresetSelector"/> class.
    /// </summary>
    public TaskPresetSelector()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    /// <summary>Current source mode.</summary>
    public AuxiliaryTaskSource Source
    {
        get => (AuxiliaryTaskSource)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Selected preset name when <see cref="Source"/> is <see cref="AuxiliaryTaskSource.Specific"/>.</summary>
    public string PresetName
    {
        get => (string)GetValue(PresetNameProperty);
        set => SetValue(PresetNameProperty, value);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads the current state from settings and populates the preset combo.
    /// Call during <c>OnNavigatedTo</c>.
    /// </summary>
    public void Load(AuxiliaryTaskSource source, string presetName)
    {
        _suppressEvents = true;

        Source = source;
        PresetName = presetName;

        SourceRadio.SelectedIndex = source == AuxiliaryTaskSource.Specific ? 1 : 0;
        PresetCombo.Visibility = source == AuxiliaryTaskSource.Specific ? Visibility.Visible : Visibility.Collapsed;

        PopulatePresetCombo(presetName);

        _suppressEvents = false;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates the preset combo box, selecting the given preset name.
    /// </summary>
    private void PopulatePresetCombo(string selectedName)
    {
        PresetCombo.Items.Clear();

        var items = AppSettings.BuildOrderedPresetItems();
        var selectedIndex = -1;

        foreach (var obj in items)
        {
            if (obj is ProviderPreset preset)
            {
                // Skip Mock — cannot generate titles, summaries, or run sub-agents
                if (preset.ProviderType == "Mock") continue;

                // Skip cloud providers without an API key
                if (preset.RequiresApiKey && string.IsNullOrEmpty(preset.ApiKey)) continue;

                PresetCombo.Items.Add(preset.Name);
                if (preset.Name == selectedName)
                    selectedIndex = PresetCombo.Items.Count - 1;
            }
            else if (obj is "-")
            {
                var border = (Border)XamlReader.Load(
                    """
                    <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            Height="1" HorizontalAlignment="Stretch"
                            Background="{ThemeResource DividerStrokeColorDefaultBrush}" />
                    """);
                PresetCombo.Items.Add(new ComboBoxItem
                {
                    IsEnabled = false,
                    IsHitTestVisible = false,
                    MinHeight = 0,
                    Height = 9,
                    Padding = new Thickness(0),
                    Content = border,
                });
            }
        }

        if (selectedIndex >= 0)
            PresetCombo.SelectedIndex = selectedIndex;
        else if (PresetCombo.Items.Count > 0)
            PresetCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Parses a source tag string into an <see cref="AuxiliaryTaskSource"/> enum value.
    /// </summary>
    private static AuxiliaryTaskSource ParseSource(string tag)
        => tag == "Specific" ? AuxiliaryTaskSource.Specific : AuxiliaryTaskSource.Inherit;

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles source radio button selection change.
    /// </summary>
    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (SourceRadio.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            Source = ParseSource(tag);
            PresetCombo.Visibility = Source == AuxiliaryTaskSource.Specific
                ? Visibility.Visible : Visibility.Collapsed;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles preset combo box selection change.
    /// </summary>
    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (PresetCombo.SelectedItem is string name)
        {
            PresetName = name;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion
}
