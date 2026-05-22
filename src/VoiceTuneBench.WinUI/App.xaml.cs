using Microsoft.UI.Xaml;

namespace VoiceTuneBench.WinUI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        new MainWindow().Activate();
    }
}
