using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace AssistStudio.Controls;

/// <summary>
/// Settings section for the Ollama local AI provider.
/// Handles server status checking, model management, and download progress.
/// </summary>
public sealed partial class OllamaProviderSection : UserControl
{
    #region Fields

    private ObservableCollection<ProviderPreset> _presets = [];
    private bool _isPopulating;
    private bool _initialized;
    private CancellationTokenSource? _pullCts;

    /// <summary>Model names queued for download, shared across page instances.</summary>
    private static readonly List<string> _pendingPulls = [];

    private static readonly (ThinkingOverride Value, string LabelKey)[] ThinkingOverrideOptions =
    [
        (ThinkingOverride.Auto, "Models_ThinkingOverrideAuto"),
        (ThinkingOverride.ForceOn, "Models_ThinkingOverrideForceOn"),
        (ThinkingOverride.ForceOff, "Models_ThinkingOverrideForceOff"),
    ];

    private static readonly (PdfCapability Value, string LabelKey)[] PdfOptions =
    [
        (PdfCapability.Auto, "Models_PdfAuto"),
        (PdfCapability.NativePdf, "Models_PdfNative"),
        (PdfCapability.PageAsImage, "Models_PdfPageAsImage"),
        (PdfCapability.TextExtraction, "Models_PdfTextExtraction"),
    ];

    #endregion

    #region Events

    /// <summary>
    /// Raised when the sub-header text should be updated.
    /// </summary>
    public event EventHandler<string>? SubHeaderChanged;

    #endregion

    #region Constructor

    public OllamaProviderSection()
    {
        InitializeComponent();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes the section UI from the current presets. Called once after x:Load realization.
    /// </summary>
    public void Initialize(ObservableCollection<ProviderPreset> presets)
    {
        if (_initialized) return;
        _initialized = true;
        _presets = presets;

        _isPopulating = true;
        try
        {
            UrlBox.Text = AppSettings.GetOllamaBaseUrl() ?? "http://localhost:11434";

            // Ollama has no fallback models — combo populated by LoadOllamaModelsAsync
            PopulatePdfCombo();
            PopulateMaxTokens();
            PopulateOllamaOptions();
            PopulateThinkingToggle();
            UpdateThinkingState();
        }
        finally { _isPopulating = false; }

        _ = CheckStatusAsync();
    }

    /// <summary>
    /// Whether there are pending model downloads that need tracking.
    /// </summary>
    public static bool HasPendingPulls => _pendingPulls.Count > 0;

    /// <summary>
    /// Resumes pull progress tracking after page re-entry.
    /// </summary>
    public Task ResumePullTrackingAsync() => TrackPullProgressAsync();

    /// <summary>
    /// Cancels pull progress tracking (called on page navigate-away).
    /// </summary>
    public void CancelPullTracking()
    {
        _pullCts?.Cancel();
        _pullCts = null;
    }

    #endregion

    #region Status Check

    private async void OnCheckStatus(object sender, RoutedEventArgs e) => await CheckStatusAsync();
    private async void OnTestUrl(object sender, RoutedEventArgs e) => await CheckStatusAsync();

    private async Task CheckStatusAsync()
    {
        Spinner.Visibility = Visibility.Visible;
        Spinner.IsActive = true;
        StatusText.Text = L("Models_Checking");
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        StartButton.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Collapsed;
        ActionButtons.Visibility = Visibility.Collapsed;
        CloudHint.Visibility = Visibility.Collapsed;

        try
        {
            var isRemote = IsRemote();

            var result = await ValidateOllamaConnectionAsync();
            if (result.IsValid)
            {
                StatusText.Text = L("Models_Running");
                StatusText.Foreground = ThemeHelper.GetBrush("StatusAccentForegroundBrush");
                ActionButtons.Visibility = Visibility.Visible;
                CloudHint.Visibility = Visibility.Visible;
                await LoadModelsAsync();
            }
            else if (!isRemote && OllamaHelper.IsOllamaInstalled())
            {
                StatusText.Text = L("Models_InstalledNotRunning");
                StatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
                StartButton.Visibility = Visibility.Visible;
            }
            else if (!isRemote)
            {
                StatusText.Text = L("Models_NotInstalled");
                StatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
                InstallPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = result.ErrorMessage ?? L("Models_Error");
                StatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            StatusText.Text = L("Models_Error");
            StatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
        }
        finally
        {
            Spinner.IsActive = false;
            Spinner.Visibility = Visibility.Collapsed;
            UpdateSubHeader();
            await SyncMainViewModelOllamaReachabilityAsync();
        }
    }

    /// <summary>
    /// Keeps main-tab preset filtering in sync when the user explicitly re-checks Ollama
    /// from the Settings UI or starts the server after app launch.
    /// </summary>
    private static async Task SyncMainViewModelOllamaReachabilityAsync()
    {
        if ((App.Current as App)?.MainWindow?.ViewModel is { } viewModel)
            await viewModel.RefreshOllamaReachabilityAsync();
    }

    /// <summary>
    /// Uses the provider-level health check so Settings and startup filtering rely on the
    /// same Ollama endpoint semantics.
    /// </summary>
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

    private async Task LoadModelsAsync()
    {
        try
        {
            var baseUrl = GetBaseUrlFromUI() ?? "http://localhost:11434";
            using var manager = new OllamaModelManager(baseUrl);
            var allModels = await manager.ListLocalModelsAsync();
            var hw = await HardwareProbe.GetAsync();

            var visibleModels = allModels
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
                ModelCombo.Items.Clear();
                foreach (var x in visibleModels)
                {
                    var suffix = x.Fit switch
                    {
                        OllamaFitKind.Gpu => $"  ({L("Models_FitGpu")})",
                        OllamaFitKind.Cpu => $"  ({L("Models_FitCpu")})",
                        OllamaFitKind.Maybe => $"  ({L("Models_FitMaybe")})",
                        _ => ""
                    };
                    ModelCombo.Items.Add(x.Model.Id + suffix);
                }

                if (ModelCombo.Items.Count > 0)
                {
                    ModelCombo.IsEnabled = true;
                    var saved = AppSettings.GetDefaultModel("Ollama");
                    var found = false;
                    for (var i = 0; i < ModelCombo.Items.Count; i++)
                    {
                        if (ModelCombo.Items[i] is string s && s.StartsWith(saved ?? ""))
                        {
                            ModelCombo.SelectedIndex = i;
                            found = true;
                            break;
                        }
                    }
                    if (!found) ModelCombo.SelectedIndex = 0;
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

    private void OnUrlChanged(object sender, RoutedEventArgs e)
    {
        var url = GetBaseUrlFromUI();
        AppSettings.SetOllamaBaseUrl(url);

        var preset = FindPreset();
        if (preset is not null)
            preset.BaseUrl = url;
        PersistPresets();

        // Keep preset filtering aligned with the saved URL even if the user does not
        // explicitly click Test/Refresh after leaving the URL box.
        _ = SyncMainViewModelOllamaReachabilityAsync();
    }

    private void OnLocalhost(object sender, RoutedEventArgs e)
    {
        UrlBox.Text = "http://localhost:11434";
        AppSettings.SetOllamaBaseUrl(null);

        var preset = FindPreset();
        if (preset is not null)
            preset.BaseUrl = null;
        PersistPresets();

        // Keep preset filtering aligned with the saved URL after switching back
        // to localhost from a remote address.
        _ = SyncMainViewModelOllamaReachabilityAsync();
        _ = CheckStatusAsync();
    }

    private string? GetBaseUrlFromUI()
    {
        var url = UrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url) || url == "http://localhost:11434")
            return null;
        return url;
    }

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

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "ollama",
            Arguments = "login",
            UseShellExecute = true
        });
    }

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
                StatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            StatusText.Text = L("Models_ErrorStarting");
            StatusText.Foreground = ThemeHelper.GetBrush("StatusErrorForegroundBrush");
        }
        finally
        {
            StartButton.IsEnabled = true;
            Spinner.IsActive = false;
            Spinner.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnBrowseModels(object sender, RoutedEventArgs e)
    {
        var baseUrl = GetBaseUrlFromUI() ?? "http://localhost:11434";
        using var manager = new OllamaModelManager(baseUrl);
        var dialog = new ModelSelectionDialog(manager)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();

        if (dialog.ModelsToPull.Count > 0)
            await PullModelsAsync(dialog.ModelsToPull);

        await LoadModelsAsync();
    }

    private async Task PullModelsAsync(List<string> modelNames)
    {
        foreach (var name in modelNames)
        {
            if (!_pendingPulls.Contains(name))
                _pendingPulls.Add(name);
        }
        await TrackPullProgressAsync();
    }

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

    #region Options

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        if (ModelCombo.SelectedItem is string display && !string.IsNullOrEmpty(display))
        {
            var model = StripFitSuffix(display);
            AppSettings.SetDefaultModel("Ollama", model);
            UpdateSubHeader();

            var preset = FindPreset();
            if (preset is not null)
                preset.ModelId = model;
            PersistPresets();
        }
    }

    private void PopulatePdfCombo()
    {
        PdfCombo.Items.Clear();
        foreach (var (_, labelKey) in PdfOptions)
            PdfCombo.Items.Add(L(labelKey));
        var saved = FindPreset()?.PdfCapability ?? PdfCapability.Auto;
        var idx = Array.FindIndex(PdfOptions, o => o.Value == saved);
        PdfCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void PopulateMaxTokens()
    {
        var preset = FindPreset();
        if (preset is not null)
            MaxTokensBox.Value = preset.MaxTokens;
    }

    private void PopulateOllamaOptions()
    {
        var preset = FindPreset();
        if (preset is not null)
        {
            NumCtxBox.Value = preset.NumCtx ?? 8192;
            KeepAliveBox.Text = preset.KeepAlive ?? "5m";
        }
    }

    private void PopulateThinkingToggle()
    {
        ThinkingOverrideCombo.Items.Clear();
        foreach (var (_, labelKey) in ThinkingOverrideOptions)
            ThinkingOverrideCombo.Items.Add(L(labelKey));

        var preset = FindPreset();
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

    private void OnPdfHandlingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        var preset = FindPreset();
        if (preset is not null)
        {
            var idx = PdfCombo.SelectedIndex;
            preset.PdfCapability = idx >= 0 && idx < PdfOptions.Length ? PdfOptions[idx].Value : PdfCapability.Auto;
        }
        PersistPresets();
    }

    private void OnMaxTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating || double.IsNaN(args.NewValue)) return;
        var preset = FindPreset();
        if (preset is not null)
            preset.MaxTokens = (int)args.NewValue;
        PersistPresets();
    }

    private void OnNumCtxChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating || double.IsNaN(args.NewValue)) return;
        var preset = FindPreset();
        if (preset is not null)
            preset.NumCtx = (int)args.NewValue;
        PersistPresets();
    }

    private void OnKeepAliveChanged(object sender, TextChangedEventArgs args)
    {
        if (_isPopulating) return;
        var preset = FindPreset();
        if (preset is not null)
        {
            var text = KeepAliveBox.Text.Trim();
            preset.KeepAlive = string.IsNullOrEmpty(text) ? null : text;
        }
        PersistPresets();
    }

    private void OnThinkingToggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulating || !ThinkingToggle.IsEnabled) return;
        ThinkingBudgetBox.IsEnabled = ThinkingToggle.IsOn;

        var preset = FindPreset();
        if (preset is not null)
        {
            preset.ThinkingEnabled = ThinkingToggle.IsOn;
            preset.ThinkingBudget = ThinkingToggle.IsOn && !double.IsNaN(ThinkingBudgetBox.Value)
                ? (int)ThinkingBudgetBox.Value : null;
        }
        PersistPresets();
    }

    private void OnThinkingOverrideChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating) return;
        _isPopulating = true;
        UpdateThinkingState();
        _isPopulating = false;

        var preset = FindPreset();
        if (preset is not null)
        {
            var idx = ThinkingOverrideCombo.SelectedIndex;
            preset.ThinkingOverride = idx >= 0 && idx < ThinkingOverrideOptions.Length
                ? ThinkingOverrideOptions[idx].Value : ThinkingOverride.Auto;
            preset.ThinkingEnabled = ThinkingToggle.IsOn;
            preset.ThinkingBudget = ThinkingToggle.IsOn && !double.IsNaN(ThinkingBudgetBox.Value)
                ? (int)ThinkingBudgetBox.Value : null;
        }
        PersistPresets();
    }

    private void OnThinkingBudgetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isPopulating) return;
        var preset = FindPreset();
        if (preset is not null)
            preset.ThinkingBudget = !double.IsNaN(sender.Value) ? (int)sender.Value : null;
        PersistPresets();
    }

    private void UpdateThinkingState()
    {
        var modelId = ModelCombo.SelectedItem is string display ? StripFitSuffix(display) : null;
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

    private ProviderPreset? FindPreset()
        => _presets.FirstOrDefault(p => p.ProviderType == "Ollama");

    private void PersistPresets() => AppSettings.SavePresets(_presets);

    private void UpdateSubHeader()
    {
        var display = ModelCombo.SelectedItem as string ?? "";
        var model = StripFitSuffix(display);
        var status = StatusText.Text;

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(model)) parts.Add(model);
        if (!string.IsNullOrEmpty(status)) parts.Add(status);
        SubHeaderChanged?.Invoke(this, string.Join(" \u00B7 ", parts));
    }

    private static string StripFitSuffix(string display)
    {
        var idx = display.IndexOf("  (", StringComparison.Ordinal);
        return idx >= 0 ? display[..idx] : display;
    }

    private static readonly ResourceLoader Res = new();

    private static string L(string key) =>
        Res.GetString(key) is { Length: > 0 } value ? value : key;

    #endregion
}
