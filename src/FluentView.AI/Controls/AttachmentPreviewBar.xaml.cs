using System.Collections.ObjectModel;
using System.Collections.Specialized;
using FluentView.AI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentView.AI.Controls;

public sealed partial class AttachmentPreviewBar : UserControl
{
    private readonly ObservableCollection<ChatAttachment> _attachments = [];

    public AttachmentPreviewBar()
    {
        InitializeComponent();
        _attachments.CollectionChanged += OnAttachmentsChanged;
    }

    public ObservableCollection<ChatAttachment> Attachments => _attachments;

    public event EventHandler<ChatAttachment>? AttachmentRemoved;

    public void AddAttachment(ChatAttachment attachment)
    {
        _attachments.Add(attachment);
    }

    public void Clear()
    {
        _attachments.Clear();
        ItemsPanel.Children.Clear();
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ChatAttachment item in e.NewItems)
            {
                ItemsPanel.Children.Add(CreatePreviewItem(item));
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ItemsPanel.Children.Clear();
        }

        Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement CreatePreviewItem(ChatAttachment attachment)
    {
        var container = new Grid
        {
            Width = 80,
            Height = 80,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
        };

        if (attachment.Type == AttachmentType.Image)
        {
            var image = new Image
            {
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Load image from byte array
            var bitmapImage = new BitmapImage();
            var stream = new MemoryStream(attachment.Data);
            var raStream = stream.AsRandomAccessStream();
            _ = bitmapImage.SetSourceAsync(raStream);
            image.Source = bitmapImage;

            container.Children.Add(image);
        }
        else
        {
            // Text file: show icon + filename
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4
            };
            stack.Children.Add(new FontIcon
            {
                Glyph = "\uE8A5", // Document icon
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = attachment.FileName,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            container.Children.Add(stack);
        }

        // Remove button (X) in top-right corner
        var removeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, // Cancel icon
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Tag = attachment
        };
        removeButton.Click += OnRemoveClick;
        container.Children.Add(removeButton);

        return container;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChatAttachment attachment })
        {
            var index = _attachments.IndexOf(attachment);
            if (index >= 0)
            {
                _attachments.RemoveAt(index);
                ItemsPanel.Children.RemoveAt(index);
                Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                AttachmentRemoved?.Invoke(this, attachment);
            }
        }
    }
}
