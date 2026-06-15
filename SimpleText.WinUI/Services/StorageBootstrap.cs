using SimpleText.Core.Storage;
using SimpleText.Core.Templates;

namespace SimpleText.WinUI.Services;

/// <summary>
/// Resolves the application's storage roots for this packaging model and seeds the
/// default templates on first run. Must run once at startup before any session,
/// theme, or template access.
///
/// <para><b>Packaged (MSIX / Store):</b> app-private state goes to the package's
/// private store (<c>ApplicationData.Current.LocalFolder</c>); user-owned
/// templates go to <c>Documents\SimpleText\Templates</c> — a durable, visible
/// location the user can open and edit, which survives uninstall. A full-trust
/// packaged app reaches Documents through normal Win32 file access, so no extra
/// capability is required.</para>
///
/// <para><b>Unpackaged (dev builds):</b> both stay under
/// <c>%LocalAppData%\SimpleText</c>, the historical layout.</para>
/// </summary>
internal static class StorageBootstrap
{
    public static void Initialize()
    {
        var (state, templates) = ResolveRoots();
        AppStorage.Configure(state, templates);

        TemplateSeeder.EnsureSeeded(
            Path.Combine(AppContext.BaseDirectory, "Templates", "Defaults"),
            templates,
            Path.Combine(state, TemplateSeeder.MarkerFileName));
    }

    private static (string State, string Templates) ResolveRoots()
    {
        if (IsPackaged())
        {
            var state = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var templates = Path.Combine(documents, "SimpleText", "Templates");
            return (state, templates);
        }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpleText");
        return (local, Path.Combine(local, "Templates"));
    }

    /// <summary>
    /// True when running with MSIX package identity. <c>Package.Current</c> throws
    /// (APPMODEL_ERROR_NO_PACKAGE) for unpackaged processes.
    /// </summary>
    private static bool IsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch
        {
            return false;
        }
    }
}
