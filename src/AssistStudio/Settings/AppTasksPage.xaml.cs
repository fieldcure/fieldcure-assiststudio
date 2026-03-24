using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for configuring app tasks behavior, including the model source,
/// auto-title generation, and auto-summarization toggles.
/// </summary>
public sealed partial class AppTasksPage : Page
{
    #region Fields

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AppTasksPage"/> class.
    /// </summary>
    public AppTasksPage()
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

        // Load model source
        var source = AppSettings.AppTasksSource;
        for (var i = 0; i < ModelSourceRadio.Items.Count; i++)
        {
            if (ModelSourceRadio.Items[i] is RadioButton rb && rb.Tag as string == source)
            {
                ModelSourceRadio.SelectedIndex = i;
                break;
            }
        }

        PresetCombo.Visibility = source == "Specific" ? Visibility.Visible : Visibility.Collapsed;

        // Populate presets
        PopulatePresetCombo();

        // Load toggles
        AutoTitleToggle.IsOn = AppSettings.AppAutoTitle;
        AutoSummaryToggle.IsOn = AppSettings.AppAutoSummary;

        // Load embedding settings
        PopulateEmbeddingPresetCombo();
        UpdateEmbeddingStatus();

        _suppressEvents = false;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates the preset combo box with available provider presets and selects the saved choice.
    /// </summary>
    private void PopulatePresetCombo()
    {
        PresetCombo.Items.Clear();

        var presets = AppSettings.LoadPresets();
        var selectedName = AppSettings.AppTasksPreset;
        var selectedIndex = -1;

        for (var i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            PresetCombo.Items.Add(preset.Name);
            if (preset.Name == selectedName)
            {
                selectedIndex = i;
            }
        }

        if (selectedIndex >= 0)
        {
            PresetCombo.SelectedIndex = selectedIndex;
        }
        else if (PresetCombo.Items.Count > 0)
        {
            PresetCombo.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Populates the embedding preset combo with available presets,
    /// excluding LM Studio (localhost:1234) and Claude (Anthropic) providers.
    /// </summary>
    private void PopulateEmbeddingPresetCombo()
    {
        EmbeddingPresetCombo.Items.Clear();

        // Only OpenAI-compatible embedding providers (exclude Claude, Gemini, LM Studio)
        var presets = AppSettings.LoadPresets()
            .Where(p => p.ProviderType is not "Claude" and not "Gemini"
                && !(p.BaseUrl?.Contains("localhost:1234") ?? false))
            .ToList();

        var selectedName = AppSettings.EmbeddingPreset;
        var selectedIndex = -1;

        for (var i = 0; i < presets.Count; i++)
        {
            EmbeddingPresetCombo.Items.Add(presets[i].Name);
            if (presets[i].Name == selectedName)
                selectedIndex = i;
        }

        if (selectedIndex >= 0)
            EmbeddingPresetCombo.SelectedIndex = selectedIndex;
    }

    /// <summary>
    /// Updates the embedding status text showing the current configuration.
    /// </summary>
    private void UpdateEmbeddingStatus()
    {
        var baseUrl = AppSettings.EmbeddingBaseUrl;
        var model = AppSettings.EmbeddingModel;

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(model))
        {
            EmbeddingStatusText.Text = "";
        }
        else
        {
            EmbeddingStatusText.Text = $"{model}  ·  {baseUrl}";
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles model source radio button changes to switch between "Current" and "Specific" modes.
    /// </summary>
    private void OnModelSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (ModelSourceRadio.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            AppSettings.AppTasksSource = tag;
            PresetCombo.Visibility = tag == "Specific" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Handles preset combo box selection changes to persist the chosen app tasks preset.
    /// </summary>
    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (PresetCombo.SelectedItem is string name)
        {
            AppSettings.AppTasksPreset = name;
        }
    }

    /// <summary>
    /// Handles the auto-title toggle switch change to persist the setting.
    /// </summary>
    private void OnAutoTitleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoTitle = AutoTitleToggle.IsOn;
    }

    /// <summary>
    /// Handles the auto-summary toggle switch change to persist the setting.
    /// </summary>
    private void OnAutoSummaryToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoSummary = AutoSummaryToggle.IsOn;
    }

    /// <summary>
    /// Handles embedding preset combo selection to update the base URL and API key.
    /// </summary>
    private void OnEmbeddingPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (EmbeddingPresetCombo.SelectedItem is not string name) return;

        var preset = AppSettings.LoadPresets().FirstOrDefault(p => p.Name == name);
        if (preset is null) return;

        // Claude and Gemini do not provide OpenAI-compatible embeddings
        if (preset.ProviderType is "Claude" or "Gemini")
        {
            EmbeddingWarningText.Text = $"{preset.ProviderType} does not support OpenAI-compatible embeddings. Please select Ollama or OpenAI.";
            EmbeddingWarningText.Visibility = Visibility.Visible;
            return;
        }

        EmbeddingWarningText.Visibility = Visibility.Collapsed;

        AppSettings.EmbeddingPreset = name;
        AppSettings.EmbeddingBaseUrl = preset.BaseUrl ?? "";

        // Store API key in vault for RAG server env var
        PasswordVaultHelper.SaveMcpEnvVar("builtin_rag", "EMBEDDING_API_KEY", preset.ApiKey ?? "");

        UpdateEmbeddingStatus();
    }

    /// <summary>
    /// Quick-selects an Ollama embedding model. The button Tag contains the model name.
    /// </summary>
    private void OnQuickSelectOllama(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string modelName) return;

        _suppressEvents = true;
        EmbeddingPresetCombo.SelectedIndex = -1;
        _suppressEvents = false;

        EmbeddingWarningText.Visibility = Visibility.Collapsed;

        AppSettings.EmbeddingPreset = "Ollama";
        AppSettings.EmbeddingBaseUrl = "http://localhost:11434";
        AppSettings.EmbeddingModel = modelName;
        PasswordVaultHelper.SaveMcpEnvVar("builtin_rag", "EMBEDDING_API_KEY", "");

        UpdateEmbeddingStatus();
    }

    /// <summary>
    /// Quick-selects an OpenAI embedding model. Reuses the API key from an existing OpenAI preset.
    /// </summary>
    private void OnQuickSelectOpenAi(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string modelName) return;

        // Find an existing OpenAI preset to get the API key
        var openAiPreset = AppSettings.LoadPresets()
            .FirstOrDefault(p => p.ProviderType is "OpenAI" && !string.IsNullOrEmpty(p.ApiKey));

        if (openAiPreset is null)
        {
            EmbeddingWarningText.Text = "No OpenAI preset found. Add an OpenAI provider in Models first.";
            EmbeddingWarningText.Visibility = Visibility.Visible;
            return;
        }

        _suppressEvents = true;
        EmbeddingPresetCombo.SelectedIndex = -1;
        _suppressEvents = false;

        EmbeddingWarningText.Visibility = Visibility.Collapsed;

        AppSettings.EmbeddingPreset = "OpenAI";
        AppSettings.EmbeddingBaseUrl = "https://api.openai.com";
        AppSettings.EmbeddingModel = modelName;
        PasswordVaultHelper.SaveMcpEnvVar("builtin_rag", "EMBEDDING_API_KEY", openAiPreset.ApiKey);

        UpdateEmbeddingStatus();
    }

    #endregion
}
