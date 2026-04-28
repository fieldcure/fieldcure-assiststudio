using AssistStudio.Mcp.ModelAvailability;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Controls;

/// <summary>
/// Reusable model selection control for embedding and chunk contextualization.
/// Uses ComboBox with ItemTemplate for compact display.
/// </summary>
public sealed partial class EmbeddingModelSelector : UserControl
{
    #region Fields

    private readonly ResourceLoader _loader = new();
    private string _selectedEmbeddingId = "nomic-embed-text";
    private string _selectedContextualizerId = "";
    private List<ModelOption> _embeddingOptions = [];
    private List<ModelOption> _contextualizerOptions = [];
    private bool _isInitializing;

    /// <summary>
    /// Per-model availability map populated by <see cref="InitializeAsync"/>.
    /// </summary>
    private IReadOnlyDictionary<string, bool>? _availability;

    #endregion

    #region Events

    /// <summary>
    /// Fired whenever the user picks a different embedding or contextualizer model.
    /// </summary>
    public event EventHandler? SelectionChanged;

    #endregion

    #region Model Definitions

    private static readonly (string Id, string Provider, string Label, string Meta, bool IsMultilingual)[] EmbeddingModels =
    [
        ("nomic-embed-text", "Ollama", "nomic-embed-text", "768d \u00b7 274MB", false),
        ("nomic-embed-text-v2-moe", "Ollama", "nomic-embed-text-v2-moe", "768d \u00b7 1.9GB", true),
        ("bge-m3", "Ollama", "bge-m3", "1024d \u00b7 1.2GB", true),
        ("qwen3-embedding:8b", "Ollama", "qwen3-embedding:8b", "4096d \u00b7 ~5GB \u00b7 32k ctx", true),
        ("text-embedding-3-small", "OpenAI", "text-embedding-3-small", "1536d", false),
        ("text-embedding-3-large", "OpenAI", "text-embedding-3-large", "3072d", false),
        ("gemini-embedding-2", "Gemini", "gemini-embedding-2", "1536d \u00b7 8k ctx", true),
    ];

    private static readonly (string Id, string Provider, string Label, string Meta, bool IsMultilingual)[] ContextualizerModels =
    [
        ("gemma3:4b", "Ollama", "gemma3:4b", "2.8GB", false),
        ("qwen3:4b", "Ollama", "qwen3:4b", "2.7GB", true),
        ("gpt-4o-mini", "OpenAI", "gpt-4o-mini", "", false),
        ("claude-haiku-4-5", "Claude", "claude-haiku-4-5", "", false),
    ];

    private static readonly Dictionary<string, (string Provider, string? ApiKeyPreset)> ProviderMap = new()
    {
        ["nomic-embed-text"] = ("ollama", null),
        ["nomic-embed-text-v2-moe"] = ("ollama", null),
        ["bge-m3"] = ("ollama", null),
        ["qwen3-embedding:8b"] = ("ollama", null),
        ["text-embedding-3-small"] = ("openai", "OpenAI"),
        ["text-embedding-3-large"] = ("openai", "OpenAI"),
        ["gemini-embedding-2"] = ("gemini", "Gemini"),
        ["gemma3:4b"] = ("ollama", null),
        ["qwen3:4b"] = ("ollama", null),
        ["gpt-4o-mini"] = ("openai", "OpenAI"),
        ["claude-haiku-4-5"] = ("anthropic", "Claude"),
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
    /// Model ID to pre-select in re-indexing mode. Null in creation mode.
    /// </summary>
    public string? CurrentEmbeddingModel { get; set; }

    /// <summary>
    /// Contextualizer ID to pre-select in re-indexing mode. Null in creation mode.
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

    /// <summary>
    /// Whether every currently-selected model is reachable.
    /// </summary>
    public bool IsCurrentSelectionAvailable
    {
        get
        {
            if (_availability is null) return true;
            if (_availability.TryGetValue(_selectedEmbeddingId, out var embOk) && !embOk)
                return false;
            if (!string.IsNullOrEmpty(_selectedContextualizerId)
                && _availability.TryGetValue(_selectedContextualizerId, out var ctxOk)
                && !ctxOk)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Whether the selected embedding model is a local model (Ollama)
    /// that may benefit from deferred indexing.
    /// </summary>
    public bool IsSelectedEmbeddingLocal =>
        ProviderMap.TryGetValue(_selectedEmbeddingId, out var info) && info.Provider == "ollama";

    #endregion

    #region Public Methods

    /// <summary>
    /// Builds the model ComboBox lists and probes model availability.
    /// Call after setting <see cref="CurrentEmbeddingModel"/> and
    /// <see cref="CurrentContextualizer"/>.
    /// </summary>
    public async Task InitializeAsync()
    {
        _isInitializing = true;
        try
        {
            var embeddingDefault = CurrentEmbeddingModel ?? "nomic-embed-text";
            _selectedEmbeddingId = embeddingDefault;

            var contextualizerDefault = CurrentContextualizer ?? "";
            _selectedContextualizerId = contextualizerDefault;

            var multilingualLabel = _loader.GetString("Connect_Multilingual");
            var notInstalledLabel = _loader.GetString("KB_ModelNotInstalled") ?? "(not installed)";

            var checker = new ModelAvailabilityChecker();
            var available = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var (id, provider, _, _, _) in EmbeddingModels)
            {
                if (!available.ContainsKey(id))
                    available[id] = await checker.IsAvailableAsync(MapProviderName(provider), id);
            }
            foreach (var (id, provider, _, _, _) in ContextualizerModels)
            {
                if (!available.ContainsKey(id))
                    available[id] = await checker.IsAvailableAsync(MapProviderName(provider), id);
            }
            _availability = available;

            _embeddingOptions = BuildModelOptions(EmbeddingModels, available, multilingualLabel, notInstalledLabel);
            _contextualizerOptions = BuildModelOptions(ContextualizerModels, available, multilingualLabel, notInstalledLabel);

            EmbeddingCombo.ItemsSource = _embeddingOptions;
            EmbeddingCombo.SelectedItem = _embeddingOptions.FirstOrDefault(o => o.Id == embeddingDefault)
                ?? _embeddingOptions.FirstOrDefault();

            ContextualizerCombo.ItemsSource = _contextualizerOptions;

            var hasContextualizer = !string.IsNullOrEmpty(contextualizerDefault);
            ContextualizerToggle.IsOn = hasContextualizer;
            SetContextualizerVisible(hasContextualizer);

            if (hasContextualizer)
            {
                ContextualizerCombo.SelectedItem = _contextualizerOptions.FirstOrDefault(o => o.Id == contextualizerDefault)
                    ?? _contextualizerOptions.FirstOrDefault();
            }

            UpdateMetaText();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// Returns the selected embedding model as a <see cref="KbProviderConfig"/>.
    /// </summary>
    public KbProviderConfig GetEmbeddingConfig()
    {
        // Gemini embedding-2 supports Matryoshka dimension truncation. 1536 is
        // the documented sweet spot — matches MTEB score of full 3072 at half
        // the storage. Users wanting 768 (further compression) or 3072 (max
        // quality) can edit config.json directly; the dialog has no dim picker.
        var dimension = _selectedEmbeddingId == "gemini-embedding-2" ? 1536 : 0;

        if (ProviderMap.TryGetValue(_selectedEmbeddingId, out var info))
            return new KbProviderConfig { Provider = info.Provider, Model = _selectedEmbeddingId, ApiKeyPreset = info.ApiKeyPreset, Dimension = dimension };
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

    private void OnEmbeddingComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (EmbeddingCombo.SelectedItem is ModelOption option)
        {
            _selectedEmbeddingId = option.Id;
            UpdateMetaText();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnContextualizerComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (ContextualizerCombo.SelectedItem is ModelOption option)
        {
            _selectedContextualizerId = option.Id;
            UpdateMetaText();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Toggles the contextualizer combobox visibility and selection when the
    /// user flips the Chunk contextualization switch. Off collapses the
    /// combobox entirely (and clears the selected id); On restores it and
    /// seeds a default selection if none exists.
    /// </summary>
    private void OnContextualizerToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var isOn = ContextualizerToggle.IsOn;
        SetContextualizerVisible(isOn);

        if (isOn)
        {
            if (ContextualizerCombo.SelectedItem is ModelOption option)
                _selectedContextualizerId = option.Id;
            else if (_contextualizerOptions.Count > 0)
            {
                ContextualizerCombo.SelectedItem = _contextualizerOptions[0];
                _selectedContextualizerId = _contextualizerOptions[0].Id;
            }
        }
        else
        {
            _selectedContextualizerId = "";
        }

        UpdateMetaText();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Shows or collapses both the contextualizer combobox and its meta
    /// caption together, so the "Off" state hides the row entirely instead
    /// of leaving a greyed-out empty combobox behind.
    /// </summary>
    private void SetContextualizerVisible(bool visible)
    {
        var v = visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        ContextualizerCombo.Visibility = v;
        ContextualizerMetaText.Visibility = v;
    }

    private static string MapProviderName(string displayProvider) => displayProvider switch
    {
        "Ollama" => "ollama",
        "OpenAI" => "openai",
        "Claude" => "anthropic",
        "Gemini" => "gemini",
        _ => displayProvider.ToLowerInvariant(),
    };

    private static List<ModelOption> BuildModelOptions(
        (string Id, string Provider, string Label, string Meta, bool IsMultilingual)[] catalog,
        IReadOnlyDictionary<string, bool> available,
        string multilingualLabel,
        string notInstalledLabel)
    {
        var options = new List<ModelOption>();

        foreach (var (id, provider, label, meta, isMultilingual) in catalog)
        {
            var metaText = meta;
            if (isMultilingual && !string.IsNullOrEmpty(multilingualLabel))
                metaText += string.IsNullOrEmpty(metaText) ? multilingualLabel : $" \u00b7 {multilingualLabel}";

            var isAvailable = !available.TryGetValue(id, out var ok) || ok;

            options.Add(new ModelOption
            {
                Id = id,
                Provider = provider,
                Label = label,
                Meta = metaText,
                IsAvailable = isAvailable,
                StatusText = isAvailable ? "" : notInstalledLabel,
                IsLocal = MapProviderName(provider) == "ollama",
            });
        }

        return options;
    }

    private void UpdateMetaText()
    {
        if (EmbeddingCombo.SelectedItem is ModelOption emb)
            EmbeddingMetaText.Text = emb.Meta;
        else
            EmbeddingMetaText.Text = "";

        if (ContextualizerToggle.IsOn && ContextualizerCombo.SelectedItem is ModelOption ctx)
            ContextualizerMetaText.Text = ctx.Meta;
        else
            ContextualizerMetaText.Text = "";
    }

    #endregion
}
