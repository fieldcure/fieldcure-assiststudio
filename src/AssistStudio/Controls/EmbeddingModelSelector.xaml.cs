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
        ("nomic-embed-text-v2-moe", "Ollama", "nomic-embed-text-v2-moe", "768d \u00b7 1.9GB \u00b7 \ub2e4\uad6d\uc5b4"),
        ("bge-m3", "Ollama", "bge-m3", "1024d \u00b7 1.2GB \u00b7 \ub2e4\uad6d\uc5b4"),
        ("qwen3-embedding:8b", "Ollama", "qwen3-embedding:8b", "4096d \u00b7 ~5GB \u00b7 32k ctx \u00b7 \ub2e4\uad6d\uc5b4"),
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
    /// Builds the model radio button lists. Call after setting Current* properties.
    /// </summary>
    public void Initialize()
    {
        EmbeddingHeader.Text = _loader.GetString("KB_DialogEmbeddingModel");
        ContextualizerHeader.Text = _loader.GetString("KB_DialogContextualizer");

        var embeddingDefault = CurrentEmbeddingModel ?? "nomic-embed-text";
        _selectedEmbeddingId = embeddingDefault;

        var contextualizerDefault = CurrentContextualizer ?? "";
        _selectedContextualizerId = contextualizerDefault;

        var multilingual = _loader.GetString("Connect_Multilingual");
        var disabledLabel = _loader.GetString("KB_DialogContextDisabled") ?? "Disabled";
        var currentLabel = _loader.GetString("KB_ModelCurrent") ?? "(current)";

        BuildModelList(EmbeddingModelPanel, EmbeddingModels, embeddingDefault,
            "EmbeddingModel", multilingual, currentLabel, CurrentEmbeddingModel, OnEmbeddingSelected);

        BuildModelList(ContextualizerPanel, ContextualizerModels, contextualizerDefault,
            "Contextualizer", multilingual, currentLabel, CurrentContextualizer, OnContextualizerSelected, disabledLabel);
    }

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
    /// Builds a grouped radio button list for model selection.
    /// Appends "(current)" label to the active model in re-indexing mode.
    /// </summary>
    private static void BuildModelList(
        StackPanel panel,
        (string Id, string Provider, string Label, string Meta)[] models,
        string selectedId,
        string groupName,
        string multilingualText,
        string currentLabel,
        string? currentModelId,
        RoutedEventHandler onChanged,
        string? disabledLabel = null)
    {
        var lastProvider = "";

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

            var isDisabled = string.IsNullOrEmpty(id);

            var radio = new RadioButton
            {
                GroupName = groupName,
                Tag = id,
                IsChecked = isDisabled ? string.IsNullOrEmpty(selectedId) : selectedId == id,
                Margin = new Thickness(0),
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock
            {
                Text = isDisabled ? (disabledLabel ?? label) : label,
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Meta info (dimensions, size, multilingual)
            if (!string.IsNullOrEmpty(meta))
            {
                var metaText = meta;
                if (id is "nomic-embed-text-v2-moe" or "bge-m3" or "qwen3:4b")
                    metaText += $" \u00b7 {multilingualText}";

                content.Children.Add(new TextBlock
                {
                    Text = metaText,
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                });
            }

            // "(current)" tag in re-indexing mode
            if (currentModelId is not null &&
                (isDisabled ? string.IsNullOrEmpty(currentModelId) : currentModelId == id))
            {
                content.Children.Add(new TextBlock
                {
                    Text = currentLabel,
                    Opacity = 0.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                });
            }

            radio.Content = content;
            radio.Checked += onChanged;
            panel.Children.Add(radio);
        }
    }

    #endregion
}
