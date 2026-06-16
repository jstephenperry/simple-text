# SimpleText

A lightweight text editor for plain text and lightweight markup formats (Markdown, AsciiDoc, reStructuredText). Built as a personal learning project — not intended for production use.

## What it does

- Edit plain text files with line numbers and find/replace
- Semantic syntax highlighting for Markdown ([Markdig](https://github.com/xoofx/markdig) AST), AsciiDoc, and reStructuredText (TextMate grammars via [TextMateSharp](https://github.com/danipen/TextMateSharp))
- Document templates for productivity (notes, technical reports, proposals) and software engineering (README, changelog, ADR, bug report, pull request, design doc) — shipped as starter files and copied into your templates folder on first run, then yours to edit, add to, or delete
- Templates are just files: drop a text file in the templates folder (open it from **Help → Open Templates Folder**) and it auto-registers in the **New from Template** menu — no restart needed. The file name becomes the template name, an immediate sub-folder becomes its category, and the extension (`.md`, `.rst`, `.adoc`, `.txt`, …) sets the editor mode. The packaged WinUI build keeps them in `Documents\SimpleText\Templates` (visible, durable, survives uninstall); the unpackaged/Avalonia build uses `%LocalAppData%/SimpleText/Templates/`
- Insert base document elements — headings, lists, tables, code blocks, links, images, and equations — from the **Insert** menu, tailored to the current format (Markdown, AsciiDoc, reStructuredText, plus ASCII-convention elements for plain text). The fragment drops in at the cursor with the caret placed where you'll type next (like the insert palettes in LaTeX editors)
- Appearance: light/dark/system themes, word wrap, and zoom; on Windows, a Mica title bar and selected-tab/button accents that follow your system accent color, plus a remembered window size & position
- Session persistence — picks up where you left off, even after a crash
- Drag-and-drop file opening

## User guide

Full usage docs live in **[docs/USER-GUIDE.md](docs/USER-GUIDE.md)** — and inside the app
at **Help → User Manual**, which opens that same guide in a new tab (with Markdown
highlighting and Find).

## Two UIs, one core

- **SimpleText.WinUI** — Windows-native [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/) (Windows App SDK)
- **SimpleText.Avalonia** — Cross-platform (Windows/macOS/Linux), built on [Avalonia UI](https://avaloniaui.net/) with [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and TextMate grammars
- **SimpleText.Core** — Shared library (session management, file types, search, templates, and the semantic highlighting engine: Markdig + TextMate span parsers behind a UI-agnostic `ISpanHighlighter`)

Both frontends share the same feature set: Notepad++-style tabbed editing, multi-tab session restore, light/dark/system themes, word wrap, and zoom.

## Building

Requires the .NET 10 SDK.

Core library and the cross-platform Avalonia version (Windows/macOS/Linux):
```
dotnet build SimpleText.Core/SimpleText.Core.csproj
dotnet run   --project SimpleText.Avalonia/SimpleText.Avalonia.csproj
```

### WinUI 3 (packaged MSIX, Windows only)

The WinUI frontend is a packaged **MSIX** application (single-project MSIX,
`x64`/`ARM64`). The simplest path is Visual Studio: open `SimpleText.sln`, set
**SimpleText.WinUI** as startup, pick a platform, and **F5** (deploys and runs).

To package from the command line, first generate the visual assets, then build:
```
./build/generate-winui-assets.sh        # needs librsvg2-bin + imagemagick; writes SimpleText.WinUI/Images
msbuild SimpleText.WinUI/SimpleText.WinUI.csproj /p:Configuration=Release /p:Platform=x64 ^
  /p:GenerateAppxPackageOnBuild=true /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageSigningEnabled=false
```

#### Installing without the "this app may harm your computer" warning

A self-signed MSIX still warns until its certificate is **trusted on your PC** —
that one-time, free step is what removes the warning. From a *Developer
PowerShell for VS*:

```powershell
pwsh build/sign-msix.ps1 -Trust          # build + self-sign + trust (UAC prompt)
Add-AppxPackage .\AppPackages\...\SimpleText.Editor_*.msix   # or double-click it
```

Installing a downloaded [Release](../../releases) instead? Trust its certificate once:

```powershell
pwsh build/trust-cert.ps1 -Path .\SimpleText.cer
Add-AppxPackage .\SimpleText.Editor_1.0.0.0_x64.msix
```

The cert is reused across builds, so you only trust it once. For zero per-machine
setup (e.g. distributing to others), use a CA-issued cert such as
[Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/)
instead. Details: [`docs/msix-packaging.md`](docs/msix-packaging.md#why-a-self-signed-package-warns--and-how-to-stop-it).

CI/CD builds this for you: see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)
(PR/branch build checks) and [`.github/workflows/release.yml`](.github/workflows/release.yml)
(tag `v*` → signed packages on a GitHub Release). Packaging and storage details
are in [`docs/msix-packaging.md`](docs/msix-packaging.md).

## Disclaimer

This is a pet project. It works on my machine. There are no tests, no CI, and no guarantees. Use at your own risk.

## AI Disclosure

A large portion of this project — the WinUI 3 and Avalonia frontends, the semantic
highlighting integration (Markdig + TextMate), the document templates, and the application
icon — was written with substantial assistance from AI (Anthropic's Claude, via Claude Code).
The code builds and has been exercised by hand, but AI-generated code can carry subtle bugs or
non-idiomatic patterns; review it before depending on it for anything important.
