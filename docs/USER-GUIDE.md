# SimpleText — User Guide

SimpleText is a lightweight editor for plain text and lightweight markup formats —
Markdown, AsciiDoc, and reStructuredText. It does tabbed editing with line numbers,
semantic syntax highlighting, document templates, an Insert menu of ready-made
markup, find, themes, and crash-safe session restore.

> Tip: you're probably reading this inside SimpleText (**Help → User Manual**). It's
> just a Markdown file open in a tab, so syntax highlighting, Find (Ctrl+F), and zoom
> all work on it.

## The window at a glance

- **Title bar** — the document name. On Windows it uses the Mica material and follows
  your light/dark theme.
- **Menu bar** — File, Mode, Insert, View, Help.
- **Tabs** — one document per tab; a **+** button (or Ctrl+N) opens a new one.
- **Editor** — with a line-number gutter on the left.
- **Status bar** — line/column, file name, current format, and encoding (UTF-8).

## Working with files

| Action | How |
| --- | --- |
| New document | **File → New**, the **+** tab button, or **Ctrl+N** |
| Open | **File → Open…** or **Ctrl+O**, or **drag a file** onto the window |
| Save | **File → Save** or **Ctrl+S** |
| Save As | **File → Save As…** or **Ctrl+Shift+S** |
| Close tab | **File → Close Tab** or **Ctrl+W** |

Files are read and written as **UTF-8**. On Windows, SimpleText registers as a handler
for `.txt`, `.text`, `.md`, `.markdown`, `.rst`, `.adoc`, and `.asciidoc`, so you can
set it as the default editor or use right-click **Open with**.

## Tabs and sessions

SimpleText restores your workspace: the next time you launch, your open tabs come back —
**including unsaved edits, even after a crash or power loss**. Because of that, closing
the app does **not** prompt you to save; your in-progress work is preserved in the session.

If a file changed on disk since you last had it open, SimpleText keeps your unsaved copy
and shows a banner so nothing is lost silently.

## Modes and syntax highlighting

Each tab has a **mode** that drives highlighting:

- **Plain Text** — no highlighting.
- **Markdown** — semantic highlighting from the Markdown document structure.
- **AsciiDoc** and **reStructuredText** — grammar-based highlighting.

The mode is auto-detected from the file extension when you open or save. To change it
manually, use the **Mode** menu. The status bar shows the current mode.

## Templates — start from a skeleton

**File → New from Template** creates a document pre-filled from a template, grouped by
category. SimpleText ships starter templates (notes, technical reports, proposals, and
software-engineering docs like README, changelog, ADR, bug report, pull request, design
doc), copied into your templates folder on first run — after that they're yours to edit,
add to, or delete.

Templates are **just files**. Open the folder with **Help → Open Templates Folder** and
drop a text file in; it auto-registers in the menu (no restart):

- the **file name** becomes the template name,
- an immediate **sub-folder** becomes its category,
- the **extension** (`.md`, `.rst`, `.adoc`, `.txt`, …) sets the mode.

## Insert — base document elements

The **Insert** menu drops a ready-made markup fragment at the cursor — like the insert
palettes in LaTeX editors. The fragment replaces any selection, and the caret lands where
you'll type next (inside a heading marker, a link's label, the first table cell, an
equation body, …).

The menu is **format-aware** — it shows the elements for the current tab's mode:

| Format | Elements include |
| --- | --- |
| Markdown | headings, bold/italic/code/strikethrough, lists (incl. tasks), blockquote, code block, table, rule, link, image, inline & block equations (`$…$` / `$$…$$`) |
| AsciiDoc | sections, bold/italic/mono, lists, source/listing/note/quote blocks, table, rule, link, image, `latexmath` |
| reStructuredText | titles, bold/italic/code, lists, code-block/note/literal/transition, link, image, `:math:` / `.. math::` |
| Plain text | underlined titles, bulleted/numbered/check lists, an aligned ASCII table, boxed text, a rule, block quote, TODO/NOTE |

## Finding text

| Action | How |
| --- | --- |
| Open the find bar | **Ctrl+F** |
| Find next / previous | **Next** / **Prev**, or **F3** / **Shift+F3** |
| Close the find bar | **Esc** or the **✕** button |

If text is selected when you press Ctrl+F, it's used as the initial search term.

## Appearance

- **Theme** — **View → Theme**: Follow System, Light, or Dark. Your choice is remembered.
- **Word Wrap** — **View → Word Wrap** toggles wrapping (the line-number gutter hides while wrapped).
- **Zoom** — **Ctrl+=** to zoom in, **Ctrl+-** to zoom out, **Ctrl+0** to reset.

On **Windows (WinUI)** there's extra Fluent polish:

- the **title bar** uses the Mica material instead of a flat bar;
- the **selected tab** and the find bar's primary button follow your **Windows accent
  color**, and update live when you change it;
- the window **remembers its size, position, and maximized state** between launches.

## Where your files live

| Data | Windows (packaged WinUI) | Cross-platform / unpackaged (Avalonia) |
| --- | --- | --- |
| Your templates | `Documents\SimpleText\Templates` | `%LocalAppData%\SimpleText\Templates` |
| App state (session, theme, window) | package-private local store | `%LocalAppData%\SimpleText` |

Templates live somewhere visible and durable (they survive uninstall); app state lives in
the per-app store.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+N | New tab |
| Ctrl+O | Open |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+W | Close tab |
| Ctrl+F | Find |
| F3 / Shift+F3 | Find next / previous |
| Esc | Close the find bar |
| Ctrl+= / Ctrl+- / Ctrl+0 | Zoom in / out / reset |
| Tab | Insert a tab character |

## Platforms

SimpleText ships in two builds that share the same core feature set (tabs, session
restore, modes & highlighting, templates, Insert, find, themes, word wrap, zoom):

- **WinUI 3** — Windows-native, packaged as MSIX. Adds the Mica title bar, accent
  integration, and window-placement memory described above.
- **Avalonia** — cross-platform (Windows, macOS, Linux).

## Getting and installing the app

Build and install instructions — including how to install the Windows MSIX without the
untrusted-publisher warning — are in the project README and
[`docs/msix-packaging.md`](msix-packaging.md).
