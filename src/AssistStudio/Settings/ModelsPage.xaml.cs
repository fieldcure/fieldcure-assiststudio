using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Controls;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing AI provider API keys, model selection,
/// and Ollama local model configuration.
/// </summary>
public sealed partial class ModelsPage : Page
{
    #region Fields

    /// <summary>
    /// The collection of provider presets loaded from settings.
    /// </summary>
    private ObservableCollection<ProviderPreset> _presets = [];
    private bool _isPopulating;

    /// <summary>Cancels the active pull progress tracking when the page is navigated away.</summary>
    private CancellationTokenSource? _pullCts;

    /// <summary>Model names queued for download, shared across page instances.</summary>
    private static readonly List<string> _pendingPulls = [];

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

        _presets = AppSettings.LoadPresets();

        LoadApiKeys();
        PopulateModelCombos();
        UpdateAllSubHeaders();
        UpdateExpandedState();

        // Presets are already loaded by AppSettings.LoadPresets() at startup.
        // Individual event handlers persist changes directly via PersistPresets().

        // Initialize Ollama URL from saved settings
        OllamaUrlBox.Text = AppSettings.GetOllamaBaseUrl() ?? "http://localhost:11434";

        // Lazy-check Ollama status to avoid blocking page entry
        _ = DelayedCheckOllamaAsync();

        // Resume progress tracking if downloads were started before navigating away
        if (_pendingPulls.Count > 0)
        {
            _ = ResumePullTrackingAsync();
        }
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _pullCts?.Cancel();
        _pullCts = null;
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

    /// <summary>
    /// Builds a sub-header string from model ID and status, joined by a middle dot separator.
    /// </summary>
    private static string BuildSubHeader(string? modelId, string? status)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(modelId)) parts.Add(modelId);
        if (!string.IsNullOrEmpty(status)) parts.Add(status);
        return string.Join(" \u00B7 ", parts);
    }

    /// <summary>
    /// Updates the sub-header of a cloud provider section with current model and key status.
    /// </summary>
    private static void UpdateProviderSubHeader(CollapsibleSection section, ComboBox modelCombo, bool hasKey)
    {
        var model = modelCombo.SelectedItem as string ?? "";
        var status = hasKey ? "\u2713" : L("Models_NoKey");
        section.SubHeader = BuildSubHeader(model, status);
    }

    /// <summary>
    /// Updates the Ollama section sub-header with current model and status text.
    /// </summary>
    private void UpdateOllamaSubHeader()
    {
        var display = OllamaModelCombo.SelectedItem as string ?? "";
        var model = StripFitSuffix(display);
        var status = OllamaStatusText.Text;
        OllamaSection.SubHeader = BuildSubHeader(
            string.IsNullOrEmpty(model) ? null : model,
            string.IsNullOrEmpty(status) ? null : status);
    }

    /// <summary>
    /// Updates sub-headers for all provider sections based on current UI state.
    /// </summary>
    private void UpdateAllSubHeaders()
    {
        var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey)
                   ?? [];

        var sections = new (string Provider, CollapsibleSection Section, ComboBox Combo)[]
        {
            ("Claude", ClaudeSection, ClaudeModelCombo),
            ("OpenAI", OpenAISection, OpenAIModelCombo),
            ("Gemini", GeminiSection, GeminiModelCombo),
            ("Groq", GroqSection, GroqModelCombo),
        };

        foreach (var (provider, section, combo) in sections)
        {
            keys.TryGetValue(provider, out var key);
            UpdateProviderSubHeader(section, combo, !string.IsNullOrEmpty(key));
        }

        UpdateOllamaSubHeader();
    }

    /// <summary>
    /// Sets the initial expanded state: if only one provider has a key, expand that section.
    /// </summary>
    private void UpdateExpandedState()
    {
        var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey)
                   ?? [];

        var sections = new (string Provider, CollapsibleSection Section)[]
        {
            ("Claude", ClaudeSection),
            ("OpenAI", OpenAISection),
            ("Gemini", GeminiSection),
            ("Groq", GroqSection),
        };

        var registeredSections = sections
            .Where(s => keys.TryGetValue(s.Provider, out var k) && !string.IsNullOrEmpty(k))
            .Select(s => s.Section)
            .ToList();

        // Ollama is counted separately — it's always present, just not always running
        // We'll handle Ollama expansion after CheckOllamaStatusAsync

        if (registeredSections.Count == 1 && !keys.Any(k => k.Key == "Ollama" && !string.IsNullOrEmpty(k.Value)))
        {
            registeredSections[0].IsExpanded = true;
        }
    }

    /// <summary>
    /// Returns the CollapsibleSection for the given provider type.
    /// </summary>
    private CollapsibleSection? GetSectionForProvider(string provider) => provider switch
    {
        "Claude" => ClaudeSection,
        "OpenAI" => OpenAISection,
        "Gemini" => GeminiSection,
        "Groq" => GroqSection,
        "Ollama" => OllamaSection,
        _ => null
    };

    #endregion

    #region API Key Management

    /// <summary>
    /// Loads API keys from the already-loaded presets and sets the UI state for each provider card.
    /// Avoids redundant PasswordVault calls since keys were loaded during AppSettings.LoadPresets().
    /// </summary>
    private void LoadApiKeys()
    {
        var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey)
                   ?? [];

        keys.TryGetValue("Claude", out var claudeKey);
        keys.TryGetValue("OpenAI", out var openAIKey);
        keys.TryGetValue("Gemini", out var geminiKey);
        keys.TryGetValue("Groq", out var groqKey);

        SetKeyState(claudeKey ?? "", ClaudeKeyInputPanel, ClaudeKeyDisplayPanel, ClaudeMaskedKeyText, ClaudeModelCombo, ClaudeOptionsPanel);
        SetKeyState(openAIKey ?? "", OpenAIKeyInputPanel, OpenAIKeyDisplayPanel, OpenAIMaskedKeyText, OpenAIModelCombo, OpenAIOptionsPanel);
        SetKeyState(geminiKey ?? "", GeminiKeyInputPanel, GeminiKeyDisplayPanel, GeminiMaskedKeyText, GeminiModelCombo, GeminiOptionsPanel);
        SetKeyState(groqKey ?? "", GroqKeyInputPanel, GroqKeyDisplayPanel, GroqMaskedKeyText, GroqModelCombo, GroqOptionsPanel);
    }

    /// <summary>
    /// Configures the visual state of a provider card based on whether an API key is present.
    /// </summary>
    private static void SetKeyState(string key, Grid inputPanel, Grid displayPanel, TextBlock maskedText, ComboBox modelCombo, StackPanel optionsPanel)
    {
        if (!string.IsNullOrEmpty(key))
        {
            inputPanel.Visibility = Visibility.Collapsed;
            displayPanel.Visibility = Visibility.Visible;
            maskedText.Text = MaskKey(key);
            modelCombo.IsEnabled = true;
            optionsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            inputPanel.Visibility = Visibility.Visible;
            displayPanel.Visibility = Visibility.Collapsed;
            modelCombo.IsEnabled = false;
            optionsPanel.Visibility = Visibility.Collapsed;
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
    private void AddProviderKey(string provider, PasswordBox keyBox, Grid inputPanel, Grid displayPanel, TextBlock maskedText, ComboBox modelCombo, StackPanel optionsPanel, CollapsibleSection section)
    {
        var key = keyBox.Password?.Trim();
        if (string.IsNullOrEmpty(key)) return;

        PasswordVaultHelper.SaveApiKey(provider, key);
        keyBox.Password = "";

        inputPanel.Visibility = Visibility.Collapsed;
        displayPanel.Visibility = Visibility.Visible;
        maskedText.Text = MaskKey(key);
        modelCombo.IsEnabled = true;
        optionsPanel.Visibility = Visibility.Visible;

        UpdateProviderSubHeader(section, modelCombo, hasKey: true);
        _ = FetchAndCacheModelsAsync(provider, key, modelCombo);

        var preset = FindPreset(provider);
        if (preset is not null)
        {
            preset.ApiKey = key;
        }
        else
        {
            var newPreset = new ProviderPreset
            {
                Name = ProviderToDisplayName(provider),
                ProviderType = provider,
                ModelId = modelCombo.SelectedItem as string
                          ?? AppSettings.GetDefaultModel(provider) ?? "",
                ApiKey = key,
                BaseUrl = provider == "Groq" ? "https://api.groq.com/openai/v1" : null,
            };
            var ollamaIdx = _presets.ToList().FindIndex(p => p.ProviderType == "Ollama");
            _presets.Insert(ollamaIdx >= 0 ? ollamaIdx : _presets.Count, newPreset);
        }
        PersistPresets();
    }

    /// <summary>
    /// Removes the API key for the specified provider and resets the provider card UI.
    /// </summary>
    private void RemoveProviderKey(string provider, Grid inputPanel, Grid displayPanel, ComboBox modelCombo, StackPanel optionsPanel, CollapsibleSection section)
    {
        PasswordVaultHelper.DeleteApiKey(provider);

        var existing = FindPreset(provider);
        if (existing is not null)
            _presets.Remove(existing);

        inputPanel.Visibility = Visibility.Visible;
        displayPanel.Visibility = Visibility.Collapsed;
        modelCombo.IsEnabled = false;
        optionsPanel.Visibility = Visibility.Collapsed;

        UpdateProviderSubHeader(section, modelCombo, hasKey: false);
        PersistPresets();
    }

    #endregion

    #region API Key Event Handlers

    /// <summary>
    /// Handles adding the Claude API key.
    /// </summary>
    private void OnAddClaudeKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("Claude", ClaudeApiKeyBox, ClaudeKeyInputPanel, ClaudeKeyDisplayPanel, ClaudeMaskedKeyText, ClaudeModelCombo, ClaudeOptionsPanel, ClaudeSection);

    /// <summary>
    /// Handles adding the OpenAI API key.
    /// </summary>
    private void OnAddOpenAIKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("OpenAI", OpenAIApiKeyBox, OpenAIKeyInputPanel, OpenAIKeyDisplayPanel, OpenAIMaskedKeyText, OpenAIModelCombo, OpenAIOptionsPanel, OpenAISection);

    /// <summary>
    /// Handles adding the Gemini API key.
    /// </summary>
    private void OnAddGeminiKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("Gemini", GeminiApiKeyBox, GeminiKeyInputPanel, GeminiKeyDisplayPanel, GeminiMaskedKeyText, GeminiModelCombo, GeminiOptionsPanel, GeminiSection);

    /// <summary>
    /// Handles adding the Groq API key.
    /// </summary>
    private void OnAddGroqKey(object sender, RoutedEventArgs e) =>
        AddProviderKey("Groq", GroqApiKeyBox, GroqKeyInputPanel, GroqKeyDisplayPanel, GroqMaskedKeyText, GroqModelCombo, GroqOptionsPanel, GroqSection);

    /// <summary>
    /// Handles removing the Claude API key.
    /// </summary>
    private void OnRemoveClaudeKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Claude", ClaudeKeyInputPanel, ClaudeKeyDisplayPanel, ClaudeModelCombo, ClaudeOptionsPanel, ClaudeSection);

    /// <summary>
    /// Handles removing the OpenAI API key.
    /// </summary>
    private void OnRemoveOpenAIKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("OpenAI", OpenAIKeyInputPanel, OpenAIKeyDisplayPanel, OpenAIModelCombo, OpenAIOptionsPanel, OpenAISection);

    /// <summary>
    /// Handles removing the Gemini API key.
    /// </summary>
    private void OnRemoveGeminiKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Gemini", GeminiKeyInputPanel, GeminiKeyDisplayPanel, GeminiModelCombo, GeminiOptionsPanel, GeminiSection);

    /// <summary>
    /// Handles removing the Groq API key.
    /// </summary>
    private void OnRemoveGroqKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Groq", GroqKeyInputPanel, GroqKeyDisplayPanel, GroqModelCombo, GroqOptionsPanel, GroqSection);

    #endregion

    #region Model ComboBox Management

    /// <summary>
    /// Populates all provider model combo boxes from cache or fallback values, then starts background refresh.
    /// </summary>
    private void PopulateModelCombos()
    {
        // Suppress SelectionChanged → PersistPresets during initial population
        _isPopulating = true;
        try
        {
            PopulateComboFromCacheOrFallback("Claude", ClaudeModelCombo, FallbackClaudeModels);
            PopulateComboFromCacheOrFallback("OpenAI", OpenAIModelCombo, FallbackOpenAIModels);
            PopulateComboFromCacheOrFallback("Gemini", GeminiModelCombo, FallbackGeminiModels);
            PopulateComboFromCacheOrFallback("Groq", GroqModelCombo, FallbackGroqModels);

            PopulatePdfCombos();
            PopulateThinkingToggles();
        }
        finally
        {
            _isPopulating = false;
        }

        // Background refresh for providers that have API keys
        _ = RefreshAllModelCachesAsync();
    }

    /// <summary>
    /// Thinking override option items used in the thinking override combo boxes.
    /// </summary>
    private static readonly (ThinkingOverride Value, string LabelKey)[] ThinkingOverrideOptions =
    [
        (ThinkingOverride.Auto, "Models_ThinkingOverrideAuto"),
        (ThinkingOverride.ForceOn, "Models_ThinkingOverrideForceOn"),
        (ThinkingOverride.ForceOff, "Models_ThinkingOverrideForceOff"),
    ];

    /// <summary>
    /// PDF handling option items used in the PDF combo boxes.
    /// </summary>
    private static readonly (PdfCapability Value, string LabelKey)[] PdfOptions =
    [
        (PdfCapability.Auto, "Models_PdfAuto"),
        (PdfCapability.NativePdf, "Models_PdfNative"),
        (PdfCapability.PageAsImage, "Models_PdfPageAsImage"),
        (PdfCapability.TextExtraction, "Models_PdfTextExtraction"),
    ];

    /// <summary>
    /// Populates all PDF handling combo boxes and selects the saved value from presets.
    /// </summary>
    private void PopulatePdfCombos()
    {
        var presets = _presets.ToDictionary(p => p.ProviderType) ?? [];

        var combos = new (string Provider, ComboBox Combo)[]
        {
            ("Claude", ClaudePdfCombo),
            ("OpenAI", OpenAIPdfCombo),
            ("Gemini", GeminiPdfCombo),
            ("Groq", GroqPdfCombo),
            ("Ollama", OllamaPdfCombo),
        };

        foreach (var (provider, combo) in combos)
        {
            combo.Items.Clear();
            foreach (var (_, labelKey) in PdfOptions)
                combo.Items.Add(L(labelKey));

            var saved = presets.TryGetValue(provider, out var p) ? p.PdfCapability : PdfCapability.Auto;
            var idx = Array.FindIndex(PdfOptions, o => o.Value == saved);
            combo.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    /// <summary>
    /// Populates all Extended Thinking toggle switches, budget boxes, and override combos
    /// from saved presets, then applies model-based thinking capability checks.
    /// </summary>
    private void PopulateThinkingToggles()
    {
        var presets = _presets.ToDictionary(p => p.ProviderType) ?? [];

        var controls = new (string Provider, ToggleSwitch Toggle, NumberBox Budget, ComboBox OverrideCombo)[]
        {
            ("Claude", ClaudeThinkingToggle, ClaudeThinkingBudget, ClaudeThinkingOverrideCombo),
            ("OpenAI", OpenAIThinkingToggle, OpenAIThinkingBudget, OpenAIThinkingOverrideCombo),
            ("Gemini", GeminiThinkingToggle, GeminiThinkingBudget, GeminiThinkingOverrideCombo),
            ("Groq", GroqThinkingToggle, GroqThinkingBudget, GroqThinkingOverrideCombo),
        };

        foreach (var (provider, toggle, budget, overrideCombo) in controls)
        {
            // Populate override combo
            overrideCombo.Items.Clear();
            foreach (var (_, labelKey) in ThinkingOverrideOptions)
                overrideCombo.Items.Add(L(labelKey));

            if (presets.TryGetValue(provider, out var p))
            {
                toggle.IsOn = p.ThinkingEnabled;
                budget.Value = p.ThinkingBudget ?? 4096;
                budget.IsEnabled = p.ThinkingEnabled;

                var overrideIdx = Array.FindIndex(ThinkingOverrideOptions, o => o.Value == p.ThinkingOverride);
                overrideCombo.SelectedIndex = overrideIdx >= 0 ? overrideIdx : 0;
            }
            else
            {
                toggle.IsOn = false;
                budget.Value = 4096;
                budget.IsEnabled = false;
                overrideCombo.SelectedIndex = 0; // Auto
            }
        }

        // Apply thinking capability checks based on selected models and overrides
        UpdateAllThinkingStates();
    }

    /// <summary>
    /// Updates the thinking UI state for all cloud providers based on currently selected models and overrides.
    /// </summary>
    private void UpdateAllThinkingStates()
    {
        var entries = new (string Provider, ComboBox ModelCombo, ComboBox OverrideCombo, ToggleSwitch Toggle, NumberBox Budget, TextBlock Hint)[]
        {
            ("Claude", ClaudeModelCombo, ClaudeThinkingOverrideCombo, ClaudeThinkingToggle, ClaudeThinkingBudget, ClaudeThinkingHint),
            ("OpenAI", OpenAIModelCombo, OpenAIThinkingOverrideCombo, OpenAIThinkingToggle, OpenAIThinkingBudget, OpenAIThinkingHint),
            ("Gemini", GeminiModelCombo, GeminiThinkingOverrideCombo, GeminiThinkingToggle, GeminiThinkingBudget, GeminiThinkingHint),
            ("Groq", GroqModelCombo, GroqThinkingOverrideCombo, GroqThinkingToggle, GroqThinkingBudget, GroqThinkingHint),
        };

        foreach (var (provider, modelCombo, overrideCombo, toggle, budget, hint) in entries)
            UpdateThinkingState(provider, modelCombo, overrideCombo, toggle, budget, hint);
    }

    /// <summary>
    /// Updates the Extended Thinking toggle, budget, and hint visibility based on
    /// the provider's thinking support heuristic and the user's override selection.
    /// </summary>
    private static void UpdateThinkingState(string provider, ComboBox modelCombo, ComboBox overrideCombo, ToggleSwitch toggle, NumberBox budget, TextBlock hint)
    {
        var modelId = modelCombo.SelectedItem as string;
        var support = ThinkingCapability.GetSupport(provider, modelId);

        // Resolve user override
        var overrideIdx = overrideCombo.SelectedIndex;
        var userOverride = overrideIdx >= 0 && overrideIdx < ThinkingOverrideOptions.Length
            ? ThinkingOverrideOptions[overrideIdx].Value
            : ThinkingOverride.Auto;

        switch (userOverride)
        {
            case ThinkingOverride.ForceOn:
                // User forces thinking on regardless of provider heuristic
                toggle.IsOn = true;
                toggle.IsEnabled = true;
                budget.IsEnabled = true;
                hint.Visibility = Visibility.Collapsed;
                break;

            case ThinkingOverride.ForceOff:
                // User forces thinking off regardless of provider heuristic
                toggle.IsOn = false;
                toggle.IsEnabled = true;
                budget.IsEnabled = false;
                hint.Visibility = Visibility.Collapsed;
                break;

            default: // Auto
                switch (support)
                {
                    case ThinkingSupport.NotSupported:
                        toggle.IsEnabled = false;
                        toggle.IsOn = false;
                        budget.IsEnabled = false;
                        hint.Text = L("Models_ThinkingNotSupported");
                        hint.Visibility = Visibility.Visible;
                        break;

                    case ThinkingSupport.Required:
                        toggle.IsOn = true;
                        toggle.IsEnabled = false;
                        budget.IsEnabled = true;
                        hint.Text = L("Models_ThinkingAlwaysOn");
                        hint.Visibility = Visibility.Visible;
                        break;

                    case ThinkingSupport.Optional:
                    default:
                        toggle.IsEnabled = true;
                        budget.IsEnabled = toggle.IsOn;
                        hint.Visibility = Visibility.Collapsed;
                        break;
                }
                break;
        }
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
        // Reuse API keys from already-loaded presets to avoid redundant vault calls
        var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey);
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
        catch (Exception ex)
        {
            // API unreachable — keep current list (cache or fallback)
            LoggingService.LogException(ex);
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
    /// Finds the preset for the specified provider type in the current collection.
    /// </summary>
    private ProviderPreset? FindPreset(string providerType)
        => _presets.FirstOrDefault(p => p.ProviderType == providerType);

    /// <summary>
    /// Persists the current preset collection to settings and fires the PresetsChanged event.
    /// </summary>
    private void PersistPresets() => AppSettings.SavePresets(_presets);

    /// <summary>
    /// Handles cloud provider model combo box selection changes to persist the model and sync presets.
    /// </summary>
    private void OnCloudModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
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

        // Update thinking UI state based on new model selection
        var (overrideCombo, toggle, budgetBox, hint) = provider switch
        {
            "Claude" => ((ComboBox)ClaudeThinkingOverrideCombo, (ToggleSwitch)ClaudeThinkingToggle, (NumberBox)ClaudeThinkingBudget, (TextBlock)ClaudeThinkingHint),
            "OpenAI" => (OpenAIThinkingOverrideCombo, OpenAIThinkingToggle, OpenAIThinkingBudget, OpenAIThinkingHint),
            "Gemini" => (GeminiThinkingOverrideCombo, GeminiThinkingToggle, GeminiThinkingBudget, GeminiThinkingHint),
            "Groq" => (GroqThinkingOverrideCombo, GroqThinkingToggle, GroqThinkingBudget, GroqThinkingHint),
            _ => ((ComboBox?)null, (ToggleSwitch?)null, (NumberBox?)null, (TextBlock?)null)
        };
        // Guard against cascading events from UpdateThinkingState setting toggle.IsOn
        _isPopulating = true;
        if (overrideCombo is not null && toggle is not null && budgetBox is not null && hint is not null)
            UpdateThinkingState(provider, combo, overrideCombo, toggle, budgetBox, hint);
        _isPopulating = false;

        // Update sub-header to reflect new model selection
        var section = GetSectionForProvider(provider);
        if (section is not null)
            UpdateProviderSubHeader(section, combo, hasKey: true);

        AppSettings.SetDefaultModel(provider, model);

        var preset = FindPreset(provider);
        if (preset is not null)
        {
            preset.ModelId = model;
            preset.ThinkingEnabled = toggle?.IsOn ?? false;
            preset.ThinkingBudget = toggle?.IsOn == true && budgetBox is not null && !double.IsNaN(budgetBox.Value)
                ? (int)budgetBox.Value : null;
            preset.ThinkingOverride = GetThinkingOverrideFromCombo(overrideCombo);
        }
        PersistPresets();
    }

    /// <summary>
    /// Handles PDF handling combo box selection changes for any provider.
    /// </summary>
    private void OnPdfHandlingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        if (sender is not ComboBox combo) return;

        var provider = combo.Name switch
        {
            nameof(ClaudePdfCombo) => "Claude",
            nameof(OpenAIPdfCombo) => "OpenAI",
            nameof(GeminiPdfCombo) => "Gemini",
            nameof(GroqPdfCombo) => "Groq",
            nameof(OllamaPdfCombo) => "Ollama",
            _ => (string?)null
        };
        if (provider is null) return;

        var preset = FindPreset(provider);
        if (preset is not null)
            preset.PdfCapability = GetPdfCapabilityFromCombo(combo);
        PersistPresets();
    }

    /// <summary>
    /// Handles the Extended Thinking toggle switch for any provider.
    /// Enables/disables the corresponding budget NumberBox and syncs presets.
    /// </summary>
    private void OnThinkingToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating) return;
        if (sender is not ToggleSwitch toggle || !toggle.IsEnabled) return;

        var (provider, budgetBox) = toggle.Name switch
        {
            nameof(ClaudeThinkingToggle) => ("Claude", (NumberBox?)ClaudeThinkingBudget),
            nameof(OpenAIThinkingToggle) => ("OpenAI", (NumberBox?)OpenAIThinkingBudget),
            nameof(GeminiThinkingToggle) => ("Gemini", (NumberBox?)GeminiThinkingBudget),
            nameof(GroqThinkingToggle) => ("Groq", (NumberBox?)GroqThinkingBudget),
            _ => ((string?)null, (NumberBox?)null)
        };
        if (provider is null) return;

        if (budgetBox is not null)
            budgetBox.IsEnabled = toggle.IsOn;

        var preset = FindPreset(provider);
        if (preset is not null)
        {
            preset.ThinkingEnabled = toggle.IsOn;
            preset.ThinkingBudget = toggle.IsOn && budgetBox is not null && !double.IsNaN(budgetBox.Value)
                ? (int)budgetBox.Value : null;
        }
        PersistPresets();
    }

    /// <summary>
    /// Handles the Thinking Override combo box selection changes for any provider.
    /// Updates the thinking UI state and syncs presets.
    /// </summary>
    private void OnThinkingOverrideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;

        // Guard against cascading events from UpdateAllThinkingStates setting toggle.IsOn
        _isPopulating = true;
        UpdateAllThinkingStates();
        _isPopulating = false;

        if (sender is not ComboBox combo) return;
        var provider = combo.Name switch
        {
            nameof(ClaudeThinkingOverrideCombo) => "Claude",
            nameof(OpenAIThinkingOverrideCombo) => "OpenAI",
            nameof(GeminiThinkingOverrideCombo) => "Gemini",
            nameof(GroqThinkingOverrideCombo) => "Groq",
            _ => (string?)null
        };
        if (provider is null) return;

        var (toggle, budgetBox) = provider switch
        {
            "Claude" => ((ToggleSwitch)ClaudeThinkingToggle, (NumberBox)ClaudeThinkingBudget),
            "OpenAI" => (OpenAIThinkingToggle, OpenAIThinkingBudget),
            "Gemini" => (GeminiThinkingToggle, GeminiThinkingBudget),
            "Groq" => (GroqThinkingToggle, GroqThinkingBudget),
            _ => ((ToggleSwitch?)null, (NumberBox?)null)
        };

        var preset = FindPreset(provider);
        if (preset is not null)
        {
            preset.ThinkingOverride = GetThinkingOverrideFromCombo(combo);
            preset.ThinkingEnabled = toggle?.IsOn ?? false;
            preset.ThinkingBudget = toggle?.IsOn == true && budgetBox is not null && !double.IsNaN(budgetBox.Value)
                ? (int)budgetBox.Value : null;
        }
        PersistPresets();
    }

    /// <summary>
    /// Handles thinking budget NumberBox value changes.
    /// </summary>
    private void OnThinkingBudgetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating) return;

        var provider = sender.Name switch
        {
            nameof(ClaudeThinkingBudget) => "Claude",
            nameof(OpenAIThinkingBudget) => "OpenAI",
            nameof(GeminiThinkingBudget) => "Gemini",
            nameof(GroqThinkingBudget) => "Groq",
            _ => (string?)null
        };
        if (provider is null) return;

        var preset = FindPreset(provider);
        if (preset is not null)
            preset.ThinkingBudget = !double.IsNaN(sender.Value) ? (int)sender.Value : null;
        PersistPresets();
    }

    /// <summary>
    /// Resolves the selected <see cref="PdfCapability"/> from a PDF combo box.
    /// </summary>
    private static PdfCapability GetPdfCapabilityFromCombo(ComboBox combo)
    {
        var idx = combo.SelectedIndex;
        return idx >= 0 && idx < PdfOptions.Length ? PdfOptions[idx].Value : PdfCapability.Auto;
    }

    /// <summary>
    /// Resolves the selected <see cref="ThinkingOverride"/> from a thinking override combo box.
    /// </summary>
    private static ThinkingOverride GetThinkingOverrideFromCombo(ComboBox? combo)
    {
        if (combo is null) return ThinkingOverride.Auto;
        var idx = combo.SelectedIndex;
        return idx >= 0 && idx < ThinkingOverrideOptions.Length ? ThinkingOverrideOptions[idx].Value : ThinkingOverride.Auto;
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
            UpdateOllamaSubHeader();

            var preset = FindPreset("Ollama");
            if (preset is not null)
                preset.ModelId = model;
            PersistPresets();
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
    /// Handles the Ollama URL TextBox losing focus to save the URL and re-check status.
    /// </summary>
    private void OnOllamaUrlChanged(object sender, RoutedEventArgs e)
    {
        var url = GetOllamaBaseUrlFromUI();
        AppSettings.SetOllamaBaseUrl(url);

        var preset = FindPreset("Ollama");
        if (preset is not null)
            preset.BaseUrl = url;
        PersistPresets();
    }

    /// <summary>
    /// Resets the Ollama URL to localhost default.
    /// </summary>
    private void OnOllamaLocalhost(object sender, RoutedEventArgs e)
    {
        OllamaUrlBox.Text = "http://localhost:11434";
        AppSettings.SetOllamaBaseUrl(null);

        var preset = FindPreset("Ollama");
        if (preset is not null)
            preset.BaseUrl = null;
        PersistPresets();

        _ = CheckOllamaStatusAsync();
    }

    /// <summary>
    /// Tests connectivity to the Ollama server URL entered by the user.
    /// </summary>
    private async void OnTestOllamaUrl(object sender, RoutedEventArgs e)
    {
        await CheckOllamaStatusAsync();
    }

    /// <summary>
    /// Returns the Ollama base URL from the UI TextBox, or <c>null</c> if it is the default localhost.
    /// </summary>
    private string? GetOllamaBaseUrlFromUI()
    {
        var url = OllamaUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url) || url == "http://localhost:11434")
            return null;
        return url;
    }

    /// <summary>
    /// Determines whether the configured Ollama URL points to a remote (non-localhost) host.
    /// </summary>
    private bool IsOllamaRemote()
    {
        var url = OllamaUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host != "localhost" && uri.Host != "127.0.0.1" && uri.Host != "::1";
        return false;
    }

    /// <summary>
    /// Delays Ollama status check to avoid blocking page navigation.
    /// </summary>
    private async Task DelayedCheckOllamaAsync()
    {
        await Task.Delay(300);
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
        OllamaStatusText.ClearValue(TextBlock.ForegroundProperty);
        StartOllamaButton.Visibility = Visibility.Collapsed;
        OllamaInstallPanel.Visibility = Visibility.Collapsed;
        BrowseModelsButton.Visibility = Visibility.Collapsed;

        try
        {
            var isRemote = IsOllamaRemote();

            if (isRemote)
            {
                // Remote host: use OllamaProvider.ValidateConnectionAsync with timeout
                var baseUrl = OllamaUrlBox.Text.Trim();
                using var provider = new OllamaProvider(baseUrl: baseUrl);
                var result = await provider.ValidateConnectionAsync();

                if (result.IsValid)
                {
                    OllamaStatusText.Text = L("Models_Running");
                    OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusAccentForegroundBrush");
                    BrowseModelsButton.Visibility = Visibility.Visible;
                    await LoadOllamaModelsAsync();
                }
                else
                {
                    OllamaStatusText.Text = result.ErrorMessage ?? L("Models_Error");
                    OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
                }
            }
            else
            {
                // Local host: use OllamaHelper for install/process detection
                var isRunning = await OllamaHelper.IsOllamaRunningAsync();
                if (isRunning)
                {
                    OllamaStatusText.Text = L("Models_Running");
                    OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusAccentForegroundBrush");
                    BrowseModelsButton.Visibility = Visibility.Visible;
                    await LoadOllamaModelsAsync();
                }
                else if (OllamaHelper.IsOllamaInstalled())
                {
                    OllamaStatusText.Text = L("Models_InstalledNotRunning");
                    OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
                    StartOllamaButton.Visibility = Visibility.Visible;
                }
                else
                {
                    OllamaStatusText.Text = L("Models_NotInstalled");
                    OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
                    OllamaInstallPanel.Visibility = Visibility.Visible;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            OllamaStatusText.Text = L("Models_Error");
            OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
        }
        finally
        {
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
            UpdateOllamaSubHeader();
        }
    }

    /// <summary>
    /// Loads locally installed Ollama models, classifies their hardware fit, and populates the combo box.
    /// </summary>
    private async Task LoadOllamaModelsAsync()
    {
        try
        {
            var baseUrl = GetOllamaBaseUrlFromUI() ?? "http://localhost:11434";
            using var manager = new OllamaModelManager(baseUrl);
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
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
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
                OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            OllamaStatusText.Text = L("Models_ErrorStarting");
            OllamaStatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
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
        var baseUrl = GetOllamaBaseUrlFromUI() ?? "http://localhost:11434";
        using var manager = new OllamaModelManager(baseUrl);
        var dialog = new ModelSelectionDialog(manager)
        {
            XamlRoot = XamlRoot,
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
    /// Kicks off model downloads and tracks progress. If the page is navigated away,
    /// tracking stops but Ollama continues downloading server-side.
    /// Re-entering the page resumes progress display automatically.
    /// </summary>
    private async Task PullModelsAsync(List<string> modelNames)
    {
        // Register pending pulls (static, survives page re-creation)
        foreach (var name in modelNames)
        {
            if (!_pendingPulls.Contains(name))
                _pendingPulls.Add(name);
        }

        await TrackPullProgressAsync();
    }

    /// <summary>
    /// Resumes progress tracking for downloads that were started before the page was navigated away.
    /// </summary>
    private Task ResumePullTrackingAsync() => TrackPullProgressAsync();

    /// <summary>
    /// Tracks download progress for all models in <see cref="_pendingPulls"/> sequentially.
    /// Cancellation via <see cref="_pullCts"/> stops only the tracking — Ollama continues server-side.
    /// </summary>
    private async Task TrackPullProgressAsync()
    {
        _pullCts?.Cancel();
        _pullCts = new CancellationTokenSource();
        var ct = _pullCts.Token;

        PullProgressPanel.Visibility = Visibility.Visible;
        BrowseModelsButton.IsEnabled = false;

        var baseUrl = GetOllamaBaseUrlFromUI() ?? "http://localhost:11434";
        using var manager = new OllamaModelManager(baseUrl);

        while (_pendingPulls.Count > 0)
        {
            var modelName = _pendingPulls[0];
            var remaining = _pendingPulls.Count;
            var prefix = remaining > 1 ? $"[1/{remaining}] " : "";

            PullProgressStatus.Text = $"{prefix}{string.Format(L("Models_PullingModel"), modelName)}";
            PullProgressBar.IsIndeterminate = true;
            PullProgressBar.Value = 0;

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ct.IsCancellationRequested) return;
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
                // Ollama /api/pull joins an existing download if one is already in progress
                LoggingService.LogInfo($"[Pull] Starting download: {modelName}");
                await manager.DownloadModelAsync(modelName, progress, ct);
                LoggingService.LogInfo($"[Pull] Completed: {modelName}");
                _pendingPulls.Remove(modelName);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Page navigated away — Ollama continues downloading server-side
                LoggingService.LogInfo($"[Pull] Tracking stopped (page navigated away): {modelName}");
                break;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[Pull] Failed: {modelName} — {ex.Message}");
                _pendingPulls.Remove(modelName);
                PullProgressStatus.Text = $"{prefix}Error: {ex.Message}";
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        if (!ct.IsCancellationRequested)
        {
            PullProgressBar.IsIndeterminate = false;
            PullProgressPanel.Visibility = Visibility.Collapsed;
            BrowseModelsButton.IsEnabled = true;
        }
    }

    #endregion
}
