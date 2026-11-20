# EFS protection during archive extraction and drag staging

ZIP and other archive providers admit staged output through the protection observations of the opened archive sources. The admission also covers a small member already materialized in memory. For a forward-only member the provider rechecks before and after reading each buffer. EFS or unknown protection prevents the initial output admission or interrupts publication if it is observed during a read.

On Windows 11 x64, four native cases exercised the actual drag and streamed transfer routes using EFS-encrypted ZIP and TAR archives. Each format included a small in-memory member and a 33 MiB member that supports forward-only reads. View reads succeeded. Drag staging and extraction were refused, the clear output directories contained no payload, and the source remained encrypted. The suite passed 111 tests with two fixture skips. The result is in `.tmp/filecat-efs-archive-publication-verified-tests.xml`.

Ubuntu and macOS each passed 50 ordinary archive and drag tests with eight fixture skips. This checkpoint covers the tested archive routes of E-OUT12 and E-OUT13 and uses the shared all-input observations qualified separately for disk caches. [Recovery image outputs](2026-11-18-efs-recovery-image-output.md) have a separate checkpoint.

## Multipart RAR output

The multipart RAR fixture copies six volumes to a disposable directory and encrypts only the third volume. A member can still be opened for viewing in memory. When the same member is sent through FileCat's disk spool, extraction job or drag staging, the protection of the opened later volume causes refusal. The archive cache is absent, the extraction destination is empty, and drag staging leaves no payload. The first and third volumes retain their distinct protection states.

The Windows 11 x64 VM passed all 10 archive cases and all 14 hex output cases at medium integrity on 4 KiB NTFS. The test source was `986d8edd` plus the output qualification changes. The XML receipts are `.tmp/filecat-arm64-output-986d8edd-r1-Core.xml` and `.tmp/filecat-arm64-output-986d8edd-r1-Platform.xml`. Native ARM64 verification uses the same selected suites in the manually dispatched Windows qualification workflow.

## Native ARM64 archive and hex output

The [ARM64 qualification run](https://github.com/benny-cz/FileCat/actions/runs/44472080610) executed and passed all 10 archive, 14 hex output and nine storage cases without skips. It used Windows 11 Enterprise build 26200, native ARM64 .NET 10.0.15, medium integrity and a 4 KiB NTFS volume. The archive receipt confirms refusal for the tested ZIP and TAR in-memory and forward-only members and for the multipart RAR member with an encrypted later volume. The tested destinations were private local fixture paths.

The hex cases verified refusal of in-place persistence and recovery on EFS inputs, rechecks when the target became encrypted during save or recovery, and legitimate recovery after explicit decryption. Imports from an actual encrypted patch retained provenance through partial application and Undo/Redo. Tested Save As and patch export cases required explicit approval before creating plaintext output. The clear and unknown input controls also ran. This qualifies the tested output routes on ARM64. The Recovery image routes, other formats and destinations, and the wider release gates still need their own evidence.

The hosted artifact contains `native-efs-archive-output.trx`, `native-efs-hex-output.trx`, `native-storage-abi.trx` and the native build manifest. The tested commit was `59da8ae513899ffc064c9651e56d06398853581a`. The Core test assembly SHA-256 was `82B571936E04174E580274E2555F3CA4C9068CCF2E30A84637DED892EC3FE230`. The Windows platform test assembly SHA-256 was `B7CA1EEA5F605C4169BD50CB966E33DDBFC288093684798F7820EBA41995C7FE`.

## Native ARM64 packing, editing and publication protections

The Windows 11 Enterprise ARM64 runner (build 26200, medium integrity, 4 KiB NTFS) ran 49 selected cases. All 49 executed and passed. The source commit was `4bb5a47ec0f2756dbf795c05b3dbd0a8cebd6aad` and the test assembly SHA-256 was `E38A67D4F72CA4C2047B40F3DDD521E1CF49FE1D9A1CB74B4E866E9C55767023`. [Run and `native-efs-archive-protection.trx`](https://github.com/benny-cz/FileCat/actions/runs/44505500976)

| Route | Cases | Observed protection on the prepared paths |
| --- | ---: | --- |
| E-OUT6, packing and move into an archive | 13 | Native and legacy admission refused protected selections before reading a payload or creating a stage, including nested, directory-default and sparse cases. The legacy job stopped on observed protection changes, and active read holds resisted encryption. |
| E-OUT8, member edit and retained snapshots | 19 | Tests preserved the clear outer hold through extraction, refused protected outer ZIP and working files, and exercised commit and Save Copy with held snapshots and encrypted sources or destinations. |
| E-OUT7 and E-OUT9, archive updates | 14 | Tested native and legacy updates stopped before staging when the target became protected. Native snapshot, held-read, legacy stage, retained backup and partial replacement checks kept the exact affected objects. |
| E-OUT10, nested and legacy ZIP spools | 3 | Protected members remained viewable via the tested memory and forward-only routes. A disk-backed read or nested spool was refused before creating payload output. |

These receipts cover the selected protection boundaries on local NTFS. Full worker publication to other filesystems, cross-volume cases and native device-loss remain separate qualifications.
