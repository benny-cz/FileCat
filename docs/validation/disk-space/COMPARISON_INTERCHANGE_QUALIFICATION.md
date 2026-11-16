# Offline comparison import and human reports

The analysis import picker now accepts local `.fcdsc` comparisons as well as `.fcdsa` inventories. A reviewed comparison restores both captured inventories, metric, applied query, cohort, ranking, focus and chart settings in a new unsaved offline tab. It does not connect recorded sources, grant file actions, create a named save or enter rolling history. The same native read-only, no-follow, stable-file and handle-budget boundary protects both formats.

Comparison imports use the existing strict container/graph decoder and bounded-memory comparison route. Two admitted imports use a bounded two-chunk read pipeline, charge its parsing/buffer allowance to the shared import-memory budget, and await actual reads, decoding and native release before publication. Cancellation cannot release a still-running input. The 8 GiB file cap is independent of the lower import/comparison memory limits; large inputs can be refused. This is not qualification of large indexed comparisons.

The comparison footer also exports Markdown, inert HTML and spreadsheet-safe CSV. Choices distinguish the captured query/cohort/ranking from the entire unfiltered comparison, and default to path omission and Cancel. Reports retain both immutable inputs and accounting rows independently of later view changes or source closure. The table and reports share cohort/ranking projection. Previous/current measurement states, exact integer amounts, observed deltas, compatibility, coverage, freshness and both observation intervals remain explicit. Totals describe the full inputs rather than sums of filtered/shared rows. Human detail is capped at 5,000 ranked rows; CSV streams the chosen accounting rows. Paths are safe display text, may be abbreviated and show at most 32 aliases per side with an explicit limitation; exact raw names belong in the machine inventories. Redaction also omits query text and detailed source explanations. Existing destinations and protected FileCat state are preserved through the guarded output route.

The r1112 native cohort passes on Windows, macOS and Ubuntu:

| Phase | Windows | macOS | Ubuntu |
| --- | --- | --- | --- |
| Import/report preflight | 44/44 | 44/44 | 44/44 |
| Core | 1,597 pass, 48 existing skips | 1,623 pass, 4 existing skips | 1,623 pass, 4 existing skips |
| Analysis rendering | 646 pass, 1 existing skip | 647/647 | 647/647 |
| Settings | 35 pass, 1 existing skip | 36/36 | 36/36 |
| Windows native | 75/75 | — | — |

There are 36 additional native/review cases. They cover metric/view restoration, invalid containers, stable bindings for both extensions, cancellation during an actual blocked read, unconfirmed release, every human format with both privacy and scope choices, unknown allocation, output conflicts, protected state, suffix mismatch, and blocked-output lifetime. Windows ran under the ordinary R2 account through a temporary Interactive/Limited task, removed after completion. Final process inventories show zero FileCat/dotnet instances on all three machines.

The original r1111 preflight passed 32 cases and stopped before the broad suites on 12 Unicode assertions. The test used culture-sensitive matching of a bidi control character; ordinal comparison now checks the actual character. The rerun reused every production input, transferring only the test DLL/PDB, approximately 627 KB per machine instead of 53–95 MB. Checkout-shaped worker dependencies and the ordinary Windows desktop route also avoid the earlier packaging/session failures.

[Exact source, inputs, raw failures, passing receipts, unchanged skips and task provenance](source-manifest-r1112-comparison-interchange.json) and [the cache plans](cache-plans-r1112-comparison-interchange.json) preserve the evidence. The completed finalizer is immutable and must not be rerun.

Comparison chart-image export, large indexed comparison parity, remaining physical desktop/resource/package qualification and later W0–W8 work remain open. Public feature registration stays off. The final independent UX/UI/functional/security review is still required after the full implementation loop.
