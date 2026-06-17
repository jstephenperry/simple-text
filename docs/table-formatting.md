# Format: align tables in the source

**Format → Align Table** (**Ctrl+Shift+T**) re-flows the table under the caret so its
columns line up in the *source text*, not just when the document is rendered. It is the
counterpart to the **Insert** menu: Insert drops a table in, Align Table keeps it tidy as
you edit it.

The command is **mode-aware** and acts on the **table containing the caret**. Cell
*contents* are never changed — only padding, column separators, and borders are rewritten —
so re-aligning an already-aligned table is a no-op (the operation is idempotent). If the
caret is not inside a table the active mode recognizes, the command does nothing and a
banner explains how to use it.

## What it aligns, per mode

Each mode has its own table syntax, so each gets its own parser/renderer:

- **Markdown** — GitHub-style pipe tables. The delimiter row sets per-column alignment
  (`:---` left, `:--:` center, `---:` right); that alignment is preserved and applied to the
  cell padding. Columns are padded to their widest cell (minimum three dashes).

  ```
  | Name  | Role     |
  | :---- | -------: |
  | Alice | Engineer |
  ```

- **AsciiDoc** — tables delimited by `|===`, with each row on one line as `| a | b`. The
  fences and any other lines inside the block (blank separators, `[cols=…]` attributes) are
  passed through untouched; only the `|`-delimited rows are re-spaced. AsciiDoc cells have no
  trailing pipe, so rows are not right-padded.

- **reStructuredText** — both table styles:
  - **grid** tables (`+---+` borders with `| cell |` rows); a `=` border (the header
    separator) is kept where it was.
  - **simple** tables (`===` rule lines, columns separated by two-or-more spaces). Data rows
    are split on their gaps rather than by fixed positions, so a table whose cells have
    *drifted out of alignment* — exactly when you reach for the command — still parses
    correctly. The last column keeps any internal spaces.

- **Plain text** — ASCII grid tables, the same `+---+`/`|` shape the **Insert** menu drops
  in. (Plain text has no renderer, so lining the source up *is* the formatting.)

## Where it lives

The logic is pure text-in/text-out and lives entirely in the shared core, under
`SimpleText.Core/Formatting`:

- `TableFormatter` — the single entry point. `Format(text, caretOffset, mode)` detects the
  table around the caret for the given mode, aligns it, and returns a `TableEdit?` — a
  targeted `(Start, Length, Text, CaretOffset)` replacement (or `null` when there is no
  alignable table at the caret). All offsets are in the `\n`-normalized space the editors
  expose through `GetText()`; the EOL of the original document is reused for the replacement,
  so `\r\n` files stay `\r\n`.
- `MarkdownTableFormatter`, `GridTableFormatter`, `AsciiDocTableFormatter`,
  `RstSimpleTableFormatter` — the per-format parsers/renderers. `TableFormatter` handles
  block detection (which lines belong to the table) and caret remapping; each formatter is a
  pure block-in/block-out function that also validates the block and returns `null` if it is
  not really a table of that kind.

Each frontend does the same small thing: the host's **Format → Align Table** handler calls
`EditorView.ReformatTable()` / `EditorPane.ReformatTable()`, which read the document and
caret, call `TableFormatter.Format`, and apply the returned edit (counting as a normal edit:
the document is marked dirty and re-highlighted). The caret is kept on the same table row at
a clamped column.

## Tests

Because this is pure logic with many edge cases, it is covered by unit tests in
`SimpleText.Core.Tests` (`TableFormatterTests`): exact alignment per format, ragged rows,
preserved Markdown alignment markers, drifted reStructuredText simple tables, idempotency,
`\r\n` handling, and the "caret not in a table → no edit" cases. CI runs them on every push
(`dotnet test` in the `core` job).

## Extending or adding a format

Add or adjust a per-format formatter under `SimpleText.Core/Formatting`, and (if it is a new
table shape) teach `TableFormatter` how to detect its block for the relevant mode. The
frontends need no changes — they only know about `TableFormatter.Format` and the
`ReformatTable` seam.
