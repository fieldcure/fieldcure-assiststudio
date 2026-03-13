using FluentView.AI.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;


namespace AssistView.Studio.Settings;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

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
}
