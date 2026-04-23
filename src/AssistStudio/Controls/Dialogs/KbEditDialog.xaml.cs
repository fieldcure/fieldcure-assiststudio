using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using IOPath = System.IO.Path;

namespace AssistStudio.Controls.Dialogs;

/// <summary>
/// Unified dialog for creating a new knowledge base or editing an existing
/// one. Mode is inferred from the constructor argument: passing <c>null</c>
/// (the default) creates a new KB; passing an existing <see cref="KnowledgeBase"/>
/// opens the editor populated with its current values.
/// </summary>
/// <remarks>
/// <para>
/// The caller must <c>await</c> <see cref="InitializeAsync"/> before
/// <see cref="ContentDialog.ShowAsync"/> — the embedding model selector
/// needs to load its availability map and any prefill values first.
/// </para>
/// <para>
/// Result semantics:
/// <list type="bullet">
/// <item><see cref="ContentDialogResult.Primary"/> — Create: make the KB and index it;
/// Edit: save and re-index (or schedule).</item>
/// <item><see cref="ContentDialogResult.Secondary"/> — Edit only: save without re-indexing.</item>
/// <item><see cref="ContentDialogResult.None"/> — user cancelled.</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class KbEditDialog : ThemedContentDialog
{
    #region Fields

    private readonly KnowledgeBase? _existing;
    private readonly List<KnowledgeBase> _allKbs;
    private readonly ObservableCollection<FolderRowViewModel> _folders = [];
    private bool _nameManuallyEdited;
    private bool _userOverrodeTiming;
    private bool _hasFolder;

    #endregion

    #region Properties

    /// <summary><c>true</c> when the dialog is in create mode (no existing KB).</summary>
    public bool IsCreate => _existing is null;

    /// <summary>Source folder paths the dialog currently lists.</summary>
    public List<string> SourcePaths => CollectFolderPaths();

    /// <summary>
    /// Name to use for the KB. Falls back to the first folder's name in
    /// create mode, or the existing name in edit mode, when the user leaves
    /// the field empty.
    /// </summary>
    public string KbName
    {
        get
        {
            var typed = NameBox.Text.Trim();
            if (!string.IsNullOrEmpty(typed))
                return typed;

            if (!IsCreate && _existing is not null)
                return _existing.Name;

            var first = SourcePaths.FirstOrDefault();
            if (string.IsNullOrEmpty(first))
                return "Untitled";

            var fromFolder = IOPath.GetFileName(first.TrimEnd(IOPath.DirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(fromFolder) ? "Untitled" : fromFolder;
        }
    }

    /// <summary>Embedding provider configuration the user selected.</summary>
    public KbProviderConfig EmbeddingConfig => ModelSelector.GetEmbeddingConfig();

    /// <summary>Contextualizer provider configuration the user selected.</summary>
    public KbProviderConfig ContextualizerConfig => ModelSelector.GetContextualizerConfig();

    /// <summary>
    /// <c>true</c> when the user chose "start indexing on app close",
    /// <c>false</c> for "start indexing now".
    /// </summary>
    public bool IsDeferred => TimingRadios.SelectedIndex == 1;

    /// <summary>
    /// Edit mode: <c>true</c> if the embedding model changed versus what
    /// the existing KB was indexed with. Always <c>false</c> in create mode.
    /// </summary>
    public bool EmbeddingModelChanged => ModelSelector.EmbeddingModelChanged;

    /// <summary>
    /// Edit mode: <c>true</c> if the contextualizer changed versus what
    /// the existing KB was indexed with. Always <c>false</c> in create mode.
    /// </summary>
    public bool ContextualizerChanged => ModelSelector.ContextualizerChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new <see cref="KbEditDialog"/>. Pass <c>null</c>
    /// (default) for create mode, or an existing <see cref="KnowledgeBase"/>
    /// for edit mode.
    /// </summary>
    public KbEditDialog(KnowledgeBase? existing = null)
    {
        _existing = existing;
        _allKbs = KnowledgeBaseStore.ListAll();

        InitializeComponent();
        FolderList.ItemsSource = _folders;

        CloseButtonText = Loader.GetString("Dialog_Cancel");

        if (IsCreate)
        {
            Title = Loader.GetString("KB_CreateDialogTitle");
            PrimaryButtonText = Loader.GetString("KB_Create/Content");
            IsPrimaryButtonEnabled = false;
        }
        else
        {
            Title = BuildEditTitle(existing!);
            PrimaryButtonText = Loader.GetString("KB_SaveAndReindex");
            SecondaryButtonText = Loader.GetString("KB_Save");
            ReindexWarning.Visibility = Visibility.Visible;
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the embedding model selector and applies timing defaults.
    /// In edit mode this also prefills the folder list, name, and model
    /// selection from the existing KB. Must be awaited before
    /// <see cref="ContentDialog.ShowAsync"/>.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!IsCreate && _existing is not null)
        {
            // Prefer the IndexedWith snapshot (what the DB was actually
            // built with) so the radios match the index, not whatever the
            // user may have edited on the top-level KB fields since the
            // last re-index.
            ModelSelector.CurrentEmbeddingModel =
                _existing.IndexedWith?.Embedding.Model ?? _existing.Embedding.Model;
            ModelSelector.CurrentContextualizer =
                _existing.IndexedWith?.Contextualizer.Model ?? _existing.Contextualizer.Model;
        }

        await ModelSelector.InitializeAsync();
        ModelSelector.SelectionChanged += OnModelSelectionChanged;

        if (!IsCreate && _existing is not null)
        {
            var removeTooltip = Loader.GetString("Connect_Remove") ?? "Remove";
            foreach (var path in _existing.SourcePaths)
                _folders.Add(new FolderRowViewModel(path, removeTooltip));
            NameBox.Text = _existing.Name;
            _hasFolder = _existing.SourcePaths.Count > 0;
        }

        // Default timing: local embedding models default to deferred since
        // indexing on the same machine slows the user down.
        if (ModelSelector.IsSelectedEmbeddingLocal)
        {
            TimingRadios.SelectedIndex = 1;
            TimingHint.Visibility = Visibility.Visible;
            _userOverrodeTiming = false;
        }
        else
        {
            TimingRadios.SelectedIndex = 0;
        }

        RefreshPrimaryButton();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Tracks whether the user has manually typed in the name box so we can
    /// stop auto-filling the name from the first folder.
    /// </summary>
    private void OnNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (NameBox.FocusState != FocusState.Unfocused)
            _nameManuallyEdited = true;
    }

    /// <summary>
    /// Marks the timing choice as user-overridden and re-evaluates the
    /// primary button (edit mode swaps its label between "Save &amp; Re-index"
    /// and "Save &amp; Schedule" based on this choice).
    /// </summary>
    private void OnTimingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _userOverrodeTiming = true;
        RefreshPrimaryButton();
    }

    /// <summary>
    /// Recomputes the primary button state and, unless the user has
    /// overridden it, resets the timing choice based on whether the newly
    /// selected embedding model runs locally.
    /// </summary>
    private void OnModelSelectionChanged(object? sender, EventArgs e)
    {
        if (!_userOverrodeTiming)
        {
            TimingRadios.SelectedIndex = ModelSelector.IsSelectedEmbeddingLocal ? 1 : 0;
            _userOverrodeTiming = false;
            TimingHint.Visibility = ModelSelector.IsSelectedEmbeddingLocal
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        RefreshPrimaryButton();
    }

    /// <summary>
    /// Opens the folder picker and, on success, appends the picked folder to
    /// the list. Auto-fills the name from the first folder (create mode), and
    /// warns if the folder is already indexed by another KB.
    /// </summary>
    private async void OnAddFolderClicked(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (folder is null)
            return;

        var currentPaths = CollectFolderPaths();
        if (currentPaths.Any(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        _folders.Add(new FolderRowViewModel(folder.Path, Loader.GetString("Connect_Remove") ?? "Remove"));
        _hasFolder = true;
        RefreshPrimaryButton();

        // Create-only: auto-fill the name from the first folder if the user
        // has not typed anything.
        if (IsCreate && !_nameManuallyEdited && _folders.Count == 1)
            NameBox.Text = IOPath.GetFileName(folder.Path.TrimEnd(IOPath.DirectorySeparatorChar));

        // Warn if this folder is bound to another KB. In edit mode, exclude
        // the KB being edited from the check.
        var otherKb = _allKbs.FirstOrDefault(k =>
            k.Id != _existing?.Id &&
            k.SourcePaths.Any(p => string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)));
        if (otherKb is not null)
        {
            FolderWarning.Text = string.Format(Loader.GetString("KB_FolderUsedWarning"), otherKb.Name);
            FolderWarning.Visibility = Visibility.Visible;
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Gates the primary button and — in edit mode — swaps its label
    /// between "Save &amp; Re-index" (immediate) and "Save &amp; Schedule"
    /// (deferred) based on the timing choice. Deferred edits are always
    /// enabled because the model availability check happens later when the
    /// deferred job actually runs.
    /// </summary>
    private void RefreshPrimaryButton()
    {
        if (IsCreate)
        {
            IsPrimaryButtonEnabled = _hasFolder && ModelSelector.IsCurrentSelectionAvailable;
            return;
        }

        IsPrimaryButtonEnabled = IsDeferred || ModelSelector.IsCurrentSelectionAvailable;
        PrimaryButtonText = IsDeferred
            ? Loader.GetString("KB_SaveAndSchedule")
            : Loader.GetString("KB_SaveAndReindex");
    }

    /// <summary>
    /// Builds the edit-mode title: KB name with a smaller "indexed with"
    /// caption beneath showing the models recorded in the IndexedWith
    /// snapshot. Caption is omitted for KBs that have never been indexed.
    /// </summary>
    private static object BuildEditTitle(KnowledgeBase kb)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = kb.Name });

        if (kb.IndexedWith is null)
            return panel;

        var indexedEmb = kb.IndexedWith.Embedding.Model;
        var indexedCtx = kb.IndexedWith.Contextualizer.Model;
        var caption = indexedEmb;
        if (!string.IsNullOrEmpty(indexedCtx))
            caption += $" \u00b7 {indexedCtx}";

        if (string.IsNullOrEmpty(caption))
            return panel;

        panel.Children.Add(new TextBlock
        {
            Text = caption,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6,
        });
        return panel;
    }

    /// <summary>
    /// Removes the row whose DataContext initiated the click and re-evaluates
    /// the primary button state so an empty folder list disables submission.
    /// </summary>
    private void OnRemoveFolderClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FolderRowViewModel vm)
            return;

        _folders.Remove(vm);
        _hasFolder = _folders.Count > 0;
        RefreshPrimaryButton();
    }

    /// <summary>
    /// Reads the folder paths out of the folder view-model collection in the
    /// order the user added them.
    /// </summary>
    private List<string> CollectFolderPaths() =>
        _folders.Select(f => f.Path).ToList();

    /// <summary>
    /// Opens a folder picker parented to the main window. Returns
    /// <c>null</c> when the user cancels or when the app has no window.
    /// </summary>
    private static async Task<Windows.Storage.StorageFolder?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var window = (App.Current as App)?.MainWindow;
        if (window is null)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    #endregion
}

/// <summary>
/// Immutable view model for one row of <see cref="KbEditDialog"/>'s source folder list.
/// The <see cref="Path"/> is shown in the row label and used by
/// <see cref="KbEditDialog.CollectFolderPaths"/> to build <see cref="KbEditDialog.SourcePaths"/>.
/// </summary>
public sealed class FolderRowViewModel
{
    /// <summary>Initializes a new folder row view model with the given path.</summary>
    public FolderRowViewModel(string path, string removeTooltip)
    {
        Path = path;
        RemoveTooltip = removeTooltip;
    }

    /// <summary>Gets the absolute folder path displayed in the row.</summary>
    public string Path { get; }

    /// <summary>Gets the localized tooltip / automation name shown on the row's remove button.</summary>
    public string RemoveTooltip { get; }
}
