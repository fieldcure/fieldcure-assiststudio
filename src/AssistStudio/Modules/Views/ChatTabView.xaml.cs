using AssistStudio.Modules.ViewModels;
using FieldCure.AssistStudio.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Modules.Views;

/// <summary>
/// UserControl that hosts a <see cref="ChatPanel"/> and binds its properties
/// declaratively to a <see cref="ChatTabViewModel"/>.
/// </summary>
public sealed partial class ChatTabView : UserControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="ViewModel"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ChatTabViewModel), typeof(ChatTabView),
            new PropertyMetadata(null, OnViewModelChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the view model that provides data and behavior for this tab.
    /// </summary>
    public ChatTabViewModel? ViewModel
    {
        get => (ChatTabViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Gets the underlying <see cref="ChatPanel"/> control for direct access
    /// (e.g., <c>GetMessages</c>, <c>AddRestoredMessage</c>).
    /// </summary>
    public ChatPanel ChatPanel => Panel;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTabView"/> class.
    /// </summary>
    public ChatTabView()
    {
        InitializeComponent();
    }

    #endregion

    #region DP Callbacks

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (ChatTabView)d;

        if (e.OldValue is ChatTabViewModel oldVm)
        {
            view.DetachEvents(oldVm);
        }

        if (e.NewValue is ChatTabViewModel newVm)
        {
            view.AttachEvents(newVm);
            newVm.AttachPanel(view.Panel);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Subscribes to ChatPanel events and forwards them to the ViewModel.
    /// </summary>
    private void AttachEvents(ChatTabViewModel vm)
    {
        Panel.PresetChanged += vm.OnPresetChanged;
        Panel.ProfileChanged += vm.OnProfileChanged;
        Panel.TitleGenerated += vm.OnTitleGenerated;
        Panel.MessageAdded += vm.OnMessageAdded;
        Panel.TitleEditRequested += vm.OnTitleEditRequested;
        Panel.BranchChanged += vm.OnBranchChanged;
        Panel.WorkspaceFoldersChanged += vm.OnWorkspaceFoldersChanged;
        Panel.WorkspaceFolderAddRequested += OnWorkspaceFolderAddRequested;
    }

    /// <summary>
    /// Unsubscribes ChatPanel events from the ViewModel.
    /// </summary>
    private void DetachEvents(ChatTabViewModel vm)
    {
        Panel.PresetChanged -= vm.OnPresetChanged;
        Panel.ProfileChanged -= vm.OnProfileChanged;
        Panel.TitleGenerated -= vm.OnTitleGenerated;
        Panel.MessageAdded -= vm.OnMessageAdded;
        Panel.TitleEditRequested -= vm.OnTitleEditRequested;
        Panel.BranchChanged -= vm.OnBranchChanged;
        Panel.WorkspaceFoldersChanged -= vm.OnWorkspaceFoldersChanged;
        Panel.WorkspaceFolderAddRequested -= OnWorkspaceFolderAddRequested;
    }

    /// <summary>
    /// Handles the "Add Folder" request by opening a FolderPicker and updating the ChatPanel.
    /// </summary>
    private async void OnWorkspaceFolderAddRequested(object? sender, EventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var window = (App.Current as App)?.MainWindow;
        if (window is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var current = Panel.WorkspaceFolders?.ToList() ?? [];
        if (!current.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
        {
            current.Add(folder.Path);
            Panel.WorkspaceFolders = current;

            // Notify ViewModel
            if (DataContext is ChatTabViewModel vm)
                vm.OnWorkspaceFoldersChanged(this, current);
        }
    }

    #endregion
}
