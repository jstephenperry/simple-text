namespace SimpleText.Core.Storage;

/// <summary>
/// The two storage roots the application uses, resolved once at startup by the
/// frontend and shared by the UI-agnostic core.
///
/// <para><see cref="StateDirectory"/> holds app-private state (session and theme
/// files, the template seed marker). <see cref="TemplatesDirectory"/> holds the
/// user-owned document templates.</para>
///
/// <para>Splitting the two matters for the packaged (MSIX / Microsoft Store)
/// build: app state belongs in the package-private store
/// (<c>ApplicationData.Current.LocalFolder</c>), while templates belong in a
/// durable, user-visible location (the user's Documents folder) that survives
/// uninstall and that the user can open and edit directly. The frontend calls
/// <see cref="Configure"/> with the right locations for its packaging model; if
/// it never does, both fall back to <c>%LocalAppData%\SimpleText</c>, preserving
/// the historical unpackaged layout.</para>
///
/// <para>Paths are resolved lazily so <see cref="Configure"/> can run before the
/// first access regardless of static-initialization order.</para>
/// </summary>
public static class AppStorage
{
    private static string? _stateDirectory;
    private static string? _templatesDirectory;

    /// <summary>App-private state root (session, theme, seed marker).</summary>
    public static string StateDirectory => _stateDirectory ??= DefaultStateDirectory();

    /// <summary>User-owned templates root.</summary>
    public static string TemplatesDirectory =>
        _templatesDirectory ??= Path.Combine(StateDirectory, "Templates");

    /// <summary>
    /// Sets the storage roots. Call once during startup, before any session,
    /// theme, or template access.
    /// </summary>
    public static void Configure(string stateDirectory, string templatesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDirectory);
        _stateDirectory = stateDirectory;
        _templatesDirectory = templatesDirectory;
    }

    private static string DefaultStateDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimpleText");
}
