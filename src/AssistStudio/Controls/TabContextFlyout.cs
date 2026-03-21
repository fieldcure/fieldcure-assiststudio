using AssistStudio.Helpers;
using AssistStudio.Modules.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;
using Windows.System;

namespace AssistStudio.Controls;

/// <summary>
/// Context menu flyout for tab headers, providing Close, Copy Path, and Rename actions.
/// Attached per-TabViewItem; resolves the current tab from DataContext on each opening.
/// </summary>
internal sealed class TabContextFlyout : MenuFlyout
{
    #region Fields

    private static readonly ResourceLoader Res = new();

    private readonly MainViewModel _viewModel;
    private readonly Func<ChatTabViewModel, Task> _closeTabAction;
    private readonly Func<Task> _closeAppAction;

    /// <summary>The tab resolved from the owning TabViewItem on each <see cref="Opening"/>.</summary>
    private ChatTabViewModel? _tab;

    private MenuFlyoutItem? _close;
    private MenuFlyoutItem? _closeOthers;
    private MenuFlyoutItem? _closeRight;
    private MenuFlyoutItem? _closeSaved;
    private MenuFlyoutItem? _copyFullPath;
    private MenuFlyoutItem? _openContainingFolder;
    private MenuFlyoutItem? _rename;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="TabContextFlyout"/> for the specified <paramref name="owner"/>.
    /// </summary>
    public TabContextFlyout(
        TabViewItem owner,
        MainViewModel viewModel,
        Func<ChatTabViewModel, Task> closeTabAction,
        Func<Task> closeAppAction)
    {
        _viewModel = viewModel;
        _closeTabAction = closeTabAction;
        _closeAppAction = closeAppAction;

        Items.Add(Close);
        Items.Add(CloseOthers);
        Items.Add(CloseRight);
        Items.Add(CloseSaved);
        Items.Add(new MenuFlyoutSeparator());
        Items.Add(CopyFullPath);
        Items.Add(OpenContainingFolder);
        Items.Add(new MenuFlyoutSeparator());
        Items.Add(Rename);

        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        MenuFlyoutPresenterStyle = presenterStyle;

        // Resolve _tab fresh on every open so we're never stale after container reuse.
        Opening += (_, _) =>
        {
            _tab = owner.DataContext as ChatTabViewModel;
            UpdateItemStates();
        };
    }

    #endregion

    #region Menu Items

    private MenuFlyoutItem Close => _close ??= CreateItem(
        Res.GetString("TabContext_Close"), () => _closeTabAction(_tab!),
        new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = VirtualKey.W, IsEnabled = false });

    private MenuFlyoutItem CloseOthers => _closeOthers ??= CreateItem(
        Res.GetString("TabContext_CloseOthers"), async () =>
        {
            var others = _viewModel.Tabs.Where(t => t != _tab).ToList();
            foreach (var tab in others)
                await _closeTabAction(tab);
        });

    private MenuFlyoutItem CloseRight => _closeRight ??= CreateItem(
        Res.GetString("TabContext_CloseRight"), async () =>
        {
            var idx = _viewModel.Tabs.IndexOf(_tab!);
            if (idx < 0) return;
            var right = _viewModel.Tabs.Skip(idx + 1).ToList();
            foreach (var tab in right)
                await _closeTabAction(tab);
        });

    private MenuFlyoutItem CloseSaved => _closeSaved ??= CreateItem(
        Res.GetString("TabContext_CloseSaved"), async () =>
        {
            var saved = _viewModel.Tabs.Where(t => !t.IsDirty).ToList();
            foreach (var tab in saved)
                _viewModel.CloseTab(tab);
            if (_viewModel.Tabs.Count == 0)
                await _closeAppAction();
        });

    private MenuFlyoutItem CopyFullPath => _copyFullPath ??= CreateItem(
        Res.GetString("TabContext_CopyFullPath"), () =>
        {
            if (string.IsNullOrEmpty(_tab?.FilePath)) return Task.CompletedTask;
            var dp = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            dp.SetText(_tab.FilePath);
            Clipboard.SetContentWithOptions(dp, new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = true });
            Clipboard.Flush();
            return Task.CompletedTask;
        });

    private MenuFlyoutItem OpenContainingFolder => _openContainingFolder ??= CreateItem(
        Res.GetString("TabContext_OpenFolder"), async () =>
        {
            if (_tab?.FilePath is null) return;
            var dir = Path.GetDirectoryName(_tab.FilePath);
            if (!string.IsNullOrEmpty(dir))
                await Launcher.LaunchFolderPathAsync(dir);
        });

    private MenuFlyoutItem Rename => _rename ??= CreateItem(
        Res.GetString("TabContext_Rename"), RenameFileAsync,
        new KeyboardAccelerator { Key = VirtualKey.F2, IsEnabled = false });

    #endregion

    #region Private Methods

    private void UpdateItemStates()
    {
        if (_tab is null) return;

        var tabCount = _viewModel.Tabs.Count;
        var tabIdx = _viewModel.Tabs.IndexOf(_tab);
        CloseOthers.IsEnabled = tabCount > 1;
        CloseRight.IsEnabled = tabIdx >= 0 && tabIdx < tabCount - 1;
        CloseSaved.IsEnabled = tabCount > 0;

        var hasFile = _tab.FilePath is not null;
        CopyFullPath.IsEnabled = hasFile;
        OpenContainingFolder.IsEnabled = hasFile;
        Rename.IsEnabled = hasFile;
    }

    private static MenuFlyoutItem CreateItem(string text, Func<Task> action, KeyboardAccelerator? accelerator = null)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) => await action();
        if (accelerator is not null)
            item.KeyboardAccelerators.Add(accelerator);
        return item;
    }

    private async Task RenameFileAsync()
    {
        if (_tab?.FilePath is null || _tab.Panel is null) return;

        var currentName = Path.GetFileNameWithoutExtension(_tab.FilePath);
        var input = new TextBox { Text = currentName, SelectionStart = currentName.Length };
        var dialog = new Dialogs.ThemedContentDialog
        {
            Title = Res.GetString("TabContext_RenameDialog"),
            Content = input,
            PrimaryButtonText = Res.GetString("Dialog_OK"),
            CloseButtonText = Res.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _tab.Panel.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var newName = input.Text?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == currentName) return;

        var dir = Path.GetDirectoryName(_tab.FilePath)!;
        var newPath = Path.Combine(dir, newName + ConversationManager.FileExtension);

        try
        {
            File.Move(_tab.FilePath, newPath);
            _tab.FilePath = newPath;
            _tab.Title = newName;
            LoggingService.LogInfo($"[File] Renamed: {currentName} → {newName}");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    #endregion
}
