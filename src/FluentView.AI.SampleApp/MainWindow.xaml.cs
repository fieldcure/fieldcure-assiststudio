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

        // Show API key field for Claude and OpenAI, hide for Mock and Ollama
        var index = ProviderCombo.SelectedIndex;
        ApiKeyBox.Visibility = (index == 1 || index == 2)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnApplyProvider(object sender, RoutedEventArgs e)
    {
        // Dispose previous provider if needed
        if (ChatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        var apiKey = ApiKeyBox.Password?.Trim() ?? "";
        var model = ModelBox.Text?.Trim() ?? "";

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
            _ => new MockProvider()
        };

        ChatPanel.Provider = provider;
        ChatPanel.ClearConversation();
    }

    private void OnClearChat(object sender, RoutedEventArgs e)
    {
        ChatPanel.ClearConversation();
    }
}
