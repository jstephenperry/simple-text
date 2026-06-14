using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using SimpleText.Avalonia.Services;
using SimpleText.Core;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Session;
using SimpleText.Core.Templates;

namespace SimpleText.Avalonia.Views;

public partial class MainWindow : Window
{
    // Must mirror EditorView's font defaults (AdjustFontSize clamps 8..32, default 14).
    private const int DefaultFontSize = 14;
    private const int MinFontSize = 8;
    private const int MaxFontSize = 32;

    private const string SessionFileName = "session.avalonia.json";

    private DispatcherTimer _sessionTimer = null!;
    private bool _sessionDirty;
    private string _lastFindTerm = string.Empty;
    private bool _dialogOpen;
    private bool _wordWrap;
    private int _fontSizeDelta;

    public MainWindow()
    {
        InitializeComponent();
        WireEvents();
        DragDrop.SetAllowDrop(this, true);
    }

    // --- Active tab / pane helpers ---

    private EditorView? ActivePane => (Tabs.SelectedItem as TabItem)?.Content as EditorView;

    private IEnumerable<EditorView> AllPanes =>
        Tabs.Items.OfType<TabItem>().Select(t => t.Content).OfType<EditorView>();

    private TabItem? FindTabFor(EditorView pane) =>
        Tabs.Items.OfType<TabItem>().FirstOrDefault(t => ReferenceEquals(t.Content, pane));

    private EditorView AddNewTab(bool select = true)
    {
        var pane = new EditorView();
        ApplyPaneDefaults(pane);
        var tab = CreateTab(pane);
        Tabs.Items.Add(tab);
        if (select)
        {
            Tabs.SelectedItem = tab;
            pane.FocusEditor();
        }
        _sessionDirty = true;
        return pane;
    }

    private TabItem CreateTab(EditorView pane)
    {
        var tab = new TabItem
        {
            Content = pane,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        UpdateTabHeader(tab);

        pane.DirtyChanged += (_, _) =>
        {
            UpdateTabHeader(tab);
            if (ReferenceEquals(pane, ActivePane))
                UpdateTitle();
            _sessionDirty = true;
        };
        pane.DocumentChanged += (_, _) => _sessionDirty = true;
        pane.CaretMoved += (_, _) =>
        {
            if (ReferenceEquals(pane, ActivePane))
                UpdateLineColStatus();
        };

        return tab;
    }

    private void ApplyPaneDefaults(EditorView pane)
    {
        pane.ApplyTheme(ActualThemeVariant);
        if (_wordWrap)
            pane.SetWordWrap(true);
        if (_fontSizeDelta != 0)
            pane.AdjustFontSize(_fontSizeDelta);
    }

    private void UpdateTabHeader(TabItem tab)
    {
        if (tab.Content is not EditorView pane) return;

        var header = new DockPanel { LastChildFill = true };

        var close = new Button
        {
            Content = "✕",
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            FontSize = 11,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        close.Click += async (_, _) => await CloseTabAsync(tab);
        DockPanel.SetDock(close, Dock.Right);

        var text = new TextBlock
        {
            Text = (pane.IsDirty ? "*" : string.Empty) + pane.FileName,
            VerticalAlignment = VerticalAlignment.Center,
        };

        header.Children.Add(close);
        header.Children.Add(text);

        tab.Header = header;
        ToolTip.SetTip(tab, pane.FilePath ?? pane.FileName);
    }

    private void RefreshPaneUi(EditorView pane)
    {
        if (FindTabFor(pane) is { } tab)
            UpdateTabHeader(tab);
        if (ReferenceEquals(pane, ActivePane))
            UpdateAllActiveUi();
        _sessionDirty = true;
    }

    private void UpdateAllActiveUi()
    {
        UpdateTitle();
        UpdateLineColStatus();
        UpdateStatusFileInfo();
        UpdateModeMenuChecks();
    }

    private void UpdateTitle()
    {
        var pane = ActivePane;
        Title = pane == null
            ? "SimpleText"
            : $"{(pane.IsDirty ? "*" : string.Empty)}{pane.FileName} - SimpleText";
    }

    private void UpdateLineColStatus()
    {
        var pane = ActivePane;
        if (pane == null)
        {
            StatusLineCol.Text = "Ln 1, Col 1";
            return;
        }
        var (line, column) = pane.GetCaretLineColumn();
        StatusLineCol.Text = $"Ln {line}, Col {column}";
    }

    private void UpdateStatusFileInfo()
    {
        var pane = ActivePane;
        StatusFileName.Text = pane?.FileName ?? "Untitled";
        StatusFileType.Text = pane?.FileTypeName ?? "Plain Text";
    }

    private void UpdateModeMenuChecks()
    {
        var mode = ActivePane?.Mode;
        ModePlainText.Icon = mode is null ? CreateCheckIcon() : null;
        ModeMarkdown.Icon = mode == TextModes.Markdown ? CreateCheckIcon() : null;
        ModeAsciiDoc.Icon = mode == TextModes.AsciiDoc ? CreateCheckIcon() : null;
        ModeRst.Icon = mode == TextModes.ReStructuredText ? CreateCheckIcon() : null;
    }

    // --- Wiring ---

    private void WireEvents()
    {
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        // Drag and drop
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // File menu
        NewMenuItem.Click += (_, _) => AddNewTab();
        BuildTemplateMenu();
        OpenMenuItem.Click += async (_, _) => await OpenFileDialogAsync();
        SaveMenuItem.Click += async (_, _) => await SaveActiveAsync();
        SaveAsMenuItem.Click += async (_, _) => await SaveActiveAsAsync();
        CloseMenuItem.Click += async (_, _) => await CloseActiveTabAsync();
        ExitMenuItem.Click += (_, _) => Close();

        // Tab strip
        AddTabButton.Click += (_, _) => AddNewTab();
        Tabs.SelectionChanged += OnTabSelectionChanged;

        // Mode menu
        ModePlainText.Click += (_, _) => SetActiveMode(null);
        ModeMarkdown.Click += (_, _) => SetActiveMode(TextModes.Markdown);
        ModeAsciiDoc.Click += (_, _) => SetActiveMode(TextModes.AsciiDoc);
        ModeRst.Click += (_, _) => SetActiveMode(TextModes.ReStructuredText);

        // View menu — theme
        ThemeSystem.Click += (_, _) => SetTheme(null);
        ThemeLight.Click += (_, _) => SetTheme(ThemeVariant.Light);
        ThemeDark.Click += (_, _) => SetTheme(ThemeVariant.Dark);

        // View menu — word wrap and zoom
        WordWrapMenuItem.Click += (_, _) => ToggleWordWrap();
        ZoomInMenuItem.Click += (_, _) => AdjustZoom(1);
        ZoomOutMenuItem.Click += (_, _) => AdjustZoom(-1);
        ResetZoomMenuItem.Click += (_, _) => AdjustZoom(0);

        // Info banner
        InfoBannerClose.Click += (_, _) => InfoBanner.IsVisible = false;

        // Find bar
        FindNextButton.Click += (_, _) => FindNext();
        FindPrevButton.Click += (_, _) => FindPrevious();
        FindCloseButton.Click += (_, _) => HideFindBar();
        FindTextBox.KeyDown += OnFindTextBoxKeyDown;
        FindTextBox.TextChanged += (_, _) => NoMatchesText.IsVisible = false;

        // Global keyboard shortcuts
        KeyDown += OnWindowKeyDown;

        // OS / app theme switch — re-apply highlight theme to every editor
        ActualThemeVariantChanged += (_, _) => ApplyThemeToAllPanes();

        // Session auto-save timer
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sessionTimer.Tick += (_, _) =>
        {
            if (_sessionDirty) { SaveWorkspaceSession(); _sessionDirty = false; }
        };
        _sessionTimer.Start();

        UpdateModeMenuChecks();
        UpdateThemeMenuChecks();
        UpdateWordWrapCheck();
    }

    private void OnWindowLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        // App wires the pending startup work (session restore + optional CLI file) before
        // the window is shown; run it now that the visual tree exists.
        try
        {
            _pendingStartup?.Invoke();
        }
        catch
        {
            // Startup must never crash; fall back to a single Untitled tab below.
        }
        finally
        {
            _pendingStartup = null;
        }

        if (Tabs.Items.Count == 0)
            AddNewTab();

        UpdateAllActiveUi();
        ActivePane?.FocusEditor();
    }

    // --- Startup hooks (called by App before the window is shown) ---

    private Action? _pendingStartup;

    /// <summary>
    /// Queues the startup sequence: restore the multi-tab workspace session, then open the
    /// optional command-line file as an extra tab. Runs on Loaded so the visual tree exists.
    /// </summary>
    public void QueueStartup(string? commandLineFile)
    {
        _pendingStartup = () =>
        {
            RestoreWorkspaceSession();
            if (commandLineFile != null && File.Exists(commandLineFile))
            {
                try { OpenPathCore(commandLineFile); }
                catch (Exception ex) { ShowInfoBanner($"Could not open \"{commandLineFile}\": {ex.Message}"); }
            }
        };
    }

    // --- Tab events / lifecycle ---

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _sessionDirty = true;
        UpdateAllActiveUi();
        // Clear the no-match hint — it referred to the previous document.
        NoMatchesText.IsVisible = false;
        ActivePane?.FocusEditor();
    }

    private async Task CloseActiveTabAsync()
    {
        if (Tabs.SelectedItem is TabItem tab)
            await CloseTabAsync(tab);
    }

    private async Task CloseTabAsync(TabItem tab)
    {
        if (tab.Content is EditorView pane && pane.IsDirty)
        {
            // Only one confirm dialog may be open; Ctrl+W can keep firing while awaiting.
            if (_dialogOpen)
                return;

            // Show the user what they are deciding about.
            if (!ReferenceEquals(Tabs.SelectedItem, tab))
                Tabs.SelectedItem = tab;

            var result = await ConfirmSaveAsync(pane.FileName);
            if (result == ConfirmResult.Cancel)
                return;
            if (result == ConfirmResult.Save && !await SaveActivePaneAsync(pane))
                return; // user cancelled the save picker
        }

        // The tab may have been removed while a dialog or save was awaiting.
        if (!Tabs.Items.Contains(tab))
            return;

        RemoveTab(tab);
    }

    private void RemoveTab(TabItem tab)
    {
        Tabs.Items.Remove(tab);
        if (Tabs.Items.Count == 0)
            AddNewTab();
        _sessionDirty = true;
    }

    // --- File operations ---

    private async Task OpenFileDialogAsync()
    {
        var storage = GetTopLevel(this)!.StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = true,
            FileTypeFilter = GetFileTypeFilters()
        });
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                await OpenPathAsync(path);
        }
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            OpenPathCore(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Could not open file:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Opens a file: activates an existing tab with the same path, reuses a pristine
    /// Untitled active tab, or opens a new tab. Loads before adding so an IO failure leaves
    /// no empty tab. Throws on IO failure.
    /// </summary>
    private void OpenPathCore(string path)
    {
        var fullPath = Path.GetFullPath(path);

        foreach (var tab in Tabs.Items.OfType<TabItem>())
        {
            if (tab.Content is EditorView existing
                && existing.FilePath is { } existingPath
                && string.Equals(Path.GetFullPath(existingPath), fullPath, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = tab;
                existing.FocusEditor();
                return;
            }
        }

        var active = ActivePane;
        if (active != null && !active.IsDirty && active.FilePath == null)
        {
            // Reuse the pristine Untitled active tab.
            active.LoadFromFile(fullPath);
            RefreshPaneUi(active);
            active.FocusEditor();
        }
        else
        {
            var pane = new EditorView();
            ApplyPaneDefaults(pane);
            pane.LoadFromFile(fullPath); // load before adding so a failure leaves no empty tab
            var tab = CreateTab(pane);
            Tabs.Items.Add(tab);
            Tabs.SelectedItem = tab;
            pane.FocusEditor();
        }

        _sessionDirty = true;
    }

    private async Task SaveActiveAsync()
    {
        if (ActivePane is { } pane)
            await SaveActivePaneAsync(pane);
    }

    private async Task SaveActiveAsAsync()
    {
        if (ActivePane is { } pane)
            await SavePaneAsAsync(pane);
    }

    private async Task<bool> SaveActivePaneAsync(EditorView pane)
    {
        if (pane.FilePath is not { } path)
            return await SavePaneAsAsync(pane);

        try
        {
            pane.SaveToFile(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Could not save file:\n{ex.Message}");
            return false;
        }

        RefreshPaneUi(pane);
        return true;
    }

    private async Task<bool> SavePaneAsAsync(EditorView pane)
    {
        var storage = GetTopLevel(this)!.StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = pane.FileName,
            FileTypeChoices = GetFileTypeFilters()
        });
        if (file == null)
            return false;

        if (file.TryGetLocalPath() is not { } path)
            return false;

        try
        {
            pane.SaveToFile(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync($"Could not save file:\n{ex.Message}");
            return false;
        }

        // The pane re-detects mode on path change; refresh tab header, menus, and status.
        RefreshPaneUi(pane);
        return true;
    }

    // --- Templates ---

    private void BuildTemplateMenu()
    {
        foreach (var group in DocumentTemplates.All.GroupBy(t => t.Category))
        {
            var categoryMenu = new MenuItem { Header = group.Key };
            foreach (var template in group)
            {
                var item = new MenuItem { Header = template.Variant };
                var captured = template;
                item.Click += (_, _) => ApplyTemplate(captured);
                categoryMenu.Items.Add(item);
            }
            NewFromTemplateMenu.Items.Add(categoryMenu);
        }
    }

    private void ApplyTemplate(DocumentTemplate template)
    {
        var pane = AddNewTab();
        pane.SetText(template.Content, markDirty: true);
        pane.SetMode(template.Mode);
        pane.CaretOffset = 0;
        RefreshPaneUi(pane);
        pane.FocusEditor();
    }

    // --- Mode menu ---

    private void SetActiveMode(string? mode)
    {
        if (ActivePane is not { } pane)
            return;
        pane.SetMode(mode);
        if (FindTabFor(pane) is { } tab)
            UpdateTabHeader(tab);
        UpdateModeMenuChecks();
        UpdateStatusFileInfo();
        _sessionDirty = true;
    }

    // --- Theme ---

    private void SetTheme(ThemeVariant? theme)
    {
        Application.Current!.RequestedThemeVariant = theme ?? ThemeVariant.Default;
        ThemeService.SavePreference(theme);
        // ActualThemeVariantChanged fans out to all panes; also fan out here in case the
        // effective variant did not change (e.g. System->Light while OS is already light).
        ApplyThemeToAllPanes();
        UpdateThemeMenuChecks();
    }

    private void ApplyThemeToAllPanes()
    {
        var variant = ActualThemeVariant;
        foreach (var pane in AllPanes)
            pane.ApplyTheme(variant);
    }

    private void UpdateThemeMenuChecks()
    {
        var current = Application.Current?.RequestedThemeVariant;
        ThemeSystem.Icon = (current == null || current == ThemeVariant.Default) ? CreateCheckIcon() : null;
        ThemeLight.Icon = current == ThemeVariant.Light ? CreateCheckIcon() : null;
        ThemeDark.Icon = current == ThemeVariant.Dark ? CreateCheckIcon() : null;
    }

    // --- Word wrap and zoom ---

    private void ToggleWordWrap()
    {
        _wordWrap = !_wordWrap;
        foreach (var pane in AllPanes)
            pane.SetWordWrap(_wordWrap);
        UpdateWordWrapCheck();
    }

    private void UpdateWordWrapCheck()
    {
        WordWrapMenuItem.Icon = _wordWrap ? CreateCheckIcon() : null;
    }

    private void AdjustZoom(int delta)
    {
        _fontSizeDelta = delta == 0
            ? 0
            : Math.Clamp(_fontSizeDelta + delta, MinFontSize - DefaultFontSize, MaxFontSize - DefaultFontSize);
        foreach (var pane in AllPanes)
            pane.AdjustFontSize(delta);
    }

    // --- Session persistence ---

    private void SaveWorkspaceSession()
    {
        var data = new WorkspaceSessionData
        {
            ActiveTabIndex = Math.Max(Tabs.SelectedIndex, 0),
            Tabs = AllPanes.Select(p => p.CaptureSession()).ToList(),
        };
        WorkspaceSessionManager.Save(data, SessionFileName);
    }

    /// <summary>
    /// Restores the multi-tab workspace session (Notepad++ style). Falls back to a single
    /// Untitled tab; wrapped so it can never crash startup.
    /// </summary>
    private void RestoreWorkspaceSession()
    {
        WorkspaceSessionData? data = null;
        try
        {
            data = WorkspaceSessionManager.Load(SessionFileName);
        }
        catch
        {
            // best-effort: fall through to a fresh Untitled tab
        }

        var conflict = false;

        try
        {
            if (data?.Tabs is { Count: > 0 } entries)
            {
                foreach (var entry in entries)
                {
                    if (entry == null)
                        continue; // hand-edited or truncated session JSON
                    try
                    {
                        var pane = new EditorView();
                        ApplyPaneDefaults(pane);
                        RestorePane(pane, entry, ref conflict);
                        Tabs.Items.Add(CreateTab(pane));
                    }
                    catch
                    {
                        // a bad entry must not crash startup
                    }
                }
                if (Tabs.Items.Count > 0)
                    Tabs.SelectedIndex = Math.Clamp(data.ActiveTabIndex, 0, Tabs.Items.Count - 1);
            }
        }
        catch
        {
            // whole-restore guard: leave whatever tabs were added; fallback handled below
        }

        if (Tabs.Items.Count == 0)
            AddNewTab();

        if (conflict)
            ShowInfoBanner("Some files changed on disk; your unsaved session copies were kept.");

        _sessionDirty = false;
    }

    private static void RestorePane(EditorView pane, SessionData entry, ref bool conflict)
    {
        if (entry.FilePath is { } missingPath && !File.Exists(missingPath))
        {
            // The file was deleted: the session copy is the only copy left, so mark it dirty
            // (RestoreFromSession honors IsDirty) to keep the close-prompt protection.
            entry.IsDirty = true;
            conflict = true;
        }
        else if (entry.FilePath is { } existingPath)
        {
            string? currentHash = null;
            try
            {
                currentHash = SessionManager.ComputeFileHash(existingPath);
            }
            catch
            {
                // unreadable file: keep the session copy
            }

            var changedOnDisk = entry.OriginalFileHash != null
                && currentHash != null
                && !string.Equals(currentHash, entry.OriginalFileHash, StringComparison.OrdinalIgnoreCase);

            if (changedOnDisk)
            {
                if (!entry.IsDirty)
                {
                    // No local edits to lose: load the externally changed file fresh.
                    try
                    {
                        pane.LoadFromFile(existingPath);
                        if (entry.Mode != null)
                            pane.SetMode(entry.Mode);
                        pane.CaretOffset = Math.Clamp(entry.CursorPosition, 0, pane.GetText().Length);
                        return;
                    }
                    catch
                    {
                        // fall back to the session copy
                    }
                }
                else
                {
                    conflict = true;
                }
            }
        }

        pane.RestoreFromSession(entry);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Notepad++ behavior: no save prompts on exit — the session preserves unsaved work.
        _sessionTimer.Stop();
        SaveWorkspaceSession();
    }

    // --- Find bar ---

    private void ShowFindBar()
    {
        FindBar.IsVisible = true;
        var selected = ActivePane?.GetSelectedText();
        if (!string.IsNullOrEmpty(selected))
            FindTextBox.Text = selected;
        NoMatchesText.IsVisible = false;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.IsVisible = false;
        ActivePane?.FocusEditor();
    }

    private void FindNext() => RunFind(forward: true);
    private void FindPrevious() => RunFind(forward: false);

    private void RunFind(bool forward)
    {
        var term = FindTextBox.Text;
        if (string.IsNullOrEmpty(term))
            term = _lastFindTerm;
        if (string.IsNullOrEmpty(term))
            return;
        _lastFindTerm = term;

        if (ActivePane is not { } pane)
            return;

        var found = forward ? pane.FindNext(term) : pane.FindPrevious(term);
        NoMatchesText.IsVisible = !found;
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

    // --- Drag and drop ---

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Deprecated API — DataTransfer replacement not yet stable
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy : DragDropEffects.None;
#pragma warning restore CS0618
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files == null) return;
        foreach (var item in files)
        {
            if (item.TryGetLocalPath() is { } path)
                await OpenPathAsync(path);
        }
    }

    // --- Keyboard handling ---

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var ctrlShift = e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift);

        if (ctrl && e.Key == Key.N)
        {
            AddNewTab(); e.Handled = true;
        }
        else if (ctrl && e.Key == Key.O)
        {
            _ = OpenFileDialogAsync(); e.Handled = true;
        }
        else if (ctrl && e.Key == Key.S)
        {
            _ = SaveActiveAsync(); e.Handled = true;
        }
        else if (ctrlShift && e.Key == Key.S)
        {
            _ = SaveActiveAsAsync(); e.Handled = true;
        }
        else if (ctrl && e.Key == Key.W)
        {
            _ = CloseActiveTabAsync(); e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F)
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
        else if (ctrl && e.Key is Key.OemPlus or Key.Add)
        {
            AdjustZoom(1); e.Handled = true;
        }
        else if (ctrl && e.Key is Key.OemMinus or Key.Subtract)
        {
            AdjustZoom(-1); e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D0)
        {
            AdjustZoom(0); e.Handled = true;
        }
    }

    // --- Confirm / error / info dialogs ---

    private enum ConfirmResult { Save, DontSave, Cancel }

    private async Task<ConfirmResult> ConfirmSaveAsync(string fileName)
    {
        _dialogOpen = true;
        try
        {
            var tcs = new TaskCompletionSource<ConfirmResult>();

            var saveButton = new Button { Content = "Save", MinWidth = 90 };
            var dontButton = new Button { Content = "Don't save", MinWidth = 90 };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 90 };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { saveButton, dontButton, cancelButton },
            };

            var dialog = new Window
            {
                Title = "SimpleText",
                Width = 400,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Save changes to {fileName}?",
                            TextWrapping = TextWrapping.Wrap,
                        },
                        buttons,
                    },
                },
            };

            void Complete(ConfirmResult result)
            {
                tcs.TrySetResult(result);
                dialog.Close();
            }

            saveButton.Click += (_, _) => Complete(ConfirmResult.Save);
            dontButton.Click += (_, _) => Complete(ConfirmResult.DontSave);
            cancelButton.Click += (_, _) => Complete(ConfirmResult.Cancel);
            // Closing via the title-bar X or Escape counts as Cancel.
            dialog.Closed += (_, _) => tcs.TrySetResult(ConfirmResult.Cancel);

            await dialog.ShowDialog(this);
            return await tcs.Task;
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var tcs = new TaskCompletionSource();
        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var dialog = new Window
        {
            Title = "Error",
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    okButton,
                },
            },
        };
        okButton.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) => tcs.TrySetResult();
        await dialog.ShowDialog(this);
        await tcs.Task;
    }

    private void ShowInfoBanner(string message)
    {
        InfoBannerText.Text = message;
        InfoBanner.IsVisible = true;
    }

    // --- Helpers ---

    private static FilePickerFileType[] GetFileTypeFilters() =>
        SupportedFileTypes.GetFilterEntries()
            .Select(e => new FilePickerFileType(e.Name) { Patterns = e.Patterns })
            .ToArray();

    private static Control CreateCheckIcon() =>
        new TextBlock { Text = "✓", FontSize = 12 };
}
