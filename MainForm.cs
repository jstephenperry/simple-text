using System.Runtime.InteropServices;
using System.Text;

namespace SimpleText;

public partial class MainForm : Form
{
    private string? _currentFilePath;
    private bool _isDirty;
    private string? _pendingOpenFile;

    public MainForm()
    {
        InitializeComponent();
        WireEvents();
        AllowDrop = true;
    }

    public void OpenFileOnLoad(string path) => _pendingOpenFile = path;

    private void WireEvents()
    {
        // Form events
        Load += (_, _) =>
        {
            if (_pendingOpenFile != null)
                OpenFile(_pendingOpenFile);
        };
        FormClosing += OnFormClosing;
        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                OpenFile(files[0]);
        };

        // Menu events
        _newMenuItem.Click += (_, _) => NewFile();
        _openMenuItem.Click += (_, _) => OpenFileDialog();
        _saveMenuItem.Click += (_, _) => SaveFile();
        _saveAsMenuItem.Click += (_, _) => SaveFileAs();
        _exitMenuItem.Click += (_, _) => Close();

        // Editor events
        _editor.TextChanged += OnTextChanged;
        _editor.SelectionChanged += (_, _) => UpdateStatusBar();
        _editor.ScrollChanged += (_, _) => _lineNumberPanel.Invalidate();

        // Find bar events
        _findNextButton.Click += (_, _) => FindNext();
        _findPrevButton.Click += (_, _) => FindPrevious();
        _findCloseButton.Click += (_, _) => HideFindBar();
        _findTextBox.KeyDown += OnFindTextBoxKeyDown;

        // Keyboard shortcuts not handled by menu
        KeyPreview = true;
        KeyDown += OnMainKeyDown;
    }

    // --- File Operations ---

    private void NewFile()
    {
        if (!PromptSaveIfDirty()) return;
        _editor.Clear();
        _currentFilePath = null;
        SetDirty(false);
        UpdateTitle();
        UpdateStatusBar();
    }

    private void OpenFileDialog()
    {
        if (!PromptSaveIfDirty()) return;
        using var dlg = new OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FilterIndex = 2
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            OpenFile(dlg.FileName);
    }

    private void OpenFile(string path)
    {
        try
        {
            _editor.TextChanged -= OnTextChanged;
            _editor.Text = File.ReadAllText(path, Encoding.UTF8);
            _editor.TextChanged += OnTextChanged;
            _currentFilePath = path;
            SetDirty(false);
            UpdateTitle();
            UpdateStatusBar();
            _lineNumberPanel.UpdateWidth();
            _lineNumberPanel.Invalidate();
            _editor.SelectionStart = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open file:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveFile()
    {
        if (_currentFilePath == null)
            SaveFileAs();
        else
            WriteFile(_currentFilePath);
    }

    private void SaveFileAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FilterIndex = 2
        };
        if (_currentFilePath != null)
        {
            dlg.InitialDirectory = Path.GetDirectoryName(_currentFilePath);
            dlg.FileName = Path.GetFileName(_currentFilePath);
        }
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            _currentFilePath = dlg.FileName;
            WriteFile(_currentFilePath);
        }
    }

    private void WriteFile(string path)
    {
        try
        {
            File.WriteAllText(path, _editor.Text, new UTF8Encoding(false));
            SetDirty(false);
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save file:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool PromptSaveIfDirty()
    {
        if (!_isDirty) return true;
        var name = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled";
        var result = MessageBox.Show($"Save changes to {name}?", "SimpleText",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes) SaveFile();
        return true;
    }

    // --- UI Updates ---

    private void SetDirty(bool dirty)
    {
        _isDirty = dirty;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        var name = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled";
        var prefix = _isDirty ? "*" : "";
        Text = $"{prefix}{name} - SimpleText";
    }

    private void UpdateStatusBar()
    {
        int index = _editor.SelectionStart;
        int line = _editor.GetLineFromCharIndex(index);
        int firstChar = _editor.GetFirstCharIndexFromLine(line);
        int col = index - firstChar;
        _statusLineCol.Text = $"Ln {line + 1}, Col {col + 1}";
        _statusFileName.Text = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled";
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (!_isDirty) SetDirty(true);
        _lineNumberPanel.UpdateWidth();
        _lineNumberPanel.Invalidate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!PromptSaveIfDirty())
            e.Cancel = true;
    }

    // --- Find Bar ---

    private void ShowFindBar()
    {
        _findBar.Visible = true;
        if (_editor.SelectedText.Length > 0)
            _findTextBox.Text = _editor.SelectedText;
        _findTextBox.Focus();
        _findTextBox.SelectAll();
    }

    private void HideFindBar()
    {
        _findBar.Visible = false;
        _editor.Focus();
    }

    private void FindNext()
    {
        var term = _findTextBox.Text;
        if (string.IsNullOrEmpty(term)) return;

        int startIndex = _editor.SelectionStart + _editor.SelectionLength;
        int foundIndex = _editor.Text.IndexOf(term, startIndex, StringComparison.OrdinalIgnoreCase);
        if (foundIndex < 0)
            foundIndex = _editor.Text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase);
        if (foundIndex >= 0)
        {
            _editor.Select(foundIndex, term.Length);
            _editor.ScrollToCaret();
        }
        else
        {
            MessageBox.Show($"Cannot find \"{term}\"", "Find",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void FindPrevious()
    {
        var term = _findTextBox.Text;
        if (string.IsNullOrEmpty(term)) return;

        int startIndex = _editor.SelectionStart - 1;
        if (startIndex < 0) startIndex = _editor.Text.Length - 1;
        int foundIndex = _editor.Text.LastIndexOf(term, startIndex, StringComparison.OrdinalIgnoreCase);
        if (foundIndex < 0)
            foundIndex = _editor.Text.LastIndexOf(term, _editor.Text.Length - 1, StringComparison.OrdinalIgnoreCase);
        if (foundIndex >= 0)
        {
            _editor.Select(foundIndex, term.Length);
            _editor.ScrollToCaret();
        }
        else
        {
            MessageBox.Show($"Cannot find \"{term}\"", "Find",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // --- Keyboard Handling ---

    private void OnMainKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F && e.Control)
        {
            ShowFindBar();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape && _findBar.Visible)
        {
            HideFindBar();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F3 && !e.Shift)
        {
            if (_findBar.Visible || !string.IsNullOrEmpty(_findTextBox.Text))
                FindNext();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F3 && e.Shift)
        {
            if (_findBar.Visible || !string.IsNullOrEmpty(_findTextBox.Text))
                FindPrevious();
            e.Handled = true;
        }
    }

    private void OnFindTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            FindNext();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter && e.Shift)
        {
            FindPrevious();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HideFindBar();
            e.Handled = true;
        }
    }
}

// --- RichTextBox subclass for scroll event support ---

internal class EditorRichTextBox : RichTextBox
{
    private const int WM_VSCROLL = 0x0115;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_PAINT = 0x000F;

    public event EventHandler? ScrollChanged;

    public EditorRichTextBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        DetectUrls = false;
        WordWrap = false;
        AcceptsTab = true;
        BorderStyle = BorderStyle.None;
        Font = new Font("Consolas", 10f);
        ScrollBars = RichTextBoxScrollBars.Both;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYDOWN or WM_PAINT)
        {
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
