using FluentView.AI.SampleApp.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentView.AI.SampleApp.Settings;

public sealed partial class PromptPage : Page
{
    private SettingsPanel? _settings;

    public PromptPage()
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

        // Load current system prompt
        SystemPromptBox.Text = AppSettings.SystemPrompt;
    }

    private void OnSystemPromptChanged(object sender, TextChangedEventArgs e)
    {
        _settings?.RaiseSystemPromptChanged(SystemPromptBox.Text);
    }
}
