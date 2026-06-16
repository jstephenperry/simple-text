# Insert: base document elements

The **Insert** menu drops a ready-made markup fragment — a heading, list, table,
code block, link, image, or equation — at the caret, like the insert palettes in
LaTeX editors. The fragment is inserted at the cursor (replacing any selection),
and the caret lands where you'll type next (inside a heading marker, a link label,
the first table cell, an equation body, …).

The menu is **mode-aware**: it shows the elements for the active tab's format and
rebuilds when you switch tabs or change the mode. Plain text has no markup, so it
shows a disabled placeholder.

## Where elements live

Elements are **built into the app** (in `SimpleText.Core/Elements`), not user-owned
files like templates. They are small, format-specific primitives the editor
provides out of the box, so they ship consistent and ready to use:

- `DocumentElement` — one fragment: `Category`, `Name`, `Body`, and a `CaretOffset`
  (where the caret lands within `Body`). `DocumentElement.Create` authors a fragment
  from a template string containing a single caret marker (`\f`, form feed — it never
  appears in real markup), which it strips and converts to the offset.
- `ElementCatalog` — the curated set per mode, keyed by `TextModes`. `ForMode(mode)`
  returns the elements for an editor mode (empty for plain text / unknown modes),
  ordered so a stable `GroupBy(Category)` in the UI preserves category order.

Each frontend exposes the same catalog: the host builds the Insert menu from
`ElementCatalog.ForMode(activePane.Mode)` and calls `EditorView.InsertElement` /
`EditorPane.InsertElement(body, caretOffset)` to perform the edit. Insertion counts
as a normal edit (marks the document dirty and re-highlights).

## Coverage

| Format | Categories |
| --- | --- |
| Markdown | Headings, Text, Lists, Blocks (incl. table, code block, rule), Links & media, Math (`$…$` / `$$…$$`) |
| AsciiDoc | Headings, Text, Lists, Blocks (source/listing/note/quote/table/rule), Links & media, Math (`latexmath`) |
| reStructuredText | Headings, Text, Lists, Blocks (code-block/note/literal/transition), Links & media, Math (`:math:` / `.. math::`) |

Block elements assume insertion on a blank line; bodies are inserted verbatim, so
place the caret on an empty line for tables, code blocks, and rules.

## Extending or adding a format

Add entries to the relevant list in `ElementCatalog` (author the body with `\f`
where the caret should land), or add a new mode key. Because the menu is generated
from the catalog, both frontends pick up the change with no UI edits.

A future option is user-defined elements (a watched folder, like templates); the
catalog seam (`ElementCatalog.ForMode`) is the single place that would change.
