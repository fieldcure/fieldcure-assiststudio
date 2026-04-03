using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings panel that hosts a NavigationView for navigating between settings pages.
/// Navigated via a Frame inside the SplitView pane.
/// </summary>
public sealed partial class SettingsPanel : Page
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPanel"/> class.
    /// </summary>
    public SettingsPanel()
    {
        InitializeComponent();
    }

    #endregion

    #region Event Handlers

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Select the target page or default to first item
        var targetTag = e.Parameter as string;
        var item = !string.IsNullOrEmpty(targetTag)
            ? NavView.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(i => i.Tag as string == targetTag)
            : null;
        item ??= NavView.MenuItems.FirstOrDefault() as NavigationViewItem;

        if (item != null)
        {
            item.IsSelected = true;
            NavigateTo(item.Tag as string);
        }

        NavView.ItemInvoked += OnItemInvoked;
    }

    /// <inheritdoc/>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        NavView.ItemInvoked -= OnItemInvoked;
    }

    /// <summary>
    /// Handles navigation item invocation in the settings NavigationView.
    /// </summary>
    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Navigates the content frame to the settings page identified by the given tag.
    /// </summary>
    private void NavigateTo(string? tag)
    {
        var pageType = tag switch
        {
            "Profiles" => typeof(ProfilesPage),
            "Models" => typeof(ModelsPage),
            "Connect" => typeof(ConnectPage),
            "KnowledgeBases" => typeof(KnowledgeBasesPage),
            "AppTasks" => typeof(AppTasksPage),
            "Memory" => typeof(MemoryPage),
            "Schedule" => typeof(SchedulePage),
            "Personalization" => typeof(PersonalizationPage),
            "Advanced" => typeof(AdvancedPage),
            "About" => typeof(AboutPage),
            _ => typeof(ProfilesPage),
        };

        ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
    }

    #endregion
}
