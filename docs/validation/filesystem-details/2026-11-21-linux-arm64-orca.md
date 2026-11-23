# Linux ARM64 screen-reader journey (Orca) — 21 November 2026

This receipt closes the Linux ARM64 part of N-UX: one complete keyboard and screen-reader flow for the linux-arm64 package. The ledger had kept it open because the only ARM64 machines are hosted runners. The hosted Windows desktop refuses keyboard focus and injected keys, but Linux has no such barrier. The Ubuntu x64 Orca fixture already ran on its own Xvfb display, with its own D-Bus accessibility session and XTEST keys. A hosted Linux runner can provide all three.

## Run

The `linux-arm64-orca` job of the [ARM64 qualification workflow](../../../.github/workflows/details-arm64-qualification.yml) ran at commit `7c94dbfac` in [run 44955174350](https://github.com/benny-cz/FileCat/actions/runs/44955174350). The runner was `ubuntu-24.04-arm` with Orca 46.1 and speech-dispatcher from Ubuntu's packages. The job:

- built FileCat in Release on the runner (native linux-arm64);
- ran the committed harness [`linux-orca-filesystem-details.py`](../../../tests/FileCat.NativeAcceptance/linux-orca-filesystem-details.py) four times, each under `dbus-run-session` and `xvfb-run`;
- judged each step by what Orca actually sent to speech, read from its own debug log.

| Journey | Result | Evidence |
|---|---|---|
| Keyboard selection, File-system details from the keyboard, full record, Copy all (metadata only), summary, and focus return to the row | Passed | [report](receipts/2026-11-21-linux-arm64-orca/orca-details.json), [speech](receipts/2026-11-21-linux-arm64-orca/speech-details.txt) |
| The same at a 520 × 420 report size | Passed | [report](receipts/2026-11-21-linux-arm64-orca/orca-sized.json), [speech](receipts/2026-11-21-linux-arm64-orca/speech-sized.txt) |
| The standard Properties shortcut | Passed | [report](receipts/2026-11-21-linux-arm64-orca/orca-properties.json), [speech](receipts/2026-11-21-linux-arm64-orca/speech-properties.txt) |
| A standalone Find result: both report tiers, the cached full observation, focus return, then the result's attributes in the main workspace | Passed | [report](receipts/2026-11-21-linux-arm64-orca/orca-find.json), [speech](receipts/2026-11-21-linux-arm64-orca/speech-find.txt) |

Orca spoke, among others:

- the focused row: "alpha-details.txt, file, 27 bytes, modified …, not marked, 2 of 3";
- the report: "Line 1 of 42. File-system details", then "Summary · observation …";
- the copy: "The whole report is on the clipboard."

In every run, the clipboard held the full record without the file's data or extended-attribute payload canaries, and the file kept its bytes and attribute. The speech files list each phrase Orca spoke.

## Scope

The keyboard events are XTEST events on an isolated X11 display, as in the Ubuntu x64 fixture. This is not a live desktop session with physical keys. Windows on Arm stays open: its hosted desktop gives FileCat's window no keyboard focus ([hosted ARM64 input probes](2026-11-18-ntfs-storage-review-nvda.md#hosted-arm64-foreground-availability)).
