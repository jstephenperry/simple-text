using Avalonia;
using Velopack;

namespace SimpleText.Avalonia;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack hook handling must run before anything else: on install/update/uninstall it
        // performs the requested action and exits before the UI starts. A no-op on a normal launch.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
