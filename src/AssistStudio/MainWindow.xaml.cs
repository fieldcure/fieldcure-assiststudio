using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Models;
using AssistStudio.Modules.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace AssistStudio;

/// <summary>
/// Main application window that hosts tabs, the settings pane, and manages the custom title bar.
/// </summary>
public sealed partial class MainWindow : Window
{
    #region Fields

    /// <summary>
    /// Reference to the underlying <see cref="AppWindow"/> for title bar customization.
    /// </summary>
    private readonly AppWindow? _appWindow;

    /// <summary>
    /// Indicates whether the window is in the process of closing to prevent re-entrant close dialogs.
    /// </summary>
    private bool _isClosing;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the main view model that manages tabs and application-level state.
    /// </summary>
    public MainViewModel ViewModel { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class, sets up the title bar,
    /// wires settings events, and registers keyboard accelerators.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Custom title bar
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarReservedArea);
        TitleBarReservedArea.MinWidth = 188;
        _appWindow = AppWindow;

        // Create ViewModel
        ViewModel = new MainViewModel
        {
            GetPresets = AppSettings.LoadPresets,
        };

        // Wire settings events → ViewModel
        AppSettings.ThemeChanged += (_, theme) =>
        {
            LoggingService.LogInfo($"[Settings] ThemeChanged → pushing to {ViewModel.Tabs.Count} tabs: {theme}");
            ApplyAppTheme(theme);
            ViewModel.ApplyThemeToAll(theme);
        };
        AppSettings.SystemPromptChanged += (_, prompt) =>
        {
            LoggingService.LogInfo($"[Settings] SystemPromptChanged → pushing to {ViewModel.Tabs.Count} tabs");
            ViewModel.ApplySystemPromptToAll(prompt);
        };
        AppSettings.PresetsChanged += (_, _) =>
        {
            LoggingService.LogInfo($"[Settings] PresetsChanged → refreshing on {ViewModel.Tabs.Count} tabs");
            ViewModel.RefreshPresetsOnAll();
        };
        AppSettings.ProfilesChanged += (_, _) =>
        {
            LoggingService.LogInfo($"[Settings] ProfilesChanged → refreshing on {ViewModel.Tabs.Count} tabs");
            ViewModel.RefreshProfilesOnAll();
            foreach (var tab in ViewModel.Tabs)
                tab.RefreshTools();
        };
        App.McpRegistry.ToolsChanged += (_, _) =>
        {
            LoggingService.LogInfo($"[Settings] MCP ToolsChanged → refreshing tools on {ViewModel.Tabs.Count} tabs");
            foreach (var tab in ViewModel.Tabs)
                tab.RefreshTools();
        };

        // Navigate to SettingsPanel when the pane opens
        RootSplitView.PaneOpening += (_, _) =>
        {
            SettingsFrame.Navigate(typeof(Settings.SettingsPanel), null,
                new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        };

        // Handle app close with unsaved changes check
        _appWindow!.Closing += OnAppWindowClosing;

        // Apply saved theme on first activation
        Activated += OnFirstActivated;

        // Forward keyboard shortcuts from WebView2 (separate HWND, can't use XAML accelerators)
        ViewModel.Tabs.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (ChatTabViewModel tab in e.NewItems)
                    tab.KeyboardShortcutPressed += OnWebViewShortcut;
            }
        };

        // Create initial tab (after CollectionChanged subscription so the first tab is wired)
        ViewModel.AddTab();

        // Title bar layout
        Tabs.Loaded += (_, _) =>
        {
            SetRegionsForCustomTitleBar();
        };
        Tabs.SizeChanged += (_, _) => SetRegionsForCustomTitleBar();

        // Global keyboard accelerators (MenuFlyout accelerators only work while open)
        RegisterAccelerator(VirtualKeyModifiers.Control, VirtualKey.N, OnMenuNewTab);
        RegisterAccelerator(VirtualKeyModifiers.Control, VirtualKey.O, OnMenuLoadConversation);
        RegisterAccelerator(VirtualKeyModifiers.Control, VirtualKey.S, OnMenuSaveConversation);
        RegisterAccelerator(VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, VirtualKey.S, OnMenuSaveAsConversation);
        RegisterAccelerator(VirtualKeyModifiers.Control, VirtualKey.F, OnToggleSearch);
        RegisterAccelerator(VirtualKeyModifiers.None, VirtualKey.F1, OnMenuSettings);

        // Notification system
        InitializeNotificationCenter();
    }

    #endregion

    #region Keyboard Accelerators

    /// <summary>
    /// Registers a global keyboard accelerator on the root split view.
    /// </summary>
    private void RegisterAccelerator(VirtualKeyModifiers modifiers, VirtualKey key, RoutedEventHandler handler)
    {
        var accel = new KeyboardAccelerator { Modifiers = modifiers, Key = key };
        accel.Invoked += (_, args) => { handler(this, new RoutedEventArgs()); args.Handled = true; };
        RootSplitView.KeyboardAccelerators.Add(accel);
    }

    /// <summary>
    /// Handles keyboard shortcuts forwarded from WebView2 (which runs in a separate HWND).
    /// </summary>
    private void OnWebViewShortcut(object? sender, string shortcut)
    {
        switch (shortcut)
        {
            case "Ctrl+S":
                OnMenuSaveConversation(this, new RoutedEventArgs());
                break;
            case "Ctrl+Shift+S":
                OnMenuSaveAsConversation(this, new RoutedEventArgs());
                break;
            case "Ctrl+F":
                OnToggleSearch(this, new RoutedEventArgs());
                break;
        }
    }

    /// <summary>
    /// Toggles the in-conversation search bar on the active tab.
    /// </summary>
    private void OnToggleSearch(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTab is { } tab)
        {
            // Find the ChatTabView for the selected tab and toggle its search bar
            var tabViewItem = Tabs.ContainerFromItem(tab) as TabViewItem;
            if (tabViewItem?.Content is Modules.Views.ChatTabView chatTabView)
                chatTabView.ChatPanel.ToggleSearchBar();
        }
    }

    #endregion

    #region First Activation and Theme

    /// <summary>
    /// Handles the first window activation to apply the saved theme.
    /// </summary>
    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        LoggingService.LogInfo($"[App] First activation — theme: {AppSettings.Theme}");
        ApplyAppTheme(AppSettings.Theme);
    }

    /// <summary>
    /// Applies the specified theme string to the content root and title bar colors.
    /// </summary>
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

    #endregion

    #region Custom Title Bar

    /// <summary>
    /// Configures the non-client passthrough regions for the custom title bar so that
    /// interactive elements (tabs) receive pointer input.
    /// </summary>
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

    /// <summary>
    /// Converts a <see cref="Rect"/> to a <see cref="RectInt32"/> by applying the given DPI scale factor.
    /// </summary>
    /// <returns>A scaled integer rectangle suitable for the non-client region API.</returns>
    private static RectInt32 GetRect(Rect bounds, double scale)
    {
        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    #endregion

    #region Main Menu

    /// <summary>
    /// Opens the main menu flyout after building the recent conversations sub-menu.
    /// </summary>
    private void OnMainMenuClick(object sender, RoutedEventArgs e)
    {
        BuildRecentConversationsMenu();
        FlyoutBase.ShowAttachedFlyout(MainMenuButton);
    }

    /// <summary>
    /// Creates a new conversation tab.
    /// </summary>
    private void OnMenuNewTab(object sender, RoutedEventArgs e)
    {
        ViewModel.AddTab();
    }

    /// <summary>
    /// Saves the current conversation to its existing file path, or prompts Save As if unsaved.
    /// </summary>
    private async void OnMenuSaveConversation(object sender, RoutedEventArgs e)
    {
        var tab = ViewModel.SelectedTab;
        if (tab is null) return;

        if (tab.FilePath is not null)
        {
            // Re-save to existing path (save full tree including branches)
            var messages = tab.GetAllMessages();
            if (messages.Count == 0) return;
            LoggingService.LogInfo($"[File] Save: {Path.GetFileName(tab.FilePath)}, messages={messages.Count}");
            await ConversationManager.SaveToFileAsync(tab.FilePath, tab.Title, tab.CurrentPreset?.Name, messages);
            tab.IsDirty = false;
        }
        else
        {
            await SaveAsAsync(tab);
        }
    }

    /// <summary>
    /// Prompts the user to choose a save location for the current conversation.
    /// </summary>
    private async void OnMenuSaveAsConversation(object sender, RoutedEventArgs e)
    {
        var tab = ViewModel.SelectedTab;
        if (tab is null) return;
        await SaveAsAsync(tab);
    }

    /// <summary>
    /// Displays a file save picker and saves the conversation to the chosen location.
    /// </summary>
    /// <returns><c>true</c> if the file was saved successfully; <c>false</c> if the user cancelled or save failed.</returns>
    private async Task<bool> SaveAsAsync(ChatTabViewModel tab)
    {
        var messages = tab.GetAllMessages();
        if (messages.Count == 0) return false;

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = tab.Title;
        picker.FileTypeChoices.Add("AssistStudio Document", [ConversationManager.FileExtension]);

        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;

        try
        {
            var presetName = tab.CurrentPreset?.Name;
            LoggingService.LogInfo($"[File] SaveAs: {Path.GetFileName(file.Path)}, messages={messages.Count}");
            await ConversationManager.SaveToFileAsync(file.Path, tab.Title, presetName, messages);
            tab.FilePath = file.Path;
            tab.IsDirty = false;
            tab.HasBeenSaved = true;
            AppSettings.AddRecentFile(file.Path);
            return true;
        }
        catch (Exception ex) { LoggingService.LogException(ex); return false; }
    }

    /// <summary>
    /// Saves all open conversation tabs, prompting Save As for any unsaved tabs.
    /// </summary>
    private async void OnMenuSaveAllConversations(object sender, RoutedEventArgs e)
    {
        LoggingService.LogInfo($"[File] SaveAll: {ViewModel.Tabs.Count} tabs");
        foreach (var tab in ViewModel.Tabs)
        {
            var messages = tab.GetAllMessages();
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

    /// <summary>
    /// Opens a file picker to load a conversation from disk into a new tab.
    /// </summary>
    private async void OnMenuLoadConversation(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(ConversationManager.FileExtension);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var data = await ConversationManager.LoadConversationAsync(file.Path);
        if (data is not null)
        {
            LoggingService.LogInfo($"[File] Load: {Path.GetFileName(file.Path)}, messages={data.Messages.Count}");
            ViewModel.LoadConversation(data, file.Path);
            AppSettings.AddRecentFile(file.Path);
        }
    }

    /// <summary>
    /// Populates the recent conversations sub-menu with existing file entries from settings.
    /// </summary>
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
            var modified = File.GetLastWriteTime(filePath).ToString("g");
            var item = new MenuFlyoutItem
            {
                Text = PathFormatter.FormatForMenu(idx, filePath),
            };
            var toolTip = new ToolTip
            {
                Content = $"{filePath}\n{modified}",
                Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
            };
            ToolTipService.SetToolTip(item, toolTip);

            var path = filePath; // capture for lambda
            item.Click += async (_, _) =>
            {
                var data = await ConversationManager.LoadConversationAsync(path);
                if (data is not null)
                {
                    ViewModel.LoadConversation(data, path);
                    AppSettings.AddRecentFile(path);
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
        };
        clearItem.Click += (_, _) =>
        {
            AppSettings.RecentFilePaths = [];
        };
        RecentConversationsSubMenu.Items.Add(clearItem);
    }

    /// <summary>
    /// Toggles the settings side pane open or closed.
    /// </summary>
    private void OnMenuSettings(object sender, RoutedEventArgs e)
    {
        RootSplitView.IsPaneOpen = !RootSplitView.IsPaneOpen;
    }

    #endregion

    #region Tab Management

    /// <summary>
    /// Attaches a <see cref="Controls.TabContextFlyout"/> to the TabViewItem when it loads.
    /// </summary>
    private void OnTabViewItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TabViewItem item) return;
        if (item.ContextFlyout is Controls.TabContextFlyout) return; // already attached

        item.ContextFlyout = new Controls.TabContextFlyout(item, ViewModel, CloseTabFromContextMenuAsync, CloseAppAsync);
    }

    /// <summary>
    /// Closes a single tab with save prompt if dirty. Used by <see cref="Controls.TabContextFlyout"/>.
    /// </summary>
    private async Task CloseTabFromContextMenuAsync(ChatTabViewModel tab)
    {
        if (tab.IsDirty && tab.GetMessages().Count > 0)
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var dialog = new ThemedContentDialog
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
                if (tab.FilePath is not null)
                {
                    await ConversationManager.SaveToFileAsync(
                        tab.FilePath, tab.Title, tab.CurrentPreset?.Name, tab.GetAllMessages());
                    tab.IsDirty = false;
                }
                else
                {
                    if (!await SaveAsAsync(tab)) return;
                }
            }
        }

        ViewModel.CloseTab(tab);

        if (ViewModel.Tabs.Count == 0)
            await CloseAppAsync();
    }

    /// <summary>
    /// Shuts down MCP servers and closes the app. Called when the last tab is closed.
    /// </summary>
    private async Task CloseAppAsync()
    {
        await ShutdownMcpServersAsync();
        _isClosing = true;
        Close();
    }

    /// <summary>
    /// Handles the TabView add-tab button click to create a new conversation tab.
    /// </summary>
    private void OnAddTab(TabView sender, object args)
    {
        ViewModel.AddTab();
    }

    /// <summary>
    /// Handles a tab close request, prompting the user to save unsaved changes before closing.
    /// </summary>
    private async void OnCloseTab(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is not ChatTabViewModel vm) return;

        // Ask to save if conversation has unsaved changes
        if (vm.IsDirty && vm.GetMessages().Count > 0)
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var dialog = new ThemedContentDialog
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
                        vm.FilePath, vm.Title, vm.CurrentPreset?.Name, vm.GetAllMessages());
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
            await ShutdownMcpServersAsync();
            _isClosing = true;
            Close();
        }
    }

    #endregion

    #region App Close

    /// <summary>
    /// Intercepts the window closing event to prompt saving of any tabs with unsaved changes.
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosing) return;

        var dirtyTabs = ViewModel.Tabs.Where(t => t.IsDirty && t.GetMessages().Count > 0).ToList();
        LoggingService.LogInfo($"[App] Window closing — {dirtyTabs.Count} dirty tabs");
        args.Cancel = true;

        if (dirtyTabs.Count == 0)
        {
            await ShutdownMcpServersAsync();
            _isClosing = true;
            Close();
            return;
        }

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var dialog = new ThemedContentDialog
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
                        tab.FilePath, tab.Title, tab.CurrentPreset?.Name, tab.GetAllMessages());
                }
                else
                {
                    if (!await SaveAsAsync(tab)) return; // User cancelled one SaveAs → abort close
                }
            }
        }

        await ShutdownMcpServersAsync();
        _isClosing = true;
        Close();
    }

    /// <summary>
    /// Gracefully shuts down all MCP server connections before app exit.
    /// </summary>
    private async Task ShutdownMcpServersAsync()
    {
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            NotificationCenter.Instance.Post(loader.GetString("Mcp_Disconnecting"), durationMs: 30000);

            LoggingService.LogInfo("[App] Shutting down MCP servers...");
            await App.McpRegistry.DisposeAsync();
            LoggingService.LogInfo("[App] MCP servers shut down.");
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[App] MCP shutdown error: {ex.Message}");
        }
    }

    #endregion

    #region File Activation

    /// <summary>
    /// Opens a conversation file from a file-activation path (e.g., double-clicking an .astd file).
    /// </summary>
    public async void OpenFileFromActivation(string filePath)
    {
        var data = await ConversationManager.LoadConversationAsync(filePath);
        if (data is not null)
        {
            LoggingService.LogInfo($"[File] Activation load: {Path.GetFileName(filePath)}, messages={data.Messages.Count}");
            ViewModel.LoadConversation(data, filePath);
            AppSettings.AddRecentFile(filePath);
        }
    }

    #endregion

}
