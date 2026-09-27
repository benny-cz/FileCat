# ADR-10: Metadata scheduling and complete ordering

**Status:** Decided for v1 (2026-09-27).

## Decision

- **Fields.** Metadata fields (`MetadataField`) declare a cost class: Cheap or Expensive. Each field also carries an
  applicability test by name, a producer, a formatter, and an optional sort key.
- **Demand.** `MetadataService` computes values only for visible rows, at most four outstanding per device.
  - Requests for rows scrolled away are dropped.
  - Values are cached by path, revision, and field, up to 50,000.
- **States.** "Pending", "absent", and "unsupported" are states, not values. Unknown values sort last in both
  directions, and the UI never presents a partial sort as complete.
- **Complete ordering.** A complete sort by an expensive field is an explicit command: Analyze folder (`view.analyze`).
  It computes the field for every entry and then re-sorts.
- **Columns.** Metadata columns come from user column profiles (Settings → Columns). Built-in fields are version,
  image dimensions, link target, and download origin. Parsing is managed and bounded, with no native parsers in-process.

## Consequences

Browsing never starts directory-wide expensive work unasked. New fields plug in through `MetadataField`.
