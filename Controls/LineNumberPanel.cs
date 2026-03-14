using System.Drawing;
using System.Windows.Forms;

namespace SimpleText.Controls;

internal class LineNumberPanel : Panel
{
    private readonly RichTextBox _editor;

    public LineNumberPanel(RichTextBox editor)
    {
        _editor = editor;
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint, true);
        BackColor = Color.FromArgb(240, 240, 240);
        ForeColor = Color.FromArgb(130, 130, 130);
        Width = 50;
    }

    public void UpdateWidth()
    {
        int digits = Math.Max(3, _editor.Lines.Length.ToString().Length);
        int newWidth = TextRenderer.MeasureText(new string('0', digits), _editor.Font).Width + 12;
        if (Width != newWidth)
            Width = newWidth;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_editor.Text.Length == 0)
        {
            DrawLineNumber(g, 1, 0);
            return;
        }

        int firstCharIndex = _editor.GetCharIndexFromPosition(new Point(0, 0));
        int firstLine = _editor.GetLineFromCharIndex(firstCharIndex);
        int lastCharIndex = _editor.GetCharIndexFromPosition(new Point(0, _editor.ClientSize.Height));
        int lastLine = _editor.GetLineFromCharIndex(lastCharIndex);

        for (int line = firstLine; line <= lastLine; line++)
        {
            int charIndex = _editor.GetFirstCharIndexFromLine(line);
            if (charIndex < 0) break;
            var pos = _editor.GetPositionFromCharIndex(charIndex);
            DrawLineNumber(g, line + 1, pos.Y);
        }
    }

    private void DrawLineNumber(Graphics g, int number, int y)
    {
        using var brush = new SolidBrush(ForeColor);
        using var sf = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near
        };
        var rect = new RectangleF(0, y, Width - 6, _editor.Font.Height);
        g.DrawString(number.ToString(), _editor.Font, brush, rect, sf);
    }
}
