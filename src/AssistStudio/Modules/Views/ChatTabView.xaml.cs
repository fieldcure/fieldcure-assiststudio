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
    }

    #endregion
}
