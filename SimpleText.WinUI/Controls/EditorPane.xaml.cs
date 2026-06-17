using System.Text;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Highlighting;
using SimpleText.Core.Search;
using SimpleText.Core.Session;
using SimpleText.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace SimpleText.WinUI.Controls;

/// <summary>
/// Plain-text editor pane: a RichEditBox text surface with a custom line-number gutter,
/// debounced visible-region syntax highlighting, dirty tracking, find, and session capture.
/// </summary>
public sealed partial class EditorPane : UserControl
{
    private const double DefaultEditorFontSize = 14;
    private const double MinEditorFontSize = 8;
    private const double MaxEditorFontSize = 32;
    private const double GutterPadding = 12;

    private readonly HighlightingService _highlighting;

    /// <summary>Offsets of each line start in the \n-normalized text (always begins with 0).</summary>
    private readonly List<int> _lineStarts = [0];

    private string? _filePath;
    private string? _originalFileHash;
    private string? _mode;
    private bool _isDirty;
    private bool _themeApplied;

    /// <summary>
    /// Last known document content. RichEditBox raises TextChanged asynchronously (after a
    /// suppress flag would already be released) and also for formatting-only changes, so the
    /// reliable filter is comparing content: unchanged text means the event came from a
    /// highlight pass or a programmatic echo and must not mark the pane dirty.
    /// </summary>
    private string _currentText = string.Empty;

    private ScrollViewer? _scrollViewer;
    private EditorPalette _palette = EditorPalette.Light;
    private SolidColorBrush _gutterForegroundBrush;
    private double _lineHeight;
    private double _topInset;
    private double _glyphWidth;
    private int _gutterDigits;

    public string? FilePath => _filePath;

    public string? Mode => _mode;

    public bool IsDirty => _isDirty;

    public string FileName => _filePath != null ? Path.GetFileName(_filePath) : "Untitled";

    public string FileTypeName => HighlighterRegistry.FileTypeNameForMode(_mode);

    public event EventHandler? DirtyChanged;
    public event EventHandler? CaretMoved;
    public event EventHandler? DocumentChanged;

    public EditorPane()
    {
        InitializeComponent();

        _gutterForegroundBrush = new SolidColorBrush(_palette.GutterForeground);
        GutterBorder.Background = new SolidColorBrush(_palette.GutterBackground);

        _highlighting = new HighlightingService(Editor);
        _highlighting.SetPalette(_palette);

        WireEvents();
    }

    private void WireEvents()
    {
        Loaded += (_, _) => OnPaneLoaded();
        SizeChanged += (_, _) => RedrawGutter();

        GutterBorder.SizeChanged += (_, e) =>
            GutterBorder.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
            };

        // The gutter is a sibling of the editor, so a wheel turn over the line numbers never
        // reaches the editor's own ScrollViewer. Forward it so scrolling works over the gutter.
        GutterBorder.PointerWheelChanged += (_, e) =>
        {
            if (_scrollViewer == null) return;
            int delta = e.GetCurrentPoint(GutterBorder).Properties.MouseWheelDelta;
            _scrollViewer.ChangeView(null, _scrollViewer.VerticalOffset - delta, null, disableAnimation: true);
            e.Handled = true;
        };

        Editor.Loaded += (_, _) => EnsureScrollViewerHooked();
        Editor.TextChanged += (_, _) => OnEditorTextChanged();
        Editor.SelectionChanged += (_, _) => CaretMoved?.Invoke(this, EventArgs.Empty);

        // Plain-text-only paste: read text from the clipboard ourselves so RTF formatting
        // never enters the document.
        Editor.Paste += (_, e) =>
        {
            e.Handled = true;
            _ = PastePlainTextAsync();
        };

        // Insert a literal tab character instead of letting Tab move focus.
        Editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Tab && !IsAnyModifierDown())
            {
                InsertAtSelection("\t");
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Enter && IsKeyDown(VirtualKey.Shift))
            {
                // RichEdit inserts a vertical-tab soft break for Shift+Enter; force a
                // normal paragraph break so plain-text semantics hold.
                InsertAtSelection("\r");
                e.Handled = true;
            }
            else if (IsKeyDown(VirtualKey.Control) && e.Key is VirtualKey.Z or VirtualKey.Y)
            {
                // Let RichEdit perform the undo/redo, then re-highlight: a formatting-only
                // undo leaves the text unchanged, so the _currentText filter swallows the
                // TextChanged and the document would stay visibly de-highlighted.
                DispatcherQueue.TryEnqueue(_highlighting.NotifyScrolled);
            }
        };
    }

    private void OnPaneLoaded()
    {
        EnsureScrollViewerHooked();

        if (!_themeApplied)
        {
            // Track the ambient theme until the host explicitly calls ApplyEditorTheme.
            SetPalette(ActualTheme == ElementTheme.Dark ? EditorPalette.Dark : EditorPalette.Light);
        }

        ApplyDefaultCharacterFormat();
        RedrawGutter();
        _highlighting.HighlightNow();
    }

    // --- Text plumbing ---

    /// <summary>
    /// Returns the document text with '\n' line endings and without the trailing
    /// RichEdit paragraph sentinel.
    /// </summary>
    public string GetText()
    {
        Editor.TextDocument.GetText(TextGetOptions.None, out var raw);
        return HighlightingService.Normalize(raw);
    }

    public void SetText(string text, bool markDirty)
    {
        int oldCaret = CaretPosition;

        // RichEdit stores paragraph breaks as single '\r'; normalize up front so document
        // positions always line up with GetText() offsets.
        var richText = text.Replace("\r\n", "\r").Replace('\n', '\r');
        Editor.TextDocument.SetText(TextSetOptions.None, richText);

        var newText = GetText();
        _currentText = newText;
        RecomputeLineStarts(newText);

        // Restore the caret explicitly — RichEdit leaves it in an unspecified place.
        int caret = Math.Clamp(oldCaret, 0, newText.Length);
        Editor.TextDocument.Selection.SetRange(caret, caret);

        SetDirty(markDirty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        _highlighting.HighlightNow();
        RedrawGutter();
    }

    /// <summary>Caret offset in the same \n-normalized space as <see cref="GetText"/>.</summary>
    public int CaretPosition
    {
        get => Editor.TextDocument.Selection.StartPosition;
        set
        {
            var selection = Editor.TextDocument.Selection;
            selection.SetRange(value, value);
            selection.ScrollIntoView(PointOptions.None);
        }
    }

    /// <summary>1-based line and column of the caret.</summary>
    public (int Line, int Column) GetCaretLineColumn()
    {
        int pos = Math.Max(0, Editor.TextDocument.Selection.StartPosition);
        int line = LineIndexForOffset(pos);
        return (line + 1, pos - _lineStarts[line] + 1);
    }

    /// <summary>
    /// Inserts <paramref name="body"/> at the caret (replacing any selection) and moves the
    /// caret to <paramref name="caretWithinBody"/> within the inserted text. The async
    /// TextChanged pass marks the pane dirty and re-highlights.
    /// </summary>
    public void InsertElement(string body, int caretWithinBody)
    {
        var selection = Editor.TextDocument.Selection;
        int start = Math.Min(selection.StartPosition, selection.EndPosition);

        // RichEdit stores paragraph breaks as '\r'; normalize like SetText/paste so
        // positions stay in the same space as GetText offsets. Each newline stays one
        // character, so caretWithinBody carries over unchanged.
        var rich = body.Replace("\r\n", "\r").Replace('\n', '\r');
        selection.SetText(TextSetOptions.None, rich);

        int target = Math.Clamp(start + caretWithinBody, 0, GetText().Length);
        selection.SetRange(target, target);
        selection.ScrollIntoView(PointOptions.None);
        FocusEditor();
    }

    // --- File operations ---

    public void LoadFromFile(string path)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        _filePath = path;
        _originalFileHash = SessionManager.ComputeFileHash(path);
        SetText(content, markDirty: false);
        SetMode(ModeDetector.DetectFromPath(path));
        CaretPosition = 0;
    }

    public void SaveToFile(string path)
    {
        // Only re-detect the mode when the path changed (Save As); a plain save to the
        // same path must preserve a manually selected mode.
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
        CursorPosition = CaretPosition,
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
        CaretPosition = Math.Clamp(data.CursorPosition, 0, GetText().Length);
    }

    // --- Mode / highlighting ---

    public void SetMode(string? mode)
    {
        _mode = mode;
        _highlighting.SetMode(mode);
    }

    // --- Find ---

    public bool FindNext(string term)
    {
        var text = GetText();
        int start = Math.Clamp(Editor.TextDocument.Selection.EndPosition, 0, text.Length);
        if (TextFinder.FindNext(text, term, start) is not { } index) return false;
        SelectMatch(index, term.Length);
        return true;
    }

    public bool FindPrevious(string term)
    {
        var text = GetText();
        // Use the selection start so repeated previous-finds walk backward.
        int start = Math.Clamp(Editor.TextDocument.Selection.StartPosition, 0, text.Length);
        if (TextFinder.FindPrevious(text, term, start) is not { } index) return false;
        SelectMatch(index, term.Length);
        return true;
    }

    public string GetSelectedText()
    {
        var selection = Editor.TextDocument.Selection;
        selection.GetText(TextGetOptions.None, out var raw);
        var text = raw.Replace('\r', '\n').Replace('\v', '\n');
        // Ctrl+A extends past the normalized document end into the RichEdit final-paragraph
        // sentinel; strip that one trailing newline so the result matches GetText().
        if (text.EndsWith('\n') && selection.EndPosition > GetText().Length)
            text = text[..^1];
        return text;
    }

    private void SelectMatch(int start, int length)
    {
        var selection = Editor.TextDocument.Selection;
        selection.SetRange(start, start + length);
        selection.ScrollIntoView(PointOptions.None);
    }

    // --- View options ---

    public void SetWordWrap(bool enabled)
    {
        Editor.TextWrapping = enabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
        GutterBorder.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        if (!enabled) RedrawGutter();
        _highlighting.NotifyScrolled();
    }

    /// <summary>Adjusts the font size by <paramref name="delta"/> (clamped 8..32); 0 resets to 14.</summary>
    public void AdjustFontSize(int delta)
    {
        double target = delta == 0
            ? DefaultEditorFontSize
            : Math.Clamp(Editor.FontSize + delta, MinEditorFontSize, MaxEditorFontSize);

        Editor.FontSize = target;

        var doc = Editor.TextDocument;

        var defaultFormat = doc.GetDefaultCharacterFormat();
        defaultFormat.Size = (float)target;
        doc.SetDefaultCharacterFormat(defaultFormat);

        doc.GetText(TextGetOptions.None, out var raw);
        if (raw.Length > 0)
        {
            // One undo unit per zoom; undoing past it can still revert the visible text
            // size until the next zoom — accepted residual edge.
            doc.BeginUndoGroup();
            try
            {
                doc.GetRange(0, raw.Length).CharacterFormat.Size = (float)target;
            }
            finally
            {
                doc.EndUndoGroup();
            }
        }

        // Recalibrate gutter metrics for the new glyph size (again after layout settles).
        _glyphWidth = 0;
        _gutterDigits = 0;
        RedrawGutter();
        DispatcherQueue.TryEnqueue(RedrawGutter);
        _highlighting.NotifyScrolled();
    }

    /// <summary>Switches the light/dark highlight palette and default text color, then re-highlights.</summary>
    public void ApplyEditorTheme(ElementTheme theme)
    {
        _themeApplied = true;
        RequestedTheme = theme;

        var resolved = theme;
        if (resolved == ElementTheme.Default)
            resolved = ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

        SetPalette(resolved == ElementTheme.Dark ? EditorPalette.Dark : EditorPalette.Light);

        ApplyDefaultCharacterFormat();
        _highlighting.ResetAllFormatting();
        _highlighting.HighlightNow();
        RedrawGutter();
    }

    public void FocusEditor()
    {
        // Focus() fails silently on a control that is not in the visual tree yet (e.g. a tab
        // created during session restore or just added to the TabView); defer until Loaded.
        if (Editor.IsLoaded)
        {
            Editor.Focus(FocusState.Programmatic);
            return;
        }

        RoutedEventHandler? once = null;
        once = (_, _) =>
        {
            Editor.Loaded -= once;
            Editor.Focus(FocusState.Programmatic);
        };
        Editor.Loaded += once;
    }

    // --- Internal: dirty tracking and text-change handling ---

    private void OnEditorTextChanged()
    {
        var text = GetText();
        if (text == _currentText) return; // formatting-only pass or programmatic echo

        _currentText = text;
        RecomputeLineStarts(text);
        RedrawGutter();

        SetDirty(true);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        _highlighting.NotifyTextChanged();

        // RichEdit raises SelectionChanged before TextChanged, so the host computed Ln/Col
        // against stale line starts; re-raise now that they are fresh.
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void SetDirty(bool value)
    {
        if (_isDirty == value) return;
        _isDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPalette(EditorPalette palette)
    {
        _palette = palette;
        _highlighting.SetPalette(palette);
        _gutterForegroundBrush = new SolidColorBrush(palette.GutterForeground);
        GutterBorder.Background = new SolidColorBrush(palette.GutterBackground);
    }

    private void ApplyDefaultCharacterFormat()
    {
        var doc = Editor.TextDocument;
        var format = doc.GetDefaultCharacterFormat();
        format.ForegroundColor = _palette.DefaultForeground;
        doc.SetDefaultCharacterFormat(format);
    }

    // --- Internal: paste and keyboard ---

    private async Task PastePlainTextAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;

            var text = await content.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;

            InsertAtSelection(text.Replace("\r\n", "\r").Replace('\n', '\r').Replace('\v', '\r'));
        }
        catch
        {
            // Clipboard access can fail transiently; ignore.
        }
    }

    private void InsertAtSelection(string text)
    {
        var selection = Editor.TextDocument.Selection;
        selection.SetText(TextSetOptions.None, text);
        selection.Collapse(false); // collapse to the end of the inserted text
        selection.ScrollIntoView(PointOptions.None);
    }

    private static bool IsAnyModifierDown() =>
        IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Shift) || IsKeyDown(VirtualKey.Menu);

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    // --- Internal: line-start index ---

    private void RecomputeLineStarts(string text)
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') _lineStarts.Add(i + 1);
        }
    }

    private int LineIndexForOffset(int offset)
    {
        int index = _lineStarts.BinarySearch(offset);
        if (index < 0) index = ~index - 1;
        return Math.Clamp(index, 0, _lineStarts.Count - 1);
    }

    // --- Internal: line-number gutter ---

    private void EnsureScrollViewerHooked()
    {
        if (_scrollViewer != null) return;
        _scrollViewer = FindScrollViewer(Editor);
        if (_scrollViewer != null)
        {
            _highlighting.SetScrollViewer(_scrollViewer);
            _scrollViewer.ViewChanged += (_, _) =>
            {
                RedrawGutter();
                _highlighting.NotifyScrolled();
            };
        }
    }

    // The RichEditBox template scroller is named "ContentScrollViewer"; prefer it, but fall
    // back to the first ScrollViewer found so a template change can't silently break sync.
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        ScrollViewer? firstFound = null;

        ScrollViewer? Search(DependencyObject node)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is ScrollViewer sv)
                {
                    firstFound ??= sv;
                    if (sv.Name == "ContentScrollViewer") return sv;
                }
                if (Search(child) is { } found) return found;
            }
            return null;
        }

        return Search(root) ?? firstFound;
    }

    /// <summary>
    /// Measures the constant line height (NoWrap + monospace) and the document's top inset.
    /// Line height is the gap between consecutive lines, which is identical in viewport or
    /// document coordinates; the top inset is only read at zero scroll, where the two coincide
    /// — so the gutter positions correctly no matter how the editor reports line rects.
    /// </summary>
    private void RecalibrateMetrics()
    {
        try
        {
            var doc = Editor.TextDocument;
            double verticalOffset = _scrollViewer?.VerticalOffset ?? 0;

            var first = doc.GetRange(0, 0);
            first.GetRect(PointOptions.AllowOffClient, out Rect firstRect, out _);

            if (_lineStarts.Count > 1)
            {
                var second = doc.GetRange(_lineStarts[1], _lineStarts[1]);
                second.GetRect(PointOptions.AllowOffClient, out Rect secondRect, out _);
                double delta = secondRect.Top - firstRect.Top;
                if (delta > 1) _lineHeight = delta;
            }

            if (_lineHeight <= 0 && firstRect.Height > 1) _lineHeight = firstRect.Height;

            // Line 0's top equals the document inset only when unscrolled; cache it there.
            if (verticalOffset <= 0) _topInset = firstRect.Top;
        }
        catch
        {
            // GetRect can fail before the control is realized; keep previous metrics.
        }
    }

    private double MeasureGlyphWidth()
    {
        var probe = new TextBlock
        {
            Text = "0",
            FontFamily = Editor.FontFamily,
            FontSize = Editor.FontSize,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(1, probe.DesiredSize.Width);
    }

    private void UpdateGutterWidth()
    {
        if (_glyphWidth <= 0) _glyphWidth = MeasureGlyphWidth();
        int digits = Math.Max(3, _lineStarts.Count.ToString().Length);
        if (digits == _gutterDigits) return;
        _gutterDigits = digits;
        GutterBorder.Width = digits * _glyphWidth + GutterPadding;
    }

    private void RedrawGutter()
    {
        if (GutterBorder.Visibility == Visibility.Collapsed) return;

        EnsureScrollViewerHooked();
        UpdateGutterWidth();
        RecalibrateMetrics();
        GutterCanvas.Children.Clear();

        if (_lineHeight <= 0) return;
        double viewportHeight = GutterCanvas.ActualHeight > 0 ? GutterCanvas.ActualHeight : Editor.ActualHeight;
        if (viewportHeight <= 0) return;

        double verticalOffset = _scrollViewer?.VerticalOffset ?? 0;
        double textWidth = _gutterDigits * _glyphWidth + GutterPadding / 2;

        // Position numbers from the scroll offset and the uniform line height — no per-line
        // hit-testing. This tracks scrolling regardless of how the editor reports line rects,
        // and drops the per-line GetRect that stalled long documents.
        int firstLine = Math.Max(0, (int)Math.Floor((verticalOffset - _topInset) / _lineHeight) - 1);
        for (int i = firstLine; i < _lineStarts.Count; i++)
        {
            double y = _topInset + i * _lineHeight - verticalOffset;
            if (y > viewportHeight) break;
            if (y + _lineHeight < 0) continue;

            var label = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontFamily = Editor.FontFamily,
                FontSize = Editor.FontSize,
                Foreground = _gutterForegroundBrush,
                Width = textWidth,
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right,
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y);
            GutterCanvas.Children.Add(label);
        }
    }
}
