using AssistStudio.Controls;
using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing AI provider API keys, model selection,
/// and Ollama local model configuration.
/// Hosts <see cref="CloudProviderSection"/> and <see cref="OllamaProviderSection"/> UserControls.
/// </summary>
public sealed partial class ModelsPage : Page
{
    #region Fields

    private ObservableCollection<ProviderPreset> _presets = [];

    #endregion

    #region Constructor

    public ModelsPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Navigation

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _presets = AppSettings.LoadPresets();

        // All section bodies are deferred (x:Load="False").
        // Individual sections are initialized via OnSectionExpanded when first expanded.
        UpdateAllSubHeaders();
        UpdateExpandedState();

        // Lazy-check Ollama status to avoid blocking page entry
        _ = DelayedCheckOllamaAsync();

        // Resume progress tracking if downloads were started before navigating away
        if (OllamaSection is not null && OllamaSection.HasPendingPulls)
        {
            _ = OllamaSection.ResumePullTrackingAsync();
        }

        // Pre-load remaining sections during idle time so they're ready when user expands
        PreLoadSectionsAsync();
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        OllamaSection?.CancelPullTracking();
    }

    #endregion

    #region Lazy Loading

    /// <summary>
    /// Handles CollapsibleSection expand to realize deferred (x:Load="False") content.
    /// </summary>
    private void OnSectionExpanded(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ClaudeHeader))
            EnsureSectionLoaded(nameof(ClaudeSection), ClaudeHeader, () => ClaudeSection);
        else if (ReferenceEquals(sender, OpenAIHeader))
            EnsureSectionLoaded(nameof(OpenAISection), OpenAIHeader, () => OpenAISection);
        else if (ReferenceEquals(sender, GeminiHeader))
            EnsureSectionLoaded(nameof(GeminiSection), GeminiHeader, () => GeminiSection);
        else if (ReferenceEquals(sender, GroqHeader))
            EnsureSectionLoaded(nameof(GroqSection), GroqHeader, () => GroqSection);
        else if (ReferenceEquals(sender, OllamaHeader))
            EnsureOllamaSectionLoaded();
    }

    /// <summary>
    /// Pre-loads all deferred sections during idle time so they're instantly
    /// available when the user expands them.
    /// </summary>
    private void PreLoadSectionsAsync()
    {
        var queue = new (string Name, Func<bool> IsLoaded, Action Load)[]
        {
            (nameof(ClaudeSection), () => ClaudeSection is not null, () => EnsureSectionLoaded(nameof(ClaudeSection), ClaudeHeader, () => ClaudeSection)),
            (nameof(OpenAISection), () => OpenAISection is not null, () => EnsureSectionLoaded(nameof(OpenAISection), OpenAIHeader, () => OpenAISection)),
            (nameof(GeminiSection), () => GeminiSection is not null, () => EnsureSectionLoaded(nameof(GeminiSection), GeminiHeader, () => GeminiSection)),
            (nameof(GroqSection), () => GroqSection is not null, () => EnsureSectionLoaded(nameof(GroqSection), GroqHeader, () => GroqSection)),
            // Ollama is loaded via DelayedCheckOllamaAsync — skip here
        };

        EnqueueSequential(queue, 0);
    }

    /// <summary>
    /// Enqueues section pre-loading one at a time on Low priority,
    /// yielding the UI thread between each section.
    /// </summary>
    private void EnqueueSequential((string Name, Func<bool> IsLoaded, Action Load)[] queue, int index)
    {
        if (index >= queue.Length) return;

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var (_, isLoaded, load) = queue[index];
            if (!isLoaded()) load();

            // Schedule next section
            EnqueueSequential(queue, index + 1);
        });
    }

    /// <summary>
    /// Ensures a cloud section is loaded and initialized.
    /// </summary>
    private void EnsureSectionLoaded(string sectionName, CollapsibleSection header, Func<CloudProviderSection?> getSection)
    {
        if (getSection() is not null) return;
        FindName(sectionName);
        var section = getSection();
        if (section is null) return;
        section.SubHeaderChanged += (_, text) => header.SubHeader = text;
        section.Initialize(_presets);
    }

    #endregion

    #region Sub-Headers

    /// <summary>
    /// Updates sub-headers for all provider sections using preset data (no UI control access needed).
    /// </summary>
    private void UpdateAllSubHeaders()
    {
        var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey) ?? [];

        var cloudProviders = new[] { "Claude", "OpenAI", "Gemini", "Groq" };
        foreach (var provider in cloudProviders)
        {
            var header = GetHeaderForProvider(provider);
            if (header is null) continue;

            keys.TryGetValue(provider, out var key);
            var preset = _presets.FirstOrDefault(p => p.ProviderType == provider);
            var model = preset?.ModelId ?? "";
            var status = !string.IsNullOrEmpty(key) ? "\u2713" : L("Models_NoKey");
            header.SubHeader = BuildSubHeader(
                string.IsNullOrEmpty(model) ? null : model, status);
        }

        // Ollama sub-header from preset data
        var ollamaPreset = _presets.FirstOrDefault(p => p.ProviderType == "Ollama");
        var ollamaModel = ollamaPreset?.ModelId ?? "";
        OllamaHeader.SubHeader = BuildSubHeader(
            string.IsNullOrEmpty(ollamaModel) ? null : ollamaModel, null);
    }

    private static string BuildSubHeader(string? modelId, string? status)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(modelId)) parts.Add(modelId);
        if (!string.IsNullOrEmpty(status)) parts.Add(status);
        return string.Join(" \u00B7 ", parts);
    }

    #endregion

    #region Expanded State

    /// <summary>
    /// Sets the initial expanded state: if only one provider has a key, expand that section.
    /// </summary>
    private void UpdateExpandedState()
    {
        var keys = _presets.ToDictionary(p => p.ProviderType, p => p.ApiKey) ?? [];

        var sections = new (string Provider, CollapsibleSection Header)[]
        {
            ("Claude", ClaudeHeader),
            ("OpenAI", OpenAIHeader),
            ("Gemini", GeminiHeader),
            ("Groq", GroqHeader),
        };

        var registeredSections = sections
            .Where(s => keys.TryGetValue(s.Provider, out var k) && !string.IsNullOrEmpty(k))
            .Select(s => s.Header)
            .ToList();

        if (registeredSections.Count == 1 && !keys.Any(k => k.Key == "Ollama" && !string.IsNullOrEmpty(k.Value)))
        {
            registeredSections[0].IsExpanded = true;
        }
    }

    #endregion

    #region Ollama Deferred Check

    private async Task DelayedCheckOllamaAsync()
    {
        await Task.Delay(300);
        EnsureOllamaSectionLoaded();
    }

    /// <summary>
    /// Ensures the Ollama section is loaded and initialized.
    /// </summary>
    private void EnsureOllamaSectionLoaded()
    {
        if (OllamaSection is not null) return;
        FindName(nameof(OllamaSection));
        if (OllamaSection is null) return;
        OllamaSection.SubHeaderChanged += (_, text) => OllamaHeader.SubHeader = text;
        OllamaSection.Initialize(_presets);
    }

    #endregion

    #region Helpers

    private CollapsibleSection? GetHeaderForProvider(string provider) => provider switch
    {
        "Claude" => ClaudeHeader,
        "OpenAI" => OpenAIHeader,
        "Gemini" => GeminiHeader,
        "Groq" => GroqHeader,
        "Ollama" => OllamaHeader,
        _ => null
    };

    private static string L(string key)
    {
        try
        {
            var loader = new ResourceLoader();
            return loader.GetString(key);
        }
        catch { return key; }
    }

    #endregion
}
