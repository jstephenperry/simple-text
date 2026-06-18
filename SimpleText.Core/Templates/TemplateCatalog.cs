using SimpleText.Core.FileTypes;
using SimpleText.Core.Storage;

namespace SimpleText.Core.Templates;

/// <summary>
/// The live set of document templates shown in the UI: every template is a file
/// discovered under the user-owned templates folder
/// (<see cref="AppStorage.TemplatesDirectory"/>). The shipped defaults are copied
/// there once by <see cref="TemplateSeeder"/>; from then on the folder is the
/// single source of truth and is entirely the user's.
///
/// <para>Templates auto-register by convention — drop a text file in the folder
/// and it appears in the menu. The file name (without extension) becomes the
/// template's <see cref="DocumentTemplate.Variant"/>; an immediate sub-folder
/// name becomes its <see cref="DocumentTemplate.Category"/> (files placed
/// directly in the root fall under "<see cref="DefaultUserCategory"/>"); and the
/// extension determines the editor mode via <see cref="ModeDetector"/>.</para>
///
/// <para>The folder is watched, so adding, editing, or removing a file raises
/// <see cref="Changed"/> without an app restart.</para>
///
/// <para><see cref="Changed"/> is raised on a background thread — UI subscribers
/// must marshal to their dispatcher before touching the UI.</para>
/// </summary>
public sealed class TemplateCatalog : IDisposable
{
    /// <summary>Category assigned to templates placed directly in the root folder.</summary>
    public const string DefaultUserCategory = "My Templates";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".rst", ".adoc", ".asciidoc",
    };

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private static TemplateCatalog? _shared;

    /// <summary>
    /// Process-wide shared catalog used by the application frontends, rooted at
    /// <see cref="AppStorage.TemplatesDirectory"/>. Configure storage (and seed
    /// defaults) before first access.
    /// </summary>
    public static TemplateCatalog Shared => _shared ??= new TemplateCatalog(AppStorage.TemplatesDirectory);

    private readonly object _gate = new();
    private readonly Timer _debounce;
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<DocumentTemplate> _all = [];

    public TemplateCatalog(string templatesDirectory)
    {
        UserTemplatesDirectory = templatesDirectory;
        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);

        Reload();
        StartWatching();
    }

    /// <summary>Raised after the catalog reloads. May fire on a background thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The folder users drop templates into. Created on first use.</summary>
    public string UserTemplatesDirectory { get; }

    /// <summary>Current snapshot, ordered by category then variant.</summary>
    public IReadOnlyList<DocumentTemplate> All
    {
        get { lock (_gate) return _all; }
    }

    /// <summary>Rebuild the catalog from disk and notify subscribers.</summary>
    public void Reload()
    {
        var templates = LoadTemplates();

        lock (_gate)
            _all = templates;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<DocumentTemplate> LoadTemplates()
    {
        string root = UserTemplatesDirectory;
        if (!Directory.Exists(root))
            return [];

        var templates = new List<DocumentTemplate>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (IOException)
        {
            return [];
        }

        foreach (var file in files)
        {
            if (!AllowedExtensions.Contains(Path.GetExtension(file)))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue; // skip locked/unreadable files; a later change re-triggers a reload
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var variant = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(variant))
                continue;

            templates.Add(new DocumentTemplate(
                DeriveCategory(root, file),
                variant,
                ModeDetector.DetectFromPath(file),
                StripTrailingNewline(content)));
        }

        return templates
            .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Variant, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DeriveCategory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var dir = Path.GetDirectoryName(relative);
        return string.IsNullOrEmpty(dir) ? DefaultUserCategory : dir;
    }

    /// <summary>Drop the single conventional end-of-file newline so applied templates have no trailing blank line.</summary>
    private static string StripTrailingNewline(string text)
    {
        if (text.EndsWith('\n'))
            text = text[..^1];
        if (text.EndsWith('\r'))
            text = text[..^1];
        return text;
    }

    private void StartWatching()
    {
        try
        {
            Directory.CreateDirectory(UserTemplatesDirectory);

            _watcher = new FileSystemWatcher(UserTemplatesDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };
            _watcher.Created += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // No live reload on this platform/environment; templates loaded at startup still work.
            _watcher = null;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        => _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
