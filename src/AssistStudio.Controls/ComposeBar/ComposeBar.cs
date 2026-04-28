using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A templated control that provides a chat input area with text entry, file attachments,
/// preset selection, and drag-and-drop support. Default style is defined in Generic.xaml.
/// </summary>
public sealed partial class ComposeBar : Control
{
    /// <summary>
    /// Resource loader for localized strings used by this control library.
    /// </summary>
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

    #region Fields

    /// <summary>
    /// Flag to suppress preset changed events during programmatic ComboBox updates.
    /// </summary>
    private bool _suppressPresetChanged;

    /// <summary>
    /// Flag indicating a pending preset ComboBox population deferred until control is loaded.
    /// </summary>
    private bool _pendingPresetPopulate;

    /// <summary>
    /// Flag indicating a pending prompt preset ComboBox population deferred until control is loaded.
    /// </summary>
    private bool _pendingProfilePopulate;

    /// <summary>
    /// Pending MaxLength value deferred until the text box is available.
    /// </summary>
    private int? _pendingMaxLength;

    /// <summary>
    /// Pending InputAreaMinHeight value deferred until the text box is available.
    /// </summary>
    private double? _pendingMinHeight;

    #endregion

    #region Template Parts

    /// <summary>
    /// The attachment preview bar displaying file and image thumbnails.
    /// </summary>
    private AttachmentPreviewBar? _previewBar;

    /// <summary>
    /// The text box for composing chat messages.
    /// </summary>
    private TextBox? _messageTextBox;

    /// <summary>
    /// The button that opens the file picker to attach files.
    /// </summary>
    private Button? _attachButton;

    /// <summary>
    /// The button that sends the current message.
    /// </summary>
    private Button? _sendButton;

    /// <summary>
    /// The button that cancels the current streaming operation.
    /// </summary>
    private Button? _stopButton;

    /// <summary>
    /// The combo box for selecting provider presets.
    /// </summary>
    private ComboBox? _presetComboBox;

    /// <summary>
    /// The combo box for selecting prompt presets.
    /// </summary>
    private ComboBox? _profileComboBox;

    /// <summary>
    /// The border container that receives a theme shadow.
    /// </summary>
    private Border? _containerBorder;

    /// <summary>
    /// The button that opens the tool toggle flyout.
    /// </summary>
    private Button? _toolButton;

    /// <summary>
    /// The edit-mode banner shown above the input area.
    /// </summary>
    private Grid? _editBanner;

    /// <summary>
    /// The label inside the edit banner.
    /// </summary>
    private TextBlock? _editBannerLabel;

    /// <summary>
    /// The cancel-edit button on the edit banner.
    /// </summary>
    private Button? _editBannerCancelButton;

    /// <summary>
    /// The audio reject bar shown when send is blocked by audio capability mismatch.
    /// </summary>
    private Grid? _audioRejectBar;

    /// <summary>
    /// The label inside the audio reject bar describing the rejection reason.
    /// </summary>
    private TextBlock? _audioRejectLabel;

    /// <summary>
    /// The cancel button on the audio reject bar (closes the bar without sending).
    /// </summary>
    private Button? _audioRejectCancelButton;

    /// <summary>
    /// The send button on the audio reject bar (drops offending audio attachments and sends).
    /// </summary>
    private Button? _audioRejectSendButton;

    /// <summary>
    /// Audio attachments that the user must accept being dropped before send proceeds.
    /// Snapshotted when the reject bar appears.
    /// </summary>
    private List<ChatAttachment>? _pendingAudioReject;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ComposeBar"/> class.
    /// </summary>
    public ComposeBar()
    {
        DefaultStyleKey = typeof(ComposeBar);
        AutomationHelper.SetAutomation(this, "ComposeBar", nameKey: "ComposeBar_ControlName");
        Loaded += OnLoaded;
    }

    #endregion
}
