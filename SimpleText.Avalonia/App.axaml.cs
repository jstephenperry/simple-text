using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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

            if (desktop.Args?.Length > 0 && File.Exists(desktop.Args[0]))
                window.OpenFileOnLoad(desktop.Args[0]);
            else
                window.RestoreSessionOnLoad();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
