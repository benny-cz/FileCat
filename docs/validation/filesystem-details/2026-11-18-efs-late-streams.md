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

## Sparse stream becomes unreadable after encryption

On Windows 11 x64 build 26340, medium integrity and 4 KiB NTFS, a two-file job encrypted its first file. A competing writer then held its sparse named stream without read sharing before the post-call inspection. FileCat successfully observed the encrypted state and verified the reviewed EFS key on the original held file. The named-stream allocation query failed. FileCat recorded one uncertain call with the verified state and key, published a pending review, and stopped before encrypting the second file. The first file's main data was unchanged.

The control using the previous implementation reproduced the loss of the post-call observation. All seven tests passed with the fix, including selected and recursive EFS jobs, canceled recovery, late sparse range changes and the pre-call inventory limit. No cases were skipped. The receipts are `late-inventory-control-r3.xml` and `late-inventory-final-r3.xml` in `.tmp/filecat-arm64-efs-copy-3adf9280-r1/`. The fixed test bundle SHA-256 was `D2DE0B8945E7880EDC7B6947E127455E4B9FC6CF48EB5AA50D21740745EF1856`.

The [native ARM64 run](https://github.com/benny-cz/FileCat/actions/runs/44492520449) passed the sparse sharing case and all nine other storage ABI and job cases on Windows 11 Enterprise build 26200, medium integrity and 4 KiB NTFS. The artifact includes the `native-storage-abi.trx` receipt for `013c81c964e1bde1d580f74d68bca11ef556d205` and test assembly SHA-256 `2ABDAC48E41413D07933D030FDFE2F9ADF200D618EEC1BCA7F649A7D80D8627D`.
