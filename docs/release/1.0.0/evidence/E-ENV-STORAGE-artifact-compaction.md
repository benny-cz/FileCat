# Artifact capacity — lossless compaction on 2026-10-09

Completed storage maintenance at main `daccc623e2a57bb3c728520881838b1e2ed79db8`. Transparent NTFS compression recovers **20,754,381,399 reported bytes (20.75 GB / 19.33 GiB)**. No file is deleted, renamed, replaced or joined by a hard link.

## Result and verification

The initial inventory contains 296,743 regular files with 146,925,028,152 logical bytes. The independent final reader finds every original path, no additional paths, unchanged logical bytes and no metadata/hash failures. `GetCompressedFileSizeW` reports aggregate allocation falling from 137,479,185,534 to 116,724,804,135 bytes (137.48 to 116.72 GB). This per-file API observation is the storage oracle; it is not a claim about exact cluster accounting. The displayed logical folder size remains 146.9 GB.

The selected 3,885 uncompressed, non-sparse, single-link build/text files are each at least 2 MiB. They include native symbols/dependencies and completed JSON/TRX output. Every exact path stays inside the artifact root; selection and execution reject symlinks/reparse points. Each explicit `compact.exe /C /I /A /Q` invocation records its raw stdout/stderr and exit status. Complete SHA-256 hashes are checked before and after compression and freshly checked by the independent reader. Sizes, modification/creation timestamps, file identities and link counts remain unchanged. Selected attributes change only by the NTFS compressed bit and, where applicable, clearing NORMAL. Every unselected file retains its recorded metadata/allocation.

The first verifier stopped after 759 verified files because the 760th file changed from NORMAL (128) to COMPRESSED (2048), with the command successful and bytes/timestamps intact. [Windows defines NORMAL as valid only alone](https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants). The original failed verifier/journal remain unchanged. V2 freshly rechecks those 760 command results without recompression, explicitly qualifies that one historical refusal, permits only this documented attribute transition, and compresses the remaining 3,125 paths. The independent reader checks all original/final raw flags and complete current bytes; no failed historical result is relabelled.

Initial and final free-space observations are retained separately because the owner is also freeing space. They are not used to attribute recovered capacity. The original low-capacity failures remain evidence: the latest private I06 App regression recorded 1,352 passes, 25 exact skips and three disk-full failures; a later private chooser build failed before tests while copying native symbols. Those results are not relabelled as product failures or passes. New tests were paused for this cleanup and require fresh runs after capacity restoration.

## Retention and scope

Historical USB images/captures, installer ISOs, source exports, payloads, original commands/results and failed attempts remain at their recorded paths. No global or directory compression policy changes. Compression is reversible with per-file `compact.exe /U` when space permits. No workstation UI, device, guest/Mac setting, production/test source, issue disposition, contract, candidate or publication changes occur. I106/I110 physical-source HOLD and explicit human GO remain in force.

Earlier observations remain separately valid at their own times: [original artifact compaction](E-ENV-STORAGE-evidence-capacity.md) and [closed private JSON compaction](E-ENV-STORAGE-closed-json-compression.md). The retained 31.71 GB of installer media remains the separately queued retention option.

## Selected evidence pins

Private root: `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\artifacts-cleanup-20261009-v1`.

| File | SHA256 |
|---|---|
| inventory-v2.json | `c470b475585581930267b0a686c33ad4cbcafb23137fc6e0207e50b46c0e02fe` |
| selected-inputs-v1.json | `3728e9d95fad3db2dad0507150be9d151fea4187df73a3fa66a95c5e2e4ffd82` |
| compress-selected-v1.py | `5872fbcfc18d8720555f0262e4aefad6102635226d1c69b41025f4464d497df7` |
| per-file-results-v1.jsonl | `29719410c7a3fe8f20dc79879129eed76c425d8f33ef4460ffaf08f931ede244` |
| compression-final-v1.json | `57ede338411fd5b14d4ef400cef4528034706fca98bfb4db82817eb80a365ce8` |
| compress-selected-v2.py | `80e5684a17edf631c9922611a0235dda652252643a2cd5c4e000a38468e7073d` |
| per-file-results-v2.jsonl | `a3d2ed6fef45b92fe50e08ba64ec79865401ef9c3c598a918f248498b4b89855` |
| compression-final-v2.json | `1a21695bb450165c3912470fb4553564d123ed656da3d60730fe2e71c1dab637` |
| independent-storage-reader-v2.py | `fa7c31786bf9c1dbdd27444f54fc33b495959013346feb67ca440ee370abc822` |
| independent-storage-final-v2.json | `ac3e6f763e394d54003b34e7e15729e32181e39ca499c0934940ee3b36b625d0` |
