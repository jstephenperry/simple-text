# SimpleText

A lightweight text editor for plain text and lightweight markup formats (Markdown, AsciiDoc, reStructuredText). Built as a personal learning project — not intended for production use.

## What it does

- Edit plain text files with line numbers and find/replace
- Semantic syntax highlighting for Markdown ([Markdig](https://github.com/xoofx/markdig) AST), AsciiDoc, and reStructuredText (TextMate grammars via [TextMateSharp](https://github.com/danipen/TextMateSharp))
- Document templates for productivity (notes, technical reports, proposals) and software engineering (README, changelog, ADR, bug report, pull request, design doc) — shipped as starter files and copied into your templates folder on first run, then yours to edit, add to, or delete
- Templates are just files: drop a text file in the templates folder (open it from **Help → Open Templates Folder**) and it auto-registers in the **New from Template** menu — no restart needed. The file name becomes the template name, an immediate sub-folder becomes its category, and the extension (`.md`, `.rst`, `.adoc`, `.txt`, …) sets the editor mode. The packaged WinUI build keeps them in `Documents\SimpleText\Templates` (visible, durable, survives uninstall); the unpackaged/Avalonia build uses `%LocalAppData%/SimpleText/Templates/`
- Insert base document elements — headings, lists, tables, code blocks, links, images, and equations — from the **Insert** menu, tailored to the current format (Markdown, AsciiDoc, reStructuredText, plus ASCII-convention elements for plain text). The fragment drops in at the cursor with the caret placed where you'll type next (like the insert palettes in LaTeX editors)
- Line tables up in the source with **Format → Align Table** (Ctrl+Shift+T): re-flows the table under the cursor so its columns line up as text, not just when rendered — Markdown pipe tables (keeping `:---`/`:--:`/`---:` alignment), AsciiDoc `|===` tables, reStructuredText grid & simple tables, and plain-text ASCII tables. Re-running on an aligned table is a no-op ([docs](docs/table-formatting.md))
- Appearance: light/dark/system themes, word wrap, and zoom; on Windows, a Mica title bar and selected-tab/button accents that follow your system accent color, plus a remembered window size & position
- Session persistence — picks up where you left off, even after a crash
- Drag-and-drop file opening

## User guide

Full usage docs live in **[docs/USER-GUIDE.md](docs/USER-GUIDE.md)** — and inside the app
at **Help → User Manual**, which opens that same guide in a new tab (with Markdown
highlighting and Find).

## Two UIs, one core

- **SimpleText.WinUI** — Windows-native [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/) (Windows App SDK)
- **SimpleText.Avalonia** — Cross-platform (Windows/macOS/Linux), built on [Avalonia UI](https://avaloniaui.net/) with [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and TextMate grammars. The editor renders in the bundled monospace [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) (OFL) so source tables line up identically on every OS, regardless of installed fonts
- **SimpleText.Core** — Shared library (session management, file types, search, templates, and the semantic highlighting engine: Markdig + TextMate span parsers behind a UI-agnostic `ISpanHighlighter`)

Both frontends share the same feature set: Notepad++-style tabbed editing, multi-tab session restore, light/dark/system themes, word wrap, zoom, and a consistent in-app updater (**Help → Check for Updates**). Both implement one shared `IUpdateService` contract (in `SimpleText.Core`) with an identical check → notify → apply flow: check GitHub Releases, then stage the update silently so it applies on the next launch. The WinUI build updates the MSIX in place (`PackageManager`, falling back to App Installer); the Avalonia build uses [Velopack](https://velopack.io) on **Windows and Linux**. macOS auto-update is **not supported** (it needs Apple notarization — see [below](#auto-update-support-by-platform)).

## Building

Requires the .NET 10 SDK.

Core library and the cross-platform Avalonia version (Windows/macOS/Linux):
```
dotnet build SimpleText.Core/SimpleText.Core.csproj
dotnet run   --project SimpleText.Avalonia/SimpleText.Avalonia.csproj
```

#### Avalonia installers (Velopack)

Released builds are packaged with [Velopack](https://velopack.io) into installers for
**Windows** (`Setup.exe`) and **Linux** (`.AppImage`) plus an update feed, so the installed
app can update itself in place. CI does this when the release workflow is run — see the
`pack-avalonia` job in [`.github/workflows/release.yml`](.github/workflows/release.yml). To build one locally:
```
dotnet tool install -g vpk                          # Velopack CLI (Linux also needs squashfs-tools)
dotnet publish SimpleText.Avalonia/SimpleText.Avalonia.csproj -c Release -r linux-x64 --self-contained -o publish
vpk pack -u SimpleText.Avalonia -v 1.0.1 -p publish -e SimpleText.Avalonia -o vpk-release
```
The Velopack **Windows** installer is unsigned, so SmartScreen warns until reputation builds
(the signed WinUI **MSIX** is the trusted Windows path). The **Linux** AppImage has no equivalent
trust gate.

##### Auto-update support by platform

| Platform | Distribution | In-app auto-update |
| --- | --- | --- |
| Windows | WinUI MSIX (signed, Artifact Signing) **+** Avalonia Velopack | ✅ Yes |
| Linux | Avalonia Velopack `.AppImage` (x64) | ✅ Yes (run the installed AppImage) |
| macOS | Build from source | ❌ Not supported — needs an Apple Developer ID signature + notarization, or Gatekeeper blocks downloaded updates. Enable later via `vpk pack --signAppIdentity … --notaryProfile …`. |

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

#### Installing a local build without the "this app may harm your computer" warning

Downloaded [Releases](../../releases) need none of this — they're signed by
[Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/),
whose root Windows already trusts, so they install (and auto-update) with no
warning and no setup. This only applies to **locally self-signed** builds.

A self-signed MSIX warns until its certificate is **trusted on your PC** — that
one-time, free step removes the warning. From a *Developer PowerShell for VS*:

```powershell
pwsh build/sign-msix.ps1 -Trust          # build + self-sign + trust (UAC prompt)
Add-AppxPackage .\AppPackages\...\SimpleText.Editor_*.msix   # or double-click it
```

The cert is reused across builds, so you only trust it once. To install a
self-signed build on **another of your machines**, copy its `.cer` and run
`pwsh build/trust-cert.ps1 -Path .\SimpleText.cer` there first. Details:
[`docs/msix-packaging.md`](docs/msix-packaging.md#why-a-self-signed-local-package-warns--and-how-to-stop-it).

CI/CD builds this for you: see [`.github/workflows/ci.yml`](.github/workflows/ci.yml)
(PR/branch build checks) and [`.github/workflows/release.yml`](.github/workflows/release.yml)
(run the workflow → Artifact-signed, build-versioned packages on a GitHub Release). Packaging,
signing secrets, and storage details are in [`docs/msix-packaging.md`](docs/msix-packaging.md).

### Versioning

The version has two parts: a **semantic** part you manage by hand and an **auto-incrementing build
number** that CI stamps on every build.

- **`version.txt`** at the repo root is the single source of truth for the 3-part semantic version
  (e.g. `1.0.1`). Every .NET project reads it via [`Directory.Build.props`](Directory.Build.props).
  Bump it with the helper, which increments `version.txt` **and** syncs the WinUI MSIX manifest's
  required 4-part Identity version:

  ```
  build/bump-version.sh            # patch: 1.0.1 -> 1.0.2
  build/bump-version.sh minor      # 1.0.2 -> 1.1.0
  build/bump-version.sh set 2.0.0  # explicit
  ```

- **Build number** — the 4th version component. CI passes the workflow run number as
  `-p:BuildNumber=<n>`; local builds default to `0`. It becomes the assembly `FileVersion` /
  `InformationalVersion` and, for releases, the MSIX Identity and Velopack package version.
  `version.txt` is never edited for it, so the committed manifest stays at `version.txt + ".0"`
  (e.g. `1.0.1.0`) and CI (`version-check`) still fails if the manifest drifts from `version.txt`.

[`build/set-build-version.sh <n>`](build/set-build-version.sh) stamps the build number into the
only file with a literal version (the MSIX manifest's 4-part Identity); the .NET projects pick it up
from `-p:BuildNumber` and need no edit.

**Cutting a release.** Releases are produced by **running the release workflow** (Actions →
*Release MSIX* → *Run workflow*, on the commit to release), **not** by pushing a hand-made tag — the
build number isn't known until the run starts. The workflow computes the full version
`version.txt.<build>`, stamps it into the MSIX manifest and the Velopack package, and publishes a
GitHub Release tagged `v<version>.<build>` (e.g. `v1.0.1.42`). Because the git tag, the MSIX Identity,
the Velopack package version, and the app's reported version are all that same number, both in-app
updaters detect new releases correctly: the WinUI build compares the release **tag** to its installed
MSIX version, and the Avalonia build compares Velopack **package** versions from the release feed —
and each release is strictly newer than the last even without a `version.txt` bump. To also move the
semantic part, run `build/bump-version.sh` and commit before releasing.

## Disclaimer

This is a pet project. It works on my machine. Test coverage is light (core logic only) and there are no guarantees. Use at your own risk.

## AI Disclosure

A large portion of this project — the WinUI 3 and Avalonia frontends, the semantic
highlighting integration (Markdig + TextMate), the document templates, and the application
icon — was written with substantial assistance from AI (Anthropic's Claude, via Claude Code).
The code builds and has been exercised by hand, but AI-generated code can carry subtle bugs or
non-idiomatic patterns; review it before depending on it for anything important.
