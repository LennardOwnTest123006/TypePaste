using System.Windows;
using TypePaste.Services;

namespace TypePaste;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirstInstance)
        {
            // TypePaste is already running: hand over the command line (e.g. a file to load) and exit.
            return SingleInstance.Forward(args) ? 0 : 1;
        }

        var options = CommandLineOptions.Parse(args);
        var store = new SettingsStore();
        var settings = store.Load();

        SplashScreen? splash = null;
        if (!options.StartInTray && !settings.StartMinimizedToTray)
        {
            try
            {
                splash = new SplashScreen(typeof(Program).Assembly, "Assets/Splash.png");
                splash.Show(autoClose: false, topMost: false);
            }
            catch (Exception ex)
            {
                App.Log.Warn($"Splash screen unavailable: {ex.Message}");
                splash = null;
            }
        }

        var app = new App(options, store, settings, splash);
        app.InitializeComponent();
        return app.Run();
    }
}
