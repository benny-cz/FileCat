# I636 — Analyze folder keeps no order by Origin, or by Link target for app aliases and WSL links

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c32, I622), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). **Status:** open, awaiting the owner's decision.

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
