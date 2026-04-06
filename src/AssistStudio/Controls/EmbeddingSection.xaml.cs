using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;

#pragma warning disable CS0618 // Obsolete AppSettings embedding properties — used as defaults for new KBs

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section for selecting default embedding and chunk contextualization models.
/// Settings apply when creating new knowledge archives.
/// </summary>
public sealed partial class EmbeddingSection : UserControl
{
    #region Fields

    private readonly ResourceLoader _loader = new();
    private bool _embeddingLoaded;
    private bool _contextualizerLoaded;

    #endregion

    #region Embedding Model Definitions

    private static readonly (string Id, string Provider, string Label, string Meta)[] EmbeddingModels =
    [
        ("nomic-embed-text", "Ollama", "nomic-embed-text", "768d \u00b7 274MB"),
        ("nomic-embed-text-v2-moe", "Ollama", "nomic-embed-text-v2-moe", "768d \u00b7 1.9GB"),
        ("bge-m3", "Ollama", "bge-m3", "1024d \u00b7 1.2GB"),
        ("text-embedding-3-small", "OpenAI", "text-embedding-3-small", "1536d"),
        ("text-embedding-3-large", "OpenAI", "text-embedding-3-large", "3072d"),
    ];

    private static readonly (string Id, string Provider, string Label, string Meta)[] ContextualizerModels =
    [
        ("", "", "disabled", ""),
        ("gemma3:4b", "Ollama", "gemma3:4b", "2.8GB"),
        ("qwen3:4b", "Ollama", "qwen3:4b", "2.7GB"),
        ("gpt-4o-mini", "OpenAI", "gpt-4o-mini", ""),
        ("claude-haiku-4-5-20251001", "Claude", "claude-haiku-4-5-20251001", ""),
    ];

    #endregion

    #region Constructor

    public EmbeddingSection()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Applies localized header text and sub-header hint.
    /// </summary>
    public void Initialize()
    {
        EmbeddingModelSection.Header = _loader.GetString("Connect_EmbeddingModel");
        ContextualizerSection.Header = _loader.GetString("Connect_ChunkContextualization");
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Lazy-loads the embedding model radio buttons on first expand.
    /// </summary>
    private void OnEmbeddingModelExpanded(object? sender, EventArgs e)
    {
        if (_embeddingLoaded) return;
        _embeddingLoaded = true;

        // Hint text inside the content area
        EmbeddingModelPanel.Children.Add(new TextBlock
        {
            Text = _loader.GetString("Connect_EmbeddingHint"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var currentModel = AppSettings.EmbeddingModel;
        if (string.IsNullOrEmpty(currentModel)) currentModel = "nomic-embed-text";

        var multilingual = _loader.GetString("Connect_Multilingual");
        BuildModelList(EmbeddingModelPanel, EmbeddingModels, currentModel,
            "EmbeddingModel", multilingual, OnEmbeddingModelChanged);
    }

    /// <summary>
    /// Lazy-loads the contextualization model radio buttons on first expand.
    /// </summary>
    private void OnContextualizerExpanded(object? sender, EventArgs e)
    {
        if (_contextualizerLoaded) return;
        _contextualizerLoaded = true;

        var currentModel = AppSettings.ContextualizerModel;

        var multilingual = _loader.GetString("Connect_Multilingual");
        var disabledLabel = _loader.GetString("Connect_ChunkDisabled") ?? "Disabled";
        BuildModelList(ContextualizerPanel, ContextualizerModels, currentModel,
            "Contextualizer", multilingual, OnContextualizerChanged, disabledLabel);
    }

    /// <summary>
    /// Persists the selected embedding model to AppSettings.
    /// </summary>
    private void OnEmbeddingModelChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio || radio.Tag is not string modelId) return;
        AppSettings.EmbeddingModel = modelId;
        LoggingService.LogInfo($"[RAG] Default embedding model changed to: {modelId}");
    }

    /// <summary>
    /// Persists the selected contextualization model to AppSettings.
    /// </summary>
    private void OnContextualizerChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio || radio.Tag is not string modelId) return;
        AppSettings.ContextualizerModel = modelId;
        LoggingService.LogInfo($"[RAG] Default contextualizer changed to: {modelId}");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Builds a grouped radio button list for model selection.
    /// </summary>
    private static void BuildModelList(
        StackPanel panel,
        (string Id, string Provider, string Label, string Meta)[] models,
        string currentId,
        string groupName,
        string multilingualText,
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

            // "Disabled" option (empty id, no provider header)
            var isDisabled = string.IsNullOrEmpty(id);

            var radio = new RadioButton
            {
                GroupName = groupName,
                Tag = id,
                IsChecked = isDisabled ? string.IsNullOrEmpty(currentId) : currentId == id,
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
                // Add multilingual tag for models that support it
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

            radio.Content = content;
            radio.Checked += onChanged;
            panel.Children.Add(radio);
        }
    }

    #endregion
}
