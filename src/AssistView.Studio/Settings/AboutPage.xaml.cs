using FluentView.AI.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Reflection;

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

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is not null
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : "v0.0.0";

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
