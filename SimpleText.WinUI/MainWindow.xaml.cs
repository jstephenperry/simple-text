using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SimpleText.Core;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Session;
using SimpleText.Core.Templates;
using SimpleText.WinUI.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace SimpleText.WinUI;

public sealed partial class MainWindow : Window
{
    // Must match the EditorPane defaults (AdjustFontSize clamps 8..32, default 14).
    private const int DefaultFontSize = 14;
    private const int MinFontSize = 8;
    private const int MaxFontSize = 32;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sessionTimer;
    private bool _sessionDirty;
    private string _lastFindTerm = string.Empty;
    private bool _modalDialogOpen;
    private bool _wordWrap;
    private int _fontSizeDelta;
    private string? _themePreference;

    private const string SessionFileName = "session.winui.json";

    public MainWindow()
    {
        InitializeComponent();

        // Mica backdrop — the standard Windows 11 window material.
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        Title = "Untitled - SimpleText";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 700));
        TrySetWindowIcon();

        BuildTemplateMenu();
        TemplateCatalog.Shared.Changed += OnTemplatesChanged;
        AddOemZoomAccelerators();

        RootGrid.ActualThemeChanged += (_, _) => ApplyEditorThemeToAllPanes();
        Closed += OnWindowClosed;
        Activated += OnFirstActivated;

        StartSessionTimer();
    }

    // --- Pane / tab helpers ---

    private EditorPane? ActivePane => (Tabs.SelectedItem as TabViewItem)?.Content as EditorPane;

    private IEnumerable<EditorPane> AllPanes
        => Tabs.TabItems.OfType<TabViewItem>().Select(t => t.Content).OfType<EditorPane>();

    private TabViewItem? FindTabFor(EditorPane pane)
        => Tabs.TabItems.OfType<TabViewItem>().FirstOrDefault(t => ReferenceEquals(t.Content, pane));

    private EditorPane AddNewTab(bool select = true)
    {
        var pane = new EditorPane();
        ApplyPaneDefaults(pane);
        var tab = CreateTab(pane);
        Tabs.TabItems.Add(tab);
        if (select)
        {
            Tabs.SelectedItem = tab;
            pane.FocusEditor();
        }
        _sessionDirty = true;
        return pane;
    }

    private TabViewItem CreateTab(EditorPane pane)
    {
        var tab = new TabViewItem
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

    private void ApplyPaneDefaults(EditorPane pane)
    {
        pane.ApplyEditorTheme(RootGrid.ActualTheme);
        if (_wordWrap)
            pane.SetWordWrap(true);
        if (_fontSizeDelta != 0)
            pane.AdjustFontSize(_fontSizeDelta);
    }

    private static void UpdateTabHeader(TabViewItem tab)
    {
        if (tab.Content is not EditorPane pane) return;
        tab.Header = (pane.IsDirty ? "*" : string.Empty) + pane.FileName;
        ToolTipService.SetToolTip(tab, pane.FilePath);
    }

    private void RefreshPaneUi(EditorPane pane)
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
        ModePlainTextItem.IsChecked = mode is null;
        ModeMarkdownItem.IsChecked = mode == TextModes.Markdown;
        ModeAsciiDocItem.IsChecked = mode == TextModes.AsciiDoc;
        ModeRstItem.IsChecked = mode == TextModes.ReStructuredText;
    }

    // --- TabView events ---

    private void OnAddTabButtonClick(TabView sender, object args) => AddNewTab();

    private async void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        => await CloseTabAsync(args.Tab);

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _sessionDirty = true;
        UpdateAllActiveUi();
        ActivePane?.FocusEditor();
    }

    private void OnTabItemsChanged(TabView sender, Windows.Foundation.Collections.IVectorChangedEventArgs args)
        => _sessionDirty = true;

    private async Task CloseTabAsync(TabViewItem tab)
    {
        if (tab.Content is EditorPane pane && pane.IsDirty)
        {
            // Only one ContentDialog may be open per root; accelerators keep firing
            // while the dialog awaits, so drop re-entrant close requests.
            if (_modalDialogOpen)
                return;

            // Show the user what they are deciding about.
            if (!ReferenceEquals(Tabs.SelectedItem, tab))
                Tabs.SelectedItem = tab;

            var dialog = new ContentDialog
            {
                Title = "SimpleText",
                Content = $"Save changes to {pane.FileName}?",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Do not save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot,
            };
            _modalDialogOpen = true;
            ContentDialogResult result;
            try
            {
                result = await dialog.ShowAsync();
            }
            finally
            {
                _modalDialogOpen = false;
            }
            if (result == ContentDialogResult.None)
                return; // Cancel
            if (result == ContentDialogResult.Primary && !await SavePaneAsync(pane))
                return; // user cancelled the save dialog
        }

        // The tab may have been removed while a dialog or save was awaiting.
        if (!Tabs.TabItems.Contains(tab))
            return;

        RemoveTab(tab);
    }

    private void RemoveTab(TabViewItem tab)
    {
        Tabs.TabItems.Remove(tab);
        if (Tabs.TabItems.Count == 0)
            AddNewTab();
        _sessionDirty = true;
    }

    // --- File menu ---

    private void OnNewClick(object sender, RoutedEventArgs e) => AddNewTab();

    private async void OnOpenClick(object sender, RoutedEventArgs e) => await OpenFileDialogAsync();

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
            await SavePaneAsync(pane);
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        if (ActivePane is { } pane)
            await SavePaneAsAsync(pane);
    }

    private async void OnCloseTabClick(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabViewItem tab)
            await CloseTabAsync(tab);
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    // --- File operations ---

    private async Task OpenFileDialogAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        InitializePickerWithWindow(picker);
        foreach (var extension in GetOpenPickerExtensions())
            picker.FileTypeFilter.Add(extension);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
            await OpenPathAsync(file.Path);
    }

    private static IEnumerable<string> GetOpenPickerExtensions()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, patterns) in SupportedFileTypes.GetFilterEntries())
        {
            foreach (var pattern in patterns)
            {
                // "*.txt" -> ".txt"; the all-files pattern (yielded as "*" by
                // GetFilterEntries) must stay "*" — FileTypeFilter.Add("") throws.
                var extension = pattern is "*" or "*.*" ? "*" : pattern.TrimStart('*');
                if (extension.Length == 0)
                    continue;
                if (seen.Add(extension))
                    yield return extension;
            }
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
            await ShowErrorDialogAsync($"Could not open file:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Opens a file: activates an existing tab with the same path, reuses a pristine
    /// Untitled active tab, or opens a new tab. Throws on I/O failure.
    /// </summary>
    private void OpenPathCore(string path)
    {
        var fullPath = Path.GetFullPath(path);

        foreach (var tab in Tabs.TabItems.OfType<TabViewItem>())
        {
            if (tab.Content is EditorPane existing
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
            // Reuse the pristine Untitled tab
            active.LoadFromFile(fullPath);
            RefreshPaneUi(active);
            active.FocusEditor();
        }
        else
        {
            var pane = new EditorPane();
            ApplyPaneDefaults(pane);
            pane.LoadFromFile(fullPath); // load before adding so a failure leaves no empty tab
            var tab = CreateTab(pane);
            Tabs.TabItems.Add(tab);
            Tabs.SelectedItem = tab;
            pane.FocusEditor();
        }

        _sessionDirty = true;
    }

    private async Task<bool> SavePaneAsync(EditorPane pane)
    {
        if (pane.FilePath is not { } path)
            return await SavePaneAsAsync(pane);

        try
        {
            pane.SaveToFile(path);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Could not save file:\n{ex.Message}");
            return false;
        }

        RefreshPaneUi(pane);
        return true;
    }

    private async Task<bool> SavePaneAsAsync(EditorPane pane)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = pane.FileName,
        };
        InitializePickerWithWindow(picker);
        foreach (var fileType in SupportedFileTypes.All)
            picker.FileTypeChoices.Add(fileType.DisplayName, fileType.AllExtensions.Select(e => "." + e).ToList());
        // "." means no enforced extension, so arbitrary extensions are not rewritten to .txt.
        picker.FileTypeChoices.Add("All Files", new List<string> { "." });

        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return false;

        try
        {
            pane.SaveToFile(file.Path);
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Could not save file:\n{ex.Message}");
            return false;
        }

        // The pane re-detects its mode from the new path; refresh menus/status/tab header.
        RefreshPaneUi(pane);
        return true;
    }

    private void InitializePickerWithWindow(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    // --- Templates ---

    private void BuildTemplateMenu()
    {
        NewFromTemplateMenu.Items.Clear();
        foreach (var group in TemplateCatalog.Shared.All.GroupBy(t => t.Category))
        {
            var categoryMenu = new MenuFlyoutSubItem { Text = group.Key };
            foreach (var template in group)
            {
                var item = new MenuFlyoutItem { Text = template.Variant };
                item.Click += (_, _) => ApplyTemplate(template);
                categoryMenu.Items.Add(item);
            }
            NewFromTemplateMenu.Items.Add(categoryMenu);
        }
    }

    private void OnTemplatesChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(BuildTemplateMenu);

    private void ApplyTemplate(DocumentTemplate template)
    {
        var pane = AddNewTab();
        pane.SetText(template.Content, markDirty: true);
        pane.SetMode(template.Mode);
        pane.CaretPosition = 0;
        RefreshPaneUi(pane);
        pane.FocusEditor();
    }

    private async void OnOpenTemplatesFolderClick(object sender, RoutedEventArgs e)
    {
        var path = TemplateCatalog.Shared.UserTemplatesDirectory;
        Directory.CreateDirectory(path);
        var folder = await StorageFolder.GetFolderFromPathAsync(path);
        await Launcher.LaunchFolderAsync(folder);
    }

    // --- Mode menu ---

    private void OnModePlainTextClick(object sender, RoutedEventArgs e) => SetActiveMode(null);
    private void OnModeMarkdownClick(object sender, RoutedEventArgs e) => SetActiveMode(TextModes.Markdown);
    private void OnModeAsciiDocClick(object sender, RoutedEventArgs e) => SetActiveMode(TextModes.AsciiDoc);
    private void OnModeRstClick(object sender, RoutedEventArgs e) => SetActiveMode(TextModes.ReStructuredText);

    private void SetActiveMode(string? mode)
    {
        if (ActivePane is not { } pane)
            return;
        pane.SetMode(mode);
        UpdateModeMenuChecks();
        UpdateStatusFileInfo();
        _sessionDirty = true;
    }

    // --- View menu: theme ---

    private void OnThemeSystemClick(object sender, RoutedEventArgs e) => SetThemePreference(null);
    private void OnThemeLightClick(object sender, RoutedEventArgs e) => SetThemePreference("Light");
    private void OnThemeDarkClick(object sender, RoutedEventArgs e) => SetThemePreference("Dark");

    private void SetThemePreference(string? preference)
    {
        SaveThemePreference(preference);
        ApplyThemePreference(preference);
    }

    /// <summary>
    /// Applies a persisted theme preference ("Light", "Dark", or null for System)
    /// to the window content and every editor pane. Does not persist.
    /// </summary>
    public void ApplyThemePreference(string? preference)
    {
        _themePreference = preference;
        RootGrid.RequestedTheme = preference switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateThemeMenuChecks();
        ApplyEditorThemeToAllPanes();
    }

    // Local adapter around the ThemeService contract so any signature drift is a one-line fix.
    private static void SaveThemePreference(string? preference)
        => Services.ThemeService.SavePreference(preference);

    private void ApplyEditorThemeToAllPanes()
    {
        var effective = RootGrid.ActualTheme; // resolves System to Light/Dark
        foreach (var pane in AllPanes)
            pane.ApplyEditorTheme(effective);
    }

    private void UpdateThemeMenuChecks()
    {
        ThemeSystemItem.IsChecked = _themePreference == null;
        ThemeLightItem.IsChecked = _themePreference == "Light";
        ThemeDarkItem.IsChecked = _themePreference == "Dark";
    }

    // --- View menu: word wrap and zoom ---

    private void OnWordWrapClick(object sender, RoutedEventArgs e)
    {
        _wordWrap = WordWrapItem.IsChecked;
        foreach (var pane in AllPanes)
            pane.SetWordWrap(_wordWrap);
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => AdjustZoom(1);
    private void OnZoomOutClick(object sender, RoutedEventArgs e) => AdjustZoom(-1);
    private void OnResetZoomClick(object sender, RoutedEventArgs e) => AdjustZoom(0);

    private void AdjustZoom(int delta)
    {
        _fontSizeDelta = delta == 0
            ? 0
            : Math.Clamp(_fontSizeDelta + delta, MinFontSize - DefaultFontSize, MaxFontSize - DefaultFontSize);
        foreach (var pane in AllPanes)
            pane.AdjustFontSize(delta);
    }

    private void AddOemZoomAccelerators()
    {
        // The main-row +/- keys are OEM virtual keys with no VirtualKey enum names,
        // so they cannot be declared in XAML. Numpad Add/Subtract are declared there.
        const VirtualKey oemPlus = (VirtualKey)0xBB;
        const VirtualKey oemMinus = (VirtualKey)0xBD;
        ZoomInItem.KeyboardAccelerators.Add(
            new KeyboardAccelerator { Key = oemPlus, Modifiers = VirtualKeyModifiers.Control });
        ZoomOutItem.KeyboardAccelerators.Add(
            new KeyboardAccelerator { Key = oemMinus, Modifiers = VirtualKeyModifiers.Control });
    }

    // --- Find bar ---

    private void OnShowFindInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ShowFindBar();
        args.Handled = true;
    }

    private void OnNewTabInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        AddNewTab();
        args.Handled = true;
    }

    private void OnFindNextInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FindNext();
        args.Handled = true;
    }

    private void OnFindPreviousInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FindPrevious();
        args.Handled = true;
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FindBar.Visibility == Visibility.Visible)
        {
            HideFindBar();
            args.Handled = true;
        }
        // Otherwise leave unhandled so Escape passes through.
    }

    private void ShowFindBar()
    {
        FindBar.Visibility = Visibility.Visible;
        var selected = ActivePane?.GetSelectedText();
        if (!string.IsNullOrEmpty(selected))
            FindTextBox.Text = selected;
        NoMatchesText.Visibility = Visibility.Collapsed;
        FindTextBox.Focus(FocusState.Programmatic);
        FindTextBox.SelectAll();
    }

    private void HideFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
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
        NoMatchesText.Visibility = found ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnFindNextClick(object sender, RoutedEventArgs e) => FindNext();
    private void OnFindPreviousClick(object sender, RoutedEventArgs e) => FindPrevious();
    private void OnFindCloseClick(object sender, RoutedEventArgs e) => HideFindBar();

    private void OnFindTextChanged(object sender, TextChangedEventArgs e)
        => NoMatchesText.Visibility = Visibility.Collapsed;

    private void OnFindTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;
        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        var shiftDown = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (shiftDown)
            FindPrevious();
        else
            FindNext();
        e.Handled = true;
    }

    // --- Drag and drop ---

    private void OnRootDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void OnRootDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var file in items.OfType<StorageFile>())
            await OpenPathAsync(file.Path);
    }

    // --- Session persistence ---

    private void StartSessionTimer()
    {
        _sessionTimer = this.DispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromSeconds(5);
        _sessionTimer.Tick += (_, _) =>
        {
            if (!_sessionDirty)
                return;
            SaveWorkspaceSession();
            _sessionDirty = false;
        };
        _sessionTimer.Start();
    }

    public void SaveWorkspaceSession()
    {
        var data = new WorkspaceSessionData
        {
            ActiveTabIndex = Math.Max(Tabs.SelectedIndex, 0),
            Tabs = AllPanes.Select(p => p.CaptureSession()).ToList(),
        };
        WorkspaceSessionManager.Save(data, SessionFileName);
    }

    /// <summary>
    /// Restores the multi-tab workspace session (Notepad++ style). Called once at startup
    /// by App before the window is activated. Falls back to a single Untitled tab.
    /// </summary>
    public void RestoreWorkspaceSession()
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
        var missing = false;
        var dropped = false;
        if (data?.Tabs is { Count: > 0 } entries)
        {
            foreach (var entry in entries)
            {
                if (entry == null)
                    continue; // hand-edited or truncated session JSON
                try
                {
                    var pane = new EditorPane();
                    ApplyPaneDefaults(pane);
                    RestorePane(pane, entry, ref conflict, ref missing);
                    Tabs.TabItems.Add(CreateTab(pane));
                }
                catch
                {
                    dropped = true; // a bad entry must not crash startup
                }
            }
            if (Tabs.TabItems.Count > 0)
                Tabs.SelectedIndex = Math.Clamp(data.ActiveTabIndex, 0, Tabs.TabItems.Count - 1);
        }

        if (Tabs.TabItems.Count == 0)
            AddNewTab();

        var notes = new List<string>();
        if (conflict)
            notes.Add("Some files changed on disk; your unsaved session copies were kept.");
        if (missing)
            notes.Add("Some files no longer exist on disk; their content was kept in the editor.");
        if (dropped)
            notes.Add("Some session tabs could not be restored and were dropped.");
        if (notes.Count > 0)
            ShowInfoBar(string.Join(" ", notes));

        _sessionDirty = false;
        UpdateAllActiveUi();
    }

    /// <summary>Adds an Untitled tab if session restore left the window with no tabs.</summary>
    public void EnsureFallbackTab()
    {
        if (Tabs.TabItems.Count == 0)
            AddNewTab();
    }

    private static void RestorePane(EditorPane pane, SessionData entry, ref bool conflict, ref bool missing)
    {
        if (entry.FilePath is { } path && !File.Exists(path))
        {
            // The file was deleted: the session copy is the only copy left, so mark it
            // dirty (RestoreFromSession honors IsDirty) to keep the close-prompt protection.
            entry.IsDirty = true;
            missing = true;
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
                    // No local edits to lose: load the externally changed file fresh from disk.
                    try
                    {
                        pane.LoadFromFile(existingPath);
                        // Keep the persisted manual mode and caret instead of the
                        // LoadFromFile defaults (auto-detected mode, caret at 0).
                        if (entry.Mode != null)
                            pane.SetMode(entry.Mode);
                        pane.CaretPosition = Math.Clamp(entry.CursorPosition, 0, pane.GetText().Length);
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

    /// <summary>
    /// Opens the command-line file after session restore. Errors surface in the InfoBar
    /// because dialogs are not available before the window is activated.
    /// </summary>
    public void OpenFileAtStartup(string path)
    {
        try
        {
            OpenPathCore(path);
        }
        catch (Exception ex)
        {
            ShowInfoBar($"Could not open \"{path}\": {ex.Message}");
        }
    }

    /// <summary>
    /// Handles an activation redirected from a second app instance: opens the requested
    /// file (if any) and brings this window to the foreground. Must run on the UI thread.
    /// </summary>
    public void HandleRedirectedActivation(IReadOnlyList<string> filePaths)
    {
        foreach (var filePath in filePaths)
            _ = OpenPathAsync(filePath);
        BringToForeground();
    }

    private void BringToForeground()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
            { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        AppWindow.Show();
        Activate();
        // Activate alone does not steal foreground from the (now exiting) second instance.
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ShowInfoBar(string message)
    {
        SessionInfoBar.Message = message;
        SessionInfoBar.IsOpen = true;
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }
        catch
        {
            // Icon is cosmetic; never let it block window creation.
        }
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            return;
        Activated -= OnFirstActivated;
        ActivePane?.FocusEditor();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // Notepad++ behavior: no save prompts on exit — the session preserves unsaved work.
        TemplateCatalog.Shared.Changed -= OnTemplatesChanged;
        _sessionTimer?.Stop();
        SaveWorkspaceSession();
    }

    // --- Helpers ---

    private async Task ShowErrorDialogAsync(string message)
    {
        if (RootGrid.XamlRoot == null || _modalDialogOpen)
            return;
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        };
        _modalDialogOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _modalDialogOpen = false;
        }
    }
}
