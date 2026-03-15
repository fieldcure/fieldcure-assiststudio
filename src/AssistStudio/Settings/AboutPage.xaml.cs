using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page that displays application version information and detected hardware specifications.
/// </summary>
public sealed partial class AboutPage : Page
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutPage"/> class.
    /// </summary>
    public AboutPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var package = Windows.ApplicationModel.Package.Current;
        AppNameText.Text = package.DisplayName;
        var pv = package.Id.Version;
        VersionText.Text = $"v{pv.Major}.{pv.Minor}.{pv.Build}";

        await Task.Run(() => HardwareInfo.Detect()).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                var hw = t.Result;
                OsText.Text = hw.OsDisplay;
                GpuText.Text = hw.GpuName;
                VramText.Text = hw.VramDisplay;
                RamText.Text = hw.RamDisplay;
            }
            else
            {
                OsText.Text = "Unknown";
                GpuText.Text = "Unknown";
                VramText.Text = "Unknown";
                RamText.Text = "Unknown";
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    #endregion
}
