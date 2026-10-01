# E-V21-M1 — Windows MTP/WPD on a real Android phone (V21, DPI P13)

The owner connected a phone on 2026-10-01 ("usable for possible MTP tests"); the standing rule for it: FileCat may
write only inside a folder named `FileCat-test` on the phone. Plan: V21 (Windows MTP/WPD), the DPI row P13 (MTP
mutations and clean-up), PPL-03 (the phone).

## The device

| | |
|---|---|
| Phone | motorola edge 60 pro (`USB\VID_22B8&PID_2E76&MI_00`), manufacturer motorola |
| Storage | "Internal shared storage", 221 GB, 199 GB free |
| Driver | Microsoft's MTP class driver, `WUDFWpdMtp`, 10.0.26100.9492 |
| Host | Windows 11 Pro 26220, FileCat build `24ae2f7` (Debug test build) |

## What ran

The device scenarios of `MtpTests` and `MtpRobustnessTests` (`FILECAT_MTP_TEST=1`, `FILECAT_MTP_DEVICE=motorola`).
They work only inside `FileCat-test` on the first storage and remove it; the read-only check that reads whatever file
the phone offers first was **not** run, since that file could lie outside `FileCat-test`.

| Case | Result |
|---|---|
| Listing devices: never fails, leaves drives out | passed |
| A folder on the device created, filled, read back, renamed and removed | passed |
| Jobs: a tree uploaded, downloaded, renamed and deleted inside the test folder | passed |
| Empty files, Unicode names, and names that differ only in letter case | passed |
| Cancelling part way leaves nothing behind and never loses the file being replaced | passed |
| Moving to the device removes the originals only after their copies are complete | passed |
| A thousand small files (`FILECAT_MTP_BENCH=1`) | passed: uploaded in 31.0 s; listed in 3,123 ms the first time, then 553 and 536 ms; copied back in 8.3 s |

Seven cases, 0 failed. Afterwards the phone's storage root, looked at read-only through the Shell, has no
`FileCat-test` left (317 items at its root, as the owner keeps them).

## What V21 asks that this does not cover

- **Disconnecting, locking or unplugging the phone mid-transfer**, and reconnecting: these need the owner's hands at
  the moment a transfer runs (the cases exist to be run with them: no false success, partial output visible, the
  source kept while completion is uncertain, no hang).
- A full device, and the temporary-name fallback a device without renames forces.
- A read-oriented device (the iPhone class) and its read-only capability shown as such.
- Physical Windows ARM64 coverage (ENV-02), which the plan requires for the native WPD feature.
- The run on the final candidate's own build.
