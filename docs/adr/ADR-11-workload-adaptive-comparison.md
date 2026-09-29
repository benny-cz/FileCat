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

- Shifted binary content shows as a difference from the shift on in the exact view; the aligned view (below) finds it
  again.
- Comparison never synchronizes on its own; synchronization is an explicit, previewed step.

## Addendum (2026-09-29): aligned binary comparison

Plan §16.2 names three comparison products; the second, aligned binary comparison, is now a view of the binary
comparison ("Align shifted bytes"), computed on request, off the UI thread, with progress, and stopped when the view is
left before it is ready.

- **Method.** The left input is indexed in fixed blocks (a power of two from 16 bytes, at most 524,288 blocks) by a
  rolling hash. The right input is searched byte by byte, a 2 MiB filter ruling out most windows before the index is
  asked; a hit is verified byte for byte and extended both ways. The matches that keep both inputs' order and cover
  the most bytes form the alignment (a weighted longest increasing chain). What lies between them is inserted, removed,
  or changed; a stretch up to 1 MiB is looked into, so bytes equal at its ends, and runs of at least 8 equal bytes
  between bytes changed in place, are equal too.
- **Truthfulness.** The view is labelled heuristic: equal stretches shorter than a block, and moved or repeated
  content, can be paired differently than a person would. Only the exact comparison claims that files are identical;
  identical files are not aligned at all.
- **Limits.** At most 8 GiB of the right input are searched byte by byte and at most 262,144 equal stretches are
  collected (a repeated find of the same stretch is kept once); beyond either, the rest is one Unaligned range and the
  summary says the work limit was reached. The window lists the first 20,000 stretches; the summary counts them all.
- **Measured** (`docs/validation/TV-08.md`): 256 MiB with 20,000 changed bytes and 100 inserted in 2.1 s, each changed
  byte found on its own; 256 MiB of unrelated input in 4.1 s; about 50 MiB of managed memory.
