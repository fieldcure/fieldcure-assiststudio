using AssistStudio.Controls;
using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
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
        if (ReferenceEquals(sender, ClaudeHeader) && ClaudeSection is null)
        {
            FindName(nameof(ClaudeSection));
            ClaudeSection!.SubHeaderChanged += (_, text) => ClaudeHeader.SubHeader = text;
            ClaudeSection.Initialize(_presets);
        }
        else if (ReferenceEquals(sender, OpenAIHeader) && OpenAISection is null)
        {
            FindName(nameof(OpenAISection));
            OpenAISection!.SubHeaderChanged += (_, text) => OpenAIHeader.SubHeader = text;
            OpenAISection.Initialize(_presets);
        }
        else if (ReferenceEquals(sender, GeminiHeader) && GeminiSection is null)
        {
            FindName(nameof(GeminiSection));
            GeminiSection!.SubHeaderChanged += (_, text) => GeminiHeader.SubHeader = text;
            GeminiSection.Initialize(_presets);
        }
        else if (ReferenceEquals(sender, GroqHeader) && GroqSection is null)
        {
            FindName(nameof(GroqSection));
            GroqSection!.SubHeaderChanged += (_, text) => GroqHeader.SubHeader = text;
            GroqSection.Initialize(_presets);
        }
        else if (ReferenceEquals(sender, OllamaHeader) && OllamaSection is null)
        {
            FindName(nameof(OllamaSection));
            OllamaSection!.SubHeaderChanged += (_, text) => OllamaHeader.SubHeader = text;
            OllamaSection.Initialize(_presets);
        }
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

        // Ollama content is deferred — load it before accessing UI
        if (OllamaSection is null)
        {
            FindName(nameof(OllamaSection));
            OllamaSection!.SubHeaderChanged += (_, text) => OllamaHeader.SubHeader = text;
            OllamaSection.Initialize(_presets);
        }
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
