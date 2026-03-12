using FluentView.AI.Models;
using FluentView.AI.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentView.AI.SampleApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ChatPanel.Provider = new MockProvider();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ApiKeyBox is null) return; // Guard during initialization

        // Show API key field for Claude, OpenAI, Gemini, Groq; hide for Mock and Ollama
        var index = ProviderCombo.SelectedIndex;
        ApiKeyBox.Visibility = (index == 1 || index == 2 || index == 4 || index == 5)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Show Browse button only for Ollama
        if (BrowseModelsButton is not null)
            BrowseModelsButton.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnApplyProvider(object sender, RoutedEventArgs e)
    {
        // Dispose previous provider if needed
        if (ChatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        var apiKey = ApiKeyBox.Password?.Trim() ?? "";
        // If a ComboBoxItem is selected, use its Tag (model Id); otherwise use typed text
        var model = ModelCombo.SelectedItem is ComboBoxItem selected && selected.Tag is string tagId
            ? tagId
            : ModelCombo.Text?.Trim() ?? "";

        IAiProvider provider = ProviderCombo.SelectedIndex switch
        {
            1 => string.IsNullOrEmpty(model)
                ? new ClaudeProvider(apiKey)
                : new ClaudeProvider(apiKey, model),
            2 => string.IsNullOrEmpty(model)
                ? new OpenAiProvider(apiKey)
                : new OpenAiProvider(apiKey, model),
            3 => string.IsNullOrEmpty(model)
                ? new OllamaProvider()
                : new OllamaProvider(model),
            4 => string.IsNullOrEmpty(model)
                ? new GeminiProvider(apiKey)
                : new GeminiProvider(apiKey, model),
            5 => string.IsNullOrEmpty(model)
                ? new OpenAiProvider(apiKey, "llama-3.3-70b-versatile",
                    "https://api.groq.com/openai/v1", "Groq")
                : new OpenAiProvider(apiKey, model,
                    "https://api.groq.com/openai/v1", "Groq"),
            _ => new MockProvider()
        };

        ChatPanel.Provider = provider;
        ChatPanel.ClearConversation();

        _ = ValidateAndLoadAsync(provider);
    }

    private async Task ValidateAndLoadAsync(IAiProvider provider)
    {
        StatusText.Text = "Connecting...";
        try
        {
            var info = await provider.ValidateConnectionAsync();
            if (info.IsValid)
            {
                var status = $"\u2705 Connected: {provider.ProviderName}";
                if (!string.IsNullOrEmpty(info.OrganizationId))
                    status += $" (org: {info.OrganizationId})";
                StatusText.Text = status;

                await LoadModelsAsync(provider);
            }
            else
            {
                StatusText.Text = $"\u274C {info.ErrorMessage ?? "Connection failed"}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"\u274C {ex.Message}";
        }
    }

    private async void OnRefreshModels(object sender, RoutedEventArgs e)
    {
        if (ChatPanel.Provider is not null)
        {
            await LoadModelsAsync(ChatPanel.Provider);
        }
    }

    private async Task LoadModelsAsync(IAiProvider provider)
    {
        try
        {
            var models = await provider.ListModelsAsync();
            ModelCombo.Items.Clear();

            foreach (var m in models)
            {
                ModelCombo.Items.Add(new ComboBoxItem
                {
                    Content = m.DisplayName ?? m.Id,
                    Tag = m.Id
                });
            }

            // Select current model if it exists in the list
            for (int i = 0; i < ModelCombo.Items.Count; i++)
            {
                if (ModelCombo.Items[i] is ComboBoxItem item &&
                    item.Tag is string id && id == provider.ModelId)
                {
                    ModelCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        catch
        {
            // Model list unavailable — user can still type manually
        }
    }

    private async void OnBrowseModels(object sender, RoutedEventArgs e)
    {
        using var manager = new OllamaModelManager();
        var dialog = new ModelSelectionDialog(manager)
        {
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.SelectedModelId is not null)
        {
            ModelCombo.Text = dialog.SelectedModelId;
        }
    }

    private void OnClearChat(object sender, RoutedEventArgs e)
    {
        ChatPanel.ClearConversation();
    }
}
