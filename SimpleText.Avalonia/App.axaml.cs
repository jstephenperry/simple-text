using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SimpleText.Avalonia.Services;
using SimpleText.Avalonia.Views;
using SimpleText.Core.Storage;
using SimpleText.Core.Templates;

namespace SimpleText.Avalonia;

public class App : Application
{
    public override void Initialize()
    {
        ConfigureStorage();
        AvaloniaXamlLoader.Load(this);

        var savedTheme = ThemeService.LoadPreference();
        if (savedTheme != null)
            RequestedThemeVariant = savedTheme;
    }

    /// <summary>
    /// Resolves storage roots and seeds the default templates on first run. The
    /// cross-platform Avalonia build keeps the historical <c>%LocalAppData%/SimpleText</c>
    /// layout (the packaged WinUI build redirects these to package-private state and the
    /// user's Documents folder instead). Must run before any session, theme, or template
    /// access.
    /// </summary>
    private static void ConfigureStorage()
    {
        var state = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpleText");
        var templates = Path.Combine(state, "Templates");

        AppStorage.Configure(state, templates);
        TemplateSeeder.EnsureSeeded(
            Path.Combine(AppContext.BaseDirectory, "Templates", "Defaults"),
            templates,
            Path.Combine(state, TemplateSeeder.MarkerFileName));
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
