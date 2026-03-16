using AssistStudio.Modules.Helpers;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Dialogs;

/// <summary>
/// First-run wizard dialog that guides the user through initial hardware detection,
/// provider selection (local vs. cloud), and optional Ollama model setup.
/// </summary>
public sealed partial class FirstRunDialog : ContentDialog
{
    #region Fields

    /// <summary>
    /// Detected hardware specifications used for model compatibility checks.
    /// </summary>
    private readonly HardwareSpec _hw;

    /// <summary>
    /// The current wizard step number (1-based).
    /// </summary>
    private int _currentStep = 1;

    /// <summary>
    /// The Ollama model ID recommended based on the detected hardware.
    /// </summary>
    private string? _recommendedModel;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FirstRunDialog"/> class,
    /// detects hardware, and displays GPU/RAM information.
    /// </summary>
    public FirstRunDialog()
    {
        InitializeComponent();
        _hw = HardwareInfo.Detect();

        HwGpuText.Text = $"GPU: {_hw.GpuName} ({_hw.VramDisplay} VRAM)";
        HwRamText.Text = $"RAM: {_hw.RamDisplay}";

        PrimaryButtonText = "Next";
        PrimaryButtonClick += OnPrimaryButton;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the primary button click to advance through wizard steps.
    /// </summary>
    private void OnPrimaryButton(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Prevent dialog from closing

        switch (_currentStep)
        {
            case 1:
                ShowStep2();
                break;
            case 2:
                ShowStep3();
                break;
            case 3:
                Hide();
                break;
        }
    }

    /// <summary>
    /// Handles the download model button click to pull the recommended Ollama model.
    /// </summary>
    private async void OnDownloadModel(object sender, RoutedEventArgs e)
    {
        if (_recommendedModel is null) return;

        DownloadModelButton.IsEnabled = false;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadStatus.Text = $"Pulling {_recommendedModel}...";
        DownloadProgress.Value = 0;

        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                DownloadStatus.Text = p.Status;
                DownloadProgress.Value = p.Percent * 100;
            });
        });

        try
        {
            using var manager = new OllamaModelManager();
            await manager.DownloadModelAsync(_recommendedModel, progress);
            DownloadStatus.Text = "\u2705 Download complete!";
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            DownloadStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Transitions to step 2 of the wizard, showing the provider selection (local vs. cloud).
    /// </summary>
    private void ShowStep2()
    {
        _currentStep = 2;
        Step1Panel.Visibility = Visibility.Collapsed;
        Step2Panel.Visibility = Visibility.Visible;
        RadioLocal.IsChecked = true;
    }

    /// <summary>
    /// Transitions to step 3 of the wizard, showing either the Ollama setup or cloud instructions.
    /// </summary>
    private async void ShowStep3()
    {
        _currentStep = 3;
        Step2Panel.Visibility = Visibility.Collapsed;
        PrimaryButtonText = "Done";

        if (RadioLocal.IsChecked == true)
        {
            Step3Panel.Visibility = Visibility.Visible;
            await SetupOllamaAsync();
        }
        else
        {
            Step3CloudPanel.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Checks Ollama installation and running status, then recommends a model if none are installed.
    /// </summary>
    private async Task SetupOllamaAsync()
    {
        OllamaSetupSpinner.Visibility = Visibility.Visible;
        OllamaSetupSpinner.IsActive = true;
        OllamaSetupStatus.Text = "Checking Ollama...";

        // Check if installed
        if (!OllamaHelper.IsOllamaInstalled())
        {
            OllamaSetupSpinner.Visibility = Visibility.Collapsed;
            OllamaSetupSpinner.IsActive = false;
            OllamaSetupStatus.Text = "";
            OllamaInstallPanel.Visibility = Visibility.Visible;
            return;
        }

        // Check if running, try to start if not
        var isRunning = await OllamaHelper.IsOllamaRunningAsync();
        if (!isRunning)
        {
            OllamaSetupStatus.Text = "Starting Ollama...";
            isRunning = await OllamaHelper.StartOllamaAsync();
        }

        OllamaSetupSpinner.Visibility = Visibility.Collapsed;
        OllamaSetupSpinner.IsActive = false;

        if (isRunning)
        {
            OllamaSetupStatus.Text = "\u2705 Ollama is running";

            // Check if any models installed
            try
            {
                using var manager = new OllamaModelManager();
                var localModels = await manager.ListLocalModelsAsync();
                if (localModels.Count > 0)
                {
                    OllamaSetupStatus.Text = $"\u2705 Ollama is running ({localModels.Count} model(s) installed)";
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex);
            }

            // Recommend a model based on hardware
            _recommendedModel = PickRecommendedModel();
            if (_recommendedModel is not null)
            {
                RecommendedModelText.Text = _recommendedModel;
                ModelRecommendPanel.Visibility = Visibility.Visible;
            }
        }
        else
        {
            OllamaSetupStatus.Text = "\u274C Could not start Ollama";
        }
    }

    /// <summary>
    /// Selects the best Ollama model that fits the detected hardware capabilities.
    /// </summary>
    /// <returns>The model ID to recommend, or a fallback model if no ideal match is found.</returns>
    private string? PickRecommendedModel()
    {
        // Pick the best model that fits the hardware
        var candidates = new[]
        {
            ("phi4", "Microsoft Phi-4 (14B)"),
            ("gemma2", "Google Gemma 2 (9B)"),
            ("llama3.1", "Meta Llama 3.1 (8B)"),
            ("qwen2.5", "Alibaba Qwen 2.5 (7B)"),
            ("mistral", "Mistral 7B"),
        };

        foreach (var (id, _) in candidates)
        {
            var level = ModelCompatibility.CheckByModelName(id, _hw);
            if (level == CompatibilityLevel.Compatible)
                return id;
        }

        // Fallback: pick smallest model even if not ideal
        foreach (var (id, _) in candidates.Reverse())
        {
            var level = ModelCompatibility.CheckByModelName(id, _hw);
            if (level != CompatibilityLevel.NotCompatible)
                return id;
        }

        return "qwen2.5"; // Smallest as last resort
    }

    #endregion
}
