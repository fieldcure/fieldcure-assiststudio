using FluentView.AI.Controls;
using FluentView.AI.SampleApp.Dialogs;
using FluentView.AI.SampleApp.Helpers;
using FluentView.AI.SampleApp.Models;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace FluentView.AI.SampleApp;

public sealed partial class MainWindow : Window
{
    private int _tabCounter;
    private AppWindow? _appWindow;

    public MainWindow()
    {
        InitializeComponent();

        // Custom title bar: use SetTitleBar for drag region
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarReservedArea);
        TitleBarReservedArea.MinWidth = 188;

        _appWindow = this.AppWindow;

        // Wire up settings events
        SettingsPane.ThemeChanged += OnThemeChanged;
        SettingsPane.SystemPromptChanged += OnSystemPromptChanged;
        SettingsPane.PresetsChanged += OnPresetsChanged;

        // Apply saved theme to app root
        Activated += OnFirstActivated;

        // Create initial tab
        CreateChatTab();

        // Set up interactive passthrough regions once content is loaded
        Tabs.Loaded += (_, _) => SetRegionsForCustomTitleBar();
        Tabs.SizeChanged += (_, _) => SetRegionsForCustomTitleBar();
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        ApplyAppTheme(AppSettings.Theme);

        if (AppSettings.IsFirstRun)
        {
            AppSettings.IsFirstRun = false;
            var dialog = new FirstRunDialog
            {
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private void ApplyAppTheme(string theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        // Update title bar caption button colors to match theme
        if (_appWindow?.TitleBar is { } titleBar)
        {
            var transparent = Colors.Transparent;
            titleBar.BackgroundColor = transparent;
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.InactiveBackgroundColor = transparent;
            titleBar.ButtonInactiveBackgroundColor = transparent;

            // Determine effective theme (resolve "System" to actual)
            var isDark = theme == "Dark" ||
                (theme != "Light" && Application.Current.RequestedTheme == ApplicationTheme.Dark);

            var foreground = isDark ? Colors.White : Colors.Black;
            var hoverBg = isDark
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
            var pressedBg = isDark
                ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
            var inactiveFg = isDark
                ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x99, 0x00, 0x00, 0x00);

            titleBar.ForegroundColor = foreground;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressedBg;
            titleBar.ButtonInactiveForegroundColor = inactiveFg;
        }
    }

    // ===== Custom Title Bar =====

    private void SetRegionsForCustomTitleBar()
    {
        if (_appWindow is null || !ExtendsContentIntoTitleBar || ShellTitlebarInset.XamlRoot is null)
            return;

        var scale = ShellTitlebarInset.XamlRoot.RasterizationScale;

        // Left padding to account for system caption buttons inset
        LeftPaddingColumn.Width = new GridLength(_appWindow.TitleBar.LeftInset / scale);
        RightPaddingColumn.Width = new GridLength(_appWindow.TitleBar.RightInset / scale);

        // The entire tab strip area (header + tabs + add button) needs to be
        // passthrough so interactive elements (menu button, tabs, close buttons) work.
        // TitleBarReservedArea is already set as the title bar drag region via SetTitleBar.
        var transform = ShellTitlebarInset.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0,
            ShellTitlebarInset.ActualWidth, ShellTitlebarInset.ActualHeight));
        var headerRect = GetRect(bounds, scale);

        var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, [headerRect]);
    }

    private static RectInt32 GetRect(Rect bounds, double scale)
    {
        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    // ===== Main Menu =====

    private void OnMainMenuClick(object sender, RoutedEventArgs e)
    {
        var flyout = FlyoutBase.GetAttachedFlyout(MainMenuButton);
        if (flyout is not null)
        {
            flyout.Closed += OnMenuFlyoutClosed;
        }

        PlayMenuRotation(-90);
        FlyoutBase.ShowAttachedFlyout(MainMenuButton);
    }

    private void OnMenuFlyoutClosed(object? sender, object e)
    {
        if (sender is FlyoutBase flyout)
        {
            flyout.Closed -= OnMenuFlyoutClosed;
        }

        PlayMenuRotation(0);
    }

    private void PlayMenuRotation(double toAngle)
    {
        var animation = new DoubleAnimation
        {
            To = toAngle,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, MainMenuButtonRotation);
        Storyboard.SetTargetProperty(animation, "Angle");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void OnMenuNewTab(object sender, RoutedEventArgs e)
    {
        CreateChatTab();
    }

    private async void OnMenuSaveConversation(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is not TabViewItem tab || tab.Content is not Grid grid)
            return;

        var chatPanel = FindChatPanel(grid);
        if (chatPanel is null) return;

        var messages = chatPanel.GetMessages();
        if (messages.Count == 0) return;

        var tabName = tab.Header as string ?? $"Chat {_tabCounter}";
        var presetName = GetCurrentPresetName(grid);

        try
        {
            await ConversationManager.SaveConversationAsync(tabName, presetName, messages);
        }
        catch
        {
            // Save failed silently
        }
    }

    private async void OnMenuLoadConversation(object sender, RoutedEventArgs e)
    {
        var conversations = ConversationManager.ListSavedConversations();
        if (conversations.Count == 0) return;

        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 300,
            ItemsSource = conversations.Select(c => new
            {
                c.FileName,
                c.FilePath,
                Modified = c.ModifiedAt.ToLocalTime().ToString("g"),
            }).ToList(),
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                """
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <StackPanel Padding="4">
                        <TextBlock Text="{Binding FileName}" />
                        <TextBlock Text="{Binding Modified}" FontSize="11" Opacity="0.6" />
                    </StackPanel>
                </DataTemplate>
                """),
        };

        var dialog = new ContentDialog
        {
            Title = "Load Conversation",
            Content = listView,
            PrimaryButtonText = "Load",
            CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = false,
            XamlRoot = Content.XamlRoot,
        };

        listView.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = listView.SelectedItem is not null;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && listView.SelectedItem is not null)
        {
            dynamic selected = listView.SelectedItem;
            string filePath = selected.FilePath;

            var data = await ConversationManager.LoadConversationAsync(filePath);
            if (data is not null)
            {
                LoadConversationIntoNewTab(data);
            }
        }
    }

    private void LoadConversationIntoNewTab(ConversationData data)
    {
        // Find matching preset or use default
        ProviderPreset? preset = null;
        if (data.ProviderPresetName is not null)
        {
            preset = SettingsPane.Presets.FirstOrDefault(p => p.Name == data.ProviderPresetName);
        }
        preset ??= GetDefaultPreset();

        _tabCounter++;
        var provider = ProviderFactory.Create(preset);

        var chatPanel = new ChatPanel
        {
            Provider = provider,
            Placeholder = "Type a message...",
            SystemPrompt = AppSettings.SystemPrompt,
            Theme = GetCurrentTheme(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // Restore messages
        foreach (var msg in data.Messages)
        {
            chatPanel.AddRestoredMessage(msg.Role, msg.Content, msg.ProviderName, msg.ProviderModelId);
        }

        var providerCombo = new ComboBox
        {
            Width = 180,
            Margin = new Thickness(12, 8, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        PopulateProviderCombo(providerCombo, preset.Name);
        providerCombo.SelectionChanged += (s, _) => OnTabProviderChanged(s as ComboBox, chatPanel);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(providerCombo, 0);
        Grid.SetRow(chatPanel, 1);
        grid.Children.Add(providerCombo);
        grid.Children.Add(chatPanel);

        var tab = new TabViewItem
        {
            Header = data.TabName,
            Content = grid,
            IconSource = new SymbolIconSource { Symbol = Symbol.Message },
        };

        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;
    }

    private void OnMenuSettings(object sender, RoutedEventArgs e)
    {
        RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;
    }

    // ===== Tab Management =====

    private void OnAddTab(TabView sender, object args)
    {
        CreateChatTab();
    }

    private async void OnCloseTab(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab.Content is Grid grid)
        {
            var chatPanel = FindChatPanel(grid);

            // Ask to save if conversation has messages
            if (chatPanel is not null && chatPanel.GetMessages().Count > 0)
            {
                var dialog = new ContentDialog
                {
                    Title = "Save conversation?",
                    Content = "Do you want to save this conversation before closing?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Don't Save",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot,
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None) // Cancel
                    return;

                if (result == ContentDialogResult.Primary) // Save
                {
                    var tabName = args.Tab.Header as string ?? $"Chat {_tabCounter}";
                    var presetName = GetCurrentPresetName(grid);
                    try
                    {
                        await ConversationManager.SaveConversationAsync(
                            tabName, presetName, chatPanel.GetMessages());
                    }
                    catch
                    {
                        // Save failed silently
                    }
                }
            }

            if (chatPanel?.Provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        sender.TabItems.Remove(args.Tab);

        // Close window when all tabs are removed
        if (sender.TabItems.Count == 0)
        {
            Close();
        }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Could be used for per-tab status updates in the future
    }

    private void CreateChatTab(ProviderPreset? preset = null)
    {
        _tabCounter++;

        preset ??= GetDefaultPreset();
        var provider = ProviderFactory.Create(preset);

        var chatPanel = new ChatPanel
        {
            Provider = provider,
            Placeholder = "Type a message...",
            SystemPrompt = AppSettings.SystemPrompt,
            Theme = GetCurrentTheme(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // Provider selector ComboBox above the chat
        var providerCombo = new ComboBox
        {
            Width = 180,
            Margin = new Thickness(12, 8, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        PopulateProviderCombo(providerCombo, preset.Name);
        providerCombo.SelectionChanged += (s, e) => OnTabProviderChanged(s as ComboBox, chatPanel);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(providerCombo, 0);
        Grid.SetRow(chatPanel, 1);

        grid.Children.Add(providerCombo);
        grid.Children.Add(chatPanel);

        var tab = new TabViewItem
        {
            Header = preset.Name,
            Content = grid,
            IconSource = new SymbolIconSource { Symbol = Symbol.Message },
        };

        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;
    }

    // ===== Provider Switching =====

    private void PopulateProviderCombo(ComboBox combo, string? selectedPresetName)
    {
        combo.Items.Clear();
        foreach (var p in SettingsPane.Presets)
        {
            combo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
        }

        // Select matching preset
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Content as string == selectedPresetName)
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void OnTabProviderChanged(ComboBox? combo, ChatPanel chatPanel)
    {
        if (combo?.SelectedItem is not ComboBoxItem selected || selected.Tag is not ProviderPreset preset)
            return;

        // Dispose old provider
        if (chatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Create new provider — conversation history is preserved
        chatPanel.Provider = ProviderFactory.Create(preset);

        // Update tab header
        if (combo.Parent is Grid grid && grid.Parent is TabViewItem tab)
        {
            tab.Header = preset.Name;
        }
    }

    // ===== Settings =====

    private void OnThemeChanged(object? sender, string theme)
    {
        var chatTheme = theme switch
        {
            "Light" => ChatTheme.Light,
            "Dark" => ChatTheme.Dark,
            _ => ChatTheme.System,
        };

        // Apply app-wide theme + title bar colors
        ApplyAppTheme(theme);

        // Apply to all chat panels
        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && tab.Content is Grid grid)
            {
                var chatPanel = FindChatPanel(grid);
                if (chatPanel is not null)
                    chatPanel.Theme = chatTheme;
            }
        }
    }

    private void OnSystemPromptChanged(object? sender, string prompt)
    {
        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && tab.Content is Grid grid)
            {
                var chatPanel = FindChatPanel(grid);
                if (chatPanel is not null)
                    chatPanel.SystemPrompt = prompt;
            }
        }
    }

    private void OnPresetsChanged(object? sender, EventArgs e)
    {
        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && tab.Content is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is ComboBox combo)
                    {
                        var currentPreset = (combo.SelectedItem as ComboBoxItem)?.Content as string;
                        PopulateProviderCombo(combo, currentPreset);
                        break;
                    }
                }
            }
        }
    }

    // ===== Helpers =====

    private static ChatPanel? FindChatPanel(Grid grid)
    {
        foreach (var child in grid.Children)
        {
            if (child is ChatPanel cp)
                return cp;
        }
        return null;
    }

    private static string? GetCurrentPresetName(Grid grid)
    {
        foreach (var child in grid.Children)
        {
            if (child is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
                return item.Content as string;
        }
        return null;
    }

    private ProviderPreset GetDefaultPreset()
    {
        return SettingsPane.Presets.Count > 0
            ? SettingsPane.Presets[0]
            : new ProviderPreset { Name = "Mock", ProviderType = "Mock" };
    }

    private ChatTheme GetCurrentTheme()
    {
        return AppSettings.Theme switch
        {
            "Light" => ChatTheme.Light,
            "Dark" => ChatTheme.Dark,
            _ => ChatTheme.System,
        };
    }
}
