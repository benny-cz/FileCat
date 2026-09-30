# E-I20 — `$LogFile` attribution in the file-system record (D-56)

Classification: preliminary automated evidence (physical Windows 11 Insider host, elevated; CI hosted runners).

## E-I20-R1 — discovery and reproduction

- CI run [36754000317](https://github.com/benny-cz/FileCat/actions/runs/36754000317), job 110019476573 (Windows ARM64,
  `windows-11-arm`), source `f87ad32`: `WindowsFileRecordsTests.As_administrator_the_MFT_record_shows_a_creation_time_set_afterwards`
  failed at `WindowsFileRecordsTests.cs` line 112 — the warnings held "Timestamp checks: 1 sign that its times were set r…",
  the test expects 2. Job log SHA-256 `21e2c1ecbc576db20660967dccb13787c6b8508dffec8cc80e79b38ebe03e4a6` (retained
  locally). The same test passed in A01 (`4f6b062`), and `f87ad32` did not touch the records code.
- Locally, source `f87ad32` (Debug, physical host, elevated), the single test run 15 times: **3 failed**. In each failure
  the `$LogFile` table's newest rows belonged to the previous item that had used the same MFT record: "DeallocateFileRecordSegment
  — the record was freed (the item deleted)" and "$STANDARD_INFORMATION: Created 2019-05-01 12:00:00", while the current
  file's own "InitializeFileRecordSegment … as “stomped.bin”" was absent.
- Cause (source): the item's record is read with `FSCTL_GET_NTFS_FILE_RECORD` (cache), `$LogFile` raw from the disk;
  `_logMadeAt` took the newest `InitializeFileRecordSegment` on the record whoever it was made for.

## E-I20-V1 — the fix (`45efc09`)

- The same test, 15 runs locally with the fix: **15 passed**.
- New `NtfsLogTests` cases for `NtfsLog.OwnHistoryStart` (history starts at the creation with the item's own sequence
  number; an earlier item's operations never count; no reuse in the log counts only when the log reaches the record's
  latest change; an unreadable newest creation is taken as the item's): pass.
- Affected regression, full solution Debug (`--logger "trx;LogFileName=i20.trx"`), exit 0: Core 511/32, Platform.Windows
  87/15, Remote 38/5, App 155/4. TRX SHA-256: Core `9360fdf3f30ee6fc854ccd0279115dc2d2081573ba9e69d90dc5e7594e3b9ba0`,
  Platform.Windows `142fa0618463b4fb889aba292daa8c8d58a7b7fd89298c98e804e2abbcb5b814`, Remote
  `8b971bdc0266aa39283a6f74be6061859b1c7dd748d3e2c0acefb11d65548a2b`, App
  `d1c739ab26e6b4254ca4aefb694c7995015078a9c86cd7b9b16451ee1365dd7a`.
- CI run [36756346845](https://github.com/benny-cz/FileCat/actions/runs/36756346845) on `45efc09`: Windows x64,
  Windows ARM64, Ubuntu and macOS all green.
- Limitations: one green CI run and 15 local runs do not prove the absence of other timing effects; V14 still has to
  compare `$LogFile` output with an independent reader on the final candidate.
