using FluentView.AI.Models;
using FluentView.AI.Providers;
using FluentView.AI.SampleApp.Dialogs;
using FluentView.AI.Helpers;
using FluentView.AI.SampleApp.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FluentView.AI.SampleApp.Settings;

public sealed partial class ModelsPage : Page
{
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

    private SettingsPanel? _settings;
    private bool _isLoading = true;

    // Fallback models when cache is empty and API is unreachable
    private static readonly string[] FallbackClaudeModels =
        ["claude-sonnet-4-20250514", "claude-opus-4-20250514", "claude-haiku-4-20250514"];
    private static readonly string[] FallbackOpenAIModels =
        ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "o1", "o1-mini", "o3-mini"];
    private static readonly string[] FallbackGeminiModels =
        ["gemini-2.0-flash", "gemini-2.0-flash-lite", "gemini-2.5-pro-preview-05-06"];
    private static readonly string[] FallbackGroqModels =
        ["llama-3.3-70b-versatile", "llama-3.1-8b-instant", "mixtral-8x7b-32768"];

    public ModelsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SettingsPanel settings)
        {
            _settings = settings;
        }

        _isLoading = true;
        LoadApiKeys();
        PopulateModelCombos();
        PopulateDefaultProvider();
        _isLoading = false;

        // Auto-check Ollama status
        _ = CheckOllamaStatusAsync();
    }

    // ===== API Key Management =====

    private void LoadApiKeys()
    {
        var claudeKey = PasswordVaultHelper.LoadApiKey("Claude");
        var openAIKey = PasswordVaultHelper.LoadApiKey("OpenAI");
        var geminiKey = PasswordVaultHelper.LoadApiKey("Gemini");
        var groqKey = PasswordVaultHelper.LoadApiKey("Groq");

        SetKeyState("Claude", claudeKey, ClaudeApiKeyBox, ClaudeStatusText, ClaudeKeyButton, ClaudeRemoveKeyButton, ClaudeModelCombo);
        SetKeyState("OpenAI", openAIKey, OpenAIApiKeyBox, OpenAIStatusText, OpenAIKeyButton, OpenAIRemoveKeyButton, OpenAIModelCombo);
        SetKeyState("Gemini", geminiKey, GeminiApiKeyBox, GeminiStatusText, GeminiKeyButton, GeminiRemoveKeyButton, GeminiModelCombo);
        SetKeyState("Groq", groqKey, GroqApiKeyBox, GroqStatusText, GroqKeyButton, GroqRemoveKeyButton, GroqModelCombo);
    }

    private void SetKeyState(string provider, string key, PasswordBox keyBox, TextBlock statusText, Button keyButton, Button removeButton, ComboBox modelCombo)
    {
        if (!string.IsNullOrEmpty(key))
        {
            keyBox.Password = MaskKey(key);
            statusText.Text = "";
            keyButton.Content = L("Models_UpdateKey");
            removeButton.Visibility = Visibility.Visible;
            modelCombo.IsEnabled = true;
        }
        else
        {
            keyBox.Password = "";
            statusText.Text = L("Models_NoKey");
            statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
            keyButton.Content = L("Models_AddKey");
            removeButton.Visibility = Visibility.Collapsed;
            modelCombo.IsEnabled = false;
        }
    }

    private static string MaskKey(string key)
    {
        if (key.Length <= 6) return "••••••";
        return key[..3] + "•••" + key[^3..];
    }

    // Track whether user has edited the password field
    private bool _claudeKeyDirty, _openAIKeyDirty, _geminiKeyDirty, _groqKeyDirty;

    private void OnClaudeKeyChanged(object s, RoutedEventArgs e) { if (!_isLoading) _claudeKeyDirty = true; }
    private void OnOpenAIKeyChanged(object s, RoutedEventArgs e) { if (!_isLoading) _openAIKeyDirty = true; }
    private void OnGeminiKeyChanged(object s, RoutedEventArgs e) { if (!_isLoading) _geminiKeyDirty = true; }
    private void OnGroqKeyChanged(object s, RoutedEventArgs e) { if (!_isLoading) _groqKeyDirty = true; }

    private void OnUpdateClaudeKey(object sender, RoutedEventArgs e)
    {
        if (_claudeKeyDirty)
        {
            SaveProviderKey("Claude", ClaudeApiKeyBox.Password, ClaudeStatusText, ClaudeKeyButton, ClaudeRemoveKeyButton, ClaudeModelCombo);
            _claudeKeyDirty = false;
        }
    }

    private void OnUpdateOpenAIKey(object sender, RoutedEventArgs e)
    {
        if (_openAIKeyDirty)
        {
            SaveProviderKey("OpenAI", OpenAIApiKeyBox.Password, OpenAIStatusText, OpenAIKeyButton, OpenAIRemoveKeyButton, OpenAIModelCombo);
            _openAIKeyDirty = false;
        }
    }

    private void OnUpdateGeminiKey(object sender, RoutedEventArgs e)
    {
        if (_geminiKeyDirty)
        {
            SaveProviderKey("Gemini", GeminiApiKeyBox.Password, GeminiStatusText, GeminiKeyButton, GeminiRemoveKeyButton, GeminiModelCombo);
            _geminiKeyDirty = false;
        }
    }

    private void OnUpdateGroqKey(object sender, RoutedEventArgs e)
    {
        if (_groqKeyDirty)
        {
            SaveProviderKey("Groq", GroqApiKeyBox.Password, GroqStatusText, GroqKeyButton, GroqRemoveKeyButton, GroqModelCombo);
            _groqKeyDirty = false;
        }
    }

    private void SaveProviderKey(string provider, string key, TextBlock statusText, Button keyButton, Button removeButton, ComboBox modelCombo)
    {
        PasswordVaultHelper.SaveApiKey(provider, key);

        if (!string.IsNullOrEmpty(key))
        {
            statusText.Text = "";
            keyButton.Content = L("Models_UpdateKey");
            removeButton.Visibility = Visibility.Visible;
            modelCombo.IsEnabled = true;

            // Fetch model list immediately after saving a valid key
            _ = FetchAndCacheModelsAsync(provider, key, modelCombo);
        }
        else
        {
            statusText.Text = L("Models_NoKey");
            statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
            keyButton.Content = L("Models_AddKey");
            removeButton.Visibility = Visibility.Collapsed;
            modelCombo.IsEnabled = false;
        }

        SyncPresetsFromUI();
        RefreshDefaultProvider();
    }

    private void RemoveProviderKey(string provider, PasswordBox keyBox, TextBlock statusText, Button keyButton, Button removeButton, ComboBox modelCombo)
    {
        PasswordVaultHelper.DeleteApiKey(provider);
        _isLoading = true;
        SetKeyState(provider, "", keyBox, statusText, keyButton, removeButton, modelCombo);
        _isLoading = false;
        SyncPresetsFromUI();
        RefreshDefaultProvider();
    }

    private void OnRemoveClaudeKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Claude", ClaudeApiKeyBox, ClaudeStatusText, ClaudeKeyButton, ClaudeRemoveKeyButton, ClaudeModelCombo);

    private void OnRemoveOpenAIKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("OpenAI", OpenAIApiKeyBox, OpenAIStatusText, OpenAIKeyButton, OpenAIRemoveKeyButton, OpenAIModelCombo);

    private void OnRemoveGeminiKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Gemini", GeminiApiKeyBox, GeminiStatusText, GeminiKeyButton, GeminiRemoveKeyButton, GeminiModelCombo);

    private void OnRemoveGroqKey(object sender, RoutedEventArgs e) =>
        RemoveProviderKey("Groq", GroqApiKeyBox, GroqStatusText, GroqKeyButton, GroqRemoveKeyButton, GroqModelCombo);

    // ===== Model ComboBoxes =====

    private void PopulateModelCombos()
    {
        // Load from cache first, fall back to hardcoded defaults
        PopulateComboFromCacheOrFallback("Claude", ClaudeModelCombo, FallbackClaudeModels);
        PopulateComboFromCacheOrFallback("OpenAI", OpenAIModelCombo, FallbackOpenAIModels);
        PopulateComboFromCacheOrFallback("Gemini", GeminiModelCombo, FallbackGeminiModels);
        PopulateComboFromCacheOrFallback("Groq", GroqModelCombo, FallbackGroqModels);

        // Background refresh for providers that have API keys
        _ = RefreshAllModelCachesAsync();
    }

    private static void PopulateComboFromCacheOrFallback(string provider, ComboBox combo, string[] fallback)
    {
        var cached = AppSettings.GetCachedModels(provider);
        var models = cached?.ToArray() ?? fallback;
        var savedModel = AppSettings.GetDefaultModel(provider);

        PopulateCombo(combo, models, savedModel);
    }

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

    private async Task RefreshAllModelCachesAsync()
    {
        // Fire-and-forget refresh for each provider with a valid key
        var tasks = new List<Task>();

        var claudeKey = PasswordVaultHelper.LoadApiKey("Claude");
        if (!string.IsNullOrEmpty(claudeKey))
            tasks.Add(FetchAndCacheModelsAsync("Claude", claudeKey, ClaudeModelCombo));

        var openAIKey = PasswordVaultHelper.LoadApiKey("OpenAI");
        if (!string.IsNullOrEmpty(openAIKey))
            tasks.Add(FetchAndCacheModelsAsync("OpenAI", openAIKey, OpenAIModelCombo));

        var geminiKey = PasswordVaultHelper.LoadApiKey("Gemini");
        if (!string.IsNullOrEmpty(geminiKey))
            tasks.Add(FetchAndCacheModelsAsync("Gemini", geminiKey, GeminiModelCombo));

        var groqKey = PasswordVaultHelper.LoadApiKey("Groq");
        if (!string.IsNullOrEmpty(groqKey))
            tasks.Add(FetchAndCacheModelsAsync("Groq", groqKey, GroqModelCombo));

        await Task.WhenAll(tasks);
    }

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
                    PopulateCombo(combo, filtered.ToArray(), current ?? AppSettings.GetDefaultModel(provider));
                });
            }
            finally
            {
                (provider_ as IDisposable)?.Dispose();
            }
        }
        catch
        {
            // API unreachable — keep current list (cache or fallback)
        }
    }

    private static IAiProvider CreateProviderForListing(string provider, string apiKey) => provider switch
    {
        "Claude" => new ClaudeProvider(apiKey, "dummy"),
        "OpenAI" => new OpenAiProvider(apiKey, "dummy"),
        "Gemini" => new GeminiProvider(apiKey, "dummy"),
        "Groq" => new OpenAiProvider(apiKey, "dummy",
            baseUrl: "https://api.groq.com/openai/v1", providerName: "Groq"),
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };

    private static List<string> FilterChatModels(string provider, IReadOnlyList<AiModel> models)
    {
        return provider switch
        {
            "Claude" => models
                .Where(m => m.Id.StartsWith("claude-"))
                .Select(m => m.Id).ToList(),
            "OpenAI" => models
                .Where(m => m.Id.StartsWith("gpt-") || m.Id.StartsWith("o1") || m.Id.StartsWith("o3") || m.Id.StartsWith("o4"))
                .Select(m => m.Id).ToList(),
            "Gemini" => models
                .Where(m => m.Id.StartsWith("gemini-"))
                .Select(m => m.Id).ToList(),
            "Groq" => models
                .Select(m => m.Id).ToList(),
            _ => models.Select(m => m.Id).ToList()
        };
    }

    // ===== Default Provider =====

    private void PopulateDefaultProvider()
    {
        DefaultProviderCombo.Items.Clear();

        // Add available providers (those with API keys or local)
        var claudeKey = PasswordVaultHelper.LoadApiKey("Claude");
        var openAIKey = PasswordVaultHelper.LoadApiKey("OpenAI");
        var geminiKey = PasswordVaultHelper.LoadApiKey("Gemini");
        var groqKey = PasswordVaultHelper.LoadApiKey("Groq");

        if (!string.IsNullOrEmpty(claudeKey)) DefaultProviderCombo.Items.Add("Anthropic Claude");
        if (!string.IsNullOrEmpty(openAIKey)) DefaultProviderCombo.Items.Add("OpenAI");
        if (!string.IsNullOrEmpty(geminiKey)) DefaultProviderCombo.Items.Add("Google Gemini");
        if (!string.IsNullOrEmpty(groqKey)) DefaultProviderCombo.Items.Add("Groq");
        // Ollama is always available if installed
        DefaultProviderCombo.Items.Add("Ollama");
        DefaultProviderCombo.Items.Add("Mock");

        // Select saved default
        var saved = AppSettings.DefaultProvider;
        var displayName = ProviderToDisplayName(saved);
        for (var i = 0; i < DefaultProviderCombo.Items.Count; i++)
        {
            if (DefaultProviderCombo.Items[i] is string s && s == displayName)
            {
                DefaultProviderCombo.SelectedIndex = i;
                return;
            }
        }

        // Fallback: select first
        if (DefaultProviderCombo.Items.Count > 0)
            DefaultProviderCombo.SelectedIndex = 0;
    }

    private void RefreshDefaultProvider()
    {
        var prev = DefaultProviderCombo.SelectedItem as string;
        _isLoading = true;
        PopulateDefaultProvider();
        _isLoading = false;
    }

    private void OnDefaultProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || DefaultProviderCombo.SelectedItem is not string display) return;

        var providerKey = DisplayNameToProvider(display);
        AppSettings.DefaultProvider = providerKey;
        SyncPresetsFromUI();
    }

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

    private static string DisplayNameToProvider(string display) => display switch
    {
        "Anthropic Claude" => "Claude",
        "OpenAI" => "OpenAI",
        "Google Gemini" => "Gemini",
        "Groq" => "Groq",
        "Ollama" => "Ollama",
        "Mock" => "Mock",
        _ => "Mock"
    };

    // ===== Sync presets back to SettingsPanel =====

    private void SyncPresetsFromUI()
    {
        if (_settings is null) return;

        _settings.Presets.Clear();

        // Build presets from current UI state
        var defaultProvider = AppSettings.DefaultProvider;

        // Cloud providers
        foreach (var provider in new[] { "Claude", "OpenAI", "Gemini", "Groq" })
        {
            var key = PasswordVaultHelper.LoadApiKey(provider);
            if (string.IsNullOrEmpty(key)) continue;

            var modelCombo = provider switch
            {
                "Claude" => ClaudeModelCombo,
                "OpenAI" => OpenAIModelCombo,
                "Gemini" => GeminiModelCombo,
                "Groq" => GroqModelCombo,
                _ => null
            };

            var modelId = modelCombo?.SelectedItem as string ?? "";
            AppSettings.SetDefaultModel(provider, modelId);

            var preset = new ProviderPreset
            {
                Name = ProviderToDisplayName(provider),
                ProviderType = provider,
                ModelId = modelId,
                ApiKey = key
            };

            if (provider == "Groq")
                preset.BaseUrl = "https://api.groq.com/openai/v1";

            _settings.Presets.Add(preset);
        }

        // Ollama
        var ollamaModel = OllamaModelCombo.SelectedItem as string ?? "";
        AppSettings.SetDefaultModel("Ollama", ollamaModel);
        _settings.Presets.Add(new ProviderPreset
        {
            Name = "Ollama",
            ProviderType = "Ollama",
            ModelId = ollamaModel
        });

        // Mock
        _settings.Presets.Add(new ProviderPreset
        {
            Name = "Mock",
            ProviderType = "Mock"
        });

        _settings.RaisePresetsChanged();
    }

    // ===== Ollama =====

    private async void OnCheckOllama(object sender, RoutedEventArgs e)
    {
        await CheckOllamaStatusAsync();
    }

    private async Task CheckOllamaStatusAsync()
    {
        OllamaSpinner.Visibility = Visibility.Visible;
        OllamaSpinner.IsActive = true;
        OllamaStatusText.Text = L("Models_Checking");
        OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.Colors.Gray);
        StartOllamaButton.Visibility = Visibility.Collapsed;
        OllamaInstallPanel.Visibility = Visibility.Collapsed;
        BrowseModelsButton.Visibility = Visibility.Collapsed;

        try
        {
            var isRunning = await OllamaHelper.IsOllamaRunningAsync();
            if (isRunning)
            {
                OllamaStatusText.Text = L("Models_Running");
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Green);
                BrowseModelsButton.Visibility = Visibility.Visible;
                await LoadOllamaModelsAsync();
            }
            else if (OllamaHelper.IsOllamaInstalled())
            {
                OllamaStatusText.Text = L("Models_InstalledNotRunning");
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Orange);
                StartOllamaButton.Visibility = Visibility.Visible;
            }
            else
            {
                OllamaStatusText.Text = L("Models_NotInstalled");
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.OrangeRed);
                OllamaInstallPanel.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            OllamaStatusText.Text = L("Models_Error");
            OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
        }
        finally
        {
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadOllamaModelsAsync()
    {
        try
        {
            using var manager = new OllamaModelManager();
            var models = await manager.ListLocalModelsAsync();
            OllamaModelCombo.Items.Clear();
            foreach (var m in models)
            {
                OllamaModelCombo.Items.Add(m.Id);
            }

            if (OllamaModelCombo.Items.Count > 0)
            {
                OllamaModelCombo.IsEnabled = true;
                var saved = AppSettings.GetDefaultModel("Ollama");
                var found = false;
                for (var i = 0; i < OllamaModelCombo.Items.Count; i++)
                {
                    if (OllamaModelCombo.Items[i] is string s && s == saved)
                    {
                        OllamaModelCombo.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found) OllamaModelCombo.SelectedIndex = 0;
            }
        }
        catch
        {
            // Failed to load models
        }
    }

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
                OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.OrangeRed);
            }
        }
        catch
        {
            OllamaStatusText.Text = L("Models_ErrorStarting");
            OllamaStatusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.OrangeRed);
        }
        finally
        {
            StartOllamaButton.IsEnabled = true;
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnBrowseOllamaModels(object sender, RoutedEventArgs e)
    {
        using var manager = new OllamaModelManager();
        var dialog = new ModelSelectionDialog(manager)
        {
            XamlRoot = XamlRoot
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

    private async Task PullModelsAsync(List<string> modelNames)
    {
        PullProgressPanel.Visibility = Visibility.Visible;
        BrowseModelsButton.IsEnabled = false;

        using var manager = new OllamaModelManager();

        for (var i = 0; i < modelNames.Count; i++)
        {
            var modelName = modelNames[i];
            var prefix = modelNames.Count > 1 ? $"[{i + 1}/{modelNames.Count}] " : "";

            PullProgressStatus.Text = $"{prefix}{string.Format(L("Models_PullingModel"), modelName)}";
            PullProgressBar.IsIndeterminate = true;
            PullProgressBar.Value = 0;

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
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
                await manager.DownloadModelAsync(modelName, progress);
            }
            catch (Exception ex)
            {
                PullProgressStatus.Text = $"{prefix}Error: {ex.Message}";
                await Task.Delay(2000);
            }
        }

        PullProgressBar.IsIndeterminate = false;
        PullProgressPanel.Visibility = Visibility.Collapsed;
        BrowseModelsButton.IsEnabled = true;
    }
}
