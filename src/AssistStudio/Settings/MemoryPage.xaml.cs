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
    private string _deleteTooltip = "Delete";

    private void RefreshList()
    {
        var entries = App.MemoryStore.GetAll();
        MemoryList.ItemsSource = null;

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            _deleteTooltip = loader.GetString("Memory_DeleteTooltip") is { Length: > 0 } s ? s : "Delete";
        }
        catch { /* fallback */ }

        if (entries.Count == 0)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            HintText.Visibility = Visibility.Collapsed;
            HintDivider.Visibility = Visibility.Collapsed;
            ClearAllButton.Visibility = Visibility.Collapsed;
            CounterText.Text = "";
            return;
        }

        EmptyPanel.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Visible;
        HintDivider.Visibility = Visibility.Visible;
        ClearAllButton.Visibility = Visibility.Visible;
        CounterText.Text = $"{entries.Count}/{MemoryStore.MaxEntries}";

        // Build item list with dividers between entries
        var items = new List<FrameworkElement>();
        var isFirst = true;
        foreach (var entry in entries)
        {
            if (!isFirst)
            {
                items.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 1,
                    Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                });
            }
            isFirst = false;

            // Entry row
            var grid = new Grid
            {
                Padding = new Thickness(0, 10, 0, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            var text = new TextBlock
            {
                Text = entry.Content,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            };
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var deleteButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                Padding = new Thickness(6),
                MinWidth = 0,
                MinHeight = 0,
                Tag = entry.Id,
                VerticalAlignment = VerticalAlignment.Top,
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                Opacity = 0,
            };
            ToolTipService.SetToolTip(deleteButton, _deleteTooltip);
            ToolTipService.SetPlacement(deleteButton, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);
            deleteButton.Click += OnDeleteClicked;
            Grid.SetColumn(deleteButton, 1);
            grid.Children.Add(deleteButton);

            // Show/hide delete button on hover
            grid.PointerEntered += (_, _) => deleteButton.Opacity = 1;
            grid.PointerExited += (_, _) => deleteButton.Opacity = 0;

            items.Add(grid);
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
