using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for viewing and managing persistent memory entries.
/// </summary>
public sealed partial class MemoryPage : Page
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryPage"/> class.
    /// </summary>
    public MemoryPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshList();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Rebuilds the memory list UI from the current MemoryStore state.
    /// </summary>
    private void RefreshList()
    {
        var entries = App.MemoryStore.GetAll();
        MemoryList.ItemsSource = null;

        if (entries.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            ClearAllButton.IsEnabled = false;
            CounterText.Text = "";
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        ClearAllButton.IsEnabled = true;

        // Load localized counter format
        string counterFormat;
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            counterFormat = loader.GetString("Memory_Counter");
            if (string.IsNullOrEmpty(counterFormat)) counterFormat = "{0} / {1} entries";
        }
        catch
        {
            counterFormat = "{0} / {1} entries";
        }
        CounterText.Text = string.Format(counterFormat, entries.Count, MemoryStore.MaxEntries);

        // Build item panels
        var items = new List<StackPanel>();
        foreach (var entry in entries)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
            };

            var text = new TextBlock
            {
                Text = entry.Content,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                MaxWidth = 500,
            };
            panel.Children.Add(text);

            var deleteButton = new Button
            {
                Content = "\uE711", // X icon
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                Padding = new Thickness(4),
                MinWidth = 0,
                MinHeight = 0,
                Tag = entry.Id,
                VerticalAlignment = VerticalAlignment.Center,
            };
            deleteButton.Click += OnDeleteClicked;
            panel.Children.Add(deleteButton);

            items.Add(panel);
        }

        MemoryList.ItemsSource = items;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles individual memory entry deletion.
    /// </summary>
    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            App.MemoryStore.RemoveById(id);
            RefreshList();
        }
    }

    /// <summary>
    /// Handles the Clear All button click with confirmation.
    /// </summary>
    private async void OnClearAllClicked(object sender, RoutedEventArgs e)
    {
        string title, content;
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            title = loader.GetString("Memory_ClearAll") is { Length: > 0 } t ? t : "Clear All";
            content = loader.GetString("Memory_ClearConfirm") is { Length: > 0 } c
                ? c : "Are you sure you want to clear all memories?";
        }
        catch
        {
            title = "Clear All";
            content = "Are you sure you want to clear all memories?";
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = title,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.MemoryStore.Clear();
            RefreshList();
        }
    }

    #endregion
}
