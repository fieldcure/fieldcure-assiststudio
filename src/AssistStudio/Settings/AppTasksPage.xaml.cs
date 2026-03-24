using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for configuring app tasks behavior, including the model source,
/// auto-title generation, auto-summarization toggles, and embedding model selection.
/// </summary>
public sealed partial class AppTasksPage : Page
{
    #region Fields

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    private readonly ResourceLoader _loader = new();

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
    protected override async void OnNavigatedTo(NavigationEventArgs e)
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

        // Embedding: check provider availability and restore selection
        await InitializeEmbeddingSectionAsync();

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

    #endregion

    #region Embedding

    /// <summary>
    /// Initializes the embedding section: checks Ollama/OpenAI availability,
    /// auto-selects best model if none saved, and restores saved selection.
    /// </summary>
    private async Task InitializeEmbeddingSectionAsync()
    {
        // 1. Restore saved selection immediately (no network wait)
        var savedPreset = AppSettings.EmbeddingPreset;
        if (!string.IsNullOrEmpty(savedPreset) && savedPreset.Contains('/'))
        {
            RestoreEmbeddingSelection(savedPreset);
        }

        // 2. Check OpenAI API key availability (sync, no network)
        var hasOpenAiKey = AppSettings.LoadPresets()
            .Any(p => p.ProviderType is "OpenAI"
                && !string.IsNullOrEmpty(p.ApiKey));
        if (!hasOpenAiKey)
        {
            var openAiTooltip = new ToolTip
            {
                Content = _loader.GetString("Embedding_OpenAiKeyMissing"),
                Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
            };
            foreach (var rb in new[] { RbEmbed3Small, RbEmbed3Large })
            {
                rb.IsEnabled = false;
                ToolTipService.SetToolTip(rb, openAiTooltip);
            }
        }

        // 3. Check Ollama server status (async, may take 2-3s if offline)
        var ollamaRunning = await OllamaHelper.IsOllamaRunningAsync();
        if (!ollamaRunning)
        {
            var ollamaTooltip = new ToolTip
            {
                Content = _loader.GetString("Embedding_OllamaNotRunning"),
                Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
            };
            foreach (var rb in new[] { RbNomicV1, RbNomicV2, RbBgeM3 })
            {
                rb.IsEnabled = false;
                ToolTipService.SetToolTip(rb, ollamaTooltip);
            }
        }
        else
        {
            // Check which models are already downloaded — hide download icon for installed ones
            await UpdateOllamaModelIconsAsync();
        }

        // 4. Auto-select only if no valid preset saved yet
        if (string.IsNullOrEmpty(savedPreset) || !savedPreset.Contains('/'))
        {
            AppSettings.EmbeddingPreset = "";
            await AutoSelectEmbeddingModelAsync(ollamaRunning, hasOpenAiKey);
            RestoreEmbeddingSelection(AppSettings.EmbeddingPreset);
        }
    }

    /// <summary>
    /// Auto-selects the best available embedding model.
    /// Priority: OpenAI > Ollama (bge-m3 > nomic-v2-moe > nomic) > None.
    /// </summary>
    private async Task AutoSelectEmbeddingModelAsync(bool ollamaRunning, bool hasOpenAiKey)
    {
        // 1st: OpenAI if API key available
        if (hasOpenAiKey)
        {
            ApplyEmbeddingSelection("openai/text-embedding-3-small");
            return;
        }

        // 2nd: Ollama with installed models
        if (!ollamaRunning) return;

        try
        {
            using var manager = new OllamaModelManager("http://localhost:11434");
            var installed = await manager.ListLocalModelsAsync();
            var installedNames = installed.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            string[] preferenceOrder = ["bge-m3", "nomic-embed-text-v2-moe", "nomic-embed-text"];
            foreach (var model in preferenceOrder)
            {
                if (installedNames.Contains(model))
                {
                    ApplyEmbeddingSelection($"ollama/{model}");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[Embedding] Auto-select failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves embedding settings from a preset tag (e.g., "ollama/bge-m3").
    /// </summary>
    private void ApplyEmbeddingSelection(string presetTag)
    {
        var parts = presetTag.Split('/');
        if (parts.Length != 2) return;

        var provider = parts[0];
        var model = parts[1];

        AppSettings.EmbeddingPreset = presetTag;
        AppSettings.EmbeddingModel = model;

        if (provider == "ollama")
        {
            AppSettings.EmbeddingBaseUrl = "http://localhost:11434";
            PasswordVaultHelper.SaveMcpEnvVar("builtin_rag", "EMBEDDING_API_KEY", "");
        }
        else // openai
        {
            var openAiPreset = AppSettings.LoadPresets()
                .FirstOrDefault(p => p.ProviderType is "OpenAI"
                    && !string.IsNullOrEmpty(p.ApiKey));
            AppSettings.EmbeddingBaseUrl = openAiPreset?.BaseUrl ?? "https://api.openai.com";
            PasswordVaultHelper.SaveMcpEnvVar("builtin_rag", "EMBEDDING_API_KEY", openAiPreset?.ApiKey ?? "");
        }
    }

    /// <summary>
    /// Restores the radio button selection from a saved preset tag.
    /// </summary>
    private void RestoreEmbeddingSelection(string? presetTag)
    {
        if (string.IsNullOrEmpty(presetTag)) return;

        // Find and check the matching radio button by Tag
        RadioButton?[] allButtons = [RbNomicV1, RbNomicV2, RbBgeM3, RbEmbed3Small, RbEmbed3Large];
        foreach (var rb in allButtons)
        {
            if (rb?.Tag as string == presetTag)
            {
                rb.IsChecked = true;
                return;
            }
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
    /// Handles embedding model radio button selection.
    /// For Ollama models: checks if downloaded, offers pull if not.
    /// For OpenAI models: applies immediately.
    /// </summary>
    private async void OnEmbeddingModelSelected(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is not RadioButton rb) return;

        var tag = rb.Tag?.ToString() ?? "";
        var parts = tag.Split('/');
        if (parts.Length != 2) return;

        var provider = parts[0];
        var model = parts[1];
        var previousPreset = AppSettings.EmbeddingPreset;

        if (provider == "ollama")
        {
            // Fire-and-forget: apply selection immediately, pull in background
            _ = HandleOllamaSelectionAsync(model, tag, rb);
        }
        else
        {
            HandleOpenAiSelection(model, tag);
        }

        // Show model change warning if Knowledge Archive is active and model changed
        if (!string.IsNullOrEmpty(previousPreset) && previousPreset != AppSettings.EmbeddingPreset)
        {
            var builtIn = AppSettings.BuiltInServers;
            var ragConfig = builtIn.GetValueOrDefault(BuiltInServerHelper.RagKey);
            if (ragConfig is { IsEnabled: true, Folders.Count: > 0 })
            {
                ModelChangeWarning.IsOpen = true;
            }
        }
    }

    /// <summary>
    /// Handles Ollama model selection. If not downloaded, starts background pull with spinner.
    /// No blocking dialog — selection is applied immediately and pull runs in background.
    /// </summary>
    private async Task HandleOllamaSelectionAsync(string model, string tag, RadioButton rb)
    {
        // Apply selection immediately
        ApplyEmbeddingSelection(tag);

        // Check if already downloaded
        var (icon, spinner) = GetOllamaModelControls(model);
        if (icon?.Visibility == Visibility.Collapsed)
            return; // Already installed, nothing to download

        // Start background pull with spinner
        if (icon is not null) icon.Visibility = Visibility.Collapsed;
        if (spinner is not null) { spinner.Visibility = Visibility.Visible; spinner.IsActive = true; }

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Informational,
            string.Format(_loader.GetString("Embedding_Downloading"), model),
            string.Empty);

        try
        {
            using var manager = new OllamaModelManager("http://localhost:11434");
            await manager.DownloadModelAsync(model);

            // Done — hide spinner
            if (spinner is not null) { spinner.IsActive = false; spinner.Visibility = Visibility.Collapsed; }

            NotificationCenter.Instance.Post(
                InfoBarSeverity.Success,
                string.Format(_loader.GetString("Embedding_DownloadComplete"), model),
                string.Empty,
                5000);
        }
        catch (Exception ex)
        {
            // Failed — restore download icon
            if (icon is not null) icon.Visibility = Visibility.Visible;
            if (spinner is not null) { spinner.IsActive = false; spinner.Visibility = Visibility.Collapsed; }

            NotificationCenter.Instance.Post(
                InfoBarSeverity.Error,
                _loader.GetString("Embedding_DownloadFailed"),
                ex.Message,
                8000);

            LoggingService.LogError($"[Embedding] Pull failed for {model}: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates download icons for Ollama models — hides icon if already installed.
    /// </summary>
    private async Task UpdateOllamaModelIconsAsync()
    {
        try
        {
            using var manager = new OllamaModelManager("http://localhost:11434");
            var installed = await manager.ListLocalModelsAsync();
            // Ollama returns IDs like "bge-m3:latest" — strip tag for comparison
            var installedNames = installed
                .Select(m => m.Id.Split(':')[0])
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var model in new[] { "nomic-embed-text", "nomic-embed-text-v2-moe", "bge-m3" })
            {
                var (icon, _) = GetOllamaModelControls(model);
                if (icon is not null)
                    icon.Visibility = installedNames.Contains(model) ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[Embedding] Failed to check Ollama models: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the download icon and spinner controls for an Ollama model name.
    /// </summary>
    private (FontIcon? icon, ProgressRing? spinner) GetOllamaModelControls(string model) => model switch
    {
        "nomic-embed-text" => (IcoNomicV1, SpinNomicV1),
        "nomic-embed-text-v2-moe" => (IcoNomicV2, SpinNomicV2),
        "bge-m3" => (IcoBgeM3, SpinBgeM3),
        _ => (null, null),
    };

    /// <summary>
    /// Handles OpenAI model selection — applies immediately using existing API key.
    /// </summary>
    private void HandleOpenAiSelection(string model, string tag)
    {
        ApplyEmbeddingSelection(tag);
    }

    #endregion
}
