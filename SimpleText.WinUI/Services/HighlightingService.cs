using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using SimpleText.Core.Highlighting;
using Windows.Foundation;

namespace SimpleText.WinUI.Services;

/// <summary>
/// Debounced, visible-region syntax highlighting for a RichEditBox, mirroring the WinForms
/// HighlightingManager: 300ms debounce after text changes, 50ms after scrolls; only the
/// visible region (expanded to full line boundaries) is formatted.
/// Note: each formatting pass is wrapped in an undo group so it collapses to a single
/// entry on the RichEdit undo stack — a known, accepted trade-off.
/// </summary>
internal sealed class HighlightingService
{
    private static readonly TimeSpan EditDebounce = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ScrollDebounce = TimeSpan.FromMilliseconds(50);

    /// <summary>Lines to format when hit-testing fails on a realized editor.</summary>
    private const int FallbackLineCount = 200;

    private readonly RichEditBox _editor;
    private readonly DispatcherQueueTimer _timer;
    private ISpanHighlighter? _highlighter;
    private EditorPalette _palette = EditorPalette.Light;
    private bool _pendingPass;

    /// <summary>
    /// True while a formatting pass is mutating character formats. This only guards
    /// synchronous reentrancy within a pass; RichEditBox raises TextChanged asynchronously,
    /// after this flag has already been cleared, so those echoes must instead be filtered by
    /// content comparison (see EditorPane._currentText).
    /// </summary>
    public bool IsApplying { get; private set; }

    public HighlightingService(RichEditBox editor)
    {
        _editor = editor;
        _timer = editor.DispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            ApplyHighlighting();
        };

        // A pass skipped while the editor was unrealized (background tab, session restore)
        // runs when the editor first gains real size.
        _editor.SizeChanged += (_, _) =>
        {
            if (_pendingPass && _editor.ActualHeight > 0) HighlightNow();
        };
    }

    public void SetPalette(EditorPalette palette) => _palette = palette;

    /// <summary>Switches the highlighter for the given TextModes constant (or null) and
    /// re-highlights immediately (or clears formatting when switching to plain text).</summary>
    public void SetMode(string? mode)
    {
        _highlighter = HighlighterRegistry.ForMode(mode);
        _timer.Stop();
        if (_highlighter == null)
            ResetAllFormatting();
        else
            ApplyHighlighting();
    }

    /// <summary>Schedules a re-highlight 300ms after the last text change.</summary>
    public void NotifyTextChanged() => Restart(EditDebounce);

    /// <summary>Schedules a re-highlight 50ms after the last scroll.</summary>
    public void NotifyScrolled() => Restart(ScrollDebounce);

    /// <summary>Cancels any pending debounce and highlights the visible region now.</summary>
    public void HighlightNow()
    {
        _timer.Stop();
        ApplyHighlighting();
    }

    /// <summary>
    /// Resets the entire document to the palette default foreground with bold/italic off.
    /// Used when switching to plain text mode and when the theme changes.
    /// </summary>
    public void ResetAllFormatting()
    {
        var doc = _editor.TextDocument;
        doc.GetText(TextGetOptions.None, out var raw);
        if (raw.Length == 0) return;

        IsApplying = true;
        doc.BatchDisplayUpdates();
        doc.BeginUndoGroup();
        try
        {
            ResetRange(doc.GetRange(0, raw.Length));
        }
        finally
        {
            doc.EndUndoGroup();
            doc.ApplyDisplayUpdates();
            IsApplying = false;
        }
    }

    private void Restart(TimeSpan interval)
    {
        if (_highlighter == null) return;
        _timer.Stop();
        _timer.Interval = interval;
        _timer.Start();
    }

    private void ApplyHighlighting()
    {
        if (_highlighter == null)
        {
            _pendingPass = false;
            return;
        }
        if (IsApplying) return;

        if (!_editor.IsLoaded || _editor.ActualHeight <= 0)
        {
            // Unrealized editor: hit-testing would fall back to a whole-document pass,
            // which freezes the UI on large files. Defer until the editor has real size.
            _pendingPass = true;
            return;
        }
        _pendingPass = false;

        var doc = _editor.TextDocument;
        doc.GetText(TextGetOptions.None, out var raw);
        var text = Normalize(raw);
        if (text.Length == 0) return;

        var (start, length) = ComputeVisibleRegion(text);
        if (length <= 0) return;

        var spans = _highlighter.GetHighlights(text, start, length);

        IsApplying = true;
        doc.BatchDisplayUpdates();
        doc.BeginUndoGroup();
        try
        {
            // Reset the expanded region to defaults first.
            ResetRange(doc.GetRange(start, start + length));

            // Paragraph breaks count as one character position in ITextRange offsets, so
            // offsets in the \n-normalized text line up 1:1 with document positions.
            foreach (var span in spans)
            {
                if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > text.Length)
                    continue;

                var (color, bold, italic) = _palette.For(span.Kind);
                var format = doc.GetRange(span.Start, span.Start + span.Length).CharacterFormat;
                format.ForegroundColor = color;
                if (bold) format.Bold = FormatEffect.On;
                if (italic) format.Italic = FormatEffect.On;
            }
        }
        finally
        {
            doc.EndUndoGroup();
            doc.ApplyDisplayUpdates();
            IsApplying = false;
        }
    }

    private void ResetRange(ITextRange range)
    {
        var format = range.CharacterFormat;
        format.ForegroundColor = _palette.DefaultForeground;
        format.Bold = FormatEffect.Off;
        format.Italic = FormatEffect.Off;
    }

    /// <summary>
    /// Computes the visible character region of <paramref name="text"/> expanded to full
    /// line boundaries. The caller guarantees the editor is realized; if hit-testing still
    /// fails, falls back to the first <see cref="FallbackLineCount"/> lines.
    /// </summary>
    private (int Start, int Length) ComputeVisibleRegion(string text)
    {
        int firstChar;
        int lastChar;
        try
        {
            var doc = _editor.TextDocument;
            double height = _editor.ActualHeight;
            firstChar = doc.GetRangeFromPoint(new Point(0, 0), PointOptions.ClientCoordinates).StartPosition;
            lastChar = doc.GetRangeFromPoint(new Point(0, height), PointOptions.ClientCoordinates).StartPosition;
        }
        catch
        {
            // Hit-testing can fail even on a realized control; clamp the fallback so a
            // large document never gets a whole-document pass.
            firstChar = 0;
            lastChar = EndOfLineCount(text, FallbackLineCount);
        }

        firstChar = Math.Clamp(firstChar, 0, text.Length);
        lastChar = Math.Clamp(lastChar, 0, text.Length);
        if (lastChar < firstChar) (firstChar, lastChar) = (lastChar, firstChar);

        int start = firstChar <= 0 ? 0 : text.LastIndexOf('\n', firstChar - 1) + 1;
        int end = lastChar >= text.Length ? text.Length : text.IndexOf('\n', lastChar);
        if (end < 0) end = text.Length;

        return (start, end - start);
    }

    /// <summary>Offset just past the first <paramref name="lineCount"/> lines of <paramref name="text"/>.</summary>
    private static int EndOfLineCount(string text, int lineCount)
    {
        int offset = 0;
        for (int i = 0; i < lineCount; i++)
        {
            int next = text.IndexOf('\n', offset);
            if (next < 0) return text.Length;
            offset = next + 1;
        }
        return offset;
    }

    /// <summary>
    /// Normalizes RichEdit text: every '\r' and '\v' (the Shift+Enter soft break) becomes
    /// '\n' and the single trailing paragraph sentinel (if present) is stripped, so offsets
    /// match document positions 1:1.
    /// </summary>
    internal static string Normalize(string raw)
    {
        var text = raw.Replace('\r', '\n').Replace('\v', '\n');
        if (text.EndsWith('\n')) text = text[..^1];
        return text;
    }
}
