using AssistStudio.Controls;
using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelsPage"/> class.
    /// </summary>
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

        // Build dynamic sections for custom providers
        BuildCustomProviderSections();

        // Lazy-check Ollama status to avoid blocking page entry
        _ = DelayedCheckOllamaAsync();

        // Resume progress tracking if downloads were started before navigating away
        if (OllamaProviderSection.HasPendingPulls)
        {
            EnsureOllamaSectionLoaded();
            _ = OllamaSection!.ResumePullTrackingAsync();
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

    /// <summary>
    /// Builds a sub-header string from model ID and status parts.
    /// </summary>
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

    /// <summary>
    /// Delays briefly then ensures the Ollama section is loaded.
    /// </summary>
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

    #region Custom Providers

    /// <summary>
    /// Builds CollapsibleSection + CloudProviderSection for each registered custom provider.
    /// </summary>
    private void BuildCustomProviderSections()
    {
        CustomProvidersPanel.Children.Clear();

        var customs = AppSettings.LoadCustomProviders();
        foreach (var config in customs)
        {
            AddCustomProviderSection(config);
        }
    }

    /// <summary>
    /// Creates and adds a CollapsibleSection with a CloudProviderSection for a custom provider.
    /// </summary>
    private void AddCustomProviderSection(CustomProviderConfig config)
    {
        var providerType = $"Custom_{config.Id}";
        var initialized = false;

        var section = new CloudProviderSection
        {
            ProviderType = providerType,
            FallbackModelsString = "",
        };

        // Delete button below the provider section
        var deleteBtn = new Button
        {
            Content = L("Models_DeleteCustomProvider"),
            Tag = config.Id,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        deleteBtn.Click += OnDeleteCustomProvider;

        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(section);
        body.Children.Add(deleteBtn);

        var header = new CollapsibleSection
        {
            Header = config.DisplayName,
            IsExpanded = false,
            Body = body,
        };

        section.SubHeaderChanged += (_, text) => header.SubHeader = text;

        header.Expanded += (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            section.Initialize(_presets);
        };

        CustomProvidersPanel.Children.Add(header);

        // Update sub-header from preset data
        var preset = _presets.FirstOrDefault(p => p.ProviderType == providerType);
        if (preset is not null)
        {
            var model = preset.ModelId ?? "";
            var status = !string.IsNullOrEmpty(preset.ApiKey) ? "\u2713" : L("Models_NoKey");
            header.SubHeader = BuildSubHeader(
                string.IsNullOrEmpty(model) ? null : model, status);
        }
    }

    /// <summary>
    /// Opens the Add Custom Provider dialog.
    /// </summary>
    private async void OnAddCustomProvider(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCustomProviderDialog
        {
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || dialog.Result is null) return;

        var config = dialog.Result;

        // Save config
        var customs = AppSettings.LoadCustomProviders();
        customs.Add(config);
        AppSettings.SaveCustomProviders(customs);

        // Register with factory
        ProviderFactory.RegisterCustomProvider(config);

        // Reload presets (includes new synthetic preset)
        _presets = AppSettings.LoadPresets();

        // Add UI section
        AddCustomProviderSection(config);
    }

    /// <summary>
    /// Deletes a custom provider after confirmation.
    /// </summary>
    private async void OnDeleteCustomProvider(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string configId }) return;

        var dialog = new ThemedContentDialog
        {
            Title = L("Models_DeleteCustomProviderConfirmTitle"),
            Content = L("Models_DeleteCustomProviderConfirmMessage"),
            PrimaryButtonText = L("Models_Delete"),
            CloseButtonText = L("Models_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var providerType = $"Custom_{configId}";

        // Remove from storage
        var customs = AppSettings.LoadCustomProviders();
        customs.RemoveAll(c => c.Id == configId);
        AppSettings.SaveCustomProviders(customs);

        // Remove from factory
        ProviderFactory.UnregisterCustomProvider(configId);

        // Remove preset and API key
        var preset = _presets.FirstOrDefault(p => p.ProviderType == providerType);
        if (preset is not null) _presets.Remove(preset);
        PasswordVaultHelper.DeleteApiKey(providerType);
        AppSettings.SavePresets(_presets.ToList());

        // Remove UI section
        for (int i = CustomProvidersPanel.Children.Count - 1; i >= 0; i--)
        {
            if (CustomProvidersPanel.Children[i] is CollapsibleSection cs
                && cs.Body is StackPanel sp
                && sp.Children.FirstOrDefault() is CloudProviderSection cps
                && cps.ProviderType == providerType)
            {
                CustomProvidersPanel.Children.RemoveAt(i);
                break;
            }
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Returns the collapsible section header for the given provider name.
    /// </summary>
    private CollapsibleSection? GetHeaderForProvider(string provider) => provider switch
    {
        "Claude" => ClaudeHeader,
        "OpenAI" => OpenAIHeader,
        "Gemini" => GeminiHeader,
        "Groq" => GroqHeader,
        "Ollama" => OllamaHeader,
        _ => null
    };

    private static readonly ResourceLoader Res = new();

    /// <summary>
    /// Resolves a localized string, falling back to the key itself.
    /// </summary>
    private static string L(string key) =>
        Res.GetString(key) is { Length: > 0 } value ? value : key;

    #endregion
}
