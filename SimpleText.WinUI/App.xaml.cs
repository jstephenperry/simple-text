using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace SimpleText.WinUI;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnAppUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        Window = window;

        window.ApplyThemePreference(LoadThemePreference());

        try
        {
            window.RestoreWorkspaceSession();
        }
        catch
        {
            // A bad session file must never crash startup.
            window.EnsureFallbackTab();
        }

        // CLI file argument: open after session restore as an additional tab
        // (or activate the already-restored tab with that path) and focus it.
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 1 && File.Exists(commandLine[1]))
            window.OpenFileAtStartup(commandLine[1]);

        window.Activate();
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // An unobserved async-void failure must not kill unsaved work: keep the app
        // alive and persist the session best-effort.
        e.Handled = true;
        try
        {
            Window?.SaveWorkspaceSession();
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Called (on an arbitrary thread) when a second instance redirects its activation
    /// here. Extracts the file argument, then hops to the UI thread to open it and
    /// bring the window to the foreground.
    /// </summary>
    public static void OnRedirectedActivation(AppActivationArguments args)
    {
        var window = Window;
        if (window == null)
            return;

        string? path = null;
        if (args.Kind == ExtendedActivationKind.Launch
            && args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
        {
            path = ExtractFilePath(launch.Arguments);
        }

        window.DispatcherQueue.TryEnqueue(() => window.HandleRedirectedActivation(path));
    }

    /// <summary>
    /// Returns the first command-line token that names an existing file, honoring quoted
    /// paths and skipping the executable itself (argv[0] in a launch command line).
    /// </summary>
    private static string? ExtractFilePath(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return null;

        var exePath = Environment.ProcessPath;
        foreach (var token in TokenizeCommandLine(arguments))
        {
            try
            {
                if (!File.Exists(token))
                    continue;
                var fullPath = Path.GetFullPath(token);
                if (string.Equals(fullPath, exePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                return fullPath;
            }
            catch
            {
                // not a usable path token
            }
        }
        return null;
    }

    private static IEnumerable<string> TokenizeCommandLine(string commandLine)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
            yield return current.ToString();
    }

    // Local adapter around the ThemeService contract so any signature drift is a one-line fix.
    private static string? LoadThemePreference()
    {
        try
        {
            return Services.ThemeService.LoadPreference();
        }
        catch
        {
            return null;
        }
    }
}
