namespace SimpleText.Core.Templates;

/// <summary>
/// Lays the shipped default templates down into the user-owned templates folder
/// on first run, then gets out of the way.
///
/// <para>The application no longer keeps an in-binary catalog of built-ins:
/// the defaults ship as ordinary files under <c>Templates/Defaults/</c> (next to
/// the executable) and are <em>copied</em> into the user's templates folder once.
/// After that the folder is entirely the user's — edits, additions, and
/// deletions are honored and never overwritten or resurrected.</para>
///
/// <para>"Once" is tracked by a marker file kept in app-private state (not in the
/// templates folder), so a clean install seeds and a deliberate emptying of the
/// folder is respected. Seeding is best-effort: any I/O failure leaves the app
/// running normally with whatever templates already exist.</para>
/// </summary>
public static class TemplateSeeder
{
    /// <summary>Marker file name written to the state directory after a successful seed.</summary>
    public const string MarkerFileName = ".templates-seeded";

    /// <summary>
    /// Copies the default templates from <paramref name="defaultsDirectory"/> into
    /// <paramref name="templatesDirectory"/> unless the marker already exists.
    /// Existing files are never overwritten. Returns the number of files copied
    /// (0 if already seeded, the source is absent, or seeding failed).
    /// </summary>
    /// <param name="defaultsDirectory">
    /// Source tree of shipped defaults, e.g.
    /// <c>Path.Combine(AppContext.BaseDirectory, "Templates", "Defaults")</c>.
    /// </param>
    /// <param name="templatesDirectory">The user-owned templates folder to seed.</param>
    /// <param name="markerPath">Full path of the seed marker in app-private state.</param>
    public static int EnsureSeeded(string defaultsDirectory, string templatesDirectory, string markerPath)
    {
        try
        {
            if (File.Exists(markerPath))
                return 0;

            int copied = 0;
            if (Directory.Exists(defaultsDirectory))
            {
                foreach (var source in Directory.EnumerateFiles(defaultsDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(defaultsDirectory, source);
                    var destination = Path.Combine(templatesDirectory, relative);

                    if (File.Exists(destination))
                        continue; // never clobber the user's copy

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination);
                    copied++;
                }
            }

            // Mark as seeded even if the source was missing, so we do not retry
            // every launch. A reinstall clears app-private state and re-seeds.
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, DateTimeOffset.UtcNow.ToString("O"));
            return copied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: the app runs fine with whatever is already on disk.
            return 0;
        }
    }
}
