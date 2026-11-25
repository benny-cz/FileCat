# I650 — macOS: typing a time in Change attributes and times ends FileCat while an accessibility client is active

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows. Scenario `x03_times_dialog_live_region`. The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/`). **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I650](../FILECAT_1_0_RELEASE_ISSUES.md#i650), merged into main as `a2f983e9a`); first recorded as open. A fix is proposed for the next candidate, and the owner decides.

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

## Fix

**Cause.** The live region is the validation message of the macOS/Linux Permissions and times dialog (`ShowUnixAttributesAsync`), a polite live `TextBlock`. `2023-01` is the first prefix that parses as a time, so the seventh key sets the message's text to null. Avalonia raises a name change for it, and Avalonia.Native 12.1.4's `-[AvnAccessibilityElement raiseLiveRegionChanged]` builds `@{NSAccessibilityAnnouncementKey: GetNSStringAndRelease(name), …}` without checking the string. An empty managed name arrives as an `IAvnString` with a null pointer, so the dictionary receives nil. Pasting a whole time never makes the message empty, which is why it did not crash. The main window's notifications already avoided the same fault with a never-empty name (`MainViewModel.NotificationAnnouncement`).

**Upstream.** Avalonia fixed this in [AvaloniaUI/Avalonia#22463](https://github.com/AvaloniaUI/Avalonia/pull/22463) ("Fix crash on macOS when a live region's text is cleared", merged 2026-10-20), which returns early for an empty announcement. Releases 12.1.5, 12.1.6 and 12.2.0 contain it; FileCat pins 12.1.4. No new upstream report is needed. The FileCat guard below stays harmless after an Avalonia update.

**Change.** `LiveRegions` (`src/FileCat.App/Controls/DescendantFocusAutomation.cs`) guards every live region FileCat watches, not only this dialog. They are text blocks, apart from Synchronize's read-only warning box. Avalonia raises a name change only for a text block's text; a text box's text raises a value change, which is not announced this way. Under the macOS bridge, a class handler for `TextBlock.TextProperty` runs before the control's own property-changed event, from which the automation peer raises the name change. When the new name is empty, it holds the live setting at Off (animation priority) while that change is raised. The control's handler in `Watch` then restores the control's own setting, after the peer's handler. Avalonia.Native therefore never announces an empty name, and non-empty messages are announced as before. The control's own live setting never changes, and Windows and Linux are untouched.

**Tests.** Each test stands in for the native bridge: it subscribes after FileCat and records the live setting and name at every name change.
- `DescendantFocusAutomationTests.Under_the_macOS_bridge_an_emptied_live_region_is_never_announced_and_keeps_its_own_setting` (every platform, with the macOS route switched on). A live text is cleared, set, emptied and set again. Only the two non-empty texts are raised live, and the control's own setting stays Polite.
- `PermissionsDialogTests.Typing_a_modified_time_key_by_key_never_announces_an_empty_live_message` (macOS and Linux; Windows' dialog has no live validation). On the real main window it opens Change attributes and times, selects Modified and types `2023-01-02 03:04:00` one key at a time, as x03 does. The invalid-time message is announced, no empty name is raised while live, and every live message keeps its own setting.

**Results.** On the physical M1 Mac (macOS 27.2.1, .NET SDK 10.0.101, Release):
- **Before:** the rc.2 product source (`ec126ea94`) with these tests failed both. Each raised an empty name while live, the crash condition.
- **After:** both passed. The focused App run (live regions, permissions dialog, notifications and supplementary data) had 35 passed, 13 skipped and 0 failed.

On Windows x64 at medium integrity, the same App classes had 35 passed, 13 skipped and 0 failed. The dialog test is among the skips there. Hosted CI on the pull request is still to run.

**Not verified here.** The Mac GUI harness could not run a build of this source. The Mac's console was locked, and a FileCat bundle published from the branch ended at start because Avalonia.Native could not start its render timer (error −6661). x03 and VoiceOver remain unrun on this fix.

**What the next installed candidate must check.** Run x03 on the Mac with an accessibility client active:
- Type `2023-01-02 03:04:00` into Modified one key at a time. FileCat must keep running, with no "quit unexpectedly" alert.
- The invalid-time message must still be announced when it appears, and Apply must be offered once the time is valid.
- Also dismiss a notification and clear another live message, such as a Find result, with VoiceOver or the harness reading. Neither may end FileCat.
