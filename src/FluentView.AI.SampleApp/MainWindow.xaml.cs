using FluentView.AI.SampleApp.Dialogs;
using FluentView.AI.Helpers;
using FluentView.AI.Models;
using FluentView.AI.SampleApp.Helpers;
using FluentView.AI.SampleApp.ViewModels;
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
    private AppWindow? _appWindow;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        // Custom title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarReservedArea);
        TitleBarReservedArea.MinWidth = 188;
        _appWindow = this.AppWindow;

        // Create ViewModel
        ViewModel = new MainViewModel
        {
            GetPresets = () => SettingsPane.Presets,
        };

        // Wire settings events → ViewModel
        SettingsPane.ThemeChanged += (_, theme) =>
        {
            ApplyAppTheme(theme);
            ViewModel.ApplyThemeToAll(theme);
        };
        SettingsPane.SystemPromptChanged += (_, prompt) => ViewModel.ApplySystemPromptToAll(prompt);
        SettingsPane.PresetsChanged += (_, _) => ViewModel.RefreshPresetsOnAll();
        SettingsPane.PromptPresetsChanged += (_, _) => ViewModel.RefreshPromptPresetsOnAll();

        // Apply saved theme on first activation
        Activated += OnFirstActivated;

        // Create initial tab
        ViewModel.AddTab();

        // Title bar layout
        Tabs.Loaded += (_, _) => SetRegionsForCustomTitleBar();
        Tabs.SizeChanged += (_, _) => SetRegionsForCustomTitleBar();
    }


    // ===== First Activation & Theme =====

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        ApplyAppTheme(AppSettings.Theme);

        if (AppSettings.IsFirstRun)
        {
            AppSettings.IsFirstRun = false;
            var dialog = new FirstRunDialog { XamlRoot = Content.XamlRoot };
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
        ViewModel.AddTab();
    }

    private async void OnMenuSaveConversation(object sender, RoutedEventArgs e)
    {
        var tab = ViewModel.SelectedTab;
        if (tab is null) return;

        if (!tab.HasBeenSaved)
        {
            // First save → show name dialog (same as Save As)
            await SaveAsAsync(tab);
        }
        else
        {
            await ViewModel.SaveTabAsync(tab);
        }
    }

    private async void OnMenuSaveAsConversation(object sender, RoutedEventArgs e)
    {
        var tab = ViewModel.SelectedTab;
        if (tab is null) return;
        await SaveAsAsync(tab);
    }

    private async Task SaveAsAsync(ChatTabViewModel tab)
    {
        var messages = tab.GetMessages();
        if (messages.Count == 0) return;

        var nameBox = new TextBox
        {
            Text = tab.Title,
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

            tab.Title = newName;
            var presetName = tab.CurrentPreset?.Name;
            try
            {
                await ConversationManager.SaveConversationAsync(newName, presetName, messages);
                tab.IsDirty = false;
                tab.HasBeenSaved = true;
            }
            catch { /* Save failed silently */ }
        }
    }

    private async void OnMenuSaveAllConversations(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveAllAsync();
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
                ViewModel.LoadConversation(data);
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
                    ViewModel.LoadConversation(data);
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

    private void OnMenuSettings(object sender, RoutedEventArgs e)
    {
        RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;
    }

    // ===== Tab Management =====

    private void OnAddTab(TabView sender, object args)
    {
        ViewModel.AddTab();
    }

    private async void OnCloseTab(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is not ChatTabViewModel vm) return;

        // Ask to save if conversation has messages
        if (vm.GetMessages().Count > 0)
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
            if (result == ContentDialogResult.None) return; // Cancel

            if (result == ContentDialogResult.Primary) // Save
            {
                if (!vm.HasBeenSaved)
                    await SaveAsAsync(vm);
                else
                    await ViewModel.SaveTabAsync(vm);
            }
        }

        ViewModel.CloseTab(vm);

        if (ViewModel.Tabs.Count == 0)
        {
            Close();
        }
    }
}
