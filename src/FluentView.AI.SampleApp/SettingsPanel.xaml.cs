using System.Collections.ObjectModel;
using FluentView.AI.Models;
using FluentView.AI.SampleApp.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentView.AI.SampleApp;

public sealed partial class SettingsPanel : Page
{
    private ObservableCollection<ProviderPreset> _presets;

    public ObservableCollection<ProviderPreset> Presets
    {
        get => _presets;
        set => _presets = value;
    }

    public event EventHandler<string>? ThemeChanged;
    public event EventHandler<string>? SystemPromptChanged;
    public event EventHandler? PresetsChanged;

    public SettingsPanel()
    {
        // Load presets early so they're available before Loaded event
        _presets = AppSettings.LoadPresets();

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Navigate to initial page
        NavigateTo("Models");

        // Select first item
        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void NavigateTo(string tag)
    {
        var pageType = tag switch
        {
            "Models" => typeof(Settings.ModelsPage),
            "Personalization" => typeof(Settings.PersonalizationPage),
            "Prompt" => typeof(Settings.PromptPage),
            "Advanced" => typeof(Settings.AdvancedPage),
            "About" => typeof(Settings.AboutPage),
            _ => typeof(Settings.ModelsPage),
        };

        ContentFrame.Navigate(pageType, this, new EntranceNavigationTransitionInfo());
    }

    // Called by sub-pages to raise events

    internal void RaiseThemeChanged(string theme)
    {
        AppSettings.Theme = theme;
        ThemeChanged?.Invoke(this, theme);
    }

    internal void RaiseSystemPromptChanged(string prompt)
    {
        AppSettings.SystemPrompt = prompt;
        SystemPromptChanged?.Invoke(this, prompt);
    }

    internal void RaisePresetsChanged()
    {
        AppSettings.SavePresets(_presets);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }
}
