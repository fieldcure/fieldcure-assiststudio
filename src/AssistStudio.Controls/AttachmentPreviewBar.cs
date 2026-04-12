using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Windows.ApplicationModel.Resources;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A templated control that displays a horizontal preview bar of file and image attachments.
/// Default style is defined in Generic.xaml.
/// </summary>
public sealed partial class AttachmentPreviewBar : Control
{
    #region Fields

    /// <summary>Resource loader for localized strings in this library.</summary>
    private static readonly ResourceLoader Res = new("AssistStudio.Controls/Resources");

    /// <summary>
    /// The observable collection backing the displayed attachments.
    /// </summary>
    private readonly ObservableCollection<ChatAttachment> _attachments = [];

    /// <summary>
    /// The panel obtained from the control template that hosts preview item visuals.
    /// </summary>
    private WrapPanel _itemsPanel = new() { HorizontalSpacing = 8, VerticalSpacing = 8 };

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

    #region Dependency Properties

    /// <summary>Identifies the <see cref="ThumbnailSize"/> dependency property.</summary>
    public static readonly DependencyProperty ThumbnailSizeProperty =
        DependencyProperty.Register(nameof(ThumbnailSize), typeof(double), typeof(AttachmentPreviewBar),
            new PropertyMetadata(80.0));

    /// <summary>Identifies the <see cref="MaxTextWidth"/> dependency property.</summary>
    public static readonly DependencyProperty MaxTextWidthProperty =
        DependencyProperty.Register(nameof(MaxTextWidth), typeof(double), typeof(AttachmentPreviewBar),
            new PropertyMetadata(72.0));

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the collection of attachments currently displayed in the preview bar.
    /// </summary>
    public ObservableCollection<ChatAttachment> Attachments => _attachments;

    /// <summary>
    /// Gets or sets the size (width and height) of each attachment thumbnail in pixels.
    /// </summary>
    public double ThumbnailSize
    {
        get => (double)GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width for the filename text in non-image attachment previews.
    /// </summary>
    public double MaxTextWidth
    {
        get => (double)GetValue(MaxTextWidthProperty);
        set => SetValue(MaxTextWidthProperty, value);
    }

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
        _itemsPanel.Children.Clear();
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_ItemsHost") is Border host)
        {
            host.Child = _itemsPanel;
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles changes to the attachments collection by adding or clearing preview items.
    /// </summary>
    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildChips();
        Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Rebuilds all chip visuals with correct numbering.
    /// Called on any collection change (add, remove, reset).
    /// </summary>
    private void RebuildChips()
    {
        _itemsPanel.Children.Clear();
        var showNumbers = _attachments.Count >= 2;
        for (var i = 0; i < _attachments.Count; i++)
        {
            var number = showNumbers ? i + 1 : 0;
            var chip = CreatePreviewItem(_attachments[i], number);
            _itemsPanel.Children.Add(chip);
        }
    }

    /// <summary>
    /// Handles the remove button click by removing the associated attachment from the collection and UI.
    /// </summary>
    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ChatAttachment attachment })
        {
            // _attachments.Remove triggers OnAttachmentsChanged → RebuildChips
            if (_attachments.Remove(attachment))
            {
                AttachmentRemoved?.Invoke(this, attachment);
            }
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Creates a preview element for the given attachment, dispatching to a type-specific builder.
    /// </summary>
    /// <param name="attachment">The attachment to visualize.</param>
    /// <param name="number">1-based index for multi-attachment numbering; 0 to hide.</param>
    private Grid CreatePreviewItem(ChatAttachment attachment, int number)
    {
        var chip = attachment.IsUnsupported
            ? CreateUnsupportedChip(attachment, number)
            : attachment.Type switch
            {
                AttachmentType.Image => CreateImageChip(attachment, number),
                AttachmentType.TextFile => CreateTextChip(attachment, number),
                _ => CreateGenericChip(attachment, number),
            };

        AddRemoveButton(chip, attachment);
        return chip;
    }

    /// <summary>
    /// Prefixes a display name with a number if applicable (e.g., "1. screenshot.png").
    /// </summary>
    private static string FormatDisplayName(string name, int number)
        => number > 0 ? $"{number}. {name}" : name;

    /// <summary>
    /// Creates a compact image chip (48px height) with thumbnail + filename.
    /// </summary>
    private static Grid CreateImageChip(ChatAttachment attachment, int number)
    {
        var chip = CreateChipContainer();

        var thumb = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(6),
        };
        var image = new Image { Stretch = Stretch.UniformToFill };
        var bitmapImage = new BitmapImage();
        var stream = new MemoryStream(attachment.Data);
        _ = bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
        image.Source = bitmapImage;
        thumb.Child = image;
        Grid.SetColumn(thumb, 0);
        chip.Children.Add(thumb);

        var name = CreateChipName(FormatDisplayName(attachment.FileName, number));
        Grid.SetColumn(name, 1);
        chip.Children.Add(name);

        ToolTipService.SetToolTip(chip, attachment.FileName);
        return chip;
    }

    /// <summary>
    /// Creates a compact text attachment chip (48px height) with icon + display name.
    /// </summary>
    private static Grid CreateTextChip(ChatAttachment attachment, int number)
    {
        var chip = CreateChipContainer();

        var thumb = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.SlateGray),
            Child = new FontIcon
            {
                Glyph = attachment.Source == AttachmentSource.Pasted ? "\uE77F" : "\uE8A5",
                FontSize = 18,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        Grid.SetColumn(thumb, 0);
        chip.Children.Add(thumb);

        var rawName = attachment.Source == AttachmentSource.Pasted
            ? "Pasted text"
            : attachment.FileName;
        var name = CreateChipName(FormatDisplayName(rawName, number));
        Grid.SetColumn(name, 1);
        chip.Children.Add(name);

        // Tooltip with char/line count
        var tooltipName = attachment.Source == AttachmentSource.Pasted
            ? "Pasted text"
            : attachment.FileName;
        ToolTipService.SetToolTip(chip,
            $"{tooltipName} \u00B7 {attachment.CharCount:N0} chars \u00B7 {attachment.LineCount:N0} lines");

        return chip;
    }

    /// <summary>
    /// Creates a generic file chip for Document type attachments.
    /// </summary>
    /// <summary>
    /// Creates an error-state chip for unsupported image formats.
    /// Shows strikethrough filename and reduced opacity.
    /// </summary>
    private static Grid CreateUnsupportedChip(ChatAttachment attachment, int number)
    {
        var chip = CreateChipContainer();
        chip.Opacity = 0.5;

        var thumb = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.DarkRed),
            Child = new FontIcon
            {
                Glyph = "\uE783",
                FontSize = 18,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        Grid.SetColumn(thumb, 0);
        chip.Children.Add(thumb);

        var name = CreateChipName(FormatDisplayName(attachment.FileName, number));
        name.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        Grid.SetColumn(name, 1);
        chip.Children.Add(name);

        ToolTipService.SetToolTip(chip, "Unsupported image format \u2014 will not be sent");
        return chip;
    }

    private static Grid CreateGenericChip(ChatAttachment attachment, int number)
    {
        var chip = CreateChipContainer();

        var thumb = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.SlateGray),
            Child = new FontIcon
            {
                Glyph = "\uE8A5",
                FontSize = 18,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
        };
        Grid.SetColumn(thumb, 0);
        chip.Children.Add(thumb);

        var name = CreateChipName(FormatDisplayName(attachment.FileName, number));
        Grid.SetColumn(name, 1);
        chip.Children.Add(name);

        ToolTipService.SetToolTip(chip, attachment.FileName);
        return chip;
    }

    /// <summary>
    /// Creates the shared chip container grid (48px height, flexible width, rounded border).
    /// </summary>
    private static Grid CreateChipContainer()
    {
        var grid = new Grid
        {
            Height = 48,
            MinWidth = 140,
            MaxWidth = 260,
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 6, 10, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // thumbnail
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // remove button
        return grid;
    }

    /// <summary>
    /// Creates the display name TextBlock for a chip (up to 2 lines with ellipsis).
    /// </summary>
    private static TextBlock CreateChipName(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 2,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0),
    };

    /// <summary>
    /// Adds a remove button overlay (hidden until hover) to the chip grid.
    /// </summary>
    private void AddRemoveButton(Grid container, ChatAttachment attachment)
    {
        var removeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Opacity = 0,
            Tag = attachment
        };
        AutomationProperties.SetName(removeButton, Res.GetString("Attachment_RemoveTooltip"));
        removeButton.Click += OnRemoveClick;
        Grid.SetColumn(removeButton, 2);
        container.Children.Add(removeButton);

        container.PointerEntered += (_, _) => removeButton.Opacity = 1;
        container.PointerExited += (_, _) => removeButton.Opacity = 0;
    }

    #endregion
}
