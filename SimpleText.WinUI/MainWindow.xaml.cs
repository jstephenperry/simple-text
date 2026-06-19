using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SimpleText.Core;
using SimpleText.Core.Elements;
using SimpleText.Core.FileTypes;
using SimpleText.Core.Session;
using SimpleText.Core.Templates;
using SimpleText.WinUI.Controls;
using SimpleText.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
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

    // Minimum window size in physical pixels (WinUI has no built-in minimum).
    private const int MinWindowWidth = 500;
    private const int MinWindowHeight = 360;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _sessionTimer;
    private bool _sessionDirty;
    private string _lastFindTerm = string.Empty;
    private bool _modalDialogOpen;
    private bool _wordWrap;
    private int _fontSizeDelta;
    private string? _themePreference;

    // Tracked window placement: last non-maximized bounds, plus the maximized flag.
    private RectInt32 _normalBounds;
    private bool _isMaximized;
    private bool _enforcingMinSize;

    private const string SessionFileName = "session.winui.json";

    public MainWindow()
    {
        InitializeComponent();

        // Mica backdrop — the standard Windows 11 window material.
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        // Extend that material into the title bar so the caption is theme-aware Mica
        // rather than a flat white bar. AppTitleBar is the draggable region.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyTitleBarColors();

        Title = "Untitled - SimpleText";
        RestoreWindowPlacement();
        AppWindow.Changed += OnAppWindowChanged;
        TrySetWindowIcon();

        BuildTemplateMenu();
        BuildInsertMenu();
        TemplateCatalog.Shared.Changed += OnTemplatesChanged;
        AddOemZoomAccelerators();

        RootGrid.ActualThemeChanged += (_, _) =>
        {
            ApplyEditorThemeToAllPanes();
            ApplyTitleBarColors();
        };
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
        BuildInsertMenu();
    }

    private void UpdateTitle()
    {
        var pane = ActivePane;
        var title = pane == null
            ? "SimpleText"
            : $"{(pane.IsDirty ? "*" : string.Empty)}{pane.FileName} - SimpleText";
        Title = title;            // taskbar / Alt+Tab
        AppTitleText.Text = title; // visible custom title bar
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

    private async void OnExportToPdfClick(object sender, RoutedEventArgs e)
    {
        if (ActivePane is not { } pane) return;
        
        var mode = pane.Mode;
        if (mode != TextModes.Markdown && mode != TextModes.AsciiDoc)
        {
            await ShowErrorDialogAsync("Export to PDF is only supported for Markdown and AsciiDoc.");
            return;
        }

        if (pane.IsDirty || string.IsNullOrEmpty(pane.FilePath))
        {
            var saved = await SavePaneAsync(pane);
            if (!saved) return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(pane.FilePath) + ".pdf",
        };
        InitializePickerWithWindow(picker);
        picker.FileTypeChoices.Add("PDF Document", new List<string> { ".pdf" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        string scriptName = mode == TextModes.Markdown ? "ExportMarkdownToPdf.ps1" : "ExportAsciiDocToPdf.ps1";
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", scriptName);

        if (!File.Exists(scriptPath))
        {
            await ShowErrorDialogAsync($"Export script not found: {scriptPath}");
            return;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -InputFile \"{pane.FilePath}\" -OutputFile \"{file.Path}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            ShowInfoBar($"Exporting {Path.GetFileName(pane.FilePath)} to PDF...", InfoBarSeverity.Informational);

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    SessionInfoBar.IsOpen = false;
                    string error = await process.StandardError.ReadToEndAsync();
                    await ShowErrorDialogAsync($"Export failed:\n{error}");
                }
                else
                {
                    ShowInfoBar($"Successfully exported to {file.Name}", InfoBarSeverity.Success);
                }
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Export failed:\n{ex.Message}");
        }
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
        ResetMenu(NewFromTemplateMenu.Items);
        var categoryNodes = new Dictionary<string, MenuFlyoutSubItem>();

        MenuFlyoutSubItem GetOrCreateCategory(string categoryPath)
        {
            if (categoryNodes.TryGetValue(categoryPath, out var existing))
                return existing;

            var parts = categoryPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            
            MenuFlyoutSubItem currentMenu = null;
            string currentPath = "";
            
            foreach (var part in parts)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + Path.DirectorySeparatorChar + part;
                
                if (!categoryNodes.TryGetValue(currentPath, out var subMenu))
                {
                    subMenu = new MenuFlyoutSubItem { Text = part };
                    categoryNodes[currentPath] = subMenu;
                    
                    if (currentMenu == null)
                        NewFromTemplateMenu.Items.Add(subMenu);
                    else
                        currentMenu.Items.Add(subMenu);
                }
                currentMenu = subMenu;
            }
            
            return currentMenu;
        }

        foreach (var group in TemplateCatalog.Shared.All.GroupBy(t => t.Category))
        {
            var categoryMenu = GetOrCreateCategory(group.Key);
            foreach (var template in group)
            {
                var item = new MenuFlyoutItem { Text = template.Variant };
                item.Click += (_, _) => ApplyTemplate(template);
                categoryMenu.Items.Add(item);
            }
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

    // --- Help ---

    private async void OnUserManualClick(object sender, RoutedEventArgs e)
    {
        // The manual is shipped next to the app (see the .csproj) and opened as a normal
        // tab, so it renders with Markdown highlighting and supports Find/zoom.
        var path = Path.Combine(AppContext.BaseDirectory, "Help", "SimpleText User Guide.md");
        if (File.Exists(path))
            await OpenPathAsync(path);
        else
            ShowInfoBar("User manual not found next to the app. See docs/USER-GUIDE.md in the project.");
    }

    private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        var update = await UpdateService.CheckForUpdatesAsync();
        if (update.IsUpdateAvailable)
        {
            ShowInfoBar($"A new version ({update.Version}) is available!", 
                InfoBarSeverity.Informational, 
                "Update Now", 
                update.DownloadUrl, 30);
        }
        else
        {
            ShowInfoBar("You are on the latest version.", InfoBarSeverity.Success, autoHideSeconds: 5);
        }
    }

    // --- Insert menu ---

    /// <summary>
    /// Rebuilds the Insert menu for the active pane's mode. Called whenever the active
    /// tab or its mode changes; plain text shows a disabled placeholder.
    /// </summary>
    private void BuildInsertMenu()
    {
        ResetMenu(InsertMenu.Items);

        var elements = ElementCatalog.ForMode(ActivePane?.Mode);
        if (elements.Count == 0)
        {
            InsertMenu.Items.Add(new MenuFlyoutItem
            {
                Text = $"No elements for {ActivePane?.FileTypeName ?? "Plain Text"}",
                IsEnabled = false,
            });
            return;
        }

        foreach (var group in elements.GroupBy(e => e.Category))
        {
            var categoryMenu = new MenuFlyoutSubItem { Text = group.Key };
            foreach (var element in group)
            {
                var item = new MenuFlyoutItem { Text = element.Name };
                item.Click += (_, _) => InsertActiveElement(element);
                categoryMenu.Items.Add(item);
            }
            InsertMenu.Items.Add(categoryMenu);
        }
    }

    private void InsertActiveElement(DocumentElement element)
        => ActivePane?.InsertElement(element.Body, element.CaretOffset);

    // --- Format menu ---

    private void OnAlignTableClick(object sender, RoutedEventArgs e) => AlignActiveTable();

    /// <summary>
    /// Re-aligns the table under the caret in the active pane. If the caret is not inside a
    /// table the mode can align, hint how to use it rather than failing silently.
    /// </summary>
    private void AlignActiveTable()
    {
        if (ActivePane is not { } pane)
            return;
        if (!pane.ReformatTable())
            ShowInfoBar("No table at the cursor to align. Put the caret inside a table and try again.");
    }

    // A MenuBar/MenuFlyout presenter can ignore a collection reset (Items.Clear()),
    // leaving stale entries behind while still honoring later Adds — so a rebuilt menu
    // accumulates. Removing items individually raises per-item changes it does honor.
    private static void ResetMenu(IList<MenuFlyoutItemBase> items)
    {
        while (items.Count > 0)
            items.RemoveAt(items.Count - 1);
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
        BuildInsertMenu();
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
        ApplyTitleBarColors();
    }

    /// <summary>
    /// Themes the system caption buttons for the extended (Mica) title bar: transparent
    /// backgrounds so the material shows through, with foreground and hover/pressed
    /// feedback chosen for the effective light/dark theme.
    /// </summary>
    private void ApplyTitleBarColors()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            return;

        var bar = AppWindow.TitleBar;
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;

        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        bar.ButtonForegroundColor = fg;
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = dark
            ? Windows.UI.Color.FromArgb(255, 150, 150, 150)
            : Windows.UI.Color.FromArgb(255, 120, 120, 120);

        // Subtle hover/pressed wash that reads over Mica in either theme.
        bar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
            : Windows.UI.Color.FromArgb(30, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
            : Windows.UI.Color.FromArgb(50, 0, 0, 0);
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

    private int _infoBarToken;
    private string? _currentActionUrl;

    private async void ShowInfoBar(string message, InfoBarSeverity severity = InfoBarSeverity.Warning, string? actionText = null, string? actionUrl = null, int autoHideSeconds = 0)
    {
        SessionInfoBar.Message = message;
        SessionInfoBar.Severity = severity;
        
        if (!string.IsNullOrEmpty(actionText) && !string.IsNullOrEmpty(actionUrl))
        {
            _currentActionUrl = actionUrl;
            SessionInfoBarAction.Content = actionText;
            SessionInfoBarAction.Visibility = Visibility.Visible;
        }
        else
        {
            _currentActionUrl = null;
            SessionInfoBarAction.Visibility = Visibility.Collapsed;
        }
        
        SessionInfoBar.IsOpen = true;

        if (autoHideSeconds > 0)
        {
            var token = ++_infoBarToken;
            await Task.Delay(TimeSpan.FromSeconds(autoHideSeconds));
            if (_infoBarToken == token)
            {
                SessionInfoBar.IsOpen = false;
            }
        }
    }

    private async void OnSessionInfoBarActionClick(object sender, RoutedEventArgs e)
    {
        var url = _currentActionUrl;
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            var isMsixDownload = url.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

            if (isMsixDownload)
            {
                // ms-appinstaller: is disabled by default on current Windows, so download the
                // package and let App Installer apply the update from the local file.
                ShowInfoBar("Downloading update…", InfoBarSeverity.Informational);
                await UpdateService.DownloadAndLaunchAsync(url);
                ShowInfoBar("Follow the App Installer prompt to finish updating.", InfoBarSeverity.Success, autoHideSeconds: 10);
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar($"Failed to download update: {ex.Message}", InfoBarSeverity.Error, autoHideSeconds: 10);
        }
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

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
            return;
        Activated -= OnFirstActivated;
        ActivePane?.FocusEditor();

        var update = await UpdateService.CheckForUpdatesAsync();
        if (update.IsUpdateAvailable)
        {
            ShowInfoBar($"A new version ({update.Version}) is available!", 
                InfoBarSeverity.Informational, 
                "Update Now", 
                update.DownloadUrl, 30);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // Notepad++ behavior: no save prompts on exit — the session preserves unsaved work.
        TemplateCatalog.Shared.Changed -= OnTemplatesChanged;
        _sessionTimer?.Stop();
        SaveWorkspaceSession();
        SaveWindowPlacement();
    }

    // --- Window placement (size / position / maximized) ---

    /// <summary>
    /// Restores the saved window placement, clamped onto a connected display, or falls back
    /// to a sensible default on first run. Re-maximizes if it was left maximized.
    /// </summary>
    private void RestoreWindowPlacement()
    {
        var saved = WindowStateService.Load();
        if (saved is { Width: > 0, Height: > 0 })
        {
            var rect = ClampToWorkArea(saved.X, saved.Y, saved.Width, saved.Height);
            AppWindow.MoveAndResize(rect);
            _normalBounds = rect;
            if (saved.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
                _isMaximized = true;
            }
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1100, 700));
            _normalBounds = new RectInt32(
                AppWindow.Position.X, AppWindow.Position.Y,
                AppWindow.Size.Width, AppWindow.Size.Height);
        }
    }

    private static RectInt32 ClampToWorkArea(int x, int y, int width, int height)
    {
        var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest);
        var work = area?.WorkArea ?? DisplayArea.Primary.WorkArea;
        int w = Math.Clamp(width, MinWindowWidth, work.Width);
        int h = Math.Clamp(height, MinWindowHeight, work.Height);
        int cx = Math.Clamp(x, work.X, work.X + work.Width - w);
        int cy = Math.Clamp(y, work.Y, work.Y + work.Height - h);
        return new RectInt32(cx, cy, w, h);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is not OverlappedPresenter presenter)
            return;

        _isMaximized = presenter.State == OverlappedPresenterState.Maximized;
        if (_isMaximized || presenter.State == OverlappedPresenterState.Minimized)
            return;

        // Enforce the minimum size: bump back up if dragged smaller (guard re-entrancy).
        if (args.DidSizeChange && !_enforcingMinSize)
        {
            int w = sender.Size.Width, h = sender.Size.Height;
            int cw = Math.Max(w, MinWindowWidth), ch = Math.Max(h, MinWindowHeight);
            if (cw != w || ch != h)
            {
                _enforcingMinSize = true;
                try { sender.Resize(new SizeInt32(cw, ch)); }
                finally { _enforcingMinSize = false; }
                return;
            }
        }

        // Remember the normal (restorable) placement for next launch.
        _normalBounds = new RectInt32(
            sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
    }

    private void SaveWindowPlacement()
    {
        WindowStateService.Save(new WindowPlacement
        {
            X = _normalBounds.X,
            Y = _normalBounds.Y,
            Width = _normalBounds.Width,
            Height = _normalBounds.Height,
            IsMaximized = _isMaximized,
        });
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
