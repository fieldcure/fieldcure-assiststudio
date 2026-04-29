using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

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
    /// Loads the current state from settings and populates the model combo.
    /// Call during <c>OnNavigatedTo</c>.
    /// </summary>
    public void Load(AuxiliaryTaskSource source, string modelName)
    {
        _suppressEvents = true;

        Source = source;
        ModelName = modelName;

        SourceRadio.SelectedIndex = source == AuxiliaryTaskSource.Specific ? 1 : 0;
        ModelCombo.Visibility = source == AuxiliaryTaskSource.Specific ? Visibility.Visible : Visibility.Collapsed;

        PopulateModelCombo(modelName);

        _suppressEvents = false;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates the model combo box, selecting the given model name.
    /// </summary>
    private void PopulateModelCombo(string selectedName)
    {
        ModelCombo.Items.Clear();

        var items = AppSettings.BuildOrderedModelItems();
        var selectedIndex = -1;

        foreach (var obj in items)
        {
            if (obj is ProviderModel model)
            {
                // Skip Mock — cannot generate titles, summaries, or run sub-agents
                if (model.ProviderType == "Mock") continue;

                // Skip cloud providers without an API key
                if (model.RequiresApiKey && string.IsNullOrEmpty(model.ApiKey)) continue;

                ModelCombo.Items.Add(model.Name);
                if (model.Name == selectedName)
                    selectedIndex = ModelCombo.Items.Count - 1;
            }
            else if (obj is "-")
            {
                var border = (Border)XamlReader.Load(
                    """
                    <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            Height="1" HorizontalAlignment="Stretch"
                            Background="{ThemeResource DividerStrokeColorDefaultBrush}" />
                    """);
                ModelCombo.Items.Add(new ComboBoxItem
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
            ModelCombo.SelectedIndex = selectedIndex;
        else if (ModelCombo.Items.Count > 0)
            ModelCombo.SelectedIndex = 0;
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
            ModelCombo.Visibility = Source == AuxiliaryTaskSource.Specific
                ? Visibility.Visible : Visibility.Collapsed;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles model combo box selection change.
    /// </summary>
    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (ModelCombo.SelectedItem is string name)
        {
            ModelName = name;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion
}
