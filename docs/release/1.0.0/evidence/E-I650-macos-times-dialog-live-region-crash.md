# I650 — macOS: typing a time in Change attributes and times ends FileCat while an accessibility client is active

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows. Scenario `x03_times_dialog_live_region`. The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/`). **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Candidate and machine

- **Package:** `FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed0c002f92c624719f567bd9768eae3fe99f904dbb776feffc771acc41`; `--version` prints "FileCat 1.0.0"; the bundle is 1.0.0 (1.0.0).
- **Machine:** the physical M1 Mac, macOS 27.2.1 (26B131).
- **How it was driven:** through the accessibility API (AX), which makes an accessibility client active. VoiceOver itself was not used.

## Observation

1. File → Inspect → Change attributes and times…
2. Click into Modified, then Cmd+A.
3. Type `2023-01` one key at a time.

After `2023-0` FileCat was still running, with Apply disabled. The seventh key ended it (exit −6):

> `*** Terminating app due to uncaught exception 'NSInvalidArgumentException', reason: '*** -[__NSPlaceholderDictionary initWithObjects:forKeys:count:]: attempt to insert nil object from objects[0]'`

The stack runs through `libAvaloniaNative.dylib`, in `-[AvnAccessibilityElement raiseLiveRegionChanged]`. It was reproduced four times. Pasting the whole time at once does not crash.

## Why it matters

FileCat quits while a user types a date: any open work in the session is lost. Screen-reader users are the users with an accessibility client active. The run also suggests that the field's live-region announcement, which changes as the text becomes a valid or invalid time, reaches Avalonia's macOS layer with a missing value.

## Remedy (proposed) and its cost

Find which live region changes as the seventh key is typed, and make sure its announced text and name are never null. Alternatively, raise the announcement only with a value. If the fault is in Avalonia.Native, guard it in FileCat and report it upstream.

This changes product source, so it reaches users only in a candidate after rc.2.
