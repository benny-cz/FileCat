# Visualization storage — lossless compaction

2026-10-09 UTC / 2026-10-10 CEST. User-authorized maintenance of `C:\Users\marek\.codex\visualizations`, at publication repository main **20f1bdc5ce168c414fb1c9457c0f76b61c9e2059**. This is storage maintenance, not a FileCat release qualification.

## Verified result

Transparent NTFS compression recovers **56.56 GB (52.67 GiB; 56558694460 API-reported bytes)** across **17244 files**. No file is removed, renamed, replaced or hard-linked by this maintenance. The original snapshot contains **1,050,597 regular paths**, **601,831,884,771 logical bytes** and **454,348,312,094 reported allocated bytes**. The base independent reader checks every original path and freshly hashes all 16,875 files compressed in that pass. A later reader freshly hashes the 369 additional alias-compressed files; it carries the closed base qualification without repeating that global walk or those base hashes. It reports zero failed checks; 10168 selected candidates are explicitly skipped.

Logical folder size remains about **601.8 GB**. The per-file `GetCompressedFileSizeW` observation measures recovered allocation, separately from changing free disk space. Every compressed file retains its exact bytes, size, modification/creation timestamps, file identity and link count. Only the NTFS compressed attribute (including the observed NORMAL transition) changes. The private record retains 409610 independently observed changes to unselected files; these are not attributed to compression. New files and this owned maintenance directory are outside the original snapshot.

Selection covers single-link, uncompressed, non-sparse, non-encrypted regular files at least 1 MiB. Known compressed media/archive extensions and magic are skipped. The original compactor stopped after 6763 journaled paths without a final receipt. Resume v2 refused before mutation because skips are interleaved with native batches; v3 verifies unique complete index coverage, preserves both original raw journal prefixes and completes the remaining files. Original native refusal/skips remain raw results, including compact.exe out-of-memory messages. A separate known-byte control verifies that a long literal argument fails while its short native alias compresses successfully. The alias retry opens only the 687 preserved refusal candidates: 369 qualify and compress, recovering another 637,817,553 bytes; 318 have changed since the refusal and are untouched. Exact canonical path and file identity qualify every alias. This does not establish the cause of every original refusal. Every selected path and all ancestors are checked against the literal root and reparse boundaries. Four pre-existing reparse directories are not followed. Each native `compact.exe /C /I /A /Q` batch retains exact arguments, raw stdout/stderr, exit status and timing. No global/directory policy changes. An independent complete-byte reader qualifies the writer results. No physical source is opened. I106/I110 HOLD and human GO remain.

## Why there are two locations

C: accumulated validation control scripts, original/fixed source/build snapshots, package copies, native traces and independent receipts. E: holds repository test/artifact outputs. Release records currently reference both roots, so C: is not a disposable rendering cache. The inventory’s largest logical groups by extension are PDB symbols (225.02 GB), DLLs (146.61 GB) and shared libraries (126.04 GB). These figures describe file types, not proven duplicate bytes or permission to discard a particular artifact.

Retain unique original logs, failure/skip evidence, source/artifact identities and verification records. Future build/source/publish payloads should use `E:\FileCat\artifacts\release-evidence`; C: control files should stay bounded. Consolidation or removal of historical copies requires a checked archive/duplicate map and reference reconciliation. The independent reader identifies **15768 repeated copies / 69.63 GB of allocation** among only the initial pass’s 16,875 freshly rehashed files. That is retained duplicate content, not additional space already reclaimed; other files and cross-volume duplication remain unqualified. The nested full-hash/member manifest supports later checked consolidation. This pass preserves all existing evidence paths. Earlier [E: artifact compaction](E-ENV-STORAGE-artifact-compaction.md) stays separately attributable.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `visualizations-cleanup-20261009-v1/inventory-final-v1.json` | `475df6d9e8239b778f86e44c53ab30484e4105ee9dab1687ad63b4f0aef88dc3` |
| `visualizations-cleanup-20261009-v1/selected-inputs-v1.json` | `341dd8d00ae93343b9d1a7990ae4c5b726fdac3f75e84f0b8702f6cf1df95f89` |
| `visualizations-cleanup-20261009-v1/compress-selected-v1.py` | `930c3254dbb7dba041d83acaf95575175c90b1915aaf60762d891b4d5816a118` |
| `visualizations-cleanup-20261009-v1/compress-selected-v3.py` | `a35decd0f07acd99836b7cdcdd45f9ca6bfdbc556d7881a67ebff5067b9d685d` |
| `visualizations-cleanup-20261009-v1/compression-final-v3.json` | `00a15bdd69a6e3f45b4a7c27e00d99e436c6f1012b43f99ecaf5f51b101537fb` |
| `visualizations-cleanup-20261009-v1/independent-storage-reader-v3.py` | `4cbd0d7fdf8b42b4a42401858311a8eefd754f455c814cc887b8dd32e29526e6` |
| `visualizations-cleanup-20261009-v1/independent-storage-combined-v4.json` | `ea0eac1d36959249e9da22d5cde18dc16c3b1136b3c93f8c76f4fc1ada7c28ac` |
| `visualizations-cleanup-20261009-v1/reader-command-v3.json` | `6810695fbe51292b6c38621685258b0c73f709fcba7513b116d371473c900ef0` |
| `visualizations-cleanup-20261009-v1/identical-compressed-content-v3.json` | `a0738b920ca5d021dd2a1f6a61479f7aa0b9d24245dd2103c895f2e22af4f580` |
| `visualizations-cleanup-20261009-v1/independent-storage-final-v3.json` | `8d05a79c12ba9fb6b30185a79d335aab31261031572efb9de072acb808b10b93` |
| `visualizations-cleanup-20261009-v1/native-path-probe-v2/proof-v1.json` | `597f811f298a14de74dbf352f13f986641b0e0aada15d3bfe170c017e54f13a8` |
| `visualizations-cleanup-20261009-v1/compress-native-aliases-v4.py` | `092081b5716075221b9d8f3e10239155195fa17dfcf2d9b2d3fd07fd149ba337` |
| `visualizations-cleanup-20261009-v1/alias-compression-final-v4.json` | `d9ee396a2b3c626b7050c99a5ef04b7934d293ef4ae39394106d0e3289629e5b` |
| `visualizations-cleanup-20261009-v1/independent-alias-reader-v4.py` | `7f93f80e03d28686b6bb032943aef83e23bccfe275999b1220a71a06ab55d4ea` |
