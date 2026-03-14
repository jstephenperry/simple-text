# SimpleText

A lightweight text editor for plain text and lightweight markup formats (Markdown, AsciiDoc, reStructuredText). Built as a personal learning project — not intended for production use.

## What it does

- Edit plain text files with line numbers and find/replace
- Syntax highlighting for Markdown, AsciiDoc, and reStructuredText
- Document templates for common formats (notes, technical reports, proposals)
- Session persistence — picks up where you left off, even after a crash
- Drag-and-drop file opening

## Two UIs, one core

- **SimpleText** — Windows-only, WinForms
- **SimpleText.Avalonia** — Cross-platform, built on [Avalonia UI](https://avaloniaui.net/) with [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and TextMate grammars
- **SimpleText.Core** — Shared library (session management, file types, search, templates)

## Building

Requires .NET 10 SDK.

```
dotnet build SimpleText.sln
```

Run the WinForms version (Windows only):
```
dotnet run --project SimpleText.csproj
```

Run the Avalonia version (cross-platform):
```
dotnet run --project SimpleText.Avalonia/SimpleText.Avalonia.csproj
```

## Disclaimer

This is a pet project. It works on my machine. There are no tests, no CI, and no guarantees. Use at your own risk.
