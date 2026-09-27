# ADR-02: Panel control and large-directory storage

**Status:** Decided (2026-09-27), with evidence from [TV-01](../validation/TV-01.md).

## Decision

- **Control.** The panel is a custom-drawn, virtualized `FileListControl`, not TableView or a DataGrid.
  - It renders only the visible rows.
  - Selection is kept as a bitset keyed by store index (`MarkSet`); focus is kept by identity.
  - Column headers sort. Borders resize columns, and the widths persist per column profile.
  - Cell text of simple scripts is drawn as glyph runs built from the font's character map, with `FormattedText` as the fallback.
- **Accessibility.** A summarized automation peer represents the list: its name is the focused item, and it raises
  changes. It deliberately does not expose one node per row (§18.3).
- **Records.** `EntryStore` holds records in compact pages. Past 96 MiB per listing, records and names move to private
  delete-on-close scratch files. Sorting, merging, and filtering read those files through read-only mappings, with names compared as spans.
- **Indexes.** Sort and view indexes live in RAM under a shared 512 MiB reservation. Beyond it, the pipeline builds
  sorted runs and memory-mapped visible/position indexes (`ExternalViewBuilder`).
- **Huge selections.** They are captured as store-index snapshots (`SelectionSnapshot`) that lease the store, so a job
  never needs a million objects.

## Why

- TableView was new and read-only. Commander selection, virtual metadata columns, and paging needed control over layout.
  The custom control's accessibility cost is contained by the summarized peer and the headless audit (TV-10).
- TV-01 native measurements for 4 × 1M entries:
  - first rows in 566 ms, completion in 2.9 s;
  - re-sort in 270 ms;
  - held-key paging p95 of 16.9 ms;
  - peak private memory 504 MiB.

## Consequences

- Screen-reader support rests on the summary peer. Manual screen-reader runs remain part of TV-10.
- SQLite is not used for listings. The spill format is private and ephemeral.
