using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;

namespace AssistStudio.Controls;

/// <summary>
/// Reusable settings section for a cloud AI provider (Claude, OpenAI, Gemini, Groq).
/// Handles API key management, model selection, and provider-specific options.
/// </summary>
public sealed partial class CloudProviderSection : UserControl
{
    #region Fields

    private ObservableCollection<ProviderPreset> _presets = [];
    private bool _isPopulating;
    private bool _initialized;

    #endregion

    #region Constants

    private static readonly (ThinkingOverride Value, string LabelKey)[] ThinkingOverrideOptions =
    [
        (ThinkingOverride.Auto, "Models_ThinkingOverrideAuto"),
        (ThinkingOverride.ForceOn, "Models_ThinkingOverrideForceOn"),
        (ThinkingOverride.ForceOff, "Models_ThinkingOverrideForceOff"),
    ];

    private static readonly (PdfCapability Value, string LabelKey)[] PdfOptions =
    [
        (PdfCapability.Auto, "Models_PdfAuto"),
        (PdfCapability.NativePdf, "Models_PdfNative"),
        (PdfCapability.PageAsImage, "Models_PdfPageAsImage"),
        (PdfCapability.TextExtraction, "Models_PdfTextExtraction"),
    ];

    #endregion

    #region Dependency Properties

    /// <summary>
    /// The provider type identifier (e.g. "Claude", "OpenAI", "Gemini", "Groq").
    /// </summary>
    public static readonly DependencyProperty ProviderTypeProperty =
        DependencyProperty.Register(nameof(ProviderType), typeof(string), typeof(CloudProviderSection),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Fallback model IDs to use when the API cache is empty.
    /// Set as a comma-separated string in XAML (parsed in code).
    /// </summary>
    public static readonly DependencyProperty FallbackModelsStringProperty =
        DependencyProperty.Register(nameof(FallbackModelsString), typeof(string), typeof(CloudProviderSection),
            new PropertyMetadata(string.Empty));

    #endregion

    #region Properties

    /// <summary>Gets or sets the provider type.</summary>
    public string ProviderType
    {
        get => (string)GetValue(ProviderTypeProperty);
        set => SetValue(ProviderTypeProperty, value);
    }

    /// <summary>Gets or sets the fallback models as a comma-separated string.</summary>
    public string FallbackModelsString
    {
        get => (string)GetValue(FallbackModelsStringProperty);
        set => SetValue(FallbackModelsStringProperty, value);
    }

    private string[] FallbackModels =>
        string.IsNullOrEmpty(FallbackModelsString) ? [] : FallbackModelsString.Split(',');

    #endregion

    #region Events

    /// <summary>
    /// Raised when the sub-header text should be updated (model + key status).
    /// </summary>
    public event EventHandler<string>? SubHeaderChanged;

    #endregion

    #region Constructor

    public CloudProviderSection()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes the section UI from the current presets. Called once after x:Load realization.
    /// </summary>
    public void Initialize(ObservableCollection<ProviderPreset> presets)
    {
        if (_initialized) return;
        _initialized = true;
        _presets = presets;

        _isPopulating = true;
        try
        {
            var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey) ?? [];
            keys.TryGetValue(ProviderType, out var apiKey);
            SetKeyState(apiKey ?? "");

            PopulateModelCombo();
            PopulatePdfCombo();
            PopulateMaxTokens();
            PopulateThinkingToggle();
            UpdateThinkingState();
            UpdateSubHeader();
        }
        finally { _isPopulating = false; }

        // Background model refresh if key exists
        var preset = FindPreset();
        if (!string.IsNullOrEmpty(preset?.ApiKey))
        {
            // Skip fetch for custom providers whose /models endpoint previously failed
            if (ProviderType.StartsWith("Custom_") && AppSettings.GetModelsEndpointFailed(ProviderType))
                EnableEditableModelCombo();
            else
                _ = FetchAndCacheModelsAsync(preset.ApiKey);
        }
    }

    #endregion

    #region API Key Management

    private void SetKeyState(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            KeyInputPanel.Visibility = Visibility.Collapsed;
            KeyDisplayPanel.Visibility = Visibility.Visible;
            MaskedKeyText.Text = MaskKey(key);
            ModelCombo.IsEnabled = true;
            OptionsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            KeyInputPanel.Visibility = Visibility.Visible;
            KeyDisplayPanel.Visibility = Visibility.Collapsed;
            ModelCombo.IsEnabled = false;
            OptionsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string MaskKey(string key)
    {
        if (key.Length <= 6) return "••••••";
        return key[..3] + "•••" + key[^3..];
    }

    private void OnAddKey(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password?.Trim();
        if (string.IsNullOrEmpty(key)) return;

        PasswordVaultHelper.SaveApiKey(ProviderType, key);
        ApiKeyBox.Password = "";

        // Update preset FIRST so SubHeader and FetchModels see the key
        var preset = FindPreset();
        if (preset is not null)
        {
            preset.ApiKey = key;
        }
        else
        {
            var newPreset = new ProviderPreset
            {
                Name = ProviderToDisplayName(ProviderType),
                ProviderType = ProviderType,
                ModelId = ModelCombo.SelectedItem as string
                          ?? AppSettings.GetDefaultModel(ProviderType) ?? "",
                ApiKey = key,
                BaseUrl = ProviderType == "Groq" ? "https://api.groq.com/openai/v1" : null,
            };
            var ollamaIdx = _presets.ToList().FindIndex(p => p.ProviderType == "Ollama");
            _presets.Insert(ollamaIdx >= 0 ? ollamaIdx : _presets.Count, newPreset);
        }

        SetKeyState(key);
        UpdateSubHeader();
        PersistPresets();

        AppSettings.ClearModelsEndpointFailed(ProviderType);
        _ = FetchAndCacheModelsAsync(key);
    }

    private void OnRemoveKey(object sender, RoutedEventArgs e)
    {
        PasswordVaultHelper.DeleteApiKey(ProviderType);

        var existing = FindPreset();
        if (existing is not null)
            _presets.Remove(existing);

        SetKeyState("");
        UpdateSubHeader();
        PersistPresets();
    }

    #endregion

    #region Model ComboBox

    private void PopulateModelCombo()
    {
        var cached = AppSettings.GetCachedModels(ProviderType);
        var models = cached?.ToArray() ?? FallbackModels;
        var savedModel = AppSettings.GetDefaultModel(ProviderType);
        PopulateCombo(models, savedModel);
    }

    private void PopulateCombo(string[] models, string? savedModel)
    {
        ModelCombo.Items.Clear();
        foreach (var m in models) ModelCombo.Items.Add(m);

        if (!string.IsNullOrEmpty(savedModel))
        {
            var idx = Array.IndexOf(models, savedModel);
            if (idx >= 0) { ModelCombo.SelectedIndex = idx; return; }
        }

        if (models.Length > 0)
            ModelCombo.SelectedIndex = 0;
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        if (ModelCombo.SelectedItem is not string model || string.IsNullOrEmpty(model)) return;

        ApplyModelSelection(model);
    }

    /// <summary>
    /// Handles manual model ID input in editable ComboBox (custom providers with no /models endpoint).
    /// </summary>
    private void OnModelTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (_isPopulating) return;
        var model = args.Text?.Trim();
        if (string.IsNullOrEmpty(model)) return;

        // Accept the custom text as a valid value
        args.Handled = true;

        // Add to items if not already present
        if (!ModelCombo.Items.Contains(model))
            ModelCombo.Items.Add(model);
        ModelCombo.SelectedItem = model;

        ApplyModelSelection(model);
    }

    private void ApplyModelSelection(string model)
    {
        _isPopulating = true;
        UpdateThinkingState();
        _isPopulating = false;

        UpdateSubHeader();
        AppSettings.SetDefaultModel(ProviderType, model);

        var preset = FindPreset();
        if (preset is not null)
        {
            preset.ModelId = model;
            preset.ThinkingEnabled = ThinkingToggle.IsOn;
            preset.ThinkingBudget = ThinkingToggle.IsOn && !double.IsNaN(ThinkingBudgetBox.Value)
                ? (int)ThinkingBudgetBox.Value : null;
            preset.ThinkingOverride = GetThinkingOverrideFromCombo();
        }
        PersistPresets();
    }

    private async Task FetchAndCacheModelsAsync(string apiKey)
    {
        try
        {
            var provider = CreateProviderForListing(apiKey);
            try
            {
                var models = await provider.ListModelsAsync();
                var filtered = FilterChatModels(models);

                if (filtered.Count == 0) return;

                AppSettings.SetCachedModels(ProviderType, filtered);

                DispatcherQueue.TryEnqueue(() =>
                {
                    _isPopulating = true;
                    try
                    {
                        var current = ModelCombo.SelectedItem as string;
                        PopulateCombo([.. filtered], current ?? AppSettings.GetDefaultModel(ProviderType));
                    }
                    finally { _isPopulating = false; }
                });
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);

            // Custom providers: enable manual model ID input when /models endpoint is unavailable
            if (ProviderType.StartsWith("Custom_"))
            {
                AppSettings.SetModelsEndpointFailed(ProviderType);
                DispatcherQueue.TryEnqueue(EnableEditableModelCombo);
            }
        }
    }

    private IAiProvider CreateProviderForListing(string apiKey)
    {
        // Custom providers: construct OpenAiProvider directly with the passed apiKey
        if (ProviderType.StartsWith("Custom_"))
        {
            var preset = FindPreset();
            if (preset?.BaseUrl is not null)
                return new OpenAiProvider(apiKey, "dummy", preset.BaseUrl, preset.Name);
        }

        return ProviderType switch
        {
            "Claude" => new ClaudeProvider(apiKey, "dummy"),
            "OpenAI" => new OpenAiProvider(apiKey, "dummy"),
            "Gemini" => new GeminiProvider(apiKey, "dummy"),
            "Groq" => new OpenAiProvider(apiKey, "dummy",
                baseUrl: "https://api.groq.com/openai/v1", providerName: "Groq"),
            _ => throw new ArgumentException($"Unknown provider: {ProviderType}")
        };
    }

    private List<string> FilterChatModels(IReadOnlyList<AiModel> models) => ProviderType switch
    {
        "Claude" => [.. models.Where(m => m.Id.StartsWith("claude-")).Select(m => m.Id)],
        "OpenAI" => [.. models.Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3") || m.Id.StartsWith("o4")).Select(m => m.Id)],
        "Gemini" => [.. models.Where(m => m.Id.StartsWith("gemini-")).Select(m => m.Id)],
        "Groq" => [.. models.Select(m => m.Id)],
        _ => [.. models.Select(m => m.Id)]
    };

    #endregion

    #region Options

    private void PopulatePdfCombo()
    {
        PdfCombo.Items.Clear();
        foreach (var (_, labelKey) in PdfOptions)
            PdfCombo.Items.Add(L(labelKey));
        var saved = FindPreset()?.PdfCapability ?? PdfCapability.Auto;
        var idx = Array.FindIndex(PdfOptions, o => o.Value == saved);
        PdfCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void PopulateMaxTokens()
    {
        var preset = FindPreset();
        if (preset is not null)
            MaxTokensBox.Value = preset.MaxTokens;
    }

    private void PopulateThinkingToggle()
    {
        ThinkingOverrideCombo.Items.Clear();
        foreach (var (_, labelKey) in ThinkingOverrideOptions)
            ThinkingOverrideCombo.Items.Add(L(labelKey));

        var preset = FindPreset();
        if (preset is not null)
        {
            ThinkingToggle.IsOn = preset.ThinkingEnabled;
            ThinkingBudgetBox.Value = preset.ThinkingBudget ?? 4096;
            ThinkingBudgetBox.IsEnabled = preset.ThinkingEnabled;
            var idx = Array.FindIndex(ThinkingOverrideOptions, o => o.Value == preset.ThinkingOverride);
            ThinkingOverrideCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            ThinkingToggle.IsOn = false;
            ThinkingBudgetBox.Value = 4096;
            ThinkingBudgetBox.IsEnabled = false;
            ThinkingOverrideCombo.SelectedIndex = 0;
        }
    }

    private void OnPdfHandlingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        var preset = FindPreset();
        if (preset is not null)
            preset.PdfCapability = GetPdfCapabilityFromCombo();
        PersistPresets();
    }

    private void OnMaxTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating || double.IsNaN(args.NewValue)) return;
        var preset = FindPreset();
        if (preset is not null)
            preset.MaxTokens = (int)args.NewValue;
        PersistPresets();
    }

    private void OnThinkingToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating || !ThinkingToggle.IsEnabled) return;
        ThinkingBudgetBox.IsEnabled = ThinkingToggle.IsOn;

        var preset = FindPreset();
        if (preset is not null)
        {
            preset.ThinkingEnabled = ThinkingToggle.IsOn;
            preset.ThinkingBudget = ThinkingToggle.IsOn && !double.IsNaN(ThinkingBudgetBox.Value)
                ? (int)ThinkingBudgetBox.Value : null;
        }
        PersistPresets();
    }

    private void OnThinkingOverrideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;

        _isPopulating = true;
        UpdateThinkingState();
        _isPopulating = false;

        var preset = FindPreset();
        if (preset is not null)
        {
            preset.ThinkingOverride = GetThinkingOverrideFromCombo();
            preset.ThinkingEnabled = ThinkingToggle.IsOn;
            preset.ThinkingBudget = ThinkingToggle.IsOn && !double.IsNaN(ThinkingBudgetBox.Value)
                ? (int)ThinkingBudgetBox.Value : null;
        }
        PersistPresets();
    }

    private void OnThinkingBudgetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating) return;
        var preset = FindPreset();
        if (preset is not null)
            preset.ThinkingBudget = !double.IsNaN(sender.Value) ? (int)sender.Value : null;
        PersistPresets();
    }

    #endregion

    #region Thinking State

    private void UpdateThinkingState()
    {
        var modelId = ModelCombo.SelectedItem as string;
        var support = ThinkingCapability.GetSupport(ProviderType, modelId);

        var overrideIdx = ThinkingOverrideCombo.SelectedIndex;
        var userOverride = overrideIdx >= 0 && overrideIdx < ThinkingOverrideOptions.Length
            ? ThinkingOverrideOptions[overrideIdx].Value
            : ThinkingOverride.Auto;

        switch (userOverride)
        {
            case ThinkingOverride.ForceOn:
                ThinkingToggle.IsOn = true;
                ThinkingToggle.IsEnabled = true;
                ThinkingBudgetBox.IsEnabled = true;
                ThinkingHint.Visibility = Visibility.Collapsed;
                break;

            case ThinkingOverride.ForceOff:
                ThinkingToggle.IsOn = false;
                ThinkingToggle.IsEnabled = true;
                ThinkingBudgetBox.IsEnabled = false;
                ThinkingHint.Visibility = Visibility.Collapsed;
                break;

            default: // Auto
                switch (support)
                {
                    case ThinkingSupport.NotSupported:
                        ThinkingToggle.IsEnabled = false;
                        ThinkingToggle.IsOn = false;
                        ThinkingBudgetBox.IsEnabled = false;
                        ThinkingHint.Text = L("Models_ThinkingNotSupported");
                        ThinkingHint.Visibility = Visibility.Visible;
                        break;

                    case ThinkingSupport.Required:
                        ThinkingToggle.IsOn = true;
                        ThinkingToggle.IsEnabled = false;
                        ThinkingBudgetBox.IsEnabled = true;
                        ThinkingHint.Text = L("Models_ThinkingAlwaysOn");
                        ThinkingHint.Visibility = Visibility.Visible;
                        break;

                    case ThinkingSupport.Optional:
                    default:
                        ThinkingToggle.IsEnabled = true;
                        ThinkingBudgetBox.IsEnabled = ThinkingToggle.IsOn;
                        ThinkingHint.Visibility = Visibility.Collapsed;
                        break;
                }
                break;
        }
    }

    #endregion

    #region Private Helpers

    private ProviderPreset? FindPreset()
        => _presets.FirstOrDefault(p => p.ProviderType == ProviderType);

    private void PersistPresets() => AppSettings.SavePresets(_presets);

    private void UpdateSubHeader()
    {
        var model = ModelCombo.SelectedItem as string ?? "";
        var preset = FindPreset();
        var hasKey = !string.IsNullOrEmpty(preset?.ApiKey);
        var status = hasKey ? "\u2713" : L("Models_NoKey");

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(model)) parts.Add(model);
        if (!string.IsNullOrEmpty(status)) parts.Add(status);
        SubHeaderChanged?.Invoke(this, string.Join(" \u00B7 ", parts));
    }

    private PdfCapability GetPdfCapabilityFromCombo()
    {
        var idx = PdfCombo.SelectedIndex;
        return idx >= 0 && idx < PdfOptions.Length ? PdfOptions[idx].Value : PdfCapability.Auto;
    }

    private ThinkingOverride GetThinkingOverrideFromCombo()
    {
        var idx = ThinkingOverrideCombo.SelectedIndex;
        return idx >= 0 && idx < ThinkingOverrideOptions.Length ? ThinkingOverrideOptions[idx].Value : ThinkingOverride.Auto;
    }

    /// <summary>
    /// Switches the model ComboBox to editable mode for manual model ID input.
    /// Used when a custom provider's /models endpoint is unavailable.
    /// </summary>
    private void EnableEditableModelCombo()
    {
        ModelCombo.IsEditable = true;
        ModelCombo.IsEnabled = true;
        ModelCombo.PlaceholderText = L("Models_TypeModelId");

        var saved = AppSettings.GetDefaultModel(ProviderType);
        if (!string.IsNullOrEmpty(saved))
        {
            _isPopulating = true;
            ModelCombo.Text = saved;
            _isPopulating = false;
        }
    }

    private static string ProviderToDisplayName(string provider) => provider switch
    {
        "Claude" => "Anthropic Claude",
        "OpenAI" => "OpenAI",
        "Gemini" => "Google Gemini",
        "Groq" => "Groq",
        _ => provider
    };

    private static readonly ResourceLoader Res = new();

    private static string L(string key) =>
        Res.GetString(key) is { Length: > 0 } value ? value : key;

    #endregion
}
