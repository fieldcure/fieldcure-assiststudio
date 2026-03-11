using Microsoft.UI.Xaml;

namespace FluentView.AI.SampleApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ChatPanel.Provider = new MockProvider();
    }
}
