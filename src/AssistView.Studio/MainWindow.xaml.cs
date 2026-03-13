using AssistView.Studio.Dialogs;
using FluentView.AI.Helpers;
using FluentView.AI.Models;
using AssistView.Studio.Helpers;
using AssistView.Studio.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace AssistView.Studio;

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

        // Handle app close with unsaved changes check
        _appWindow!.Closing += OnAppWindowClosing;

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

        if (tab.FilePath is not null)
        {
            // Re-save to existing path
            var messages = tab.GetMessages();
            if (messages.Count == 0) return;
            await ConversationManager.SaveToFileAsync(tab.FilePath, tab.Title, tab.CurrentPreset?.Name, messages);
            tab.IsDirty = false;
        }
        else
        {
            await SaveAsAsync(tab);
        }
    }

    private async void OnMenuSaveAsConversation(object sender, RoutedEventArgs e)
    {
        var tab = ViewModel.SelectedTab;
        if (tab is null) return;
        await SaveAsAsync(tab);
    }

    private async Task<bool> SaveAsAsync(ChatTabViewModel tab)
    {
        var messages = tab.GetMessages();
        if (messages.Count == 0) return false;

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = tab.Title;
        picker.FileTypeChoices.Add("AssistView Conversation", [ConversationManager.FileExtension]);
        picker.FileTypeChoices.Add("JSON", [".json"]);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;

        try
        {
            var presetName = tab.CurrentPreset?.Name;
            await ConversationManager.SaveToFileAsync(file.Path, tab.Title, presetName, messages);
            tab.FilePath = file.Path;
            tab.Title = Path.GetFileNameWithoutExtension(file.Path);
            tab.IsDirty = false;
            tab.HasBeenSaved = true;
            AppSettings.AddRecentFile(file.Path);
            return true;
        }
        catch { return false; }
    }

    private async void OnMenuSaveAllConversations(object sender, RoutedEventArgs e)
    {
        foreach (var tab in ViewModel.Tabs)
        {
            var messages = tab.GetMessages();
            if (messages.Count == 0) continue;

            if (tab.FilePath is not null)
            {
                await ConversationManager.SaveToFileAsync(tab.FilePath, tab.Title, tab.CurrentPreset?.Name, messages);
                tab.IsDirty = false;
            }
            else
            {
                await SaveAsAsync(tab);
            }
        }
    }

    private async void OnMenuLoadConversation(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(ConversationManager.FileExtension);
        picker.FileTypeFilter.Add(".json");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var data = await ConversationManager.LoadConversationAsync(file.Path);
        if (data is not null)
        {
            var tab = ViewModel.LoadConversation(data, file.Path);
            AppSettings.AddRecentFile(file.Path);
        }
    }

    private void BuildRecentConversationsMenu()
    {
        RecentConversationsSubMenu.Items.Clear();

        var recentPaths = AppSettings.RecentFilePaths
            .Where(File.Exists)
            .ToList();

        if (recentPaths.Count == 0)
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
        foreach (var filePath in recentPaths)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var modified = File.GetLastWriteTime(filePath).ToString("g");
            var item = new MenuFlyoutItem
            {
                Text = $"{idx}  {name}",
            };
            ToolTipService.SetToolTip(item, $"{filePath}\n{modified}");

            var path = filePath; // capture for lambda
            item.Click += async (_, _) =>
            {
                var data = await ConversationManager.LoadConversationAsync(path);
                if (data is not null)
                {
                    ViewModel.LoadConversation(data, path);
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
            AppSettings.RecentFilePaths = [];
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

        // Ask to save if conversation has unsaved changes
        if (vm.IsDirty && vm.GetMessages().Count > 0)
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var dialog = new ContentDialog
            {
                Title = loader.GetString("Dialog_SaveConversation"),
                Content = loader.GetString("Dialog_SaveConversationContent"),
                PrimaryButtonText = loader.GetString("Dialog_Save"),
                SecondaryButtonText = loader.GetString("Dialog_DontSave"),
                CloseButtonText = loader.GetString("Dialog_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None) return; // Cancel

            if (result == ContentDialogResult.Primary) // Save
            {
                if (vm.FilePath is not null)
                {
                    await ConversationManager.SaveToFileAsync(
                        vm.FilePath, vm.Title, vm.CurrentPreset?.Name, vm.GetMessages());
                    vm.IsDirty = false;
                }
                else
                {
                    if (!await SaveAsAsync(vm)) return; // User cancelled SaveAs
                }
            }
        }

        ViewModel.CloseTab(vm);

        if (ViewModel.Tabs.Count == 0)
        {
            Close();
        }
    }

    // ===== App Close — Unsaved Changes =====

    private bool _isClosing;

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosing) return;

        var dirtyTabs = ViewModel.Tabs.Where(t => t.IsDirty && t.GetMessages().Count > 0).ToList();
        if (dirtyTabs.Count == 0) return;

        args.Cancel = true;

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var dialog = new ContentDialog
        {
            Title = loader.GetString("Dialog_UnsavedChanges"),
            Content = string.Format(loader.GetString("Dialog_UnsavedChangesContent"), dirtyTabs.Count),
            PrimaryButtonText = loader.GetString("Dialog_SaveAllExit"),
            SecondaryButtonText = loader.GetString("Dialog_DiscardExit"),
            CloseButtonText = loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return; // Cancel

        if (result == ContentDialogResult.Primary) // Save All
        {
            foreach (var tab in dirtyTabs)
            {
                if (tab.FilePath is not null)
                {
                    await ConversationManager.SaveToFileAsync(
                        tab.FilePath, tab.Title, tab.CurrentPreset?.Name, tab.GetMessages());
                }
                else
                {
                    if (!await SaveAsAsync(tab)) return; // User cancelled one SaveAs → abort close
                }
            }
        }

        _isClosing = true;
        Close();
    }

    // ===== File Activation =====

    public async void OpenFileFromActivation(string filePath)
    {
        var data = await ConversationManager.LoadConversationAsync(filePath);
        if (data is not null)
        {
            ViewModel.LoadConversation(data, filePath);
            AppSettings.AddRecentFile(filePath);
        }
    }
}
