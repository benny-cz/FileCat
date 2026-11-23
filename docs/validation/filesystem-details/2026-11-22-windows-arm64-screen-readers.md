# Screen readers on Windows on Arm — 22 November 2026

N-UX and N-UI.C stayed open on Windows on Arm because the only ARM64 Windows machines were hosted runners, and their desktop gives FileCat's window no keyboard focus ([hosted ARM64 input probes](2026-11-18-ntfs-storage-review-nvda.md#hosted-arm64-foreground-availability)). This receipt runs the x64 NVDA journeys on a disposable Windows 11 on Arm VM that has a signed-in desktop.

## Machine

- **Host:** the Apple silicon test Mac, running UTM 5.0.9 with QEMU and Apple's Hypervisor framework.
- **Guest:** Windows 11 Pro 26H2 on Arm, build 26300.9457, unactivated, with 4 cores, 6 GiB of memory and an NVMe system disk. The display was the Red Hat VirtIO GPU driver at 1024 × 768 and 96 DPI. A local administrator account signed in automatically. Each run started from a one-shot scheduled task in that desktop session, with the account's limited token (medium integrity).
- **Screen reader:** portable NVDA 2026.3.1 with a valid signature and a fresh configuration for each run. Its main program is x64, so it runs under Windows' x64 emulation; its in-process helpers are native Arm64.
- **FileCat:** self-contained win-arm64 Release builds of commit `28bbf6ee2`. Every details run used `FileCat.dll` `4BBF3571…`. The storage reviews used the qualification host: the first five runs with `FileCat.Platform.Windows.dll` `D510A0F1…`, the two reruns and both scale runs with a host rebuilt from the same commit plus the longer postcondition hold described below (`FileCat.dll` `DC7A93BC…`, `FileCat.Platform.Windows.dll` `D38C44EB…`).
- **Clipboard:** UTM's SPICE agent shares the guest clipboard with the Mac. It was stopped for the reruns and the storage reviews, so test data stayed in the guest. The first runs had it running; see the runner changes below.

## File-system details (N-UX)

The runner is [`win-nvda-filesystem-details.ps1`](../../../tests/FileCat.NativeAcceptance/win-nvda-filesystem-details.ps1), as for x64. Each run selected `alpha-details.txt` by keyboard in a folder with a long path, opened File-system details with Ctrl+Alt+Enter, read the full record, used Copy all, returned to the summary, reopened the cached full record and closed the report with Escape.

| Run | Report size at 96 DPI | Result | Receipt |
|---|---|---|---|
| Standard list | 506 × 413 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-standard.json) |
| Narrow High Contrast list | 322 × 553 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-contrast.json) |
| Short narrow High Contrast list, reached through **Next report line** | 322 × 413 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-short-narrow.json) |
| Narrow High Contrast picture grid | 322 × 553 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-contrast-picture.json) |
| Standard list, then native Properties with Alt+Enter | 506 × 413 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-properties.json) |
| Narrow High Contrast picture grid, then native Properties | 322 × 553 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-contrast-picture-properties.json) |
| Standalone Find result, then native Properties | 506 × 413 DIP | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/details-find-properties.json) |

NVDA spoke, among others:

- the focused row: "alpha-details.txt, not selected, file, 33 bytes, modified …, not marked, 2 of 3";
- the report: "alpha-details.txt – File-system details – FileCat", then "Line 1 of 57. File-system details" and "Full · observation … · F5 reads this tier again";
- the summary: "Line 1 of 41. File-system details" and "Summary · observation …";
- the return: the FileCat window, "Panel 1, source, pictures grid, …" in the picture grid, and the same row;
- in Find: the result with its location, and on return "Find: alpha-details.txt, window";
- native Properties: "alpha-details Properties, dialog".

Every copy held 2,311 UTF-8 bytes: the full record of the focused file, without the other file's name or either stream payload. The file and its named stream kept their bytes. In the Find run, the original Find window and row were active again after Escape. Alt+Enter opened the Windows `#32770` dialog titled for the same file, never File-system details.

## Runner changes

The first attempts failed in the runners, not in FileCat:

- **Properties title:** twice, NVDA stalled for about half a second as the Properties sheet opened (its watchdog logged a freeze recovery). It then announced only "General, property page" with the file's type, location and size, without the dialog title. The runner now accepts that page announcement. If the title was not spoken, it presses NVDA+T and requires the title. In the passing runs NVDA spoke the title by itself.
- **Clipboard:** `Set-Clipboard` failed with `ExternalException` while the SPICE agent held the clipboard. One run got as far as the return to the row, then lost its receipt when the final restore failed. Another stopped within 18 seconds, before NVDA started, and its restore failed too, hiding the first error. The runner now retries its own clipboard calls and records a failed restore instead of throwing.
- **Window announcement:** once, NVDA spoke the FileCat window's title without the word "window". The three NVDA runners now accept the title alone.
- **Timeouts:** a cold FileCat start and emulated NVDA's startup took longer here than the runners allowed. The waits are now 90 seconds for FileCat and 120 seconds for NVDA.
- **Guest check:** the runners accept the UTM guest when Windows reports an Arm64 OS, besides the VMware guest.

## Storage reviews (N-UI.C)

The runner is [`win-nvda-storage-review.ps1`](../../../tests/FileCat.NativeAcceptance/win-nvda-storage-review.ps1). On this guest it starts the qualification host with `--ntfs-storage-arm64-review`, which composes the native Arm64 compression adapter and the EFS candidate. The window was resized to a 640 × 420 DIP client. Keyboard input only.

| Run | What it adds | Result | Receipt |
|---|---|---|---|
| Default | Compression and EFS reviews from command search: the action radios, the fixed help, the unchecked acknowledgments, the enabled footer, and focus back to the file after each dismissal | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/review-default.json) |
| High Contrast picture grid | Both reviews from the picture grid with FileCat's High Contrast preset | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/review-contrast-pictures.json) |
| Two same-name roots | Both reviews of `alpha-review.txt` from two folders; each disclosure listed both original locations | Passed on the second attempt | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/review-duplicate-roots.json) |
| Operations recovery | The blocker escape to the running operation, Stop with its help text, the same-session pending review, and after a restart the older history row "Reviewed; effect remains uncertain" | Passed on the second attempt | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/review-operations-recovery.json) |
| Already satisfied | Enter submission of Remove compression and Decrypt on a clear file: "0 changed, 1 already satisfied, … The requested scope was fully checked." for both | Passed | [JSON](receipts/2026-11-22-windows-arm64-screen-readers/review-already-satisfied.json) |

NVDA read the dialogs ("NTFS compression… dialog", "Windows file encryption (EFS)… dialog"), each radio and its state, the inspection result and the acknowledgments. In the recovery run it read the running operation, "Stop, button, Stops before the next call. An admitted call finishes; completed changes remain.", and the interrupted operations needing review.

**First attempts:** the duplicate-roots run never saw its file list. A probe of the same start showed both rows as soon as the window appeared, after about 26 seconds. The runner had taken the window too early in the cold start and kept looking at that empty UI Automation element; it now takes the window again on each retry ([first attempt](receipts/2026-11-22-windows-arm64-screen-readers/review-duplicate-roots-first-attempt.json)). In the recovery run, the test host holds the compression call for the runner, at most 60 seconds. Emulated NVDA needed longer to walk the blocker escape, so the held call ended as uncertain before the runner looked for it ([first attempt](receipts/2026-11-22-windows-arm64-screen-readers/review-operations-recovery-first-attempt.json)). The hold is now five minutes. The runner's restart also launched the x64 qualification mode; it now reuses the mode chosen for the first start.

## Larger text and display scale

The x64 controllers of 18 November were repeated on this guest. Each ran the narrow High Contrast picture-grid details journey with native Properties, then the High Contrast picture storage review with two same-name roots. Each restored and verified the original setting.

| Setting | Result | Receipts |
|---|---|---|
| Windows text size 150% (`TextScaleFactor` read 1.5, then 1.0 after restore) | Both journeys passed at 96 DPI; details measured 322 × 553 DIP | [controller](receipts/2026-11-22-windows-arm64-screen-readers/text-scale-controller.json), [details](receipts/2026-11-22-windows-arm64-screen-readers/text-scale-details.json), [review](receipts/2026-11-22-windows-arm64-screen-readers/text-scale-review.json) |
| Display scale 150% at 1920 × 1080 | Both journeys passed at 144 DPI; details measured 324 × 554 DIP, the review client 640 × 420 DIP | [controller](receipts/2026-11-22-windows-arm64-screen-readers/display-scale-controller.json), [details](receipts/2026-11-22-windows-arm64-screen-readers/display-scale-details.json), [review](receipts/2026-11-22-windows-arm64-screen-readers/display-scale-review.json), [Settings changes](receipts/2026-11-22-windows-arm64-screen-readers/display-scale-applied.json) and [restore](receipts/2026-11-22-windows-arm64-screen-readers/display-scale-restore.json) |

The VirtIO display offers no 1600 × 900 mode, which the x64 run used. The controller set 1920 × 1080 for the session only, selected 150% through Settings with [`win-ax-display-scale.ps1`](../../../tests/FileCat.NativeAcceptance/win-ax-display-scale.ps1), and restored 100% and 1024 × 768 afterwards.

## NVDA+T check

The Properties fallback presses NVDA+T as a numpad Insert with T, injected like the runner's other keys. In every passing run NVDA spoke the title by itself, so a separate probe checked the fallback: with a plain window titled "FileCat NVDA title probe" in the foreground, NVDA logged the gesture as `NVDA+t` and spoke the title ([probe](receipts/2026-11-22-windows-arm64-screen-readers/nvda-title-probe.json)).

## Scope

- One guest, one NVDA version, and keyboard input injected in the signed-in session. NVDA's main program runs emulated, as it does on Windows on Arm devices.
- The JSON receipts are re-indented copies of what the runners wrote; their content is unchanged.
- The compression release on Windows on Arm, with its own release-build NVDA run, is in the [Windows on Arm compression receipt](2026-11-22-compression-release-arm64.md).
