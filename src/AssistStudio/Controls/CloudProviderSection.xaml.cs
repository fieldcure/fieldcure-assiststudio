using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using Windows.System;

namespace AssistStudio.Controls;

/// <summary>
/// Reusable settings section for a cloud AI provider (Claude, OpenAI, Gemini, Groq) and
/// custom OpenAI/Anthropic-compatible providers. Hosts the per-provider checklist of
/// enabled models, broadcast dials (max tokens, PDF handling, reasoning), and inline
/// "+ Add model ID" entry.
/// </summary>
public sealed partial class CloudProviderSection : UserControl
{
    #region Fields

    /// <summary>The provider model collection shared with the parent ModelsPage.</summary>
    private ObservableCollection<ProviderModel> _presets = [];
    /// <summary>Suppresses change handlers while the section is repopulating UI from saved state.</summary>
    private bool _isPopulating;
    /// <summary>Tracks whether <see cref="Initialize"/> has run, to make it idempotent.</summary>
    private bool _initialized;
    /// <summary>Backing collection for <see cref="ModelChecklist"/>.</summary>
    private readonly ObservableCollection<ModelChecklistItem> _checklist = [];

    #endregion

    #region Constants

    /// <summary>Resource keys for the thinking-override ComboBox entries.</summary>
    private static readonly (ThinkingOverride Value, string LabelKey)[] ThinkingOverrideOptions =
    [
        (ThinkingOverride.Auto, "Models_ThinkingOverrideAuto"),
        (ThinkingOverride.ForceOn, "Models_ThinkingOverrideForceOn"),
        (ThinkingOverride.ForceOff, "Models_ThinkingOverrideForceOff"),
    ];

    /// <summary>Resource keys for the PDF-handling ComboBox entries.</summary>
    private static readonly (PdfCapability Value, string LabelKey)[] PdfOptions =
    [
        (PdfCapability.Auto, "Models_PdfAuto"),
        (PdfCapability.NativePdf, "Models_PdfNative"),
        (PdfCapability.PageAsImage, "Models_PdfPageAsImage"),
        (PdfCapability.TextExtraction, "Models_PdfTextExtraction"),
    ];

    #endregion

    #region Dependency Properties

    /// <summary>The provider type identifier (e.g. "Claude", "OpenAI", "Gemini", "Groq", "Custom_xxx").</summary>
    public static readonly DependencyProperty ProviderTypeProperty =
        DependencyProperty.Register(nameof(ProviderType), typeof(string), typeof(CloudProviderSection),
            new PropertyMetadata(string.Empty));

    /// <summary>Fallback model IDs used when the cache is empty (comma-separated).</summary>
    public static readonly DependencyProperty FallbackModelsStringProperty =
        DependencyProperty.Register(nameof(FallbackModelsString), typeof(string), typeof(CloudProviderSection),
            new PropertyMetadata(string.Empty));

    #endregion

    #region Properties

    /// <summary>Gets or sets the provider type identifier.</summary>
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

    /// <summary>Fallback model IDs parsed from <see cref="FallbackModelsString"/>.</summary>
    private string[] FallbackModels =>
        string.IsNullOrEmpty(FallbackModelsString) ? [] : FallbackModelsString.Split(',');

    #endregion

    #region Events

    /// <summary>Raised when the sub-header text should be updated.</summary>
    public event EventHandler<string>? SubHeaderChanged;

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="CloudProviderSection"/>.</summary>
    public CloudProviderSection()
    {
        InitializeComponent();
        ModelChecklist.ItemsSource = _checklist;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes the section UI from the current presets. Called once after x:Load realization.
    /// </summary>
    public void Initialize(ObservableCollection<ProviderModel> presets)
    {
        if (_initialized) return;
        _initialized = true;
        _presets = presets;

        _isPopulating = true;
        try
        {
            var apiKey = FindAnyPreset()?.ApiKey
                ?? PasswordVaultHelper.LoadApiKey(ProviderType);
            SetKeyState(apiKey ?? "");

            RebuildChecklist();
            PopulatePdfCombo();
            PopulateMaxTokens();
            PopulateThinkingToggle();
            UpdateThinkingState();
            UpdateSubHeader();
        }
        finally { _isPopulating = false; }

        // Background model refresh if key exists
        var presetWithKey = FindAnyPreset();
        if (!string.IsNullOrEmpty(presetWithKey?.ApiKey))
        {
            if (ProviderType.StartsWith("Custom_") && AppSettings.GetModelsEndpointFailed(ProviderType))
            {
                // Skip fetch — checklist falls back to manual-add only.
            }
            else
            {
                _ = FetchAndCacheModelsAsync(presetWithKey.ApiKey, refreshFromUser: false);
            }
        }
    }

    #endregion

    #region API Key Management

    /// <summary>Toggles between the API-key entry and masked-display panels.</summary>
    private void SetKeyState(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            KeyInputPanel.Visibility = Visibility.Collapsed;
            KeyDisplayPanel.Visibility = Visibility.Visible;
            MaskedKeyText.Text = MaskKey(key);
            OptionsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            KeyInputPanel.Visibility = Visibility.Visible;
            KeyDisplayPanel.Visibility = Visibility.Collapsed;
            OptionsPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Masks an API key for display, keeping the first and last three characters.</summary>
    private static string MaskKey(string key)
    {
        if (key.Length <= 6) return "••••••";
        return key[..3] + "•••" + key[^3..];
    }

    /// <summary>
    /// Saves the entered API key. Does not yet create any <see cref="ProviderModel"/>;
    /// the user picks which models to register via the checklist after the fetch completes.
    /// </summary>
    private void OnAddKey(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password?.Trim();
        if (string.IsNullOrEmpty(key)) return;

        PasswordVaultHelper.SaveApiKey(ProviderType, key);
        ApiKeyBox.Password = "";
        SetKeyState(key);

        // Apply key to any existing ProviderModel of this type so subsequent
        // SaveModels writes preserve it.
        foreach (var p in FindAllPresets()) p.ApiKey = key;

        UpdateSubHeader();
        AppSettings.ClearModelsEndpointFailed(ProviderType);
        _ = FetchAndCacheModelsAsync(key, refreshFromUser: false, autoEnableFirst: true);
    }

    /// <summary>
    /// Removes the stored API key and ALL ProviderModel entries for this provider type.
    /// </summary>
    private void OnRemoveKey(object sender, RoutedEventArgs e)
    {
        PasswordVaultHelper.DeleteApiKey(ProviderType);

        foreach (var p in FindAllPresets().ToList())
            _presets.Remove(p);

        SetKeyState("");
        _checklist.Clear();
        UpdateSubHeader();
        AppSettings.SaveModels(_presets);
    }

    #endregion

    #region Checklist Build

    /// <summary>
    /// Rebuilds <see cref="_checklist"/> from the union of cached upstream model IDs,
    /// manually added IDs, and currently registered <see cref="ProviderModel"/> entries.
    /// </summary>
    private void RebuildChecklist()
    {
        _checklist.Clear();

        var cached = AppSettings.GetCachedModels(ProviderType) ?? [.. FallbackModels];
        var manual = AppSettings.GetManualModels(ProviderType);
        var enabled = FindAllPresets().Select(p => p.ModelId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in cached.Concat(manual).Concat(enabled))
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            _checklist.Add(new ModelChecklistItem(id)
            {
                IsEnabled = enabled.Contains(id),
                IsManuallyAdded = manual.Contains(id) && !cached.Contains(id),
                IsMissingUpstream = enabled.Contains(id) && !cached.Contains(id) && !manual.Contains(id),
            });
        }

        EmptyChecklistHint.Visibility = _checklist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Re-runs <see cref="RebuildChecklist"/> while merging in <paramref name="freshModels"/> as the upstream list.</summary>
    private void RebuildChecklistAfterFetch(IReadOnlyList<string> freshModels)
    {
        AppSettings.SetCachedModels(ProviderType, [.. freshModels]);

        var freshSet = freshModels.ToHashSet(StringComparer.Ordinal);
        var manual = AppSettings.GetManualModels(ProviderType);
        var enabled = FindAllPresets().Select(p => p.ModelId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

        _checklist.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in freshModels.Concat(manual).Concat(enabled))
        {
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            _checklist.Add(new ModelChecklistItem(id)
            {
                IsEnabled = enabled.Contains(id),
                IsManuallyAdded = !freshSet.Contains(id) && manual.Contains(id),
                IsMissingUpstream = enabled.Contains(id) && !freshSet.Contains(id) && !manual.Contains(id),
            });
        }

        EmptyChecklistHint.Visibility = _checklist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the "Enable all" button — checks every visible row that is not
    /// already registered, creating <see cref="ProviderModel"/> instances for
    /// the new ones in one batch save.
    /// </summary>
    private void OnEnableAllModels(object sender, RoutedEventArgs e)
    {
        var enabled = FindAllPresets().Select(p => p.ModelId).ToHashSet();
        var toEnable = _checklist.Where(i => !enabled.Contains(i.ModelId)).ToList();
        if (toEnable.Count == 0) return;

        _isPopulating = true;
        try
        {
            foreach (var item in toEnable)
            {
                EnableModel(item.ModelId);
                item.IsEnabled = true;
            }
        }
        finally { _isPopulating = false; }

        AppSettings.SaveModels(_presets);
        UpdateSubHeader();
    }

    /// <summary>Handles the Refresh button click — re-fetches the upstream model list.</summary>
    private async void OnRefreshModels(object sender, RoutedEventArgs e)
    {
        var key = FindAnyPreset()?.ApiKey ?? PasswordVaultHelper.LoadApiKey(ProviderType);
        if (string.IsNullOrEmpty(key)) return;

        RefreshModelsButton.IsEnabled = false;
        try
        {
            AppSettings.ClearModelsEndpointFailed(ProviderType);
            await FetchAndCacheModelsAsync(key, refreshFromUser: true);
        }
        finally
        {
            RefreshModelsButton.IsEnabled = true;
        }
    }

    #endregion

    #region Checklist Toggling

    /// <summary>Handles a checklist row's check/uncheck — adds or removes the matching ProviderModel.</summary>
    private async void OnModelToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating) return;
        if (sender is not CheckBox cb || cb.Tag is not string modelId) return;

        var item = _checklist.FirstOrDefault(i => i.ModelId == modelId);
        if (item is null) return;

        if (cb.IsChecked == true)
        {
            EnableModel(modelId);
        }
        else
        {
            // Confirm removal if the model is referenced by auxiliary keys.
            var usages = AppSettings.FindAuxiliaryKeyUsages(modelId);
            if (usages.Count > 0)
            {
                var ok = await ConfirmRemoveInUseAsync(modelId, usages);
                if (!ok)
                {
                    _isPopulating = true;
                    item.IsEnabled = true;
                    _isPopulating = false;
                    return;
                }
            }
            DisableModel(modelId);
        }

        AppSettings.SaveModels(_presets);
        UpdateSubHeader();
    }

    /// <summary>Adds a <see cref="ProviderModel"/> for <paramref name="modelId"/>, copying broadcast fields from any sibling.</summary>
    private void EnableModel(string modelId)
    {
        if (FindAllPresets().Any(p => p.ModelId == modelId)) return;

        var template = FindAnyPreset();
        var apiKey = template?.ApiKey ?? PasswordVaultHelper.LoadApiKey(ProviderType);
        var baseUrl = template?.BaseUrl ?? DefaultBaseUrlForProvider(ProviderType);

        var newPreset = new ProviderModel
        {
            Name = modelId,
            ProviderType = ProviderType,
            ModelId = modelId,
            ApiKey = apiKey ?? "",
            BaseUrl = baseUrl,
            MaxTokens = template?.MaxTokens ?? 4096,
            Temperature = template?.Temperature ?? 0.7,
            StreamingEnabled = template?.StreamingEnabled ?? true,
            PdfCapability = template?.PdfCapability ?? PdfCapability.Auto,
            ThinkingEnabled = template?.ThinkingEnabled ?? false,
            ThinkingOverride = template?.ThinkingOverride ?? ThinkingOverride.Auto,
            ThinkingBudget = template?.ThinkingBudget,
        };

        // Insert before Ollama/Mock if those exist, else append.
        var ollamaIdx = -1;
        for (var i = 0; i < _presets.Count; i++)
        {
            if (_presets[i].ProviderType is "Ollama" or "Mock") { ollamaIdx = i; break; }
        }
        if (ollamaIdx >= 0) _presets.Insert(ollamaIdx, newPreset);
        else _presets.Add(newPreset);
    }

    /// <summary>Removes the <see cref="ProviderModel"/> matching <paramref name="modelId"/>.</summary>
    private void DisableModel(string modelId)
    {
        var existing = FindAllPresets().FirstOrDefault(p => p.ModelId == modelId);
        if (existing is not null) _presets.Remove(existing);
    }

    /// <summary>Shows a confirmation dialog when the user unchecks a model still referenced elsewhere.</summary>
    private async Task<bool> ConfirmRemoveInUseAsync(string modelId, IReadOnlyList<string> usages)
    {
        var dialog = new ContentDialog
        {
            Title = L("Models_ModelInUseTitle"),
            Content = string.Format(
                L("Models_ModelInUseMessage"),
                modelId,
                string.Join(", ", usages)),
            PrimaryButtonText = L("Models_Remove"),
            CloseButtonText = L("Models_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    #endregion

    #region Add Custom Model ID

    /// <summary>Allows pressing Enter inside <see cref="CustomModelIdBox"/> to commit the new model ID.</summary>
    private void OnCustomModelIdKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        OnAddCustomModelId(sender, e);
    }

    /// <summary>
    /// Adds a manually entered model ID to the checklist. If the ID already exists,
    /// briefly highlights the existing row instead of creating a duplicate.
    /// </summary>
    private async void OnAddCustomModelId(object sender, RoutedEventArgs e)
    {
        var modelId = CustomModelIdBox.Text?.Trim();
        if (string.IsNullOrEmpty(modelId)) return;
        CustomModelIdBox.Text = "";

        var existing = _checklist.FirstOrDefault(i => i.ModelId == modelId);
        if (existing is not null)
        {
            await FlashRowAsync(existing);
            return;
        }

        var manual = AppSettings.GetManualModels(ProviderType);
        if (!manual.Contains(modelId))
        {
            manual.Add(modelId);
            AppSettings.SetManualModels(ProviderType, manual);
        }

        var cached = AppSettings.GetCachedModels(ProviderType) ?? [];
        var item = new ModelChecklistItem(modelId)
        {
            IsEnabled = false,
            IsManuallyAdded = !cached.Contains(modelId),
            IsMissingUpstream = false,
        };
        _checklist.Add(item);
        EmptyChecklistHint.Visibility = Visibility.Collapsed;
    }

    /// <summary>Briefly highlights <paramref name="item"/>'s row to confirm a duplicate-add.</summary>
    private static async Task FlashRowAsync(ModelChecklistItem item)
    {
        var highlight = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xC1, 0x07));
        item.RowBackground = highlight;
        await Task.Delay(700);
        item.RowBackground = null;
    }

    #endregion

    #region Model Fetching

    /// <summary>Fetches the provider's model list, updates the cache, and rebuilds the checklist.</summary>
    private async Task FetchAndCacheModelsAsync(string apiKey, bool refreshFromUser, bool autoEnableFirst = false)
    {
        try
        {
            var provider = CreateProviderForListing(apiKey);
            try
            {
                var models = await provider.ListModelsAsync();
                var filtered = FilterChatModels(models);
                if (filtered.Count == 0) return;

                DispatcherQueue.TryEnqueue(() =>
                {
                    _isPopulating = true;
                    try
                    {
                        RebuildChecklistAfterFetch(filtered);

                        if (autoEnableFirst && _presets.All(p => p.ProviderType != ProviderType))
                        {
                            var first = filtered[0];
                            EnableModel(first);
                            var match = _checklist.FirstOrDefault(i => i.ModelId == first);
                            if (match is not null) match.IsEnabled = true;
                            AppSettings.SaveModels(_presets);
                        }
                    }
                    finally { _isPopulating = false; }
                    UpdateSubHeader();
                });
            }
            finally { (provider as IDisposable)?.Dispose(); }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);

            if (ProviderType.StartsWith("Custom_"))
                AppSettings.SetModelsEndpointFailed(ProviderType);

            // On user-triggered refresh failure, surface but don't blow up; checklist
            // unchanged so the user can retry.
            if (refreshFromUser)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _ = ShowRefreshFailedAsync(ex);
                });
            }
        }
    }

    /// <summary>Shows a content dialog when refresh fails.</summary>
    private async Task ShowRefreshFailedAsync(Exception ex)
    {
        var dialog = new ContentDialog
        {
            Title = L("Models_RefreshFailedTitle"),
            Content = ex.Message,
            CloseButtonText = L("Models_Close"),
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>Creates a temporary provider used only for listing available models.</summary>
    private IAiProvider CreateProviderForListing(string apiKey)
    {
        if (ProviderType.StartsWith("Custom_"))
        {
            var preset = FindAnyPreset();
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

    /// <summary>Filters a model catalog down to chat-capable IDs.</summary>
    private List<string> FilterChatModels(IReadOnlyList<AiModel> models) => ProviderType switch
    {
        "Claude" => [.. models.Where(m => m.Id.StartsWith("claude-")).Select(m => m.Id)],
        "OpenAI" => [.. models.Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3") || m.Id.StartsWith("o4")).Select(m => m.Id)],
        "Gemini" => [.. models.Where(m => m.Id.StartsWith("gemini-")).Select(m => m.Id)],
        "Groq" => [.. models.Select(m => m.Id)],
        _ => [.. models.Select(m => m.Id)]
    };

    #endregion

    #region Broadcast Options

    /// <summary>Populates the PDF handling ComboBox and selects the saved capability.</summary>
    private void PopulatePdfCombo()
    {
        PdfCombo.Items.Clear();
        foreach (var (_, labelKey) in PdfOptions)
            PdfCombo.Items.Add(L(labelKey));
        var saved = FindAnyPreset()?.PdfCapability ?? PdfCapability.Auto;
        var idx = Array.FindIndex(PdfOptions, o => o.Value == saved);
        PdfCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    /// <summary>Loads the broadcast max-tokens value.</summary>
    private void PopulateMaxTokens()
    {
        var preset = FindAnyPreset();
        if (preset is not null) MaxTokensBox.Value = preset.MaxTokens;
    }

    /// <summary>Initializes the thinking override controls from the broadcast values.</summary>
    private void PopulateThinkingToggle()
    {
        ThinkingOverrideCombo.Items.Clear();
        foreach (var (_, labelKey) in ThinkingOverrideOptions)
            ThinkingOverrideCombo.Items.Add(L(labelKey));

        var preset = FindAnyPreset();
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

    /// <summary>Broadcasts a PDF handling change to all matching ProviderModel entries.</summary>
    private void OnPdfHandlingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        var value = GetPdfCapabilityFromCombo();
        foreach (var p in FindAllPresets()) p.PdfCapability = value;
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a max-tokens change to all matching ProviderModel entries.</summary>
    private void OnMaxTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating || double.IsNaN(args.NewValue)) return;
        foreach (var p in FindAllPresets()) p.MaxTokens = (int)args.NewValue;
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a thinking-toggle change to all matching ProviderModel entries.</summary>
    private void OnThinkingToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating || !ThinkingToggle.IsEnabled) return;
        ThinkingBudgetBox.IsEnabled = ThinkingToggle.IsOn;
        var enabled = ThinkingToggle.IsOn;
        var budget = enabled && !double.IsNaN(ThinkingBudgetBox.Value) ? (int?)ThinkingBudgetBox.Value : null;
        foreach (var p in FindAllPresets())
        {
            p.ThinkingEnabled = enabled;
            p.ThinkingBudget = budget;
        }
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a thinking-override change to all matching ProviderModel entries.</summary>
    private void OnThinkingOverrideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        _isPopulating = true;
        UpdateThinkingState();
        _isPopulating = false;

        var ovr = GetThinkingOverrideFromCombo();
        var enabled = ThinkingToggle.IsOn;
        var budget = enabled && !double.IsNaN(ThinkingBudgetBox.Value) ? (int?)ThinkingBudgetBox.Value : null;
        foreach (var p in FindAllPresets())
        {
            p.ThinkingOverride = ovr;
            p.ThinkingEnabled = enabled;
            p.ThinkingBudget = budget;
        }
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a thinking-budget change to all matching ProviderModel entries.</summary>
    private void OnThinkingBudgetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating) return;
        var budget = !double.IsNaN(sender.Value) ? (int?)sender.Value : null;
        foreach (var p in FindAllPresets()) p.ThinkingBudget = budget;
        AppSettings.SaveModels(_presets);
    }

    #endregion

    #region Thinking State

    /// <summary>
    /// Recomputes whether thinking is available, required, or blocked for the
    /// first checked model and updates the related controls and hint text.
    /// </summary>
    private void UpdateThinkingState()
    {
        var modelId = FindAllPresets().FirstOrDefault()?.ModelId;
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

            default:
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

    /// <summary>Returns the first <see cref="ProviderModel"/> for this provider type, or null.</summary>
    private ProviderModel? FindAnyPreset()
        => _presets.FirstOrDefault(p => p.ProviderType == ProviderType);

    /// <summary>Returns every <see cref="ProviderModel"/> registered for this provider type.</summary>
    private IEnumerable<ProviderModel> FindAllPresets()
        => _presets.Where(p => p.ProviderType == ProviderType);

    /// <summary>Default base URL for built-in providers; null otherwise.</summary>
    private static string? DefaultBaseUrlForProvider(string providerType) => providerType switch
    {
        "Groq" => "https://api.groq.com/openai/v1",
        _ => null,
    };

    /// <summary>Updates the parent header's sub-text to "{N} models · {key indicator}".</summary>
    private void UpdateSubHeader()
    {
        var hasKey = FindAllPresets().Any(p => !string.IsNullOrEmpty(p.ApiKey))
            || !string.IsNullOrEmpty(PasswordVaultHelper.LoadApiKey(ProviderType));
        var status = hasKey ? "✓" : L("Models_NoKey");
        var count = FindAllPresets().Count();
        var modelText = count switch
        {
            0 => L("Models_NoModelsSelected"),
            1 => string.Format(L("Models_OneModelSelected"), 1),
            _ => string.Format(L("Models_NModelsSelected"), count),
        };

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(modelText)) parts.Add(modelText);
        if (!string.IsNullOrEmpty(status)) parts.Add(status);
        SubHeaderChanged?.Invoke(this, string.Join(" · ", parts));
    }

    /// <summary>Maps the PDF handling ComboBox selection to its corresponding capability enum.</summary>
    private PdfCapability GetPdfCapabilityFromCombo()
    {
        var idx = PdfCombo.SelectedIndex;
        return idx >= 0 && idx < PdfOptions.Length ? PdfOptions[idx].Value : PdfCapability.Auto;
    }

    /// <summary>Maps the thinking-override ComboBox selection to its enum value.</summary>
    private ThinkingOverride GetThinkingOverrideFromCombo()
    {
        var idx = ThinkingOverrideCombo.SelectedIndex;
        return idx >= 0 && idx < ThinkingOverrideOptions.Length ? ThinkingOverrideOptions[idx].Value : ThinkingOverride.Auto;
    }

    /// <summary>Shared resource loader for localized strings on this section.</summary>
    private static readonly ResourceLoader Res = new();

    /// <summary>Resolves a localized string for the given resource key, falling back to the key itself.</summary>
    private static string L(string key) =>
        Res.GetString(key) is { Length: > 0 } value ? value : key;

    #endregion
}
