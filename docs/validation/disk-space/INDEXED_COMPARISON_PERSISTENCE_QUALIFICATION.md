# Indexed comparison persistence and navigation — 2026-11-17

Indexed comparison views now save and export both entire captured inputs, open private saved comparisons and import external `.fcdsc` bundles through bounded indexed validation. Applied metric, query, query side, cohort, order, root display identifiers, chart mode/side/scope and exact captured focus are restored offline. Unapplied editor changes remain unsaved. Named saves preserve existing files; confirmed saved-state markers are invalidated when their private copy is removed.

Chart selection locates accounting rows across table pages without changing the applied filter. A filtered-out selection remains inspectable with an explicit notice. Table and alias panes read bounded backward pages directly, without accumulating page-history arrays. Saved alias focus uses a binary search in existing cohort membership and restores that exact member, including beyond the first alias page.

The [immutable r1164 evidence](source-manifest-r1164-indexed-comparison-persistence.json) retains exact compiled sources, earlier source deltas, input hashes, original-controller receipts, failed preflights, raw XML/log archives and 32 outputs per platform. This builds on the [report/streaming-engine checkpoint](INDEXED_COMPARISON_OUTPUTS_QUALIFICATION.md).

| Runtime | Core | Rendering | Settings | Native Windows |
| --- | --- | --- | --- | --- |
| Windows VM | 1,932 pass / 48 existing skips | 767 pass / 1 existing skip | 35 pass / 1 existing skip | 75 pass |
| Mac | 1,957 pass / 5 declared skips | 768 pass | 36 pass | — |
| Ubuntu VM | 1,957 pass / 5 declared skips | 768 pass | 36 pass | — |

The new coverage comprises 17 Core navigation cases and 14 native-filesystem/headless UI cases. Pending imports reserve a session before loading; canceled or failed unpublished restores close both graphs before releasing capacity. A successful private save can close without a discard warning; unapplied edits and a removed saved copy retain that warning. Ordinary Windows interactive-account receipts are retained.

r1163 supplied the full product build. r1164 changed only a rendering test fixture and replayed the exact frozen product/Core/native inputs. This avoided unrelated generated version-metadata changes after committing the earlier checkpoint. Original preflight failures remain preserved: selecting summary/aggregated chart entries, assuming scratch existed before canceled preparation, racing dialog availability, a short cold-import deadline, and unnecessarily opening extra graphs to read a receipt ID.

One unidentified `dotnet` PID appeared in the Windows post-collection inventory. It was left untouched. The original observation is retained, together with a later empty global inventory before the next admission; no ownership claim is made for that PID. All original r1164 controllers completed successfully.

This is scoped native-filesystem/API/headless-view qualification. The full resource/scale, physical pointer/keyboard, assistive-technology, package and remaining W0–W8 gates remain open. Indexed catalog maintenance is the following interface slice. Public registration remains off, and the final independent UX/UI/functional/security review has not been performed.
