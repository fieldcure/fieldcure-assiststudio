using System;
using AssistStudio.Helpers;
using AssistStudio.Modules.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Settings;

/// <summary>
/// Reusable control for selecting a provider source (Inherit / Specific) and model
/// for an auxiliary task (title, summary, sub-agent).
/// </summary>
public sealed partial class TaskModelSelector : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Source"/> dependency property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(AuxiliaryTaskSource), typeof(TaskModelSelector),
            new PropertyMetadata(AuxiliaryTaskSource.Inherit));

    /// <summary>Identifies the <see cref="ModelName"/> dependency property.</summary>
    public static readonly DependencyProperty ModelNameProperty =
        DependencyProperty.Register(nameof(ModelName), typeof(string), typeof(TaskModelSelector),
            new PropertyMetadata(""));

    #endregion

    #region Fields

    /// <summary>Suppresses change handlers while loading saved state into UI.</summary>
    private bool _suppressEvents;

    #endregion

    #region Events

    /// <summary>Raised when the user changes the source or model.</summary>
    public event EventHandler? SettingsChanged;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskModelSelector"/> class.
    /// </summary>
    public TaskModelSelector()
    {
        InitializeComponent();
        ModelPickerControl.SelectionChanged += OnModelPickerSelectionChanged;
    }

    #endregion

    #region Properties

    /// <summary>Current source mode.</summary>
    public AuxiliaryTaskSource Source
    {
        get => (AuxiliaryTaskSource)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Selected model name when <see cref="Source"/> is <see cref="AuxiliaryTaskSource.Specific"/>.</summary>
    public string ModelName
    {
        get => (string)GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads the current state from settings and populates the model picker.
    /// Call during <c>OnNavigatedTo</c>.
    /// </summary>
    /// <param name="source">The source mode (Inherit or Specific).</param>
    /// <param name="modelName">The model name to preselect when source is Specific.</param>
    public void Load(AuxiliaryTaskSource source, string modelName)
    {
        _suppressEvents = true;

        Source = source;
        ModelName = modelName;

        SourceRadio.SelectedIndex = source == AuxiliaryTaskSource.Specific ? 1 : 0;
        ModelPickerControl.Visibility = source == AuxiliaryTaskSource.Specific ? Visibility.Visible : Visibility.Collapsed;

        PopulateModelPicker(modelName);

        _suppressEvents = false;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates the model picker, selecting the entry whose <see cref="ModelPickerEntry.ModelId"/>
    /// matches the given name. Excludes Mock and any cloud provider lacking an API key.
    /// </summary>
    /// <param name="selectedName">The model name to preselect (matches against <see cref="ProviderModel.ModelId"/>;
    /// post-PR-1 <see cref="ProviderModel.Name"/> equals <see cref="ProviderModel.ModelId"/>).</param>
    private void PopulateModelPicker(string selectedName)
    {
        var items = AppSettings.BuildOrderedModelItems();
        var entries = ModelPickerAdapter.BuildEntriesFromOrderedItems(
            items,
            filter: m => m.ProviderType != "Mock"
                      && (!m.RequiresApiKey || !string.IsNullOrEmpty(m.ApiKey)));

        ModelPickerControl.ItemsSource = (System.Collections.IList)entries;

        ModelPickerEntry? match = null;
        foreach (var entry in entries)
        {
            if (entry.ModelId == selectedName)
            {
                match = entry;
                break;
            }
        }
        ModelPickerControl.SelectedItem = match ?? (entries.Count > 0 ? entries[0] : null);
    }

    /// <summary>
    /// Parses a source tag string into an <see cref="AuxiliaryTaskSource"/> enum value.
    /// </summary>
    /// <param name="tag">The radio-button tag value.</param>
    /// <returns>The parsed source mode.</returns>
    private static AuxiliaryTaskSource ParseSource(string tag)
        => tag == "Specific" ? AuxiliaryTaskSource.Specific : AuxiliaryTaskSource.Inherit;

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles source radio button selection change.
    /// </summary>
    /// <param name="sender">The radio buttons control.</param>
    /// <param name="e">Selection-changed event args.</param>
    private void OnSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (SourceRadio.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            Source = ParseSource(tag);
            ModelPickerControl.Visibility = Source == AuxiliaryTaskSource.Specific
                ? Visibility.Visible : Visibility.Collapsed;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles the <see cref="ModelPicker.SelectionChanged"/> event to commit the new model name.
    /// </summary>
    /// <param name="sender">The model picker.</param>
    /// <param name="entry">The selected entry, or null when cleared.</param>
    private void OnModelPickerSelectionChanged(object? sender, ModelPickerEntry? entry)
    {
        if (_suppressEvents) return;

        ModelName = entry?.ModelId ?? string.Empty;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
