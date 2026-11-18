# EFS late stream changes and the native inventory limit

The Windows 11 x64 VM exercised the native EFS binding at medium integrity (RID 8192). Each encryption test started with a clear regular file and a clear named stream. The original review pins and rename/deletion guard stayed live. The test introduced a change at the call boundary after the binding's final inspection and before invoking the real `EncryptFileW`.

| Change introduced after final inspection | Native result |
| --- | --- |
| Compress the unnamed stream | EFS enabled, that stream's compression removed |
| Compress an existing named stream | EFS enabled, that stream's compression removed |
| Create and compress a new named stream | EFS enabled, the new stream's compression removed |

Each run used the independent all-stream decompression acknowledgment, recorded one successful change, and preserved the original file identity and the exact bytes in every fixture stream. This qualifies the approved consent boundary for those three deterministic regular-file schedules. Directory-stream behavior and recovery-key verification have separate receipts.

An independent regular file had 1,025 real NTFS data streams, including the unnamed stream. Preparation returned `stream-inventory-limit` before mutation admission. An attempt to use that unprepared binding was refused. The EFS flag stayed clear, and the unnamed stream and the first and last named-stream canaries retained their bytes.

The combined EFS binding and Windows storage-inspection selection passed 23 tests. Eight cases requiring separate prepared volumes or large sparse fixtures were skipped. All four new cases passed. The xUnit XML and runner log are in `.tmp/filecat-efs-late-streams-r1-tests.xml` and `.tmp/filecat-efs-late-streams-r1.log`.
