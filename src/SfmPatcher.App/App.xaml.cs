using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ModernSfmPatcher.App;
public partial class App : Application
{
    private Window? _window;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint window, string text, string caption, uint type);

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        UnhandledException += (_, e) =>
        {
            MessageBoxW(0, e.Message, "Modern SFM Patcher startup error", 0x10);
        };

        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            MessageBoxW(0, ex.ToString(), "Modern SFM Patcher startup error", 0x10);
        }
    }
}
