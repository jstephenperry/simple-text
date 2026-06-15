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
        // Resolve storage roots and seed default templates before anything reads them.
        Services.StorageBootstrap.Initialize();

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

        // Open any files this launch was activated with (file-type association or a
        // command-line path) after session restore, as additional tabs.
        foreach (var path in GetStartupFiles())
            window.OpenFileAtStartup(path);

        window.Activate();
    }

    /// <summary>
    /// The files this instance was launched to open. A packaged file-type-association
    /// launch arrives as a <see cref="ExtendedActivationKind.File"/> activation (the
    /// LaunchActivatedEventArgs reports "Launch" unconditionally and cannot be used);
    /// an unpackaged launch may carry a single path on the command line.
    /// </summary>
    private static IEnumerable<string> GetStartupFiles()
    {
        AppActivationArguments? activation = null;
        try
        {
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        }
        catch
        {
            // Activation args are unavailable in some hosts; fall through to the command line.
        }

        if (activation?.Kind == ExtendedActivationKind.File
            && activation.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            foreach (var item in fileArgs.Files)
                if (!string.IsNullOrEmpty(item.Path) && File.Exists(item.Path))
                    yield return item.Path;
            yield break;
        }

        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length > 1 && File.Exists(commandLine[1]))
            yield return commandLine[1];
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

        var paths = new List<string>();
        if (args.Kind == ExtendedActivationKind.File
            && args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            foreach (var item in fileArgs.Files)
                if (!string.IsNullOrEmpty(item.Path) && File.Exists(item.Path))
                    paths.Add(item.Path);
        }
        else if (args.Kind == ExtendedActivationKind.Launch
            && args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            && ExtractFilePath(launch.Arguments) is { } path)
        {
            paths.Add(path);
        }

        window.DispatcherQueue.TryEnqueue(() => window.HandleRedirectedActivation(paths));
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
