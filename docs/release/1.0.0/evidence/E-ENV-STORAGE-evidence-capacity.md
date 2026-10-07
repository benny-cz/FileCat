# E-ENV-STORAGE — evidence capacity and lossless compaction

**Storage maintenance only at main a4504f1; product producer remains 3b84bc1.** Windows-reported allocation for selected files falls by 9,097,739,962 bytes (9.10 GB / 8.47 GiB). All original evidence remains at its original paths.

## Inventory and action

The owner's requested review finds 21,721 files with 80,169,265,066 logical bytes. Five Ubuntu installer ISOs account for 31,712,819,200 bytes; two historical USB source images account for 15,592,325,120 logical bytes. Among the eligible large text/binary files, exact duplicate content accounts for 12,648,005,655 redundant logical bytes. These are retained test/build inputs, not disposable merely because they are repeated.

The selected 2,194 files total 19,623,661,242 bytes before compression. Ninety-two explicit per-file `compact.exe /C /A /Q` commands apply transparent NTFS compression; reported allocation becomes 10,525,921,280 bytes. No files are removed, renamed or joined with hard links; no directory compression default is set. The original E: zero-capacity receipt remains intact. Space had already been restored before this maintenance: the command records 52,156,567,552 free bytes before and 61,258,452,992 after.

A fresh reader independently enumerates all 21,721 original paths and checks every size, modification time and single-link count. Every selected file's complete SHA-256 is rechecked against its pre-action value, along with allocation queries, raw command statuses and log hashes. Unselected attributes remain unchanged; selected attributes change only by the NTFS compressed bit. All 285 private review/command/result/source files are pinned.

## Retention and remaining options

The five ISOs remain unchanged. One Ubuntu 26.04 remastered ISO is still referenced by the running guest's disconnected-at-start DVD configuration; saved older VM configurations also reference remastered media. Removing these exact installer inputs could reclaim a further 31.71 GB, but requires an explicit retention choice and management of the guest's media reference. It is queued as an optional storage decision; it does not block other release work.

The USB before/after images, sparse ETLs, installer ISOs and existing archives are untouched. Historical source/capture-attribution evidence remains needed for I110 and the physical-source hold. The compression can be reversed for selected files with per-file `compact.exe /U` when capacity permits. No product/source/test, VM/Mac, candidate, contract or publication changes occur.

Private `FileCatReleaseEvidence/evidence-storage-review-20261007-v1`:

| Path | SHA-256 |
|---|---|
| compression-review-v3.json | 87a6633585e4b64a5e5fb8896bf6f2ac84ef822de3d876d7fea13a44ef33ccde |
| compression-result-v4.json | 1b0298880eb87efae1d8806dfa42980136fad335a8865d739859c209a660d8e1 |
| independent-storage-v5.json | c5f207454a88715c5c77d1ed1e4d82ff9670600a6badddbb253b571d0d0f41e2 |
