using System.Runtime.InteropServices;
using SimpleText.Core;
using SimpleText.Core.FileTypes;

namespace SimpleText.Highlighting;

internal sealed class HighlightingManager
{
    private readonly EditorRichTextBox _editor;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private ISyntaxHighlighter? _highlighter;
    private bool _isHighlighting;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETREDRAW = 0x000B;

    public string FileTypeName => _highlighter?.FileTypeName ?? "Plain Text";

    public HighlightingManager(EditorRichTextBox editor)
    {
        _editor = editor;

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            ApplyHighlighting();
        };

        _editor.TextChanged += (_, _) =>
        {
            if (_isHighlighting || _highlighter == null) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };

        _editor.ScrollChanged += (_, _) =>
        {
            if (_isHighlighting || _highlighter == null) return;
            // Shorter debounce for scroll — just re-highlight visible region
            _debounceTimer.Stop();
            _debounceTimer.Interval = 50;
            _debounceTimer.Start();
        };
    }

    public string? SetFilePath(string? filePath)
    {
        var mode = ModeDetector.DetectFromPath(filePath);
        ApplyMode(mode);
        return mode;
    }

    public void SetMode(string? mode)
    {
        ApplyMode(mode);
    }

    private void ApplyMode(string? mode)
    {
        _highlighter = mode switch
        {
            TextModes.Markdown => new MarkdownHighlighter(),
            TextModes.AsciiDoc => new AsciiDocHighlighter(),
            TextModes.ReStructuredText => new ReStructuredTextHighlighter(),
            _ => null
        };

        if (_highlighter != null)
            ApplyHighlighting();
        else
            ClearHighlighting();
    }

    private void ApplyHighlighting()
    {
        if (_highlighter == null || _isHighlighting) return;
        _isHighlighting = true;
        _debounceTimer.Interval = 300; // Reset interval after scroll

        try
        {
            var text = _editor.Text;
            if (string.IsNullOrEmpty(text)) return;

            // Get visible region
            int firstChar = _editor.GetCharIndexFromPosition(new Point(0, 0));
            int lastChar = _editor.GetCharIndexFromPosition(new Point(0, _editor.ClientSize.Height));

            // Expand to full lines with padding
            int start = Math.Max(0, text.LastIndexOf('\n', Math.Max(0, firstChar - 1)) + 1);
            int endPos = text.IndexOf('\n', Math.Min(text.Length - 1, lastChar));
            if (endPos < 0) endPos = text.Length;
            int length = Math.Min(endPos - start, text.Length - start);
            if (length <= 0) return;

            var spans = _highlighter.GetHighlights(text, start, length);

            // Save state
            int savedSelStart = _editor.SelectionStart;
            int savedSelLength = _editor.SelectionLength;

            // Suspend rendering
            SendMessage(_editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            try
            {
                // Reset visible region to default
                _editor.Select(start, length);
                _editor.SelectionColor = _editor.ForeColor;
                _editor.SelectionFont = _editor.Font;

                // Apply highlight spans
                foreach (var span in spans)
                {
                    if (span.Start < 0 || span.Start + span.Length > text.Length) continue;
                    _editor.Select(span.Start, span.Length);
                    _editor.SelectionColor = span.ForeColor;
                    if (span.Style != FontStyle.Regular)
                        _editor.SelectionFont = new Font(_editor.Font, span.Style);
                }

                // Restore selection
                _editor.Select(savedSelStart, savedSelLength);
            }
            finally
            {
                // Resume rendering
                SendMessage(_editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                _editor.Invalidate();
            }
        }
        finally
        {
            _isHighlighting = false;
        }
    }

    private void ClearHighlighting()
    {
        _isHighlighting = true;
        try
        {
            int savedSelStart = _editor.SelectionStart;
            int savedSelLength = _editor.SelectionLength;

            SendMessage(_editor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            _editor.SelectAll();
            _editor.SelectionColor = _editor.ForeColor;
            _editor.SelectionFont = _editor.Font;
            _editor.Select(savedSelStart, savedSelLength);
            SendMessage(_editor.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            _editor.Invalidate();
        }
        finally
        {
            _isHighlighting = false;
        }
    }
}
