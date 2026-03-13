using FluentView.AI.Controls;
using FluentView.AI.SampleApp.Dialogs;
using FluentView.AI.Helpers;
using FluentView.AI.Models;
using FluentView.AI.Providers;
using FluentView.AI.SampleApp.Helpers;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace FluentView.AI.SampleApp;

public sealed partial class MainWindow : Window
{
    private int _tabCounter;
    private AppWindow? _appWindow;
    private List<PromptPreset> _promptPresets;

    public MainWindow()
    {
        InitializeComponent();

        // Custom title bar: use SetTitleBar for drag region
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarReservedArea);
        TitleBarReservedArea.MinWidth = 188;

        _appWindow = this.AppWindow;

        // Load prompt presets
        _promptPresets = AppSettings.LoadPromptPresets();

        // Wire up settings events
        SettingsPane.ThemeChanged += OnThemeChanged;
        SettingsPane.SystemPromptChanged += OnSystemPromptChanged;
        SettingsPane.PresetsChanged += OnPresetsChanged;
        SettingsPane.PromptPresetsChanged += OnPromptPresetsChanged;

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

        LeftPaddingColumn.Width = new GridLength(_appWindow.TitleBar.LeftInset / scale);
        RightPaddingColumn.Width = new GridLength(_appWindow.TitleBar.RightInset / scale);

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
        BuildRecentConversationsMenu();
        FlyoutBase.ShowAttachedFlyout(MainMenuButton);
    }

    private void OnMenuNewTab(object sender, RoutedEventArgs e)
    {
        CreateChatTab();
    }

    private async void OnMenuSaveConversation(object sender, RoutedEventArgs e)
    {
        await SaveTabAsync(Tabs.SelectedItem as TabViewItem);
    }

    private async void OnMenuSaveAsConversation(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is not TabViewItem tab) return;

        var chatPanel = GetChatPanelFromTab(tab);
        if (chatPanel is null) return;

        var messages = chatPanel.GetMessages();
        if (messages.Count == 0) return;

        var nameBox = new TextBox
        {
            Text = tab.Header as string ?? "",
            PlaceholderText = "Conversation name",
        };

        var dialog = new ContentDialog
        {
            Title = "Save As",
            Content = nameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var newName = nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            tab.Header = newName;
            var presetName = chatPanel.SelectedPreset?.Name;
            try
            {
                await ConversationManager.SaveConversationAsync(newName, presetName, messages);
            }
            catch { /* Save failed silently */ }
        }
    }

    private async void OnMenuSaveAllConversations(object sender, RoutedEventArgs e)
    {
        foreach (TabViewItem tab in Tabs.TabItems)
        {
            await SaveTabAsync(tab);
        }
    }

    private async Task SaveTabAsync(TabViewItem? tab)
    {
        if (tab is null) return;

        var chatPanel = GetChatPanelFromTab(tab);
        if (chatPanel is null) return;

        var messages = chatPanel.GetMessages();
        if (messages.Count == 0) return;

        var tabName = tab.Header as string ?? $"Chat {_tabCounter}";
        var presetName = chatPanel.SelectedPreset?.Name;

        try
        {
            await ConversationManager.SaveConversationAsync(tabName, presetName, messages);
        }
        catch { /* Save failed silently */ }
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

    private void BuildRecentConversationsMenu()
    {
        RecentConversationsSubMenu.Items.Clear();

        var conversations = ConversationManager.ListSavedConversations(top: 10);

        if (conversations.Count == 0)
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var emptyItem = new MenuFlyoutItem
            {
                Text = loader.GetString("Menu_NoRecentConversations"),
                IsEnabled = false,
            };
            RecentConversationsSubMenu.Items.Add(emptyItem);
            return;
        }

        var idx = 1;
        foreach (var conv in conversations)
        {
            var filePath = conv.FilePath;
            var modified = conv.ModifiedAt.ToLocalTime().ToString("g");
            var item = new MenuFlyoutItem
            {
                Text = $"{idx}  {conv.FileName}",
            };
            ToolTipService.SetToolTip(item, $"{conv.FileName}\n{modified}");

            item.Click += async (_, _) =>
            {
                var data = await ConversationManager.LoadConversationAsync(filePath);
                if (data is not null)
                {
                    LoadConversationIntoNewTab(data);
                }
            };

            RecentConversationsSubMenu.Items.Add(item);
            idx++;
        }

        // Separator + Clear
        RecentConversationsSubMenu.Items.Add(new MenuFlyoutSeparator());

        var loader2 = new Windows.ApplicationModel.Resources.ResourceLoader();
        var clearItem = new MenuFlyoutItem
        {
            Text = loader2.GetString("Menu_ClearRecentHistory"),
            Icon = new FontIcon { Glyph = "\xE74D" },
        };
        clearItem.Click += (_, _) =>
        {
            ConversationManager.ClearAll();
        };
        RecentConversationsSubMenu.Items.Add(clearItem);
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
            SystemPrompt = GetActivePromptText(),
            Theme = GetCurrentTheme(),
            AvailablePresets = SettingsPane.Presets,
            SelectedPreset = preset,
            AvailablePromptPresets = _promptPresets,
            SelectedPromptPreset = GetActivePromptPreset(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
#if DEBUG
            IsDebugMode = true,
#endif
        };
        chatPanel.PresetChanged += OnChatPanelPresetChanged;

        // Restore messages
        foreach (var msg in data.Messages)
        {
            chatPanel.AddRestoredMessage(msg.Role, msg.Content, msg.ProviderName, msg.ProviderModelId);
        }

        var tab = new TabViewItem
        {
            Header = data.TabName,
            Content = chatPanel,
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
        var chatPanel = GetChatPanelFromTab(args.Tab);

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
                var presetName = chatPanel.SelectedPreset?.Name;
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

        sender.TabItems.Remove(args.Tab);

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
            SystemPrompt = GetActivePromptText(),
            Theme = GetCurrentTheme(),
            AvailablePresets = SettingsPane.Presets,
            SelectedPreset = preset,
            AvailablePromptPresets = _promptPresets,
            SelectedPromptPreset = GetActivePromptPreset(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
#if DEBUG
            IsDebugMode = true,
#endif
        };
        chatPanel.PresetChanged += OnChatPanelPresetChanged;
        chatPanel.AutoTitle = AppSettings.UtilityAutoTitle;
        chatPanel.TitleGenerated += OnTitleGenerated;

        var tab = new TabViewItem
        {
            Header = preset.Name,
            Content = chatPanel,
            IconSource = new SymbolIconSource { Symbol = Symbol.Message },
        };

        Tabs.TabItems.Add(tab);
        Tabs.SelectedItem = tab;
    }

    // ===== Provider Switching (via InputContainer ComboBox) =====

    private void OnChatPanelPresetChanged(object? sender, ProviderPreset preset)
    {
        if (sender is not ChatPanel chatPanel) return;

        // Dispose old provider
        if (chatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Create new provider — conversation history is preserved
        chatPanel.Provider = ProviderFactory.Create(preset);

        // Update tab header
        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && ReferenceEquals(tab.Content, chatPanel))
            {
                tab.Header = preset.Name;
                break;
            }
        }
    }

    private void OnTitleGenerated(object? sender, string title)
    {
        if (sender is not ChatPanel chatPanel) return;

        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab && ReferenceEquals(tab.Content, chatPanel))
            {
                tab.Header = title;
                break;
            }
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

        ApplyAppTheme(theme);

        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab)
            {
                var chatPanel = GetChatPanelFromTab(tab);
                if (chatPanel is not null)
                    chatPanel.Theme = chatTheme;
            }
        }
    }

    private void OnSystemPromptChanged(object? sender, string prompt)
    {
        // Reload prompt presets (text may have changed)
        _promptPresets = AppSettings.LoadPromptPresets();

        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab)
            {
                var chatPanel = GetChatPanelFromTab(tab);
                if (chatPanel is not null)
                {
                    chatPanel.SystemPrompt = prompt;
                    chatPanel.AvailablePromptPresets = _promptPresets;
                    chatPanel.SelectedPromptPreset = GetActivePromptPreset();
                }
            }
        }
    }

    private void OnPromptPresetsChanged(object? sender, EventArgs e)
    {
        _promptPresets = AppSettings.LoadPromptPresets();
        var active = GetActivePromptPreset();

        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab)
            {
                var chatPanel = GetChatPanelFromTab(tab);
                if (chatPanel is not null)
                {
                    chatPanel.AvailablePromptPresets = _promptPresets;
                    chatPanel.SelectedPromptPreset = active;
                }
            }
        }
    }

    private void OnPresetsChanged(object? sender, EventArgs e)
    {
        foreach (var item in Tabs.TabItems)
        {
            if (item is TabViewItem tab)
            {
                var chatPanel = GetChatPanelFromTab(tab);
                if (chatPanel is not null)
                {
                    var currentName = chatPanel.SelectedPreset?.Name;
                    chatPanel.AvailablePresets = SettingsPane.Presets;
                    // Re-select the same preset if it still exists
                    if (currentName is not null)
                    {
                        var match = SettingsPane.Presets.FirstOrDefault(p => p.Name == currentName);
                        if (match is not null)
                        {
                            chatPanel.SelectedPreset = match;
                        }
                    }
                }
            }
        }
    }

    // ===== Helpers =====

    private static ChatPanel? GetChatPanelFromTab(TabViewItem tab)
    {
        return tab.Content as ChatPanel;
    }

    private ProviderPreset GetDefaultPreset()
    {
        if (SettingsPane.Presets.Count == 0)
            return new ProviderPreset { Name = "Mock", ProviderType = "Mock" };

        var defaultName = AppSettings.DefaultProvider;
        return SettingsPane.Presets.FirstOrDefault(p => p.Name == defaultName)
               ?? SettingsPane.Presets[0];
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

    private PromptPreset? GetActivePromptPreset()
    {
        var name = AppSettings.ActivePromptPreset;
        return _promptPresets.Find(p => p.Name == name) ?? _promptPresets.FirstOrDefault();
    }

    private string GetActivePromptText()
    {
        return GetActivePromptPreset()?.Text ?? AppSettings.SystemPrompt;
    }
}
