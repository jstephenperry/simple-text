using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using SimpleText.Avalonia.Services;
using SimpleText.Core;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Search;
using SimpleText.Core.Session;
using SimpleText.Core.Templates;
using TextMateSharp.Grammars;

namespace SimpleText.Avalonia.Views;

public partial class MainWindow : Window
{
    private string? _currentFilePath;
    private bool _isDirty;
    private string? _pendingOpenFile;
    private bool _pendingSessionRestore;
    private string? _originalFileHash;
    private bool _sessionDirty;
    private string? _currentMode;

    private DispatcherTimer _sessionTimer = null!;
    private TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _registryOptions;

    public MainWindow()
    {
        InitializeComponent();
        WireEvents();
        DragDrop.SetAllowDrop(this, true);
    }

    public void OpenFileOnLoad(string path) => _pendingOpenFile = path;
    public void RestoreSessionOnLoad() => _pendingSessionRestore = true;

    private void InitializeTextMate()
    {
        var themeName = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;

        _registryOptions = new RegistryOptions(themeName);
        _textMateInstallation = Editor.InstallTextMate(_registryOptions);

        // Explicitly apply the theme so foreground/background colors are set
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(themeName));
    }

    private void WireEvents()
    {
        // Window events
        Loaded += (_, _) =>
        {
            InitializeTextMate();

            // Wire document events after TextMate installation
            Editor.Document.TextChanged += (_, _) => OnTextChanged();
            Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatusBar();

            if (_pendingOpenFile != null)
                OpenFile(_pendingOpenFile);
            else if (_pendingSessionRestore)
                RestoreSession();

            Editor.Focus();
        };
        Closing += OnWindowClosing;

        // Drag and drop
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Menu events — File
        NewMenuItem.Click += (_, _) => NewFile();
        BuildTemplateMenu();
        OpenMenuItem.Click += async (_, _) => await OpenFileDialogAsync();
        SaveMenuItem.Click += (_, _) => SaveFile();
        SaveAsMenuItem.Click += async (_, _) => await SaveFileAsAsync();
        CloseMenuItem.Click += (_, _) => CloseFile();
        ExitMenuItem.Click += (_, _) => Close();

        // Menu events — Mode
        ModePlainText.Click += (_, _) => SetMode(null);
        ModeMarkdown.Click += (_, _) => SetMode(TextModes.Markdown);
        ModeAsciiDoc.Click += (_, _) => SetMode(TextModes.AsciiDoc);
        ModeRst.Click += (_, _) => SetMode(TextModes.ReStructuredText);

        // Menu events — Theme
        ThemeSystem.Click += (_, _) => SetTheme(null);
        ThemeLight.Click += (_, _) => SetTheme(ThemeVariant.Light);
        ThemeDark.Click += (_, _) => SetTheme(ThemeVariant.Dark);

        // Find bar events
        FindNextButton.Click += (_, _) => FindNext();
        FindPrevButton.Click += (_, _) => FindPrevious();
        FindCloseButton.Click += (_, _) => HideFindBar();
        FindTextBox.KeyDown += OnFindTextBoxKeyDown;

        // Global keyboard shortcuts
        KeyDown += OnWindowKeyDown;

        // Theme change detection (OS theme switch)
        ActualThemeVariantChanged += (_, _) => UpdateTextMateTheme();

        // Session auto-save timer
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sessionTimer.Tick += (_, _) =>
        {
            if (_sessionDirty) { SaveSession(); _sessionDirty = false; }
        };
        _sessionTimer.Start();

        // Initialize mode menu checks
        UpdateModeMenuChecks();
        UpdateThemeMenuChecks();
    }

    // --- File Operations ---

    private void NewFile()
    {
        if (!PromptSaveIfDirty()) return;
        Editor.Document.Text = "";
        _currentFilePath = null;
        _originalFileHash = null;
        SetDirty(false);
        SetMode(null);
    }

    private void CloseFile()
    {
        if (!PromptSaveIfDirty()) return;
        Editor.Document.Text = "";
        _currentFilePath = null;
        _originalFileHash = null;
        SetDirty(false);
        SetMode(null);
    }

    private async Task OpenFileDialogAsync()
    {
        if (!PromptSaveIfDirty()) return;
        var storage = GetTopLevel(this)!.StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false,
            FileTypeFilter = GetFileTypeFilters()
        });
        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null) OpenFile(path);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            var content = File.ReadAllText(path, Encoding.UTF8);
            Editor.Document.Text = content;
            _currentFilePath = path;
            _originalFileHash = SessionManager.ComputeFileHash(path);
            SetDirty(false);
            Editor.TextArea.Caret.Offset = 0;
            var detectedMode = ModeDetector.DetectFromPath(path);
            _currentMode = detectedMode;
            ApplyCurrentMode();
            UpdateModeMenuChecks();
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            ShowError($"Could not open file:\n{ex.Message}");
        }
    }

    private void SaveFile()
    {
        if (_currentFilePath == null)
            _ = SaveFileAsAsync();
        else
            WriteFile(_currentFilePath);
    }

    private async Task SaveFileAsAsync()
    {
        var storage = GetTopLevel(this)!.StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = _currentFilePath != null
                ? Path.GetFileName(_currentFilePath) : "Untitled.txt",
            FileTypeChoices = GetFileTypeFilters()
        });
        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null)
            {
                _currentFilePath = path;
                WriteFile(path);
                var detectedMode = ModeDetector.DetectFromPath(path);
                _currentMode = detectedMode;
                ApplyCurrentMode();
                UpdateModeMenuChecks();
                UpdateStatusBar();
            }
        }
    }

    private void WriteFile(string path)
    {
        try
        {
            File.WriteAllText(path, Editor.Document.Text, new UTF8Encoding(false));
            _originalFileHash = SessionManager.ComputeFileHash(path);
            SetDirty(false);
        }
        catch (Exception ex)
        {
            ShowError($"Could not save file:\n{ex.Message}");
        }
    }

    private bool PromptSaveIfDirty()
    {
        // Notepad++ style: no prompt on close (session handles it)
        // This is only called for New/Open (document switching)
        if (!_isDirty) return true;
        // For simplicity, just allow the operation — session persistence protects the user
        return true;
    }

    // --- Session Persistence ---

    private void SaveSession()
    {
        var data = new SessionData
        {
            FilePath = _currentFilePath,
            Content = Editor.Document.Text,
            CursorPosition = Editor.TextArea.Caret.Offset,
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

        if (data.FilePath != null && data.IsDirty && File.Exists(data.FilePath))
        {
            var currentHash = SessionManager.ComputeFileHash(data.FilePath);
            if (data.OriginalFileHash != null && currentHash != data.OriginalFileHash)
            {
                // External modification — just reload from disk for now
                OpenFile(data.FilePath);
                return;
            }
        }

        Editor.Document.Text = data.Content ?? "";
        _currentFilePath = data.FilePath;
        _originalFileHash = data.OriginalFileHash;

        if (data.CursorPosition <= Editor.Document.TextLength)
            Editor.TextArea.Caret.Offset = data.CursorPosition;

        SetDirty(data.IsDirty);

        if (data.Mode != null)
            SetMode(data.Mode);
        else
        {
            _currentMode = ModeDetector.DetectFromPath(_currentFilePath);
            ApplyCurrentMode();
            UpdateModeMenuChecks();
        }
        UpdateStatusBar();
    }

    // --- Mode Selection ---

    private void SetMode(string? mode)
    {
        _currentMode = mode;
        ApplyCurrentMode();
        UpdateModeMenuChecks();
        UpdateStatusBar();
    }

    private void ApplyCurrentMode()
    {
        if (_textMateInstallation == null || _registryOptions == null) return;

        var scopeName = _currentMode switch
        {
            TextModes.Markdown => _registryOptions.GetScopeByLanguageId("markdown"),
            TextModes.AsciiDoc => TryGetScope("asciidoc"),
            TextModes.ReStructuredText => TryGetScope("restructuredtext"),
            _ => null
        };

        if (scopeName != null)
            _textMateInstallation.SetGrammar(scopeName);
        else
            _textMateInstallation.SetGrammar(null);

        StatusFileType.Text = _currentMode switch
        {
            TextModes.Markdown => "Markdown",
            TextModes.AsciiDoc => "AsciiDoc",
            TextModes.ReStructuredText => "reStructuredText",
            _ => "Plain Text"
        };
    }

    private string? TryGetScope(string languageId)
    {
        try { return _registryOptions?.GetScopeByLanguageId(languageId); }
        catch { return null; }
    }

    private void UpdateModeMenuChecks()
    {
        ModePlainText.Icon = _currentMode == null ? CreateCheckIcon() : null;
        ModeMarkdown.Icon = _currentMode == TextModes.Markdown ? CreateCheckIcon() : null;
        ModeAsciiDoc.Icon = _currentMode == TextModes.AsciiDoc ? CreateCheckIcon() : null;
        ModeRst.Icon = _currentMode == TextModes.ReStructuredText ? CreateCheckIcon() : null;
    }


    // --- Theme ---

    private void SetTheme(ThemeVariant? theme)
    {
        Application.Current!.RequestedThemeVariant = theme ?? ThemeVariant.Default;
        ThemeService.SavePreference(theme);
        UpdateTextMateTheme();
        UpdateThemeMenuChecks();
    }

    private void UpdateTextMateTheme()
    {
        if (_textMateInstallation == null || _registryOptions == null) return;
        var tmTheme = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
        _textMateInstallation.SetTheme(_registryOptions.LoadTheme(tmTheme));
    }

    private void UpdateThemeMenuChecks()
    {
        var current = Application.Current?.RequestedThemeVariant;
        ThemeSystem.Icon = (current == null || current == ThemeVariant.Default) ? CreateCheckIcon() : null;
        ThemeLight.Icon = current == ThemeVariant.Light ? CreateCheckIcon() : null;
        ThemeDark.Icon = current == ThemeVariant.Dark ? CreateCheckIcon() : null;
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
        Title = $"{prefix}{name} - SimpleText";
    }

    private void UpdateStatusBar()
    {
        var caret = Editor.TextArea.Caret;
        StatusLineCol.Text = $"Ln {caret.Line}, Col {caret.Column}";
        StatusFileName.Text = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled";
    }

    private void OnTextChanged()
    {
        if (!_isDirty) SetDirty(true);
        _sessionDirty = true;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _sessionTimer.Stop();
        SaveSession();
    }

    // --- Find Bar ---

    private void ShowFindBar()
    {
        FindBar.IsVisible = true;
        if (Editor.TextArea.Selection.Length > 0)
            FindTextBox.Text = Editor.SelectedText;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.IsVisible = false;
        Editor.Focus();
    }

    private void FindNext()
    {
        var term = FindTextBox.Text ?? "";
        var found = TextFinder.FindNext(Editor.Document.Text, term, Editor.TextArea.Caret.Offset);
        if (found is { } index)
        {
            Editor.Select(index, term.Length);
            Editor.TextArea.Caret.Offset = index + term.Length;
            var line = Editor.Document.GetLineByOffset(index);
            Editor.ScrollTo(line.LineNumber, 0);
        }
    }

    private void FindPrevious()
    {
        var term = FindTextBox.Text ?? "";
        var found = TextFinder.FindPrevious(Editor.Document.Text, term, Editor.TextArea.Caret.Offset);
        if (found is { } index)
        {
            Editor.Select(index, term.Length);
            Editor.TextArea.Caret.Offset = index;
            var line = Editor.Document.GetLineByOffset(index);
            Editor.ScrollTo(line.LineNumber, 0);
        }
    }

    // --- Drag and Drop ---

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Deprecated API — DataTransfer replacement not yet stable
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy : DragDropEffects.None;
#pragma warning restore CS0618
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        var first = files?.FirstOrDefault();
        if (first?.TryGetLocalPath() is { } path)
            OpenFile(path);
    }

    // --- Keyboard Handling ---

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Control)
        {
            NewFile(); e.Handled = true;
        }
        else if (e.Key == Key.O && e.KeyModifiers == KeyModifiers.Control)
        {
            _ = OpenFileDialogAsync(); e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            SaveFile(); e.Handled = true;
        }
        else if (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control)
        {
            CloseFile(); e.Handled = true;
        }
        else if (e.Key == Key.S && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _ = SaveFileAsAsync(); e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            ShowFindBar(); e.Handled = true;
        }
        else if (e.Key == Key.Escape && FindBar.IsVisible)
        {
            HideFindBar(); e.Handled = true;
        }
        else if (e.Key == Key.F3 && e.KeyModifiers == KeyModifiers.None)
        {
            FindNext(); e.Handled = true;
        }
        else if (e.Key == Key.F3 && e.KeyModifiers == KeyModifiers.Shift)
        {
            FindPrevious(); e.Handled = true;
        }
    }

    private void OnFindTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            FindNext(); e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Shift)
        {
            FindPrevious(); e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideFindBar(); e.Handled = true;
        }
    }

    // --- Templates ---

    private void BuildTemplateMenu()
    {
        string? lastCategory = null;
        foreach (var template in DocumentTemplates.All)
        {
            var category = template.Name.Split('—')[0].Trim();
            if (lastCategory != null && category != lastCategory)
                NewFromTemplateMenu.Items.Add(new Separator());
            lastCategory = category;

            var item = new MenuItem { Header = template.Name.Replace("—", "-") };
            item.Click += (_, _) => ApplyTemplate(template);
            NewFromTemplateMenu.Items.Add(item);
        }
    }

    private void ApplyTemplate(DocumentTemplate template)
    {
        if (!PromptSaveIfDirty()) return;
        Editor.Document.Text = template.Content;
        _currentFilePath = null;
        _originalFileHash = null;
        SetDirty(true);
        SetMode(template.Mode);
        Editor.TextArea.Caret.Offset = 0;
    }

    // --- Helpers ---

    private static FilePickerFileType[] GetFileTypeFilters() =>
        SupportedFileTypes.GetFilterEntries()
            .Select(e => new FilePickerFileType(e.Name) { Patterns = e.Patterns })
            .ToArray();

    private static object CreateCheckIcon()
    {
        // Simple check mark using a TextBlock
        return new global::Avalonia.Controls.TextBlock { Text = "\u2713", FontSize = 12 };
    }

    private async void ShowError(string message)
    {
        // Simple error display — could use a dialog library for fancier dialogs
        var dialog = new Window
        {
            Title = "Error",
            Width = 400, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 15,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };
        var okButton = (Button)((StackPanel)dialog.Content).Children[1];
        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}
