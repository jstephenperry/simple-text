using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SimpleText.Avalonia.Services;
using SimpleText.Avalonia.Views;

namespace SimpleText.Avalonia;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var savedTheme = ThemeService.LoadPreference();
        if (savedTheme != null)
            RequestedThemeVariant = savedTheme;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // Restore the multi-tab workspace session, then open the optional CLI file as an
            // extra tab. The window runs this on Loaded (visual tree ready); keep it crash-safe.
            var commandLineFile = desktop.Args is { Length: > 0 } args && File.Exists(args[0])
                ? args[0]
                : null;
            window.QueueStartup(commandLineFile);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
