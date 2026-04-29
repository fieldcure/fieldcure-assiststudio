using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace AssistStudio.Controls;

/// <summary>
/// Settings section for the Ollama local AI provider. Hosts the multi-model checklist
/// (one row per locally pulled model), per-row Advanced expander for NumCtx/KeepAlive,
/// broadcast dials (max tokens, PDF handling, reasoning), and pull-progress reporting.
/// </summary>
public sealed partial class OllamaProviderSection : UserControl
{
    #region Fields

    /// <summary>The provider model collection shared with the parent ModelsPage.</summary>
    private ObservableCollection<ProviderModel> _presets = [];
    /// <summary>Suppresses change handlers while the section is repopulating UI from saved state.</summary>
    private bool _isPopulating;
    /// <summary>Tracks whether <see cref="Initialize"/> has run, to make it idempotent.</summary>
    private bool _initialized;
    /// <summary>Cancellation source for the background model-pull progress tracker.</summary>
    private CancellationTokenSource? _pullCts;
    /// <summary>Backing collection for the per-model checklist.</summary>
    private readonly ObservableCollection<OllamaModelChecklistItem> _checklist = [];

    /// <summary>
    /// Current <see cref="StatusText"/> foreground brush resource key, or <see langword="null"/>
    /// when the default foreground applies.
    /// </summary>
    private string? _statusBrushKey;

    /// <summary>Model names queued for download, shared across page instances.</summary>
    private static readonly List<string> _pendingPulls = [];

    /// <summary>Resource keys for the thinking-override ComboBox entries.</summary>
    private static readonly (ThinkingOverride Value, string LabelKey)[] ThinkingOverrideOptions =
    [
        (ThinkingOverride.Auto, "Models_ThinkingOverrideAuto"),
        (ThinkingOverride.ForceOn, "Models_ThinkingOverrideForceOn"),
        (ThinkingOverride.ForceOff, "Models_ThinkingOverrideForceOff"),
    ];

    /// <summary>Resource keys for the PDF-handling ComboBox entries.</summary>
    private static readonly (PdfCapability Value, string LabelKey)[] PdfOptions =
    [
        (PdfCapability.Auto, "Models_PdfAuto"),
        (PdfCapability.NativePdf, "Models_PdfNative"),
        (PdfCapability.PageAsImage, "Models_PdfPageAsImage"),
        (PdfCapability.TextExtraction, "Models_PdfTextExtraction"),
    ];

    #endregion

    #region Events

    /// <summary>Raised when the sub-header text should be updated.</summary>
    public event EventHandler<string>? SubHeaderChanged;

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="OllamaProviderSection"/> and subscribes to theme changes.</summary>
    public OllamaProviderSection()
    {
        InitializeComponent();
        ModelChecklist.ItemsSource = _checklist;
        ThemeHelper.SubscribeThemeChanges(this, RefreshStatusBrush);
    }

    #endregion

    #region Public Methods

    /// <summary>Initializes the section UI from the current presets.</summary>
    public void Initialize(ObservableCollection<ProviderModel> presets)
    {
        if (_initialized) return;
        _initialized = true;
        _presets = presets;

        _isPopulating = true;
        try
        {
            UrlBox.Text = AppSettings.GetOllamaBaseUrl() ?? "http://localhost:11434";
            PopulatePdfCombo();
            PopulateMaxTokens();
            PopulateThinkingToggle();
            UpdateThinkingState();
        }
        finally { _isPopulating = false; }

        _ = CheckStatusAsync();
    }

    /// <summary>Whether there are pending model downloads that need tracking.</summary>
    public static bool HasPendingPulls => _pendingPulls.Count > 0;

    /// <summary>Resumes pull progress tracking after page re-entry.</summary>
    public Task ResumePullTrackingAsync() => TrackPullProgressAsync();

    /// <summary>Cancels pull progress tracking (called on page navigate-away).</summary>
    public void CancelPullTracking()
    {
        _pullCts?.Cancel();
        _pullCts = null;
    }

    #endregion

    #region Status Check

    /// <summary>Handles the status check button.</summary>
    private async void OnCheckStatus(object sender, RoutedEventArgs e) => await CheckStatusAsync();
    /// <summary>Handles the URL test button.</summary>
    private async void OnTestUrl(object sender, RoutedEventArgs e) => await CheckStatusAsync();

    /// <summary>Probes the configured Ollama endpoint and updates the status UI accordingly.</summary>
    private async Task CheckStatusAsync()
    {
        Spinner.Visibility = Visibility.Visible;
        Spinner.IsActive = true;
        StatusText.Text = L("Models_Checking");
        SetStatusBrush(null);
        StartButton.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Collapsed;
        ActionButtons.Visibility = Visibility.Collapsed;
        CloudHint.Visibility = Visibility.Collapsed;
        ChecklistHeader.Visibility = Visibility.Collapsed;

        try
        {
            var isRemote = IsRemote();
            var result = await ValidateOllamaConnectionAsync();
            if (result.IsValid)
            {
                StatusText.Text = L("Models_Running");
                SetStatusBrush("StatusAccentForegroundBrush");
                ActionButtons.Visibility = Visibility.Visible;
                CloudHint.Visibility = Visibility.Visible;
                ChecklistHeader.Visibility = Visibility.Visible;
                await LoadModelsAsync();
            }
            else if (!isRemote && OllamaHelper.IsOllamaInstalled())
            {
                StatusText.Text = L("Models_InstalledNotRunning");
                SetStatusBrush("StatusErrorForegroundBrush");
                StartButton.Visibility = Visibility.Visible;
            }
            else if (!isRemote)
            {
                StatusText.Text = L("Models_NotInstalled");
                SetStatusBrush("StatusErrorForegroundBrush");
                InstallPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = result.ErrorMessage ?? L("Models_Error");
                SetStatusBrush("StatusErrorForegroundBrush");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            StatusText.Text = L("Models_Error");
            SetStatusBrush("StatusErrorForegroundBrush");
        }
        finally
        {
            Spinner.IsActive = false;
            Spinner.Visibility = Visibility.Collapsed;
            UpdateSubHeader();
            await SyncMainViewModelOllamaReachabilityAsync();
        }
    }

    /// <summary>Refreshes the MainViewModel's Ollama reachability state so preset filtering stays in sync.</summary>
    private static async Task SyncMainViewModelOllamaReachabilityAsync()
    {
        if ((App.Current as App)?.MainWindow?.ViewModel is { } viewModel)
            await viewModel.RefreshOllamaReachabilityAsync();
    }

    /// <summary>Runs the provider-level health check against the current base URL.</summary>
    private async Task<ConnectionInfo> ValidateOllamaConnectionAsync()
    {
        var baseUrl = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = "http://localhost:11434";

        using var provider = new OllamaProvider(baseUrl: baseUrl);
        return await provider.ValidateConnectionAsync();
    }

    #endregion

    #region Model Loading

    /// <summary>Handles the Refresh button click — re-runs the model fetch.</summary>
    private async void OnRefreshModels(object sender, RoutedEventArgs e)
    {
        RefreshModelsButton.IsEnabled = false;
        try { await LoadModelsAsync(); }
        finally { RefreshModelsButton.IsEnabled = true; }
    }

    /// <summary>Loads pulled models, classifies hardware fit, and rebuilds the checklist.</summary>
    private async Task LoadModelsAsync()
    {
        try
        {
            var baseUrl = GetBaseUrlFromUI() ?? "http://localhost:11434";
            using var manager = new OllamaModelManager(baseUrl);
            var allModels = await manager.ListLocalModelsAsync();
            var hw = await HardwareProbe.GetAsync();

            var visible = allModels
                .Select(m => new
                {
                    Model = m,
                    Fit = OllamaFitPolicy.Classify(
                        new OllamaModelMeta(m.Id, m.SizeBytes, m.ParameterSize, m.QuantizationLevel),
                        hw)
                })
                .Where(x => x.Fit != OllamaFitKind.NoFit)
                .OrderBy(x => x.Fit)
                .ThenBy(x => x.Model.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _isPopulating = true;
            try
            {
                _checklist.Clear();
                var enabled = FindAllPresets()
                    .Select(p => p.ModelId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToHashSet();

                foreach (var x in visible)
                {
                    var fitLabel = x.Fit switch
                    {
                        OllamaFitKind.Gpu => L("Models_FitGpu"),
                        OllamaFitKind.Cpu => L("Models_FitCpu"),
                        OllamaFitKind.Maybe => L("Models_FitMaybe"),
                        _ => "",
                    };

                    var preset = _presets.FirstOrDefault(p =>
                        p.ProviderType == "Ollama" && p.ModelId == x.Model.Id);

                    var item = new OllamaModelChecklistItem(x.Model.Id, fitLabel)
                    {
                        IsEnabled = enabled.Contains(x.Model.Id),
                        NumCtxValue = preset?.NumCtx ?? double.NaN,
                        KeepAlive = preset?.KeepAlive ?? "",
                    };
                    _checklist.Add(item);
                }

                EmptyChecklistHint.Visibility =
                    _checklist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Auto-enable first pulled model if no Ollama preset yet — keeps app
                // usable on first install.
                if (!_presets.Any(p => p.ProviderType == "Ollama") && _checklist.Count > 0)
                {
                    EnableModel(_checklist[0].ModelId);
                    _checklist[0].IsEnabled = true;
                    AppSettings.SaveModels(_presets);
                }
            }
            finally { _isPopulating = false; }

            UpdateThinkingState();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    #endregion

    #region URL Management

    /// <summary>Persists the new URL to settings and broadcasts it to all Ollama presets.</summary>
    private void OnUrlChanged(object sender, RoutedEventArgs e)
    {
        var url = GetBaseUrlFromUI();
        AppSettings.SetOllamaBaseUrl(url);

        foreach (var p in FindAllPresets()) p.BaseUrl = url;
        AppSettings.SaveModels(_presets);

        _ = SyncMainViewModelOllamaReachabilityAsync();
    }

    /// <summary>Resets the base URL to localhost.</summary>
    private void OnLocalhost(object sender, RoutedEventArgs e)
    {
        UrlBox.Text = "http://localhost:11434";
        AppSettings.SetOllamaBaseUrl(null);

        foreach (var p in FindAllPresets()) p.BaseUrl = null;
        AppSettings.SaveModels(_presets);

        _ = SyncMainViewModelOllamaReachabilityAsync();
        _ = CheckStatusAsync();
    }

    /// <summary>Returns the user-entered base URL, or null when matching the default.</summary>
    private string? GetBaseUrlFromUI()
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url) || url == "http://localhost:11434") return null;
        return url;
    }

    /// <summary>Returns true when the configured base URL targets a non-loopback host.</summary>
    private bool IsRemote()
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host != "localhost" && uri.Host != "127.0.0.1" && uri.Host != "::1";
        return false;
    }

    #endregion

    #region Ollama Actions

    /// <summary>Launches the Ollama CLI login flow.</summary>
    private void OnLogin(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ollama",
            Arguments = "login",
            UseShellExecute = true
        });
    }

    /// <summary>Handles the Start button click by launching the local Ollama service.</summary>
    private async void OnStartOllama(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        Spinner.Visibility = Visibility.Visible;
        Spinner.IsActive = true;
        StatusText.Text = L("Models_Starting");

        try
        {
            var started = await OllamaHelper.StartOllamaAsync();
            if (started)
                await CheckStatusAsync();
            else
            {
                StatusText.Text = L("Models_FailedToStart");
                SetStatusBrush("StatusErrorForegroundBrush");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            StatusText.Text = L("Models_ErrorStarting");
            SetStatusBrush("StatusErrorForegroundBrush");
        }
        finally
        {
            StartButton.IsEnabled = true;
            Spinner.IsActive = false;
            Spinner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Opens the Ollama model browser dialog and queues any selected models for download.</summary>
    private async void OnBrowseModels(object sender, RoutedEventArgs e)
    {
        var baseUrl = GetBaseUrlFromUI() ?? "http://localhost:11434";
        using var manager = new OllamaModelManager(baseUrl);
        var dialog = new ModelSelectionDialog(manager) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();

        if (dialog.ModelsToPull.Count > 0)
            await PullModelsAsync(dialog.ModelsToPull);

        await LoadModelsAsync();
    }

    /// <summary>Adds <paramref name="modelNames"/> to the pending-pull queue.</summary>
    private async Task PullModelsAsync(List<string> modelNames)
    {
        foreach (var name in modelNames)
        {
            if (!_pendingPulls.Contains(name))
                _pendingPulls.Add(name);
        }
        await TrackPullProgressAsync();
    }

    /// <summary>Drains the pending-pull queue, updating the progress bar/status until cancelled or empty.</summary>
    private async Task TrackPullProgressAsync()
    {
        _pullCts?.Cancel();
        _pullCts = new CancellationTokenSource();
        var ct = _pullCts.Token;

        PullProgressPanel.Visibility = Visibility.Visible;
        BrowseModelsButton.IsEnabled = false;

        var baseUrl = GetBaseUrlFromUI() ?? "http://localhost:11434";
        using var manager = new OllamaModelManager(baseUrl);

        while (_pendingPulls.Count > 0)
        {
            var modelName = _pendingPulls[0];
            var remaining = _pendingPulls.Count;
            var prefix = remaining > 1 ? $"[1/{remaining}] " : "";

            PullProgressStatus.Text = $"{prefix}{string.Format(L("Models_PullingModel"), modelName)}";
            PullProgressBar.IsIndeterminate = true;
            PullProgressBar.Value = 0;

            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    var pct = p.Percent * 100;
                    if (pct > 0)
                    {
                        PullProgressBar.IsIndeterminate = false;
                        PullProgressBar.Value = pct;
                        var totalMb = p.TotalBytes.HasValue ? p.TotalBytes.Value / 1_048_576.0 : 0;
                        var doneMb = p.CompletedBytes.HasValue ? p.CompletedBytes.Value / 1_048_576.0 : 0;
                        PullProgressStatus.Text = totalMb > 0
                            ? $"{prefix}{p.Status} — {doneMb:F0} / {totalMb:F0} MB ({pct:F0}%)"
                            : $"{prefix}{p.Status}";
                    }
                    else
                    {
                        PullProgressBar.IsIndeterminate = true;
                        PullProgressStatus.Text = $"{prefix}{p.Status}";
                    }
                });
            });

            try
            {
                LoggingService.LogInfo($"[Pull] Starting download: {modelName}");
                await manager.DownloadModelAsync(modelName, progress, ct);
                LoggingService.LogInfo($"[Pull] Completed: {modelName}");
                _pendingPulls.Remove(modelName);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LoggingService.LogInfo($"[Pull] Tracking stopped (page navigated away): {modelName}");
                break;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[Pull] Failed: {modelName} — {ex.Message}");
                _pendingPulls.Remove(modelName);
                PullProgressStatus.Text = $"{prefix}Error: {ex.Message}";
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
            }
        }

        if (!ct.IsCancellationRequested)
        {
            PullProgressBar.IsIndeterminate = false;
            PullProgressPanel.Visibility = Visibility.Collapsed;
            BrowseModelsButton.IsEnabled = true;
        }
    }

    #endregion

    #region Checklist Toggling

    /// <summary>Handles a checklist row's check/uncheck.</summary>
    private async void OnModelToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating) return;
        if (sender is not CheckBox cb || cb.Tag is not string modelId) return;

        var item = _checklist.FirstOrDefault(i => i.ModelId == modelId);
        if (item is null) return;

        if (cb.IsChecked == true)
        {
            EnableModel(modelId);
        }
        else
        {
            var usages = AppSettings.FindAuxiliaryKeyUsages(modelId);
            if (usages.Count > 0)
            {
                var ok = await ConfirmRemoveInUseAsync(modelId, usages);
                if (!ok)
                {
                    _isPopulating = true;
                    item.IsEnabled = true;
                    _isPopulating = false;
                    return;
                }
            }
            DisableModel(modelId);
        }
        AppSettings.SaveModels(_presets);
        UpdateSubHeader();
    }

    /// <summary>Adds an Ollama <see cref="ProviderModel"/> for <paramref name="modelId"/>.</summary>
    private void EnableModel(string modelId)
    {
        if (FindAllPresets().Any(p => p.ModelId == modelId)) return;

        var template = FindAnyPreset();
        var newPreset = new ProviderModel
        {
            Name = modelId,
            ProviderType = "Ollama",
            ModelId = modelId,
            BaseUrl = template?.BaseUrl ?? GetBaseUrlFromUI(),
            MaxTokens = template?.MaxTokens ?? 4096,
            Temperature = template?.Temperature ?? 0.7,
            StreamingEnabled = template?.StreamingEnabled ?? true,
            PdfCapability = template?.PdfCapability ?? PdfCapability.Auto,
            ThinkingEnabled = template?.ThinkingEnabled ?? false,
            ThinkingOverride = template?.ThinkingOverride ?? ThinkingOverride.Auto,
            ThinkingBudget = template?.ThinkingBudget,
            // Per-model defaults (null = use Ollama protocol default).
            KeepAlive = null,
            NumCtx = null,
        };

        // Insert before Mock if present, else append.
        var mockIdx = -1;
        for (var i = 0; i < _presets.Count; i++)
        {
            if (_presets[i].ProviderType == "Mock") { mockIdx = i; break; }
        }
        if (mockIdx >= 0) _presets.Insert(mockIdx, newPreset);
        else _presets.Add(newPreset);
    }

    /// <summary>Removes the matching Ollama <see cref="ProviderModel"/>.</summary>
    private void DisableModel(string modelId)
    {
        var existing = FindAllPresets().FirstOrDefault(p => p.ModelId == modelId);
        if (existing is not null) _presets.Remove(existing);
    }

    /// <summary>Shows a confirmation dialog when the user unchecks a model still referenced elsewhere.</summary>
    private async Task<bool> ConfirmRemoveInUseAsync(string modelId, IReadOnlyList<string> usages)
    {
        var dialog = new ContentDialog
        {
            Title = L("Models_ModelInUseTitle"),
            Content = string.Format(L("Models_ModelInUseMessage"), modelId, string.Join(", ", usages)),
            PrimaryButtonText = L("Models_Remove"),
            CloseButtonText = L("Models_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    #endregion

    #region Per-Row Edits

    /// <summary>Persists the per-model NumCtx change.</summary>
    private void OnNumCtxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating) return;
        if (sender.Tag is not string modelId) return;
        var preset = FindAllPresets().FirstOrDefault(p => p.ModelId == modelId);
        if (preset is null) return;
        preset.NumCtx = double.IsNaN(args.NewValue) ? null : (int)args.NewValue;
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Persists the per-model KeepAlive change.</summary>
    private void OnKeepAliveChanged(object sender, TextChangedEventArgs args)
    {
        if (_isPopulating) return;
        if (sender is not TextBox tb || tb.Tag is not string modelId) return;
        var preset = FindAllPresets().FirstOrDefault(p => p.ModelId == modelId);
        if (preset is null) return;
        var text = tb.Text.Trim();
        preset.KeepAlive = string.IsNullOrEmpty(text) ? null : text;
        AppSettings.SaveModels(_presets);
    }

    #endregion

    #region Broadcast Options

    /// <summary>Populates the PDF-handling ComboBox.</summary>
    private void PopulatePdfCombo()
    {
        PdfCombo.Items.Clear();
        foreach (var (_, labelKey) in PdfOptions)
            PdfCombo.Items.Add(L(labelKey));
        var saved = FindAnyPreset()?.PdfCapability ?? PdfCapability.Auto;
        var idx = Array.FindIndex(PdfOptions, o => o.Value == saved);
        PdfCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    /// <summary>Loads the saved max-tokens value.</summary>
    private void PopulateMaxTokens()
    {
        var preset = FindAnyPreset();
        if (preset is not null) MaxTokensBox.Value = preset.MaxTokens;
    }

    /// <summary>Initializes the thinking override controls.</summary>
    private void PopulateThinkingToggle()
    {
        ThinkingOverrideCombo.Items.Clear();
        foreach (var (_, labelKey) in ThinkingOverrideOptions)
            ThinkingOverrideCombo.Items.Add(L(labelKey));

        var preset = FindAnyPreset();
        if (preset is not null)
        {
            ThinkingToggle.IsOn = preset.ThinkingEnabled;
            ThinkingBudgetBox.Value = preset.ThinkingBudget ?? 4096;
            ThinkingBudgetBox.IsEnabled = preset.ThinkingEnabled;
            var idx = Array.FindIndex(ThinkingOverrideOptions, o => o.Value == preset.ThinkingOverride);
            ThinkingOverrideCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
        {
            ThinkingToggle.IsOn = false;
            ThinkingBudgetBox.Value = 4096;
            ThinkingBudgetBox.IsEnabled = false;
            ThinkingOverrideCombo.SelectedIndex = 0;
        }
    }

    /// <summary>Broadcasts a PDF-handling change to all Ollama presets.</summary>
    private void OnPdfHandlingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        var idx = PdfCombo.SelectedIndex;
        var value = idx >= 0 && idx < PdfOptions.Length ? PdfOptions[idx].Value : PdfCapability.Auto;
        foreach (var p in FindAllPresets()) p.PdfCapability = value;
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a max-tokens change.</summary>
    private void OnMaxTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating || double.IsNaN(args.NewValue)) return;
        foreach (var p in FindAllPresets()) p.MaxTokens = (int)args.NewValue;
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a thinking-toggle change.</summary>
    private void OnThinkingToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating || !ThinkingToggle.IsEnabled) return;
        ThinkingBudgetBox.IsEnabled = ThinkingToggle.IsOn;
        var enabled = ThinkingToggle.IsOn;
        var budget = enabled && !double.IsNaN(ThinkingBudgetBox.Value) ? (int?)ThinkingBudgetBox.Value : null;
        foreach (var p in FindAllPresets())
        {
            p.ThinkingEnabled = enabled;
            p.ThinkingBudget = budget;
        }
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a thinking-override change.</summary>
    private void OnThinkingOverrideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        _isPopulating = true;
        UpdateThinkingState();
        _isPopulating = false;

        var idx = ThinkingOverrideCombo.SelectedIndex;
        var ovr = idx >= 0 && idx < ThinkingOverrideOptions.Length
            ? ThinkingOverrideOptions[idx].Value : ThinkingOverride.Auto;
        var enabled = ThinkingToggle.IsOn;
        var budget = enabled && !double.IsNaN(ThinkingBudgetBox.Value) ? (int?)ThinkingBudgetBox.Value : null;
        foreach (var p in FindAllPresets())
        {
            p.ThinkingOverride = ovr;
            p.ThinkingEnabled = enabled;
            p.ThinkingBudget = budget;
        }
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Broadcasts a thinking-budget change.</summary>
    private void OnThinkingBudgetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating) return;
        var budget = !double.IsNaN(sender.Value) ? (int?)sender.Value : null;
        foreach (var p in FindAllPresets()) p.ThinkingBudget = budget;
        AppSettings.SaveModels(_presets);
    }

    /// <summary>Recomputes whether thinking is available, required, or blocked for the first checked model.</summary>
    private void UpdateThinkingState()
    {
        var modelId = FindAllPresets().FirstOrDefault()?.ModelId;
        var support = ThinkingCapability.GetSupport("Ollama", modelId);

        var overrideIdx = ThinkingOverrideCombo.SelectedIndex;
        var userOverride = overrideIdx >= 0 && overrideIdx < ThinkingOverrideOptions.Length
            ? ThinkingOverrideOptions[overrideIdx].Value
            : ThinkingOverride.Auto;

        switch (userOverride)
        {
            case ThinkingOverride.ForceOn:
                ThinkingToggle.IsOn = true;
                ThinkingToggle.IsEnabled = true;
                ThinkingBudgetBox.IsEnabled = true;
                ThinkingHint.Visibility = Visibility.Collapsed;
                break;
            case ThinkingOverride.ForceOff:
                ThinkingToggle.IsOn = false;
                ThinkingToggle.IsEnabled = true;
                ThinkingBudgetBox.IsEnabled = false;
                ThinkingHint.Visibility = Visibility.Collapsed;
                break;
            default:
                switch (support)
                {
                    case ThinkingSupport.NotSupported:
                        ThinkingToggle.IsEnabled = false;
                        ThinkingToggle.IsOn = false;
                        ThinkingBudgetBox.IsEnabled = false;
                        ThinkingHint.Text = L("Models_ThinkingNotSupported");
                        ThinkingHint.Visibility = Visibility.Visible;
                        break;
                    case ThinkingSupport.Required:
                        ThinkingToggle.IsOn = true;
                        ThinkingToggle.IsEnabled = false;
                        ThinkingBudgetBox.IsEnabled = true;
                        ThinkingHint.Text = L("Models_ThinkingAlwaysOn");
                        ThinkingHint.Visibility = Visibility.Visible;
                        break;
                    default:
                        ThinkingToggle.IsEnabled = true;
                        ThinkingBudgetBox.IsEnabled = ThinkingToggle.IsOn;
                        ThinkingHint.Visibility = Visibility.Collapsed;
                        break;
                }
                break;
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>Returns the first Ollama <see cref="ProviderModel"/>, or null.</summary>
    private ProviderModel? FindAnyPreset()
        => _presets.FirstOrDefault(p => p.ProviderType == "Ollama");

    /// <summary>Returns every Ollama <see cref="ProviderModel"/>.</summary>
    private IEnumerable<ProviderModel> FindAllPresets()
        => _presets.Where(p => p.ProviderType == "Ollama");

    /// <summary>Updates the parent header's sub-text.</summary>
    private void UpdateSubHeader()
    {
        var status = StatusText.Text;
        var count = FindAllPresets().Count();
        var modelText = count switch
        {
            0 => L("Models_NoModelsSelected"),
            1 => string.Format(L("Models_OneModelSelected"), 1),
            _ => string.Format(L("Models_NModelsSelected"), count),
        };

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(modelText)) parts.Add(modelText);
        if (!string.IsNullOrEmpty(status)) parts.Add(status);
        SubHeaderChanged?.Invoke(this, string.Join(" · ", parts));
    }

    /// <summary>Shared resource loader for localized strings on this section.</summary>
    private static readonly ResourceLoader Res = new();

    /// <summary>Resolves a localized string for the given resource key, falling back to the key itself.</summary>
    private static string L(string key) =>
        Res.GetString(key) is { Length: > 0 } value ? value : key;

    /// <summary>Assigns <see cref="StatusText"/>'s foreground to the resolved theme brush.</summary>
    private void SetStatusBrush(string? resourceKey)
    {
        _statusBrushKey = resourceKey;
        if (resourceKey is null)
            StatusText.ClearValue(TextBlock.ForegroundProperty);
        else
            StatusText.Foreground = ThemeHelper.GetBrush(resourceKey);
    }

    /// <summary>Reapplies the last <see cref="SetStatusBrush"/> key against the current theme.</summary>
    private void RefreshStatusBrush()
    {
        if (_statusBrushKey is null)
            StatusText.ClearValue(TextBlock.ForegroundProperty);
        else
            StatusText.Foreground = ThemeHelper.GetBrush(_statusBrushKey);
    }

    #endregion
}
