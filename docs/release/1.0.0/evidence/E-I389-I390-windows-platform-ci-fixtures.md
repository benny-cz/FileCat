# E-I389/I390 — Windows platform test bounds and path identity

Preliminary I108/ENV-06 test corrections, 2026-10-12. Neither observation proves a product device-read or source-safety failure. The original failed results remain failed; corrected-source hosted CI and final-candidate qualification are pending.

## I389: ARM64 helper closure wait

Exact documentation-only `f205828` [run 38254066723](https://github.com/benny-cz/FileCat/actions/runs/38254066723) reached the Windows ARM64 platform suite after Core, dependency, build and asset controls passed. Its `DeviceReadTests.A_disk_image_scans_through_the_helper_session_like_the_file_itself` recovered the expected FAT16 image entry and content, then timed out at the test's five-second wait for the dedicated named-pipe server task to return its exact close acknowledgement. The raw platform TRX reports **381 passed/34 not executed/one failed** out of 416 enumerated cases; downstream ARM64 Remote/App/package steps were skipped. The preceding exact `5788c96` hosted run passed the same executable product/test source, including this case, on ARM64. The runner pause or server scheduling cause is not attributed.

The correction keeps the finite wait and the exact `FileCat closed the session.` assertion, extending the three equivalent helper-session close waits from five to twenty seconds. A genuine persistent server/pipe leak still fails. This changes no device protocol, recovery bytes or production code. The focused local Windows test class passes **9/1 explicit skip/0 failures**; the final full platform suite passes **378/38 explicit skips/0 failures**. The local host has a different skip set from hosted ARM64.

## I390: case-sensitive comparison in an owned Windows safety fixture

The first local full platform run after the I389 change failed twelve `WindowsBackingStagingReplacementTests` cases. All had `OriginalIdentity=null` only because the fixture compared source paths with ordinal case sensitivity: `GetTempPath()` returned `C:\Windows\Temp` while `DirectoryInfo.FullName` used `C:\WINDOWS\Temp`. The native identity query itself returned a nonempty volume/file ID, and the normal cases retained exact source/incoming bytes and successful publication. The changed cases' raw outputs and failure messages remain in the original local TRX. This is a fixture observation on the elevated host, not a claimed product source deletion.

The fixture now compares its two Windows paths with `OrdinalIgnoreCase`; it still requires the original native file identity and every source-retention/publication/byte/cleanup assertion. The full local platform suite changes from **366 passed/38 skipped/12 failed** to **378 passed/38 skipped/0 failed**. It does not establish physical-source safety, arbitrary path alias equivalence or final candidate acceptance.

## Immutable private inputs

`V:\FileCat\artifacts\release-evidence\ci-i389-i390-20261012-v1` contains the ARM64 TRX, both local full-suite TRX, focused TRX, exact original source exports, correction diff, run/artifact metadata and a rechecked file manifest. `manifest.json` SHA-256: `6b59a60150c19a56de9bcfe077deb1382f7fc0dca160b6ca468b4e0c415df25b`. The original workflow was still running when its ARM64 artifact was collected; this record does not invent outcomes for unfinished lanes. I106/I110 physical-source validation remains on HOLD.
