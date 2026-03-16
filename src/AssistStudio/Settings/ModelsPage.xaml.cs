using AssistStudio.Dialogs;
using AssistStudio.Modules.Helpers;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing AI provider API keys, model selection,
/// and Ollama local model configuration.
/// </summary>
public sealed partial class ModelsPage : Page
{
    #region Fields

    /// <summary>
    /// Reference to the parent settings panel for syncing preset changes.
    /// </summary>
    private SettingsPanel? _settings;

    #endregion

    #region Constants

    /// <summary>
    /// Fallback Claude model IDs used when the API is unreachable and no cache exists.
    /// </summary>
    private static readonly string[] FallbackClaudeModels =
        ["claude-sonnet-4-20250514", "claude-opus-4-20250514", "claude-haiku-4-20250514"];

    /// <summary>
    /// Fallback OpenAI model IDs used when the API is unreachable and no cache exists.
    /// </summary>
    private static readonly string[] FallbackOpenAIModels =
        ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "o1", "o1-mini", "o3-mini"];

    /// <summary>
    /// Fallback Gemini model IDs used when the API is unreachable and no cache exists.
    /// </summary>
    private static readonly string[] FallbackGeminiModels =
        ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-2.5-pro-preview-05-06"];

    /// <summary>
    /// Fallback Groq model IDs used when the API is unreachable and no cache exists.
    /// </summary>
    private static readonly string[] FallbackGroqModels =
        ["llama-3.3-70b-versatile", "llama-3.1-8b-instant", "mixtral-8x7b-32768"];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelsPage"/> class.
    /// </summary>
    public ModelsPage()
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

        LoadApiKeys();
        PopulateModelCombos();

        // Sync presets from current API keys/models so tabs get correct provider list
        SyncPresetsFromUI();

        // Auto-check Ollama status
        _ = CheckOllamaStatusAsync();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads a localized string by key, returning the key itself if loading fails.
    /// </summary>
    /// <returns>The localized string or the key as fallback.</returns>
    private static string L(string key)
    {
        try
        {
            var loader = new ResourceLoader();
            return loader.GetString(key);
        }
        catch
        {
            return key;
        }
    }

    #endregion

    #region API Key Management

    /// <summary>
    /// Loads API keys from the already-loaded presets and sets the UI state for each provider card.
    /// Avoids redundant PasswordVault calls since keys were loaded during AppSettings.LoadPresets().
    /// </summary>
    private void LoadApiKeys()
    {
        var keys = _settings?.Presets.ToDictionary(p => p.ProviderType, p => p.ApiKey)
                   ?? new Dictionary<string, string>();

        keys.TryGetValue("Claude", out var claudeKey);
        keys.TryGetValue("OpenAI", out var openAIKey);
        keys.TryGetValue("Gemini", out var geminiKey);
        keys.TryGetValue("Groq", out var groqKey);

        SetKeyState(claudeKey ?? "", ClaudeKeyInputPanel, ClaudeKeyDisplayPanel, ClaudeMaskedKeyText, ClaudeStatusText, ClaudeModelCombo);
        SetKeyState(openAIKey ?? "", OpenAIKeyInputPanel, OpenAIKeyDisplayPanel, OpenAIMaskedKeyText, OpenAIStatusText, OpenAIModelCombo);
        SetKeyState(geminiKey ?? "", GeminiKeyInputPanel, GeminiKeyDisplayPanel, GeminiMaskedKeyText, GeminiStatusText, GeminiModelCombo);
        SetKeyState(groqKey ?? "", GroqKeyInputPanel, GroqKeyDisplayPanel, GroqMaskedKeyText, GroqStatusText, GroqModelCombo);
    }

    /// <summary>
    /// Configures the visual state of a provider card based on whether an API key is present.
    /// </summary>
    private static void SetKeyState(string key, Grid inputPanel, Grid displayPanel, TextBlock maskedText, TextBlock statusText, ComboBox modelCombo)
    {
        if (!string.IsNullOrEmpty(key))
        {
            inputPanel.Visibility = Visibility.Collapsed;
            displayPanel.Visibility = Visibility.Visible;
            maskedText.Text = MaskKey(key);
            statusText.Text = "";
            modelCombo.IsEnabled = true;
        }
        else
        {
            inputPanel.Visibility = Visibility.Visible;
            displayPanel.Visibility = Visibility.Collapsed;
            statusText.Text = L("Models_NoKey");
            statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
            modelCombo.IsEnabled = false;
        }
    }

    /// <summary>
    /// Masks an API key string, showing only the first and last three characters.
    /// </summary>
    /// <returns>A masked representation of the key.</returns>
    private static string MaskKey(string key)
    {
        if (key.Length <= 6) return "••••••";
        return key[..3] + "•••" + key[^3..];
    }

    /// <summary>
    /// Saves an API key for the specified provider, updates the UI state, and triggers a model cache refresh.
    /// </summary>
    private void AddProviderKey(string provider, PasswordBox keyBox, Grid inputPanel, Grid displayPanel, TextBlock maskedText, TextBlock statusText, ComboBox modelCombo)
    {
        var key = keyBox.Password?.Trim();
        if (string.IsNullOrEmpty(key)) return;

        PasswordVaultHelper.SaveApiKey(provider, key);
        keyBox.Password = "";

        inputPanel.Visibility = Visibility.Collapsed;
        displayPanel.Visibility = Visibility.Visible;
        maskedText.Text = MaskKey(key);
        statusText.Text = "";
        modelCombo.IsEnabled = true;

        _ = FetchAndCacheModelsAsync(provider, key, modelCombo);
        SyncPresetsFromUI();
    }

    /// <summary>
    /// Removes the API key for the specified provider and resets the provider card UI.
    /// </summary>
    private void RemoveProviderKey(string provider, Grid inputPanel, Grid displayPanel, TextBlock statusText, ComboBox modelCombo)
    {
        PasswordVaultHelper.DeleteApiKey(provider);

        inputPanel.Visibility = Visibility.Visible;
        displayPanel.Visibility = Visibility.Collapsed;
        statusText.Text = L("Models_NoKey");
        statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.Colors.OrangeRed);
        modelCombo.IsEnabled = false;

        SyncPresetsFromUI();
    }

    #endregion

    #region API Key Event Handlers

    /// <summary>
    /// Handles adding the Claude API key.
    /// </summary>
    private void OnAddClaudeKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("Claude", ClaudeApiKeyBox, ClaudeKeyInputPanel, ClaudeKeyDisplayPanel, ClaudeMaskedKeyText, ClaudeStatusText, ClaudeModelCombo);

    /// <summary>
    /// Handles adding the OpenAI API key.
    /// </summary>
    private void OnAddOpenAIKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("OpenAI", OpenAIApiKeyBox, OpenAIKeyInputPanel, OpenAIKeyDisplayPanel, OpenAIMaskedKeyText, OpenAIStatusText, OpenAIModelCombo);

    /// <summary>
    /// Handles adding the Gemini API key.
    /// </summary>
    private void OnAddGeminiKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("Gemini", GeminiApiKeyBox, GeminiKeyInputPanel, GeminiKeyDisplayPanel, GeminiMaskedKeyText, GeminiStatusText, GeminiModelCombo);

    /// <summary>
    /// Handles adding the Groq API key.
    /// </summary>
    private void OnAddGroqKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("Groq", GroqApiKeyBox, GroqKeyInputPanel, GroqKeyDisplayPanel, GroqMaskedKeyText, GroqStatusText, GroqModelCombo);

    /// <summary>
    /// Handles removing the Claude API key.
    /// </summary>
    private void OnRemoveClaudeKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Claude", ClaudeKeyInputPanel, ClaudeKeyDisplayPanel, ClaudeStatusText, ClaudeModelCombo);

    /// <summary>
    /// Handles removing the OpenAI API key.
    /// </summary>
    private void OnRemoveOpenAIKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("OpenAI", OpenAIKeyInputPanel, OpenAIKeyDisplayPanel, OpenAIStatusText, OpenAIModelCombo);

    /// <summary>
    /// Handles removing the Gemini API key.
    /// </summary>
    private void OnRemoveGeminiKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Gemini", GeminiKeyInputPanel, GeminiKeyDisplayPanel, GeminiStatusText, GeminiModelCombo);

    /// <summary>
    /// Handles removing the Groq API key.
    /// </summary>
    private void OnRemoveGroqKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Groq", GroqKeyInputPanel, GroqKeyDisplayPanel, GroqStatusText, GroqModelCombo);

    #endregion

    #region Model ComboBox Management

    /// <summary>
    /// Populates all provider model combo boxes from cache or fallback values, then starts background refresh.
    /// </summary>
    private void PopulateModelCombos()
    {
        // Load from cache first, fall back to hardcoded defaults
        PopulateComboFromCacheOrFallback("Claude", ClaudeModelCombo, FallbackClaudeModels);
        PopulateComboFromCacheOrFallback("OpenAI", OpenAIModelCombo, FallbackOpenAIModels);
        PopulateComboFromCacheOrFallback("Gemini", GeminiModelCombo, FallbackGeminiModels);
        PopulateComboFromCacheOrFallback("Groq", GroqModelCombo, FallbackGroqModels);

        // Background refresh for providers that have API keys
        _ = RefreshAllModelCachesAsync();
    }

    /// <summary>
    /// Populates a combo box from the model cache, falling back to the provided default model list.
    /// </summary>
    private static void PopulateComboFromCacheOrFallback(string provider, ComboBox combo, string[] fallback)
    {
        var cached = AppSettings.GetCachedModels(provider);
        var models = cached?.ToArray() ?? fallback;
        var savedModel = AppSettings.GetDefaultModel(provider);

        PopulateCombo(combo, models, savedModel);
    }

    /// <summary>
    /// Fills a combo box with model IDs and selects the saved or first model.
    /// </summary>
    private static void PopulateCombo(ComboBox combo, string[] models, string? savedModel)
    {
        combo.Items.Clear();
        foreach (var m in models) combo.Items.Add(m);

        if (!string.IsNullOrEmpty(savedModel))
        {
            var idx = Array.IndexOf(models, savedModel);
            if (idx >= 0)
            {
                combo.SelectedIndex = idx;
                return;
            }
        }

        if (models.Length > 0)
            combo.SelectedIndex = 0;
    }

    /// <summary>
    /// Refreshes model caches in the background for all providers that have API keys.
    /// </summary>
    private async Task RefreshAllModelCachesAsync()
    {
        if (_settings is null) return;

        // Reuse API keys from already-loaded presets to avoid redundant vault calls
        var keys = _settings.Presets.ToDictionary(p => p.ProviderType, p => p.ApiKey);
        var tasks = new List<Task>();

        var combos = new Dictionary<string, ComboBox>
        {
            ["Claude"] = ClaudeModelCombo,
            ["OpenAI"] = OpenAIModelCombo,
            ["Gemini"] = GeminiModelCombo,
            ["Groq"] = GroqModelCombo,
        };

        foreach (var (provider, combo) in combos)
        {
            if (keys.TryGetValue(provider, out var key) && !string.IsNullOrEmpty(key))
                tasks.Add(FetchAndCacheModelsAsync(provider, key, combo));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Fetches the model list from a provider's API, caches the results, and updates the combo box.
    /// </summary>
    private async Task FetchAndCacheModelsAsync(string provider, string apiKey, ComboBox combo)
    {
        try
        {
            var provider_ = CreateProviderForListing(provider, apiKey);
            try
            {
                var models = await provider_.ListModelsAsync();
                var filtered = FilterChatModels(provider, models);

                if (filtered.Count == 0) return;

                AppSettings.SetCachedModels(provider, filtered);

                // Update ComboBox on UI thread, preserving current selection
                DispatcherQueue.TryEnqueue(() =>
                {
                    var current = combo.SelectedItem as string;
                    PopulateCombo(combo, [.. filtered], current ?? AppSettings.GetDefaultModel(provider));
                });
            }
            finally
            {
                (provider_ as IDisposable)?.Dispose();
            }
        }
        catch
        {
            // API unreachable — keep current list (cache or fallback)
        }
    }

    /// <summary>
    /// Creates a temporary AI provider instance configured for model listing only.
    /// </summary>
    /// <returns>An AI provider capable of listing models.</returns>
    private static IAiProvider CreateProviderForListing(string provider, string apiKey) => provider switch
    {
        "Claude" => new ClaudeProvider(apiKey, "dummy"),
        "OpenAI" => new OpenAiProvider(apiKey, "dummy"),
        "Gemini" => new GeminiProvider(apiKey, "dummy"),
        "Groq" => new OpenAiProvider(apiKey, "dummy",
            baseUrl: "https://api.groq.com/openai/v1", providerName: "Groq"),
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };

    /// <summary>
    /// Filters the raw model list to include only chat-capable models based on naming conventions.
    /// </summary>
    /// <returns>A filtered list of model IDs appropriate for the provider.</returns>
    private static List<string> FilterChatModels(string provider, IReadOnlyList<AiModel> models)
    {
        return provider switch
        {
            "Claude" => [.. models
                .Where(m => m.Id.StartsWith("claude-"))
                .Select(m => m.Id)],
            "OpenAI" => [.. models
                .Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3") || m.Id.StartsWith("o4"))
                .Select(m => m.Id)],
            "Gemini" => [.. models
                .Where(m => m.Id.StartsWith("gemini-"))
                .Select(m => m.Id)],
            "Groq" => [.. models
                .Select(m => m.Id)],
            _ => [.. models.Select(m => m.Id)]
        };
    }

    #endregion

    #region Preset Synchronization

    /// <summary>
    /// Maps a provider type key to its user-facing display name.
    /// </summary>
    /// <returns>The display name for the provider.</returns>
    private static string ProviderToDisplayName(string provider) => provider switch
    {
        "Claude" => "Anthropic Claude",
        "OpenAI" => "OpenAI",
        "Gemini" => "Google Gemini",
        "Groq" => "Groq",
        "Ollama" => "Ollama",
        "Mock" => "Mock",
        _ => "Mock"
    };

    /// <summary>
    /// Rebuilds the provider preset collection from the current UI state (API keys and selected models)
    /// and notifies the settings panel of the changes.
    /// </summary>
    private void SyncPresetsFromUI()
    {
        if (_settings is null) return;

        // Cloud providers — reuse API keys already loaded in presets to avoid redundant vault calls
        var existingKeys = _settings.Presets.ToDictionary(p => p.ProviderType, p => p.ApiKey);
        _settings.Presets.Clear();

        foreach (var provider in new[] { "Claude", "OpenAI", "Gemini", "Groq" })
        {
            existingKeys.TryGetValue(provider, out var key);
            if (string.IsNullOrEmpty(key)) continue;

            var modelCombo = provider switch
            {
                "Claude" => ClaudeModelCombo,
                "OpenAI" => OpenAIModelCombo,
                "Gemini" => GeminiModelCombo,
                "Groq" => GroqModelCombo,
                _ => null
            };

            var modelId = modelCombo?.SelectedItem as string ?? "";
            if (string.IsNullOrEmpty(modelId))
                modelId = AppSettings.GetDefaultModel(provider) ?? "";
            else
                AppSettings.SetDefaultModel(provider, modelId);

            var preset = new ProviderPreset
            {
                Name = ProviderToDisplayName(provider),
                ProviderType = provider,
                ModelId = modelId,
                ApiKey = key
            };

            if (provider == "Groq")
                preset.BaseUrl = "https://api.groq.com/openai/v1";

            _settings.Presets.Add(preset);
        }

        // Ollama
        var ollamaDisplay = OllamaModelCombo.SelectedItem as string ?? "";
        var ollamaModel = StripFitSuffix(ollamaDisplay);
        if (string.IsNullOrEmpty(ollamaModel))
            ollamaModel = AppSettings.GetDefaultModel("Ollama") ?? "";
        else
            AppSettings.SetDefaultModel("Ollama", ollamaModel);
        _settings.Presets.Add(new ProviderPreset
        {
            Name = "Ollama",
            ProviderType = "Ollama",
            ModelId = ollamaModel
        });

        // Mock
        _settings.Presets.Add(new ProviderPreset
        {
            Name = "Mock",
            ProviderType = "Mock"
        });

        _settings.RaisePresetsChanged();
    }

    /// <summary>
    /// Handles cloud provider model combo box selection changes to persist the model and sync presets.
    /// </summary>
    private void OnCloudModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not string model || string.IsNullOrEmpty(model))
            return;

        var provider = combo.Name switch
        {
            nameof(ClaudeModelCombo) => "Claude",
            nameof(OpenAIModelCombo) => "OpenAI",
            nameof(GeminiModelCombo) => "Gemini",
            nameof(GroqModelCombo) => "Groq",
            _ => null
        };

        if (provider is null) return;

        AppSettings.SetDefaultModel(provider, model);
        SyncPresetsFromUI();
    }

    /// <summary>
    /// Handles Ollama model combo box selection changes to persist the model and sync presets.
    /// </summary>
    private void OnOllamaModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OllamaModelCombo.SelectedItem is string display && !string.IsNullOrEmpty(display))
        {
            // Strip fit-kind suffix before saving
            var model = StripFitSuffix(display);
            AppSettings.SetDefaultModel("Ollama", model);
            SyncPresetsFromUI();
        }
    }

    /// <summary>
    /// Strips the fit-kind suffix (e.g. "  (GPU)") from a display string.
    /// </summary>
    /// <returns>The model ID without the fit-kind suffix.</returns>
    private static string StripFitSuffix(string display)
    {
        var idx = display.IndexOf("  (", StringComparison.Ordinal);
        return idx >= 0 ? display[..idx] : display;
    }

    #endregion

    #region Ollama Management

    /// <summary>
    /// Handles the check Ollama button click to refresh the Ollama connection status.
    /// </summary>
    private async void OnCheckOllama(object sender, RoutedEventArgs e)
    {
        await CheckOllamaStatusAsync();
    }

    /// <summary>
    /// Checks whether Ollama is installed and running, updating the UI status indicators accordingly.
    /// </summary>
    private async Task CheckOllamaStatusAsync()
    {
        OllamaSpinner.Visibility = Visibility.Visible;
        OllamaSpinner.IsActive = true;
        OllamaStatusText.Text = L("Models_Checking");
        OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.Colors.Gray);
        StartOllamaButton.Visibility = Visibility.Collapsed;
        OllamaInstallPanel.Visibility = Visibility.Collapsed;
        BrowseModelsButton.Visibility = Visibility.Collapsed;

        try
        {
            var isRunning = await OllamaHelper.IsOllamaRunningAsync();
            if (isRunning)
            {
                OllamaStatusText.Text = L("Models_Running");
                OllamaStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
                BrowseModelsButton.Visibility = Visibility.Visible;
                await LoadOllamaModelsAsync();
            }
            else if (OllamaHelper.IsOllamaInstalled())
            {
                OllamaStatusText.Text = L("Models_InstalledNotRunning");
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Orange);
                StartOllamaButton.Visibility = Visibility.Visible;
            }
            else
            {
                OllamaStatusText.Text = L("Models_NotInstalled");
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.OrangeRed);
                OllamaInstallPanel.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            OllamaStatusText.Text = L("Models_Error");
            OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
        }
        finally
        {
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Loads locally installed Ollama models, classifies their hardware fit, and populates the combo box.
    /// </summary>
    private async Task LoadOllamaModelsAsync()
    {
        try
        {
            using var manager = new OllamaModelManager();
            var allModels = await manager.ListLocalModelsAsync();
            var hw = await HardwareProbe.GetAsync();

            var visibleModels = allModels
                .Select(m => new
                {
                    Model = m,
                    Fit = OllamaFitPolicy.Classify(
                        new OllamaModelMeta(m.Id, m.SizeBytes, m.ParameterSize, m.QuantizationLevel),
                        hw)
                })
                .Where(x => x.Fit != OllamaFitKind.NoFit)
                .OrderBy(x => x.Fit)
                .ToList();

            OllamaModelCombo.Items.Clear();
            foreach (var x in visibleModels)
            {
                var suffix = x.Fit switch
                {
                    OllamaFitKind.Gpu => $"  ({L("Models_FitGpu")})",
                    OllamaFitKind.Cpu => $"  ({L("Models_FitCpu")})",
                    OllamaFitKind.Maybe => $"  ({L("Models_FitMaybe")})",
                    _ => ""
                };
                OllamaModelCombo.Items.Add(x.Model.Id + suffix);
            }

            if (OllamaModelCombo.Items.Count > 0)
            {
                OllamaModelCombo.IsEnabled = true;
                var saved = AppSettings.GetDefaultModel("Ollama");
                var found = false;
                for (var i = 0; i < OllamaModelCombo.Items.Count; i++)
                {
                    if (OllamaModelCombo.Items[i] is string s && s.StartsWith(saved ?? ""))
                    {
                        OllamaModelCombo.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found) OllamaModelCombo.SelectedIndex = 0;
            }
        }
        catch
        {
            // Failed to load models
        }
    }

    /// <summary>
    /// Handles the start Ollama button click to launch the Ollama service.
    /// </summary>
    private async void OnStartOllama(object sender, RoutedEventArgs e)
    {
        StartOllamaButton.IsEnabled = false;
        OllamaSpinner.Visibility = Visibility.Visible;
        OllamaSpinner.IsActive = true;
        OllamaStatusText.Text = L("Models_Starting");

        try
        {
            var started = await OllamaHelper.StartOllamaAsync();
            if (started)
            {
                await CheckOllamaStatusAsync();
            }
            else
            {
                OllamaStatusText.Text = L("Models_FailedToStart");
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.OrangeRed);
            }
        }
        catch
        {
            OllamaStatusText.Text = L("Models_ErrorStarting");
            OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
        }
        finally
        {
            StartOllamaButton.IsEnabled = true;
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Opens the model selection dialog to browse and pull Ollama models.
    /// </summary>
    private async void OnBrowseOllamaModels(object sender, RoutedEventArgs e)
    {
        using var manager = new OllamaModelManager();
        var dialog = new ModelSelectionDialog(manager)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();

        // Pull queued models
        if (dialog.ModelsToPull.Count > 0)
        {
            await PullModelsAsync(dialog.ModelsToPull);
        }

        // Refresh local models after everything
        await LoadOllamaModelsAsync();
    }

    /// <summary>
    /// Downloads one or more Ollama models sequentially, showing progress in the UI.
    /// </summary>
    private async Task PullModelsAsync(List<string> modelNames)
    {
        PullProgressPanel.Visibility = Visibility.Visible;
        BrowseModelsButton.IsEnabled = false;

        using var manager = new OllamaModelManager();

        for (var i = 0; i < modelNames.Count; i++)
        {
            var modelName = modelNames[i];
            var prefix = modelNames.Count > 1 ? $"[{i + 1}/{modelNames.Count}] " : "";

            PullProgressStatus.Text = $"{prefix}{string.Format(L("Models_PullingModel"), modelName)}";
            PullProgressBar.IsIndeterminate = true;
            PullProgressBar.Value = 0;

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    var pct = p.Percent * 100;
                    if (pct > 0)
                    {
                        PullProgressBar.IsIndeterminate = false;
                        PullProgressBar.Value = pct;
                        var totalMb = p.TotalBytes.HasValue ? p.TotalBytes.Value / 1_048_576.0 : 0;
                        var doneMb = p.CompletedBytes.HasValue ? p.CompletedBytes.Value / 1_048_576.0 : 0;
                        PullProgressStatus.Text = totalMb > 0
                            ? $"{prefix}{p.Status} — {doneMb:F0} / {totalMb:F0} MB ({pct:F0}%)"
                            : $"{prefix}{p.Status}";
                    }
                    else
                    {
                        PullProgressBar.IsIndeterminate = true;
                        PullProgressStatus.Text = $"{prefix}{p.Status}";
                    }
                });
            });

            try
            {
                await manager.DownloadModelAsync(modelName, progress);
            }
            catch (Exception ex)
            {
                PullProgressStatus.Text = $"{prefix}Error: {ex.Message}";
                await Task.Delay(2000);
            }
        }

        PullProgressBar.IsIndeterminate = false;
        PullProgressPanel.Visibility = Visibility.Collapsed;
        BrowseModelsButton.IsEnabled = true;
    }

    #endregion
}
