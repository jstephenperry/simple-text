using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.TextMate;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Formatting;
using SimpleText.Core.Highlighting;
using SimpleText.Core.Search;
using SimpleText.Core.Session;
using TextMateSharp.Grammars;

namespace SimpleText.Avalonia.Views;

/// <summary>
/// Per-document editor surface: a single AvaloniaEdit <c>TextEditor</c> with its own
/// TextMate installation and <see cref="SimpleTextRegistryOptions"/> instance, plus dirty
/// tracking, caret reporting, find, word wrap, zoom, theme, and session capture/restore.
/// One instance lives per tab; the host (MainWindow) drives file ops and chrome.
/// </summary>
public partial class EditorView : UserControl
{
    private const int DefaultFontSize = 14;
    private const int MinFontSize = 8;
    private const int MaxFontSize = 32;

    private string? _filePath;
    private string? _originalFileHash;
    private string? _mode;
    private bool _isDirty;

    // AvaloniaEdit raises Document.TextChanged synchronously when Text is assigned
    // programmatically. Programmatic edits (SetText/LoadFromFile/RestoreFromSession) set this
    // guard so the change updates state without marking the document dirty.
    private bool _suppressDirty;

    private TextMate.Installation? _textMateInstallation;
    private SimpleTextRegistryOptions? _registryOptions;
    private ThemeVariant _currentVariant = ThemeVariant.Default;

    // --- Frozen public contract ---

    public string? FilePath => _filePath;

    public string? Mode => _mode;

    public bool IsDirty => _isDirty;

    public string FileName => _filePath != null ? Path.GetFileName(_filePath) : "Untitled";

    public string FileTypeName => HighlighterRegistry.FileTypeNameForMode(_mode);

    /// <summary>Raised only when <see cref="IsDirty"/> flips value.</summary>
    public event EventHandler? DirtyChanged;

    /// <summary>Raised when the caret or selection moves.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Raised after text changes (user or programmatic), once dirty handling is done.</summary>
    public event EventHandler? DocumentChanged;

    public EditorView()
    {
        InitializeComponent();

        // Resolve a sane default theme from the ambient application variant; the host
        // calls ApplyTheme later to override.
        _currentVariant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;

        Editor.Document.TextChanged += (_, _) => OnDocumentTextChanged();
        Editor.TextArea.Caret.PositionChanged += (_, _) => CaretMoved?.Invoke(this, EventArgs.Empty);

        // Install TextMate once the control is attached to the visual tree.
        Loaded += (_, _) => EnsureTextMateInstalled();
    }

    // --- TextMate setup ---

    private void EnsureTextMateInstalled()
    {
        if (_textMateInstallation != null) return;

        var themeName = ResolveThemeName(_currentVariant);
        _registryOptions = new SimpleTextRegistryOptions(themeName);
        _textMateInstallation = Editor.InstallTextMate(_registryOptions);

        // Explicitly apply theme and any mode chosen before the control loaded.
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(themeName));
        _textMateInstallation.SetGrammar(SimpleTextRegistryOptions.ScopeForMode(_mode));
    }

    private ThemeName ResolveThemeName(ThemeVariant variant)
    {
        var resolved = variant;
        if (resolved == ThemeVariant.Default)
            resolved = Application.Current?.ActualThemeVariant ?? ThemeVariant.Light;
        return resolved == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus;
    }

    // --- Text plumbing ---

    public string GetText() => Editor.Document.Text;

    public void SetText(string text, bool markDirty)
    {
        // Always suppress the synchronous Document.TextChanged so SetText is the single source
        // of the dirty update and DocumentChanged event (the inner handler would otherwise also
        // raise both, double-firing on the markDirty path).
        _suppressDirty = true;
        try
        {
            Editor.Document.Text = text;
        }
        finally
        {
            _suppressDirty = false;
        }

        SetDirty(markDirty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public int CaretOffset
    {
        get => Editor.TextArea.Caret.Offset;
        set
        {
            int clamped = Math.Clamp(value, 0, Editor.Document.TextLength);
            Editor.TextArea.Caret.Offset = clamped;
            var line = Editor.Document.GetLineByOffset(clamped);
            Editor.ScrollTo(line.LineNumber, 0);
        }
    }

    /// <summary>1-based caret line and column (AvaloniaEdit reports these 1-based already).</summary>
    public (int Line, int Column) GetCaretLineColumn()
        => (Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);

    /// <summary>
    /// Inserts <paramref name="body"/> at the caret (replacing any selection) and moves the
    /// caret to <paramref name="caretWithinBody"/> within the inserted text. Counts as a normal
    /// edit (marks the document dirty and re-highlights).
    /// </summary>
    public void InsertElement(string body, int caretWithinBody)
    {
        int start;
        if (Editor.SelectionLength > 0)
        {
            start = Editor.SelectionStart;
            Editor.Document.Replace(start, Editor.SelectionLength, body);
        }
        else
        {
            start = Editor.TextArea.Caret.Offset;
            Editor.Document.Insert(start, body);
        }

        int target = Math.Clamp(start + caretWithinBody, 0, Editor.Document.TextLength);
        Editor.Select(target, 0); // collapse any selection to the target caret
        Editor.TextArea.Caret.Offset = target;
        var line = Editor.Document.GetLineByOffset(target);
        Editor.ScrollTo(line.LineNumber, 0);
        FocusEditor();
    }

    /// <summary>
    /// Re-aligns the table under the caret (per the active mode) so its columns line up in
    /// the source text. Returns <c>false</c> when the caret is not inside a table the mode
    /// can align; otherwise counts as a normal edit (marks the document dirty, re-highlights).
    /// </summary>
    public bool ReformatTable()
    {
        if (TableFormatter.Format(GetText(), CaretOffset, _mode) is not { } edit)
            return false;
        Editor.Document.Replace(edit.Start, edit.Length, edit.Text);
        CaretOffset = edit.CaretOffset;
        FocusEditor();
        return true;
    }

    // --- File operations ---

    public void LoadFromFile(string path)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        _filePath = path;
        _originalFileHash = SessionManager.ComputeFileHash(path);
        SetText(content, markDirty: false);
        SetMode(ModeDetector.DetectFromPath(path));
        CaretOffset = 0;
    }

    public void SaveToFile(string path)
    {
        // Re-detect the mode only when the path actually changed (Save As); a plain re-save to
        // the same path must preserve a manually chosen mode.
        bool pathChanged = !string.Equals(_filePath, path, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, GetText(), new UTF8Encoding(false));
        _filePath = path;
        _originalFileHash = SessionManager.ComputeFileHash(path);
        SetDirty(false);
        if (pathChanged) SetMode(ModeDetector.DetectFromPath(path));
    }

    // --- Session ---

    public SessionData CaptureSession() => new()
    {
        FilePath = _filePath,
        Content = GetText(),
        CursorPosition = CaretOffset,
        IsDirty = _isDirty,
        OriginalFileHash = _originalFileHash,
        Mode = _mode,
    };

    public void RestoreFromSession(SessionData data)
    {
        _filePath = data.FilePath;
        _originalFileHash = data.OriginalFileHash;
        SetText(data.Content ?? string.Empty, markDirty: false);
        SetDirty(data.IsDirty);
        SetMode(data.Mode);
        CaretOffset = Math.Clamp(data.CursorPosition, 0, Editor.Document.TextLength);
    }

    // --- Mode / highlighting ---

    public void SetMode(string? mode)
    {
        _mode = mode;
        _textMateInstallation?.SetGrammar(SimpleTextRegistryOptions.ScopeForMode(mode));
    }

    public void ApplyTheme(ThemeVariant variant)
    {
        _currentVariant = variant;
        if (_textMateInstallation == null || _registryOptions == null) return;
        var themeName = ResolveThemeName(variant);
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(themeName));
    }

    // --- Find ---

    public bool FindNext(string term)
    {
        var text = GetText();
        // Resume from the end of the current selection (or the caret) so repeats walk forward.
        int start = Editor.SelectionLength > 0
            ? Editor.SelectionStart + Editor.SelectionLength
            : Editor.TextArea.Caret.Offset;
        start = Math.Clamp(start, 0, text.Length);

        if (TextFinder.FindNext(text, term, start) is not { } index) return false;
        SelectMatch(index, term.Length, caretAtEnd: true);
        return true;
    }

    public bool FindPrevious(string term)
    {
        var text = GetText();
        // Resume from the start of the current selection so repeats walk backward.
        int start = Editor.SelectionLength > 0
            ? Editor.SelectionStart
            : Editor.TextArea.Caret.Offset;
        start = Math.Clamp(start, 0, text.Length);

        if (TextFinder.FindPrevious(text, term, start) is not { } index) return false;
        SelectMatch(index, term.Length, caretAtEnd: false);
        return true;
    }

    public string GetSelectedText() => Editor.SelectedText;

    private void SelectMatch(int index, int length, bool caretAtEnd)
    {
        Editor.Select(index, length);
        Editor.TextArea.Caret.Offset = caretAtEnd ? index + length : index;
        var line = Editor.Document.GetLineByOffset(index);
        Editor.ScrollTo(line.LineNumber, 0);
    }

    // --- View options ---

    public void SetWordWrap(bool enabled) => Editor.WordWrap = enabled;

    /// <summary>Adjusts font size by <paramref name="delta"/> (clamped 8..32); 0 resets to 14.</summary>
    public void AdjustFontSize(int delta)
    {
        double target = delta == 0
            ? DefaultFontSize
            : Math.Clamp(Editor.FontSize + delta, MinFontSize, MaxFontSize);
        Editor.FontSize = target;
    }

    public void FocusEditor()
    {
        // A control just added to a freshly selected tab is not in the visual tree yet, so
        // Focus() silently no-ops and keystrokes go nowhere. Defer until the editor loads.
        if (Editor.IsLoaded)
        {
            FocusTextArea();
            return;
        }

        EventHandler<global::Avalonia.Interactivity.RoutedEventArgs>? once = null;
        once = (_, _) =>
        {
            Editor.Loaded -= once;
            FocusTextArea();
        };
        Editor.Loaded += once;
    }

    private void FocusTextArea()
    {
        // Focus the TextArea (the actual keyboard-input element) rather than the TextEditor
        // wrapper, and post it so it runs after the current layout/activation pass — otherwise
        // initial focus can land on the menu bar and keystrokes are dropped.
        global::Avalonia.Threading.Dispatcher.UIThread.Post(
            () => Editor.TextArea.Focus(),
            global::Avalonia.Threading.DispatcherPriority.Input);
    }

    // --- Internal: dirty tracking ---

    private void OnDocumentTextChanged()
    {
        if (!_suppressDirty)
        {
            SetDirty(true);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }
}
