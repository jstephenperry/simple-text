using SimpleText.Controls;

namespace SimpleText;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    private MenuStrip _menuStrip = null!;
    private ToolStripMenuItem _fileMenu = null!;
    private ToolStripMenuItem _newMenuItem = null!;
    private ToolStripMenuItem _openMenuItem = null!;
    private ToolStripMenuItem _saveMenuItem = null!;
    private ToolStripMenuItem _saveAsMenuItem = null!;
    private ToolStripMenuItem _exitMenuItem = null!;

    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLineCol = null!;
    private ToolStripStatusLabel _statusFileName = null!;
    private ToolStripStatusLabel _statusEncoding = null!;

    private Panel _editorPanel = null!;
    private LineNumberPanel _lineNumberPanel = null!;
    private EditorRichTextBox _editor = null!;

    private Panel _findBar = null!;
    private TextBox _findTextBox = null!;
    private Button _findNextButton = null!;
    private Button _findPrevButton = null!;
    private Button _findCloseButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        // --- MenuStrip ---
        _menuStrip = new MenuStrip();
        _fileMenu = new ToolStripMenuItem("&File");
        _newMenuItem = new ToolStripMenuItem("&New", null, null, Keys.Control | Keys.N);
        _openMenuItem = new ToolStripMenuItem("&Open...", null, null, Keys.Control | Keys.O);
        _saveMenuItem = new ToolStripMenuItem("&Save", null, null, Keys.Control | Keys.S);
        _saveAsMenuItem = new ToolStripMenuItem("Save &As...", null, null, Keys.Control | Keys.Shift | Keys.S);
        _exitMenuItem = new ToolStripMenuItem("E&xit");
        _fileMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _newMenuItem, _openMenuItem,
            new ToolStripSeparator(),
            _saveMenuItem, _saveAsMenuItem,
            new ToolStripSeparator(),
            _exitMenuItem
        });
        _menuStrip.Items.Add(_fileMenu);
        _menuStrip.Dock = DockStyle.Top;

        // --- StatusStrip ---
        _statusStrip = new StatusStrip();
        _statusLineCol = new ToolStripStatusLabel("Ln 1, Col 1") { AutoSize = false, Width = 120, TextAlign = ContentAlignment.MiddleLeft };
        _statusFileName = new ToolStripStatusLabel("Untitled") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusEncoding = new ToolStripStatusLabel("UTF-8") { AutoSize = false, Width = 60, TextAlign = ContentAlignment.MiddleRight };
        _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLineCol, _statusFileName, _statusEncoding });
        _statusStrip.Dock = DockStyle.Bottom;

        // --- Find Bar ---
        _findBar = new Panel { Dock = DockStyle.Top, Height = 30, Visible = false, Padding = new Padding(4, 3, 4, 3) };
        var findLabel = new Label { Text = "Find:", AutoSize = true, Location = new Point(6, 7) };
        _findTextBox = new TextBox { Location = new Point(44, 4), Width = 220 };
        _findNextButton = new Button { Text = "Next", Location = new Point(272, 3), Width = 50, Height = 24, FlatStyle = FlatStyle.System };
        _findPrevButton = new Button { Text = "Prev", Location = new Point(326, 3), Width = 50, Height = 24, FlatStyle = FlatStyle.System };
        _findCloseButton = new Button { Text = "X", Location = new Point(382, 3), Width = 28, Height = 24, FlatStyle = FlatStyle.System };
        _findBar.Controls.AddRange(new Control[] { findLabel, _findTextBox, _findNextButton, _findPrevButton, _findCloseButton });

        // --- Editor area ---
        _editor = new EditorRichTextBox { Dock = DockStyle.Fill };
        _lineNumberPanel = new LineNumberPanel(_editor) { Dock = DockStyle.Left };
        _editorPanel = new Panel { Dock = DockStyle.Fill };
        _editorPanel.Controls.Add(_editor);
        _editorPanel.Controls.Add(_lineNumberPanel);

        // --- MainForm ---
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 560);
        MinimumSize = new Size(400, 300);
        Text = "Untitled - SimpleText";
        Controls.Add(_editorPanel);   // Fill - added first
        Controls.Add(_findBar);       // Top
        Controls.Add(_statusStrip);   // Bottom
        Controls.Add(_menuStrip);     // Top (above find bar)
        MainMenuStrip = _menuStrip;

        ResumeLayout(false);
        PerformLayout();
    }
}
