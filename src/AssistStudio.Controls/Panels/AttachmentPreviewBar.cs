using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

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
    /// The collection projected into XAML-friendly preview items.
    /// </summary>
    private readonly ObservableCollection<AttachmentPreviewItemViewModel> _previewItems = [];

    /// <summary>
    /// The items control obtained from the control template that hosts preview item visuals.
    /// </summary>
    private ItemsControl? _itemsHost;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AttachmentPreviewBar"/> class.
    /// </summary>
    public AttachmentPreviewBar()
    {
        DefaultStyleKey = typeof(AttachmentPreviewBar);
        AutomationHelper.SetAutomation(this, "AttachmentPreviewBar",
            nameKey: "AttachmentPreviewBar_ControlName");
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
        _previewItems.Clear();
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _itemsHost = GetTemplateChild("PART_ItemsHost") as ItemsControl;
        if (_itemsHost is not null)
        {
            _itemsHost.ItemsSource = _previewItems;
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
        _previewItems.Clear();
        var showNumbers = _attachments.Count >= 2;
        for (var i = 0; i < _attachments.Count; i++)
        {
            var number = showNumbers ? i + 1 : 0;
            _previewItems.Add(CreatePreviewItem(_attachments[i], number));
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Creates a preview element for the given attachment, dispatching to a type-specific builder.
    /// </summary>
    /// <param name="attachment">The attachment to visualize.</param>
    /// <param name="number">1-based index for multi-attachment numbering; 0 to hide.</param>
    private AttachmentPreviewItemViewModel CreatePreviewItem(ChatAttachment attachment, int number)
        => attachment.IsUnsupported
            ? CreateUnsupportedChip(attachment, number)
            : attachment.Type switch
            {
                AttachmentType.Image => CreateImageChip(attachment, number),
                AttachmentType.TextFile => CreateTextChip(attachment, number),
                _ => CreateGenericChip(attachment, number),
            };

    /// <summary>
    /// Prefixes a display name with a number if applicable (e.g., "1. screenshot.png").
    /// </summary>
    private static string FormatDisplayName(string name, int number)
        => number > 0 ? $"{number}. {name}" : name;

    /// <summary>
    /// Creates a compact image chip (48px height) with thumbnail + filename.
    /// </summary>
    private AttachmentPreviewItemViewModel CreateImageChip(ChatAttachment attachment, int number)
    {
        var bitmapImage = new BitmapImage();
        var stream = new MemoryStream(attachment.Data);
        _ = bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
        return new AttachmentPreviewItemViewModel(
            attachment,
            automationId: "AttachmentPreviewImageChip",
            displayName: FormatDisplayName(attachment.FileName, number),
            tooltipText: attachment.FileName,
            accessibilityName: FormatAttachmentAccessibilityName(attachment.FileName),
            removeButtonName: GetRemoveButtonName(),
            removeAction: RemoveAttachment)
        {
            ThumbnailSource = bitmapImage,
            ImageVisibility = Visibility.Visible,
            IconVisibility = Visibility.Collapsed,
            ThumbnailBackgroundBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
    }

    /// <summary>
    /// Creates a compact text attachment chip (48px height) with icon + display name.
    /// </summary>
    private AttachmentPreviewItemViewModel CreateTextChip(ChatAttachment attachment, int number)
    {
        var rawName = attachment.Source == AttachmentSource.Pasted
            ? "Pasted text"
            : attachment.FileName;
        var tooltipName = attachment.Source == AttachmentSource.Pasted
            ? "Pasted text"
            : attachment.FileName;
        var tooltipText = $"{tooltipName} \u00B7 {attachment.CharCount:N0} chars \u00B7 {attachment.LineCount:N0} lines";
        return CreateIconChip(
            attachment,
            automationId: "AttachmentPreviewTextChip",
            displayName: FormatDisplayName(rawName, number),
            tooltipText: tooltipText,
            accessibilityName: FormatAttachmentAccessibilityName(tooltipText),
            glyph: attachment.Source == AttachmentSource.Pasted ? "\uE77F" : "\uE8A5");
    }

    /// <summary>
    /// Creates an error-state chip for unsupported image formats.
    /// Shows strikethrough filename and reduced opacity.
    /// </summary>
    private AttachmentPreviewItemViewModel CreateUnsupportedChip(ChatAttachment attachment, int number)
    {
        var unsupportedText = Res.GetString("ComposeBar_AttachmentUnsupportedTooltip") is { Length: > 0 } u
            ? u
            : "Unsupported image format \u2014 will not be sent";
        return CreateIconChip(
            attachment,
            automationId: "AttachmentPreviewUnsupportedChip",
            displayName: FormatDisplayName(attachment.FileName, number),
            tooltipText: unsupportedText,
            accessibilityName: FormatAttachmentAccessibilityName($"{attachment.FileName} — {unsupportedText}"),
            glyph: "\uE783",
            chipOpacity: 0.5,
            thumbnailBackgroundBrush: new SolidColorBrush(Microsoft.UI.Colors.DarkRed),
            nameTextDecorations: Windows.UI.Text.TextDecorations.Strikethrough);
    }

    /// <summary>
    /// Creates a generic file chip for Document type attachments.
    /// </summary>
    private AttachmentPreviewItemViewModel CreateGenericChip(ChatAttachment attachment, int number)
        => CreateIconChip(
            attachment,
            automationId: "AttachmentPreviewGenericChip",
            displayName: FormatDisplayName(attachment.FileName, number),
            tooltipText: attachment.FileName,
            accessibilityName: FormatAttachmentAccessibilityName(attachment.FileName),
            glyph: "\uE8A5");

    /// <summary>
    /// Builds a localized accessibility announcement for an attachment chip,
    /// e.g. "Attachment: report.pdf".
    /// </summary>
    private static string FormatAttachmentAccessibilityName(string value)
    {
        var format = Res.GetString("ComposeBar_AttachmentItem");
        return string.IsNullOrEmpty(format) ? value : string.Format(format, value);
    }

    private AttachmentPreviewItemViewModel CreateIconChip(
        ChatAttachment attachment,
        string automationId,
        string displayName,
        string tooltipText,
        string accessibilityName,
        string glyph,
        double chipOpacity = 1.0,
        Brush? thumbnailBackgroundBrush = null,
        Windows.UI.Text.TextDecorations nameTextDecorations = Windows.UI.Text.TextDecorations.None)
        => new(
            attachment,
            automationId,
            displayName,
            tooltipText,
            accessibilityName,
            GetRemoveButtonName(),
            RemoveAttachment)
        {
            Glyph = glyph,
            ChipOpacity = chipOpacity,
            ThumbnailBackgroundBrush = thumbnailBackgroundBrush ?? new SolidColorBrush(Microsoft.UI.Colors.SlateGray),
            NameTextDecorations = nameTextDecorations,
        };

    private string GetRemoveButtonName()
        => Res.GetString("Attachment_RemoveTooltip") is { Length: > 0 } text ? text : "Remove attachment";

    private void RemoveAttachment(ChatAttachment attachment)
    {
        if (_attachments.Remove(attachment))
        {
            AttachmentRemoved?.Invoke(this, attachment);
        }
    }

    #endregion
}

/// <summary>
/// XAML-facing projection of an attachment chip. The control keeps the dynamic content
/// shaping in code, but the visual tree itself now lives in XAML data templates.
/// </summary>
public sealed class AttachmentPreviewItemViewModel
{
    /// <summary>
    /// Initializes a new attachment chip view model for the XAML item template.
    /// </summary>
    public AttachmentPreviewItemViewModel(
        ChatAttachment attachment,
        string automationId,
        string displayName,
        string tooltipText,
        string accessibilityName,
        string removeButtonName,
        Action<ChatAttachment> removeAction)
    {
        Attachment = attachment;
        AutomationId = automationId;
        DisplayName = displayName;
        TooltipText = tooltipText;
        AccessibilityName = accessibilityName;
        RemoveButtonName = removeButtonName;
        RemoveCommand = new AttachmentPreviewCommand(() => removeAction(attachment));
    }

    /// <summary>
    /// Gets the underlying attachment represented by this chip.
    /// </summary>
    public ChatAttachment Attachment { get; }

    /// <summary>
    /// Gets the stable automation id applied to the chip container.
    /// </summary>
    public string AutomationId { get; }

    /// <summary>
    /// Gets the user-visible attachment label shown in the chip.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the tooltip text shown for the chip.
    /// </summary>
    public string TooltipText { get; }

    /// <summary>
    /// Gets the localized accessibility name announced for the chip.
    /// </summary>
    public string AccessibilityName { get; }

    /// <summary>
    /// Gets the localized remove button label and tooltip text.
    /// </summary>
    public string RemoveButtonName { get; }

    /// <summary>
    /// Gets the command that removes the represented attachment.
    /// </summary>
    public ICommand RemoveCommand { get; }

    /// <summary>
    /// Gets the image thumbnail source when the attachment is an image.
    /// </summary>
    public ImageSource? ThumbnailSource { get; init; }

    /// <summary>
    /// Gets the glyph shown for non-image attachments.
    /// </summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>
    /// Gets the thumbnail background brush used behind icon-based chips.
    /// </summary>
    public Brush ThumbnailBackgroundBrush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <summary>
    /// Gets the foreground brush used by the icon glyph.
    /// </summary>
    public Brush GlyphForegroundBrush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.White);

    /// <summary>
    /// Gets the border brush used for the chip container.
    /// </summary>
    public Brush ChipBorderBrush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);

    /// <summary>
    /// Gets the background brush used for the chip container.
    /// </summary>
    public Brush ChipBackgroundBrush { get; init; } = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <summary>
    /// Gets the overall opacity applied to the chip.
    /// </summary>
    public double ChipOpacity { get; init; } = 1.0;

    /// <summary>
    /// Gets whether the thumbnail image element should be visible.
    /// </summary>
    public Visibility ImageVisibility { get; init; } = Visibility.Collapsed;

    /// <summary>
    /// Gets whether the icon glyph element should be visible.
    /// </summary>
    public Visibility IconVisibility { get; init; } = Visibility.Visible;

    /// <summary>
    /// Gets any text decorations applied to the display name.
    /// </summary>
    public Windows.UI.Text.TextDecorations NameTextDecorations { get; init; } = Windows.UI.Text.TextDecorations.None;
}

internal sealed partial class AttachmentPreviewCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
