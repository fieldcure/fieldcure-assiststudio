using System.Collections.ObjectModel;
using System.Collections.Specialized;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A templated control that displays a horizontal preview bar of file and image attachments.
/// Default style is defined in Generic.xaml.
/// </summary>
public sealed class AttachmentPreviewBar : Control
{
    #region Fields

    /// <summary>
    /// The observable collection backing the displayed attachments.
    /// </summary>
    private readonly ObservableCollection<ChatAttachment> _attachments = [];

    /// <summary>
    /// The panel obtained from the control template that hosts preview item visuals.
    /// </summary>
    private StackPanel? _itemsPanel;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentPreviewBar"/> class.
    /// </summary>
    public AttachmentPreviewBar()
    {
        DefaultStyleKey = typeof(AttachmentPreviewBar);
        _attachments.CollectionChanged += OnAttachmentsChanged;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the collection of attachments currently displayed in the preview bar.
    /// </summary>
    public ObservableCollection<ChatAttachment> Attachments => _attachments;

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the user removes an attachment by clicking its remove button.
    /// </summary>
    public event EventHandler<ChatAttachment>? AttachmentRemoved;

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds an attachment to the preview bar and renders its thumbnail.
    /// </summary>
    public void AddAttachment(ChatAttachment attachment)
    {
        _attachments.Add(attachment);
    }

    /// <summary>
    /// Removes all attachments and clears the preview bar.
    /// </summary>
    public void Clear()
    {
        _attachments.Clear();
        _itemsPanel?.Children.Clear();
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _itemsPanel = GetTemplateChild("PART_ItemsPanel") as StackPanel;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles changes to the attachments collection by adding or clearing preview items.
    /// </summary>
    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Ensure template is applied (may be deferred when control starts Collapsed)
        if (_itemsPanel is null)
        {
            ApplyTemplate();
            _itemsPanel = GetTemplateChild("PART_ItemsPanel") as StackPanel;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ChatAttachment item in e.NewItems)
            {
                _itemsPanel?.Children.Add(CreatePreviewItem(item));
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _itemsPanel?.Children.Clear();
        }

        Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the remove button click by removing the associated attachment from the collection and UI.
    /// </summary>
    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChatAttachment attachment })
        {
            var index = _attachments.IndexOf(attachment);
            if (index >= 0)
            {
                _attachments.RemoveAt(index);
                _itemsPanel?.Children.RemoveAt(index);
                Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                AttachmentRemoved?.Invoke(this, attachment);
            }
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Creates a visual preview element for the given attachment, including a thumbnail and a remove button overlay.
    /// </summary>
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
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Transparent),
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

        // Remove button (X) overlay -- hidden until hover
        var removeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, // Cancel icon
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0),
            Opacity = 0,
            Tag = attachment
        };
        removeButton.Click += OnRemoveClick;
        container.Children.Add(removeButton);

        // Show/hide remove button on hover
        container.PointerEntered += (_, _) => removeButton.Opacity = 1;
        container.PointerExited += (_, _) => removeButton.Opacity = 0;

        return container;
    }

    #endregion
}
