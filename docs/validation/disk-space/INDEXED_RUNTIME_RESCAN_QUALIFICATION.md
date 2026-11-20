# Indexed runtime acquisition and rescan qualification

This packet qualifies the internal native acquisition and headless analyzer view lifecycle on
Windows, macOS and Ubuntu. It does not enable the public command or finish the product plan.
The exact inputs, raw failures, corrected fixtures and successful reruns are recorded in
[the evidence manifest](source-manifest-r1248-indexed-runtime-views.json).

## Implemented behavior

- A window reserves a process-wide analysis place before preparing private storage or source
  factories. The owning acquisition stays alive through actual producer completion and then
  transfers to either an indexed view or its bounded-memory fallback.
- Only a live acquisition supplies rescan factories. Data-only dataset leases and imported
  inventories cannot turn recorded locators into source authority.
- Rescan all opens a successor tab. The predecessor keeps its own observations, marks, basket
  and retained revisions. The successor owns an independent runtime context and can rescan
  after the predecessor tab actually closes.
- Indexed navigation continuity uses bounded ancestry and exact captured child indexes. It
  requires matching runtime domains, roots, raw names, physical identity and known creation
  time/precision. Missing or ambiguous continuity falls back safely. It never maps marks or
  grants filesystem operation authority.
- Compatible folder scope, applied display settings, table page, focused entry and inspector
  are restored before publication. Synthetic/group scopes return to their group overview;
  numeric indexed path IDs are not copied into a memory fallback.
- Pause/continue and Stop target the successor. Closing or canceling an unpublished result
  retains ownership until late factory work and native cleanup actually finish. A failed
  close continues to hold its admission place.
- Resource refusal preserves the old epoch and supports retry after capacity is released.
  Eight tab places are a maximum; they do not override the separate global 128-handle limit.

## Executed coverage

| Coverage | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Indexed navigation, reordered IDs/raw names/refusals/ancestry budget | 15 passed | 15 passed | 15 passed |
| Runtime factories, memory rescan views, private timestamp exclusions | 14 passed | 14 passed | 14 passed |
| Affected Core suite | 2,022 passed, 56 declared skips | 2,072 passed, 6 declared skips | 2,072 passed, 6 declared skips |
| Full analyzer rendering suite on the current product | 809 passed, 1 declared skip | 810 passed | 810 passed |
| Final ownership/resource fixture revision | 10 passed | 10 passed | 10 passed |

The final ten-case run includes the corrected cross-page ownership test and one additional
handle-budget refusal/retry test. Together with the full 810-case runs, this covers 811 distinct
rendering cases. It is a composed matrix over byte-identical product inputs, rather than a
claim that a single latest test assembly ran all 811 cases on every platform.

The final cases verify indexed and memory owner handoff; capacity and closed-window refusal
before factory calls; cancellation/window closure during a late factory return; actual owner
and admission closure; scoped focus restoration beyond the first page; preserved predecessor
marks and basket; root replacement refusal and recovery; absence of data-only rescan authority;
and native-handle refusal/retry. Relevant cases verify native handle counts return to their
starting values. All final process inventories were clear.

## Retained failures and corrections

1. Windows r1233 passed the new helpers and Core suite, then failed an existing comparison test
   while a full native inventory import exceeded its five-second UI-dispatch deadline. Only
   that native import/index-build wait received a separate 30-second deadline. Its assertions
   remain intact, and both the focused case and Windows full suite subsequently passed.
2. The original ownership test disposed the predecessor acquisition but kept its indexed tab
   open, then required a third result regardless of the global native-handle ceiling. The
   longer diagnostic run exposed a safe resource refusal on Mac and Ubuntu. The independence
   check now closes the entire predecessor tab and awaits its actual cleanup. A separate
   test deliberately exhausts all handle reservations, verifies preservation, releases them,
   and confirms retry. The product handle limit was not increased.

All failed original receipts remain in the packet. The r1243 and r1247 continuations verify
every cached product input and overlay only the rendering test DLL/PDB. They transfer about
0.8 MB each; unchanged native builds and passing Unix full suites are reused.

## Boundaries still open

This is native filesystem API and Avalonia headless qualification, not physical desktop,
keyboard, screen-reader, theme/DPI, packaged application or complete accessibility acceptance.
The current timestamp/runtime UI product has not yet repeated the historical million-file
qualification. Ten-million and global multi-session memory/performance gates remain open.

The public entry remains off. Branch refresh/watchers, complete W1 lifecycle and resource
qualification, remaining action/recipe/helper work, and the final independent
UX/UI/functional/security review are unfinished. Capacity notifications across windows,
memory-adoption status copy and rescan-binding health presentation remain follow-up work.
