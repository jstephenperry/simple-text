using SimpleText.Core.FileTypes;

namespace SimpleText.Core.Templates;

/// <summary>
/// The live set of document templates shown in the UI: the embedded built-ins
/// plus any user-supplied templates discovered under
/// <c>%LocalAppData%/SimpleText/Templates/</c>.
///
/// <para>User templates auto-register by convention — drop a text file in that
/// folder and it appears in the menu. The file name (without extension) becomes
/// the template's <see cref="DocumentTemplate.Variant"/>; an immediate
/// sub-folder name becomes its <see cref="DocumentTemplate.Category"/> (files
/// placed directly in the root fall under "<see cref="DefaultUserCategory"/>");
/// and the extension determines the editor mode via
/// <see cref="ModeDetector"/>.</para>
///
/// <para>The folder is watched, so adding, editing, or removing a file raises
/// <see cref="Changed"/> without an app restart. User templates are always shown
/// <em>alongside</em> built-ins; they never override them.</para>
///
/// <para><see cref="Changed"/> is raised on a background thread — UI subscribers
/// must marshal to their dispatcher before touching the UI.</para>
/// </summary>
public sealed class TemplateCatalog : IDisposable
{
    /// <summary>Category assigned to user templates placed directly in the root folder.</summary>
    public const string DefaultUserCategory = "My Templates";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".rst", ".adoc", ".asciidoc",
    };

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Process-wide shared catalog used by the application frontends.</summary>
    public static TemplateCatalog Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Timer _debounce;
    private FileSystemWatcher? _watcher;
    private IReadOnlyList<DocumentTemplate> _all = DocumentTemplates.BuiltIns;

    public TemplateCatalog() : this(DefaultUserTemplatesDirectory())
    {
    }

    public TemplateCatalog(string userTemplatesDirectory)
    {
        UserTemplatesDirectory = userTemplatesDirectory;
        _debounce = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);

        Reload();
        StartWatching();
    }

    /// <summary>Raised after the catalog reloads. May fire on a background thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The folder users drop templates into. Created on first use.</summary>
    public string UserTemplatesDirectory { get; }

    /// <summary>Current snapshot: built-ins first, then user templates.</summary>
    public IReadOnlyList<DocumentTemplate> All
    {
        get { lock (_gate) return _all; }
    }

    private static string DefaultUserTemplatesDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SimpleText",
        "Templates");

    /// <summary>Rebuild the merged catalog from disk and notify subscribers.</summary>
    public void Reload()
    {
        var merged = new List<DocumentTemplate>(DocumentTemplates.BuiltIns);
        merged.AddRange(LoadUserTemplates());

        lock (_gate)
            _all = merged;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<DocumentTemplate> LoadUserTemplates()
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
                DocumentTemplates.StripTrailingNewline(content)));
        }

        return templates
            .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Variant, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Immediate sub-folder name, or <see cref="DefaultUserCategory"/> for files in the root.</summary>
    private static string DeriveCategory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separator < 0 ? DefaultUserCategory : relative[..separator];
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
