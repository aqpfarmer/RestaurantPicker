using Microsoft.UI.Xaml;

namespace RestaurantPicker;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        SQLitePCL.Batteries_V2.Init();
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
