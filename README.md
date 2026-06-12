# SimpleText

A lightweight text editor for plain text and lightweight markup formats (Markdown, AsciiDoc, reStructuredText). Built as a personal learning project — not intended for production use.

## What it does

- Edit plain text files with line numbers and find/replace
- Semantic syntax highlighting for Markdown ([Markdig](https://github.com/xoofx/markdig) AST), AsciiDoc, and reStructuredText (TextMate grammars via [TextMateSharp](https://github.com/danipen/TextMateSharp))
- Document templates for productivity (notes, technical reports, proposals) and software engineering (README, changelog, ADR, bug report, pull request, design doc)
- Session persistence — picks up where you left off, even after a crash
- Drag-and-drop file opening

## Two UIs, one core

- **SimpleText.WinUI** — Windows-native [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/) (Windows App SDK)
- **SimpleText.Avalonia** — Cross-platform (Windows/macOS/Linux), built on [Avalonia UI](https://avaloniaui.net/) with [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and TextMate grammars

Both frontends share the same feature set: Notepad++-style tabbed editing, multi-tab session restore, light/dark/system themes, word wrap, and zoom.
- **SimpleText.Core** — Shared library (session management, file types, search, templates, and the semantic highlighting engine: Markdig + TextMate span parsers behind a UI-agnostic `ISpanHighlighter`)

## Building

Requires .NET 10 SDK.

```
dotnet build SimpleText.sln
```

Run the WinUI 3 version (Windows only):
```
dotnet build SimpleText.WinUI/SimpleText.WinUI.csproj
SimpleText.WinUI/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/SimpleText.WinUI.exe
```

Run the Avalonia version (cross-platform):
```
dotnet run --project SimpleText.Avalonia/SimpleText.Avalonia.csproj
```

## Disclaimer

This is a pet project. It works on my machine. There are no tests, no CI, and no guarantees. Use at your own risk.
