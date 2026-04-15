using AssistStudio.Mcp.ModelAvailability;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Controls;

/// <summary>
/// Reusable model selection control for embedding and chunk contextualization.
/// Used in KB creation and re-indexing dialogs.
/// </summary>
public sealed partial class EmbeddingModelSelector : UserControl
{
    #region Fields

    private readonly ResourceLoader _loader = new();
    private string _selectedEmbeddingId = "nomic-embed-text";
    private string _selectedContextualizerId = "";

    #endregion

    #region Model Definitions

    /// <summary>
    /// Available embedding models grouped by provider.
    /// </summary>
    private static readonly (string Id, string Provider, string Label, string Meta)[] EmbeddingModels =
    [
        ("nomic-embed-text", "Ollama", "nomic-embed-text", "768d \u00b7 274MB"),
        ("nomic-embed-text-v2-moe", "Ollama", "nomic-embed-text-v2-moe", "768d \u00b7 1.9GB"),
        ("bge-m3", "Ollama", "bge-m3", "1024d \u00b7 1.2GB"),
        ("qwen3-embedding:8b", "Ollama", "qwen3-embedding:8b", "4096d \u00b7 ~5GB \u00b7 32k ctx"),
        ("text-embedding-3-small", "OpenAI", "text-embedding-3-small", "1536d"),
        ("text-embedding-3-large", "OpenAI", "text-embedding-3-large", "3072d"),
    ];

    /// <summary>
    /// Available contextualizer models grouped by provider. Empty Id = disabled.
    /// </summary>
    private static readonly (string Id, string Provider, string Label, string Meta)[] ContextualizerModels =
    [
        ("", "", "disabled", ""),
        ("gemma3:4b", "Ollama", "gemma3:4b", "2.8GB"),
        ("qwen3:4b", "Ollama", "qwen3:4b", "2.7GB"),
        ("gpt-4o-mini", "OpenAI", "gpt-4o-mini", ""),
        ("claude-haiku-4-6", "Claude", "claude-haiku-4-6", ""),
    ];

    /// <summary>
    /// Maps model ID to provider and API key preset for <see cref="KbProviderConfig"/> construction.
    /// </summary>
    private static readonly Dictionary<string, (string Provider, string? ApiKeyPreset)> ProviderMap = new()
    {
        ["nomic-embed-text"] = ("ollama", null),
        ["nomic-embed-text-v2-moe"] = ("ollama", null),
        ["bge-m3"] = ("ollama", null),
        ["qwen3-embedding:8b"] = ("ollama", null),
        ["text-embedding-3-small"] = ("openai", "OpenAI"),
        ["text-embedding-3-large"] = ("openai", "OpenAI"),
        ["gemma3:4b"] = ("ollama", null),
        ["qwen3:4b"] = ("ollama", null),
        ["gpt-4o-mini"] = ("openai", "OpenAI"),
        ["claude-haiku-4-6"] = ("anthropic", "Claude"),
    };

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the control.
    /// </summary>
    public EmbeddingModelSelector()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Model ID to pre-select and mark as "(current)" in re-indexing mode.
    /// Null in creation mode.
    /// </summary>
    public string? CurrentEmbeddingModel { get; set; }

    /// <summary>
    /// Contextualizer ID to pre-select and mark as "(current)" in re-indexing mode.
    /// Null in creation mode.
    /// </summary>
    public string? CurrentContextualizer { get; set; }

    /// <summary>
    /// Whether the embedding model selection differs from the current value.
    /// </summary>
    public bool EmbeddingModelChanged =>
        CurrentEmbeddingModel is not null && _selectedEmbeddingId != CurrentEmbeddingModel;

    /// <summary>
    /// Whether the contextualizer selection differs from the current value.
    /// </summary>
    public bool ContextualizerChanged =>
        CurrentContextualizer is not null && _selectedContextualizerId != CurrentContextualizer;

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds the model radio button lists with availability checks against
    /// Ollama (<c>/api/tags</c>) and the credential vault (OpenAI / Claude
    /// API keys). Unavailable models are rendered as <c>IsEnabled=false</c>
    /// with a "(설치 안 됨)" or "(API 키 없음)" badge so a new KB dialog
    /// simply prevents picking a broken model at the source.
    ///
    /// In re-indexing mode (when <see cref="CurrentEmbeddingModel"/> or
    /// <see cref="CurrentContextualizer"/> is set), the current selection
    /// is preserved even if it is unavailable — we never auto-switch to a
    /// working model on the user's behalf. Instead we surface an inline
    /// warning explaining the trade-off (install the model vs. full
    /// re-index cost) and leave the decision to the user. The selected
    /// radio stays disabled so the user has to make an explicit choice.
    ///
    /// If the Ollama check itself fails (daemon not running), we fall back
    /// to rendering every Ollama model as available — we can't tell
    /// installed from not, and false positives would be worse than
    /// silence. Call after setting Current* properties.
    /// </summary>
    public async Task InitializeAsync()
    {
        EmbeddingHeader.Text = _loader.GetString("KB_DialogEmbeddingModel");
        ContextualizerHeader.Text = _loader.GetString("KB_DialogContextualizer");

        var embeddingDefault = CurrentEmbeddingModel ?? "nomic-embed-text";
        _selectedEmbeddingId = embeddingDefault;

        var contextualizerDefault = CurrentContextualizer ?? "";
        _selectedContextualizerId = contextualizerDefault;

        var labels = new ModelListLabels(
            Multilingual: _loader.GetString("Connect_Multilingual"),
            Disabled: _loader.GetString("KB_DialogContextDisabled") ?? "Disabled",
            Current: _loader.GetString("KB_ModelCurrent") ?? "(current)",
            CurrentUnavailableWarning: _loader.GetString("KB_ModelCurrentUnavailableWarning")
                ?? "This KB is indexed with the currently selected model. Install the model, or pick a different one to re-index.");

        // Pre-compute the availability verdict for every catalog entry so
        // BuildModelList stays synchronous. One service instance is shared
        // across both lists (embedding + contextualizer) so the underlying
        // Ollama /api/tags probe and credential lookups only happen once.
        var service = new ModelAvailabilityService();
        var availability = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, provider, _, _) in EmbeddingModels)
        {
            if (!availability.ContainsKey(id))
                availability[id] = await service.CheckModelAsync(MapProviderName(provider), id);
        }
        foreach (var (id, provider, _, _) in ContextualizerModels)
        {
            if (!string.IsNullOrEmpty(id) && !availability.ContainsKey(id))
                availability[id] = await service.CheckModelAsync(MapProviderName(provider), id);
        }

        BuildModelList(EmbeddingModelPanel, EmbeddingModels, embeddingDefault,
            "EmbeddingModel", CurrentEmbeddingModel, availability, labels, OnEmbeddingSelected);

        BuildModelList(ContextualizerPanel, ContextualizerModels, contextualizerDefault,
            "Contextualizer", CurrentContextualizer, availability, labels, OnContextualizerSelected);
    }

    /// <summary>
    /// Normalizes the catalog's display-cased provider label (e.g. "Ollama",
    /// "OpenAI", "Claude") to the lowercase provider key the availability
    /// service expects (<c>ollama</c>, <c>openai</c>, <c>anthropic</c>).
    /// </summary>
    private static string MapProviderName(string displayProvider) => displayProvider switch
    {
        "Ollama" => "ollama",
        "OpenAI" => "openai",
        "Claude" => "anthropic",
        _ => displayProvider.ToLowerInvariant(),
    };

    /// <summary>
    /// Bundle of localized label strings used by <see cref="BuildModelList"/>.
    /// Pulled into a record so the signature doesn't grow every time a new
    /// variant string is added. The availability reason strings live on the
    /// checker classes themselves (see <see cref="OllamaChecker"/> etc.)
    /// and reach the UI via the per-model dictionary.
    /// </summary>
    private sealed record ModelListLabels(
        string Multilingual,
        string Disabled,
        string Current,
        string CurrentUnavailableWarning);


    /// <summary>
    /// Returns the selected embedding model as a <see cref="KbProviderConfig"/>.
    /// </summary>
    public KbProviderConfig GetEmbeddingConfig()
    {
        if (ProviderMap.TryGetValue(_selectedEmbeddingId, out var info))
            return new KbProviderConfig { Provider = info.Provider, Model = _selectedEmbeddingId, ApiKeyPreset = info.ApiKeyPreset };
        return new KbProviderConfig { Provider = "ollama", Model = _selectedEmbeddingId };
    }

    /// <summary>
    /// Returns the selected contextualizer as a <see cref="KbProviderConfig"/>.
    /// </summary>
    public KbProviderConfig GetContextualizerConfig()
    {
        if (string.IsNullOrEmpty(_selectedContextualizerId))
            return new KbProviderConfig { Provider = "", Model = "" };
        if (ProviderMap.TryGetValue(_selectedContextualizerId, out var info))
            return new KbProviderConfig { Provider = info.Provider, Model = _selectedContextualizerId, ApiKeyPreset = info.ApiKeyPreset };
        return new KbProviderConfig { Provider = "ollama", Model = _selectedContextualizerId };
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Tracks the selected embedding model ID.
    /// </summary>
    private void OnEmbeddingSelected(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string modelId)
            _selectedEmbeddingId = modelId;
    }

    /// <summary>
    /// Tracks the selected contextualizer model ID.
    /// </summary>
    private void OnContextualizerSelected(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string modelId)
            _selectedContextualizerId = modelId;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Builds a grouped radio button list for model selection. Appends a
    /// "(current)" label to the active model in re-indexing mode and a
    /// "(설치 안 됨)" / "(API 키 없음)" caution badge to unavailable
    /// models. If the user's currently selected model is unavailable, a
    /// warning InfoBar is appended to the panel explaining the trade-off
    /// of install-vs-switch so the user can make an explicit call.
    /// </summary>
    private static void BuildModelList(
        StackPanel panel,
        (string Id, string Provider, string Label, string Meta)[] models,
        string selectedId,
        string groupName,
        string? currentModelId,
        IReadOnlyDictionary<string, string?> availability,
        ModelListLabels labels,
        RoutedEventHandler onChanged)
    {
        var lastProvider = "";
        var currentSelectionIsUnavailable = false;

        foreach (var (id, provider, label, meta) in models)
        {
            // Provider group header
            if (!string.IsNullOrEmpty(provider) && provider != lastProvider)
            {
                lastProvider = provider;
                panel.Children.Add(new TextBlock
                {
                    Text = provider,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Opacity = 0.6,
                    Margin = new Thickness(0, panel.Children.Count > 0 ? 8 : 0, 0, 2),
                });
            }

            var isPlaceholder = string.IsNullOrEmpty(id);
            var unavailableReason = isPlaceholder
                ? null
                : (availability.TryGetValue(id, out var reason) ? reason : null);
            var isUnavailable = unavailableReason is not null;

            var isCurrentlySelected = isPlaceholder
                ? string.IsNullOrEmpty(selectedId)
                : selectedId == id;
            var isTheCurrentIndex = currentModelId is not null &&
                (isPlaceholder ? string.IsNullOrEmpty(currentModelId) : currentModelId == id);

            // If the currently-indexed model is unavailable we keep it
            // selected and disabled, and track the fact so we can append a
            // warning block at the end of the list. New-KB mode (where
            // currentModelId is null) just disables unavailable radios
            // without a warning — nothing has been committed yet.
            if (isUnavailable && isTheCurrentIndex)
                currentSelectionIsUnavailable = true;

            var radio = new RadioButton
            {
                GroupName = groupName,
                Tag = id,
                IsChecked = isCurrentlySelected,
                IsEnabled = !isUnavailable,
                Margin = new Thickness(0),
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = isPlaceholder ? labels.Disabled : label,
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Meta info (dimensions, size, multilingual)
            if (!string.IsNullOrEmpty(meta))
            {
                var metaText = meta;
                if (id is "nomic-embed-text-v2-moe" or "bge-m3" or "qwen3-embedding:8b" or "qwen3:4b")
                    metaText += $" \u00b7 {labels.Multilingual}";

                content.Children.Add(new TextBlock
                {
                    Text = metaText,
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                });
            }

            // Unavailability badge — "(설치 안 됨)" or "(API 키 없음)" in
            // caution color. Shown both in create mode (user picking a
            // broken model) and edit mode (previously-valid model no
            // longer installed).
            if (unavailableReason is not null)
            {
                content.Children.Add(new TextBlock
                {
                    Text = unavailableReason,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources["SystemFillColorCautionBrush"],
                });
            }

            // "(current)" tag in re-indexing mode
            if (isTheCurrentIndex)
            {
                content.Children.Add(new TextBlock
                {
                    Text = labels.Current,
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                });
            }

            radio.Content = content;
            radio.Checked += onChanged;
            panel.Children.Add(radio);
        }

        // Edit-mode-only warning: the current selection is no longer
        // available. We tell the user exactly what the trade-off is so
        // they can make the call.
        if (currentSelectionIsUnavailable)
        {
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Message = labels.CurrentUnavailableWarning,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
    }

    #endregion
}
