# I636 — Analyze folder keeps no order by Origin, or by Link target for app aliases and WSL links

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c32, I622), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I636](../FILECAT_1_0_RELEASE_ISSUES.md#i636), merged into main as `a2f983e9a`); first recorded as open, awaiting the owner's decision.

## Observation

Three pictures in an owned folder: `a.png` with `ZoneId=3`, `c.png` with `ZoneId=1`, and `b.png` with no mark. View → Analyze folder → **Origin** read the marks but did not order by them:

> Read Origin for 3 items: 2 current values. 1 without a value. The order retains 1 of 3 items' results. Unsupported value or sort key could not be retained. Unretained items have unknown keys and sort last within their folder/file partition.

The rows stayed in name order (`a.png`, `b.png`, `c.png`). Dimensions and Extra data, analyzed on the same folder, retained all 3 results and ordered them.

The test account's `%LOCALAPPDATA%\Microsoft\WindowsApps` holds 20 app execution aliases and 11 folders. Analyze folder → **Link target** there:

> Read Link target for 31 items: 20 current values. 11 without a value. The order retains 11 of 31 items' results. Unsupported value or sort key could not be retained.

The 11 retained results are the folders without a value; none of the 20 aliases' targets was kept.

## Cause

Rc.2's source:
- **The analysis keeps each value as well as its sort key.** `TabViewModel.Analysis` passes both to `MetadataOrderSnapshot.TryAdd`.
- **The ordering snapshot accepts only listed value types.** These are strings, numbers, dates, principals, versions and a few FileCat types. Its `Payload` check refuses anything else. A refused row keeps no key and is counted as "Unsupported value or sort key could not be retained."
- **Two value types arrived on 2026-11-23, and the list was not extended for them:**
  - `8044a0c34` made Origin's value an `OriginMark` record (the zone with its explanation); before, it was a string.
  - `978784569` made the link target of an app execution alias or a WSL symbolic link a `LinkTargetText` record.
- **Verified had the same problem and has its own fix.** `dc22579ad` (2026-11-16) stores Verified's sort key without its value. The two new types have no such case.

**Not affected (source review):** sorting by a column header. That path captures only sort keys (`MetadataOrderSnapshot.Capture`), and both fields' keys are strings.

## Consequence

Low:
- Nothing is changed or read beyond the analysis.
- The banner says the order is incomplete.
- Origin sorting by column header still works.

But Analyze folder offers Origin, and on Windows (where marks are common) it orders none of the marked files. Link target fails only for its descriptive values: app execution aliases and WSL links.

## Remedy (proposed) and its cost

Keep the sort key whenever the value itself cannot be retained, as the Verified case does, or add the two record types to `Payload` with their character cost. Add a test that analyzes every metadata field with a value of each type the field can produce. Either way the change is in product source, so it reaches users only in a candidate after rc.2.

## Finite check and stop

On the candidate that carries a fix, analyze the same three pictures by Origin: the banner says all 3 results are retained, and the order is by the mark (`Internet`, `Intranet`, then the unmarked file last, or the reverse). Then analyze `%LOCALAPPDATA%\Microsoft\WindowsApps` by Link target: all aliases retained. Stop when both hold. If the owner accepts the behavior for 1.0.0 instead, record the acceptance and stop.

## Fix

Branch `fix/i636-analysis-record-values`, from `main` at `565f75778` (product source as `v1.0.0-rc.2`).

**Correction to the cause.** Sorting by a column header was affected too. In the product nothing sets `ListingModel.MetadataKeys` (only tests do), so a column-header sort does not capture through `MetadataOrderSnapshot.Capture`: it goes through `TabViewModel.CaptureMetadataOrderCore`, which passes each cached value to the same `TryAdd`. Rc.2 refused every cached mark and alias target there too, so those rows sorted as unknown, in name order, and "Origin sorting by column header still works" (Consequence) did not hold either. The new tests' column-header cases failed before the fix (Origin: 1 of 3 rows retained; Link target: 1 of 3).

**Change.** One rule for every field, in place of a case per type:
- `MetadataOrderSnapshot.CanRetain(value)` says whether a snapshot keeps a value itself: its existing list (text, numbers, dates, principals, versions, cells). The list is unchanged.
- **Analyze folder** (`TabViewModel.AnalyzeAsync`): a value the snapshot does not keep is retained as its field's sort key, taken when it is read, with its state and observation. Those are all that sorting reads. Verified's case from `dc22579ad` is now this rule, and an Origin mark and an alias's or WSL link's target follow it. A value with no key is still refused, and the banner still says a value could not be retained.
- **Column-header sort** (`CaptureMetadataOrderCore` with no analysis): the same rule, for the cached values.
- Explanations stay in the metadata cache, where cells and tooltips read them. The ordering snapshot keeps only the keys, so an Origin analysis charges a mark's text ("Internet"), not its addresses, against the character limit. Values the snapshot already kept are retained as before.
- ADR-10's retention paragraph says this instead of the Verified case.

**Tests** (this workstation, Debug build):
- `FileCat.App.Tests.AnalysisRecordValueOrderingTests`, new, at medium integrity: 26 passed, 0 skipped. Before the product change, 9 of them failed: every Origin and Link target case, in Analyze folder and in a column-header sort. Verified and the other fields already passed. The class covers:
  - Every field this platform registers, each with a value of every type its producer returns and results without a key. Each goes through Analyze folder and through a column-header sort, both directions; the reversed sort reads nothing. A guard fails when a registered field has no samples. On Windows the fields are Version, Dimensions, Link target, Origin, Extra data, Verified, Owner, Allocated, Hard links and Reparse type; Linux and macOS have Permissions and Group instead of Version and Reparse type.
  - Origin from real `Zone.Identifier` streams (ZoneId=3, none, ZoneId=1), as in the observation; on Linux and macOS, real `user.xdg.origin.url` and `com.apple.quarantine` attributes. The cell still explains the mark.
  - An app execution alias and a WSL symbolic link set with `FSCTL_SET_REPARSE_POINT` (the layouts of `LinkTargetColumnTests`), and a plain file.
  - This account's own `%LOCALAPPDATA%\Microsoft\WindowsApps`, read only: "Read Link target for 98 items: 74 current values. 24 without a value. The order retains all 98 items' results." Before the fix the banner did not say all were retained.
- The whole `FileCat.App.Tests` suite with the final change, at medium integrity: 4,013 tests, 0 failed, 294 skipped, in 40 minutes. The skips are opt-in lanes (standard-user native UI, disposable VMs, EFS and other fixtures) and other platforms' tests; none is in the new class.
- `FileCat.Core.Tests`, elevated: `MetadataOrderingSnapshotTests` 15 passed, 2 of them new: which values a snapshot keeps, and record values retained as keys and charged only for the keys. With `LinkTargetColumnTests`, `MetadataStreamingOrderTests` and `MetadataTests`: 38 tests, 0 failed, 1 skipped (Linux and macOS only).
- The Linux and macOS cases (Origin from extended attributes; Permissions and Group samples) did not run here; the pull request's CI runs them.

**Remaining.** The finite check above, on the installed candidate that carries this fix.
