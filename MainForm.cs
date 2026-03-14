using System.Text;
using SimpleText.Core;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Search;
using SimpleText.Core.Session;
using SimpleText.Core.Templates;
using SimpleText.Highlighting;

namespace SimpleText;

public partial class MainForm : Form
{
    private string? _currentFilePath;
    private bool _isDirty;
    private string? _pendingOpenFile;
    private bool _pendingSessionRestore;
    private string? _originalFileHash;
    private bool _sessionDirty;
    private System.Windows.Forms.Timer _sessionTimer = null!;
    private HighlightingManager _highlightingManager = null!;
    private string? _currentMode;

    public MainForm()
    {
        InitializeComponent();
        WireEvents();
        AllowDrop = true;
    }

    public void OpenFileOnLoad(string path) => _pendingOpenFile = path;
    public void RestoreSessionOnLoad() => _pendingSessionRestore = true;

    private void WireEvents()
    {
        // Form events
        Load += (_, _) =>
        {
            if (_pendingOpenFile != null)
                OpenFile(_pendingOpenFile);
            else if (_pendingSessionRestore)
                RestoreSession();
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
        BuildTemplateMenu();
        _openMenuItem.Click += (_, _) => OpenFileDialog();
        _saveMenuItem.Click += (_, _) => SaveFile();
        _saveAsMenuItem.Click += (_, _) => SaveFileAs();
        _closeMenuItem.Click += (_, _) => CloseFile();
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

        // Mode menu events
        _modePlainText.Click += (_, _) => SetMode(null);
        _modeMarkdown.Click += (_, _) => SetMode(TextModes.Markdown);
        _modeAsciiDoc.Click += (_, _) => SetMode(TextModes.AsciiDoc);
        _modeRst.Click += (_, _) => SetMode(TextModes.ReStructuredText);

        // Highlighting manager
        _highlightingManager = new HighlightingManager(_editor);

        // Session auto-save timer (5 seconds, crash protection)
        _sessionTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _sessionTimer.Tick += (_, _) =>
        {
            if (_sessionDirty)
            {
                SaveSession();
                _sessionDirty = false;
            }
        };
        _sessionTimer.Start();
    }

    // --- File Operations ---

    private void NewFile()
    {
        if (!PromptSaveIfDirty()) return;
        _editor.Clear();
        _currentFilePath = null;
        _originalFileHash = null;
        SetDirty(false);
        UpdateTitle();
        UpdateStatusBar();
        SetMode(null);
    }

    private void CloseFile()
    {
        if (!PromptSaveIfDirty()) return;
        _editor.Clear();
        _currentFilePath = null;
        _originalFileHash = null;
        SetDirty(false);
        UpdateTitle();
        UpdateStatusBar();
        SetMode(null);
    }

    private void BuildTemplateMenu()
    {
        string? lastCategory = null;
        foreach (var template in DocumentTemplates.All)
        {
            var category = template.Name.Split('—')[0].Trim();
            if (lastCategory != null && category != lastCategory)
                _newFromTemplateMenu.DropDownItems.Add(new ToolStripSeparator());
            lastCategory = category;

            var item = new ToolStripMenuItem(template.Name.Replace("—", "-"));
            item.Click += (_, _) => ApplyTemplate(template);
            _newFromTemplateMenu.DropDownItems.Add(item);
        }
    }

    private void ApplyTemplate(DocumentTemplate template)
    {
        if (!PromptSaveIfDirty()) return;
        _editor.TextChanged -= OnTextChanged;
        _editor.Text = template.Content;
        _editor.TextChanged += OnTextChanged;
        _currentFilePath = null;
        _originalFileHash = null;
        SetDirty(true);
        UpdateTitle();
        UpdateStatusBar();
        _lineNumberPanel.UpdateWidth();
        _lineNumberPanel.Invalidate();
        SetMode(template.Mode);
        _editor.SelectionStart = 0;
    }

    private void OpenFileDialog()
    {
        if (!PromptSaveIfDirty()) return;
        using var dlg = new OpenFileDialog
        {
            Filter = SupportedFileTypes.BuildWinFormsFilter(),
            FilterIndex = 5
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
            _originalFileHash = SessionManager.ComputeFileHash(path);
            SetDirty(false);
            UpdateTitle();
            UpdateStatusBar();
            _lineNumberPanel.UpdateWidth();
            _lineNumberPanel.Invalidate();
            _editor.SelectionStart = 0;
            var detectedMode = _highlightingManager.SetFilePath(_currentFilePath);
            _currentMode = detectedMode;
            UpdateModeMenuChecks();
            UpdateFileTypeStatus();
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
            Filter = SupportedFileTypes.BuildWinFormsFilter(),
            FilterIndex = 5
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
            var detectedMode = _highlightingManager.SetFilePath(_currentFilePath);
            _currentMode = detectedMode;
            UpdateModeMenuChecks();
            UpdateFileTypeStatus();
        }
    }

    private void WriteFile(string path)
    {
        try
        {
            File.WriteAllText(path, _editor.Text, new UTF8Encoding(false));
            _originalFileHash = SessionManager.ComputeFileHash(path);
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

    // --- Session Persistence ---

    private void SaveSession()
    {
        var data = new SessionData
        {
            FilePath = _currentFilePath,
            Content = _editor.Text,
            CursorPosition = _editor.SelectionStart,
            IsDirty = _isDirty,
            OriginalFileHash = _originalFileHash,
            Mode = _currentMode
        };
        SessionManager.Save(data);
    }

    private void RestoreSession()
    {
        var data = SessionManager.Load();
        if (data == null) return;

        // Check for external modification of the file
        if (data.FilePath != null && data.IsDirty && File.Exists(data.FilePath))
        {
            var currentHash = SessionManager.ComputeFileHash(data.FilePath);
            if (data.OriginalFileHash != null && currentHash != data.OriginalFileHash)
            {
                var result = MessageBox.Show(
                    $"The file \"{Path.GetFileName(data.FilePath)}\" was modified outside SimpleText.\n\n" +
                    "Do you want to reload the file from disk (losing your unsaved changes) " +
                    "or keep your session version?",
                    "External Modification Detected",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    // Reload from disk
                    OpenFile(data.FilePath);
                    return;
                }
            }
        }

        // Restore session state
        _editor.TextChanged -= OnTextChanged;
        _editor.Text = data.Content ?? "";
        _editor.TextChanged += OnTextChanged;
        _currentFilePath = data.FilePath;
        _originalFileHash = data.OriginalFileHash;

        if (data.CursorPosition <= _editor.TextLength)
            _editor.SelectionStart = data.CursorPosition;

        SetDirty(data.IsDirty);
        UpdateTitle();
        UpdateStatusBar();
        _lineNumberPanel.UpdateWidth();
        _lineNumberPanel.Invalidate();

        // Restore mode: use saved mode if present, otherwise auto-detect from file path
        if (data.Mode != null)
            SetMode(data.Mode);
        else
        {
            var detectedMode = _highlightingManager.SetFilePath(_currentFilePath);
            _currentMode = detectedMode;
            UpdateModeMenuChecks();
            UpdateFileTypeStatus();
        }
    }

    // --- Mode Selection ---

    private void SetMode(string? mode)
    {
        _currentMode = mode;
        _highlightingManager.SetMode(mode);
        UpdateModeMenuChecks();
        UpdateFileTypeStatus();
    }

    private void UpdateModeMenuChecks()
    {
        _modePlainText.Checked = _currentMode == null;
        _modeMarkdown.Checked = _currentMode == TextModes.Markdown;
        _modeAsciiDoc.Checked = _currentMode == TextModes.AsciiDoc;
        _modeRst.Checked = _currentMode == TextModes.ReStructuredText;
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

    private void UpdateFileTypeStatus()
    {
        _statusFileType.Text = _highlightingManager.FileTypeName;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (!_isDirty) SetDirty(true);
        _sessionDirty = true;
        _lineNumberPanel.UpdateWidth();
        _lineNumberPanel.Invalidate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Notepad++ behavior: always save session, no "save changes?" prompt on close
        _sessionTimer.Stop();
        SaveSession();
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
        int startIndex = _editor.SelectionStart + _editor.SelectionLength;
        var found = TextFinder.FindNext(_editor.Text, term, startIndex);
        if (found is { } index)
        {
            _editor.Select(index, term.Length);
            _editor.ScrollToCaret();
        }
        else if (!string.IsNullOrEmpty(term))
        {
            MessageBox.Show($"Cannot find \"{term}\"", "Find",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void FindPrevious()
    {
        var term = _findTextBox.Text;
        int startIndex = _editor.SelectionStart;
        var found = TextFinder.FindPrevious(_editor.Text, term, startIndex);
        if (found is { } index)
        {
            _editor.Select(index, term.Length);
            _editor.ScrollToCaret();
        }
        else if (!string.IsNullOrEmpty(term))
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
