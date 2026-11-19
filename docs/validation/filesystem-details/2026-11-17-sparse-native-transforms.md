# Sparse native transform fixtures

The tests exercised Windows 11 x64 NTFS with 4,096-byte allocation units on the disposable VM as a medium-integrity user. They called native EFS encryption/decryption or the existing held-stream compression SET on synthetic 4 MiB main and named streams. These tests characterize filesystem behavior. FileCat's review, job and release admission rules still apply.

Each post-call observation records the object ID, the attributes returned for each stream, EOF, `FileStandardInfo` allocation, classic compression format, allocated ranges and SHA-256 of the complete logical stream. Both streams stayed on the original object and retained their EOF, logical bytes, reported sparse state and allocated-range lists after each tested call. The range reports use [FSCTL_QUERY_ALLOCATED_RANGES](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_query_allocated_ranges). They describe the filesystem API's view of areas that can contain nonzero bytes, rather than disk addresses.

| Native actions | Fixtures | Observations |
|---|---|---|
| Encrypt then decrypt | All-hole main, holes in main only, holes in named only, holes in both, sparse main with every range allocated | Five cases passed. Each call returned success, and the expected EFS attribute was observed. Allocation stayed unchanged in both streams, including zero bytes in the all-hole main. |
| Compress then remove main compression | Holes in main only, holes in named only, holes in both, sparse main with every range allocated | Four cases passed. The main stream observed classic compression and then no compression. Its two sparse extents used 131,072 allocation bytes before and after the round trip, and 8,192 while compressed. The named stream kept its attributes, format, ranges and allocation. |

The initial fixture assumed that a sparse named stream implies a sparse attribute on the main handle. Two cases stopped before the first call on that assertion. The corrected fixture queried both streams. The named-only fixture returned `SparseFile` and both 65,536-byte ranges on the named stream. The main handle returned no sparse flag and one full range. This distinction held through all four native actions. An EFS inventory must consider supported per-stream observations to reason about named-stream holes.

Final VM build completed with zero warnings and errors. All nine cases passed with `FILECAT_TEST_SPARSE_TRANSFORMS=1`. Exact outputs are in `.tmp/filecat-sparse-transforms-v2-tests.xml` and `.tmp/filecat-sparse-transforms-v2-results.log`. The initial probe is retained as `.tmp/filecat-sparse-transforms-tests.xml`.

The remaining gates include the FileCat held-path and held-stream adapters, read-only/denied cases, large logical size, named compression mixed with sparse layout, bounds under many extents and concurrent changes, and per-route output behavior. The fixture establishes native behavior for the tested shapes on this volume. Product sparse eligibility remains gated.

## Large sparse decompression on the x64 VM

A later run on Windows 11 x64 build 26340 produced a different native result from the earlier [sparse removal pass](2026-11-18-ntfs-compression-volume-matrix.md). Compression was removed from the 30,000,000,001-byte file and the sparse flag stayed set. Windows reported a new allocation at the start of the file. The first range grew from `65536+65536` to `0+131072`. The range at offset 2,097,152 stayed 65,536 bytes long. Total reported allocation grew from 8,192 to 196,608 bytes.

The FileCat job detected the changed hole map and finished with an uncertain native result. The same postcondition refusal recurred in three runs. Of the 24 selected tests in the diagnostic run, the other 23 passed. The ARM64 workflow covers those 23 bounded transform, overflow, concurrent-change and Save As cases. Large sparse decompression remains an open qualification case. The x64 diagnostic receipt is `.tmp/filecat-efs-live-166b6897-r1/storage-sparse-observed-8ebe335c/native-storage-sparse-observed-x64.trx`.

Using the same test binary, account, volume and temporary location, three alternating VSTest launches each reproduced the new allocation. Three direct xUnit launches each retained the original two ranges and finished with 131,072 allocated bytes. The older test binary also passed when run directly in that account. The repeated comparison is in `.tmp/filecat-efs-live-166b6897-r1/storage-sparse-observed-8ebe335c/sparse-runner-comparison.json`. The reason for the difference between the launches is still open.

A paired rerun with `FILECAT_TEST_STORAGE_EXTENTS=1` read the NTFS retrieval pointers from the same main stream before and after removal. Both initial maps reported no physical cluster mapping for VCNs 0–15. After removal, the direct xUnit run still reported that leading gap, and the VSTest run mapped all 16 clusters to physical storage. Both native extent maps completed. On this volume that is 65,536 newly allocated bytes, matching the difference in `FileStandardInfo` and the allocated-range report. The FileCat outcome remained uncertain for the VSTest launch. The extra reporting does not change the qualification criteria or establish why the launches produced different results.

The test assembly SHA-256 is `8E5B61860804581EFA71983D817A6E58B31090AB47DF774626F40AA79E552C78`. The receipt directory is `.tmp/filecat-efs-live-166b6897-r1/storage-retrieval-diagnostic-4c909222`. The VSTest TRX SHA-256 is `F5F2117B320B225A98AC217A7B207667A937CB9F70A07BF6345DD5F29193A393`. The direct xUnit XML SHA-256 is `AB2DA1DAFC82EF1A21603CE94B3D0EA6AADEDB1EFE035A471796351BAE6A9517`.

### Windows process launch comparison

The same enhanced test assembly ran twice in each of three modes on Windows 11 x64 build 26340 using the same account and NTFS volume. VSTest and a direct launch of the test's Windows executable each allocated the entire 65,536-byte leading hole. Both direct launches through `dotnet` with the test DLL preserved the two original ranges. The Windows executable reproduced the change independently of VSTest. The reason Windows made different allocation decisions remains open.

In every run the original file ID, sparse flag, 30,000,000,001-byte EOF, first 4 MiB and final byte matched the fixture. The entire named stream retained its digest, length, sparse layout, allocation and compression format. Each changed-hole run recorded the exact native attempt and `sparse-postcondition-changed` in the durable result. FileCat kept the result pending with its effect uncertain. The hole-preservation assertion still failed for those runs.

The enhanced assembly SHA-256 is `AFC425AA355EB3345573EF02A90FA08B68711F3FB6B1D0570C318504788C3DD5`. The receipt directory is `.tmp/filecat-efs-live-166b6897-r1/storage-sparse-payload-8633ad7c/three-launches-r2`. The comparison receipt `three-launches-r2.json` has SHA-256 `DED60D73E7A37C964EF488ADF459A9577670A9A554DE4F82EB4653A4FD0B8F6D`. The second-round VSTest, standalone executable and DLL receipts have SHA-256 `E978EE713350519A63F99EAD6E03D790BC6E638BC1CAE6E5488A205919A99DBB`, `B9DAB1567CDB1F3BF7C429896EF5A00C364C31FBFCC8D0CE3091C37116DFE2BE` and `6AB4F29B537E5EC180FD39EF617664A257CC0500E003DCA562BEDF65AA9017EB`.

### FileCat desktop result

The native x64 FileCat desktop qualification host reproduced the allocation change through the keyboard-reviewed **Remove compression** action. At 640 × 420 DIP, Operations reported one uncertain result and Ctrl+J reached its pending review. The independent observer verified the original identity, sparse flag, unchanged EOF, first 4 MiB, final zero byte and entire named stream. Windows removed main-stream compression and reported 196,608 allocated bytes with the leading hole filled. The applicable preservation gate is the hole map.

The receipt `native-storage-large-sparse-ui-r3.json` under `.tmp/filecat-efs-live-166b6897-r1` has SHA-256 `0320C8432CD8624B575F74D0245302C8E91F266DBD12D372080A59652D43E4ED`. The tested `FileCat.dll` and `FileCat.Platform.Windows.dll` have SHA-256 `24042895EE6888B59F0FB82CB3FD79C8171D6A51C9EA7BA53628D5A30F18EE56` and `0D4B9B81C5EF5B8FC3E55E948FFCF47EB050F31AEEC9833DA5521436F0E060FA`.

### Admission for large sparse removal

The compression candidate now refuses a required removal from a sparse main stream above 30,000,000,000 bytes. It uses the logical length and sparse flag from the same held stream. It applies the rule both during fresh review and when a previously reviewed file grows before work starts. An already-clear large sparse file still completes as already satisfied without a native call. The broader rule for compression enablement remains independent.

The Windows x64 regression executed once through VSTest and once directly. Both passed without skips. It verified fresh refusal before approval, late-growth refusal before the native call, unchanged compressed layout and the already-clear no-op. The test binary has SHA-256 `73629CD03D5BE01771CA8451C690E41B70DB899A788605202AE2665044817ED4`. Under `.tmp/filecat-efs-live-166b6897-r1/storage-sparse-boundary-cb04c6c2`, `vstest-1.trx` has SHA-256 `70D305199696E73A6F1B5D0FF1763FE07172CEBA5CBE68ED933243B09B778E68` and `direct-1.xml` has SHA-256 `BC2FF46DF5A4674F3CBE5237E78D8A9E42ED21C8A0727658E61B119A3EF3E245`.

The updated x64 desktop fixture opened the original-item disclosure with the reason, kept **Remove compression** and **Queue** disabled, and wrote no mutation journal. The original LZNT1 compression, 8,192 allocated bytes, logical length, hole map, checked main-stream bytes and complete named stream all matched. The receipt `native-storage-large-sparse-refusal-ui-r2.json` has SHA-256 `25E5D6CDE076A2A6C03D0DC87F0E06DF50D3666727957546ED40A7A81DA0E83C`. The tested Windows platform assembly SHA-256 is `65E2B5856E43FCD543999D9A5FCE11CB7DDDB3E83482CF7F7614AAC31F69C2F3`.

## Native ARM64 bounded sparse cases

All 23 selected cases executed and passed on Windows 11 Enterprise ARM64 build 26200 at medium integrity on fixed 4 KiB NTFS. The suite includes native and reviewed Compress and Remove compression, native and held-path EFS Encrypt and Decrypt, exact no-call observations for already-satisfied states, range overflow, concurrent named-stream growth, and reviewed Hex Save As to an unencrypted sparse NTFS destination. The source commit is `87e2c9d0fa824636d6e636fc08a521567fdbd8cd`. The SHA-256 of `native-storage-sparse.trx` is `1608A2C81130BD2A3E095537B3ED9792CCF4441C95FE271E2B497BC3C6929101`. [Native ARM64 sparse receipts](https://github.com/benny-cz/FileCat/actions/runs/44589829204)
