# ADR-11: Workload-adaptive comparison

**Status:** Decided for P7 (2026-09-28). TV-08's pathological-input matrix beyond the automated tests is open.

## Decision

- **Binary comparison is exact and same-offset.** Every byte of both inputs is streamed, so "equal" is a whole-content
  claim, never a sample. Differences are byte ranges at the same offsets (a length difference is a final range); up to
  10,000 ranges are kept, and more are reported as truncated while equality stays exact.
- **Text comparison aligns lines within a budget.** Equal anchors split the files into regions; each region is aligned
  line by line with a bounded amount of work (50 million explored cells by default). A region that exceeds the budget is
  marked **Unaligned**: it differs, but no line pairs are claimed, and the whole result is labelled approximate. Paired
  changed lines get within-line detail.
- **Directory comparison** is recursive, previews its result, and turns into result sets or a one-way synchronization
  plan, which runs as ordinary jobs (P7 approved scope).
- Nothing loads a whole huge file: comparison reads through the same bounded readers as the viewer.

## Consequences

- Shifted binary content shows as a difference from the shift on; aligned binary comparison would be a separate mode.
- Comparison never synchronizes on its own; synchronization is an explicit, previewed step.
