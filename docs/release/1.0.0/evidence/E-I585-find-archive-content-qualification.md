# I585 — Find hid the archive-content limit behind “No matches”

## Consequence and accepted boundary

In an isolated native Mac source build, the owned `target/source.zip` contained `alpha.txt` and `beta.txt`; `beta.txt` held the exact text `owned beta`. Find, opened from inside the ZIP, correctly required a local folder. With **Look in** set to its owning target folder, **Named** `*.txt`, **Containing** `owned beta`, and **Inside archives** checked, Find reported **No matches** and **1 folder · 0 files read · 0 matched**. The checkbox tooltip disclosed a name-only archive limit, but neither the active criteria nor the empty result did. Clearing only Containing found both members, proving archive-name discovery was working. The 235-byte ZIP had SHA-256 `aa514b0d11f6bd498e24d8fc87977375abe3c4681f2d168930fca27d3f6bc73e`; independent `unzip -p` rehashes matched the two original text files. The source and sibling sentinel remained unchanged.

This does not reopen the accepted archive reader, content safety or result-set identity cases. Core `SearchEngine` intentionally searches archive member **names** only when a content query is absent. This issue is the visible interpretation of an empty result when the two criteria are combined.

## Correction and bounded verification

Product commit `9055a16abde9c8fab8c2dff63edb037a934cd806` adds an accessible, persistent qualification directly under Find's action buttons whenever **Inside archives** is checked and **Containing** has text: **“Inside archives is ignored when Containing has text. This search checks local file contents only; archive member names and contents are not searched.”** It remains visible after the result arrives and disappears when the combination is removed. No archive parser or search scope changed.

Focused Windows x64 headless App checks passed **2/2** for the no-result qualification, archive-name positive control and minimum-size viewport. The first assertion was corrected for Avalonia's deferred text-change notification; the second test fixture was scoped to its own folder so the positive control excluded pre-existing local text files. The final two-case run passed; no unrelated suite was repeated.

A separate clean Mac worktree at exact `9055a16a` produced an ad-hoc-signed local package, ZIP SHA-256 `4721ee2b9b8ebbf3c37634cd807dbdd503eb06b329bf810bb5e915a83bb8aace`, App DLL SHA-256 `48f6be644a2da66a768d30defa4d2de7a55b8ddce6e5a84fe12cc905d29bab7b`; extracted `FileCat.app` passed `codesign --verify --deep --strict`. An isolated `--new-instance --data` session used only the owned `journey19` source/target and a new `journey20` state root. Native Mac AX showed the exact limitation **before** Find and **after** the zero-result completion alongside **No matches · 1 folder · 0 files read · 0 matched**. Clearing only Containing removed the qualification, and the next Find showed exactly the two ZIP member rows `alpha.txt`/`beta.txt` with **2 matched**. The ZIP and all three source hashes remained exact, including the unselected sentinel. This verifies the intended user handoff on Mac, not a final installed candidate.

## Disposition and stopping rule

Medium misleading-results UX issue, remediated preliminarily. The exact-source Mac native before/after criterion is met. [Run 41247623005](https://github.com/benny-cz/FileCat/actions/runs/41247623005) at `9055a16a` passed producer, Windows x64, Windows ARM64, Ubuntu and macOS; tag-only package/draft jobs skipped by design. Detailed TRX skip reconciliation and final installed 1.0.0 candidate acceptance remain separate. Public preview.5 predates I585. Reopen only for a later user-visible contradictory result or a consequential CI/native regression. Stable 1.0.0 remains **NO-GO**.
