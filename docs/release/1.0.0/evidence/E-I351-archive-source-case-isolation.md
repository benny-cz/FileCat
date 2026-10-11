# E-I351 — preserve distinct archive sources and nested member bytes

Baseline **69cd5639d598cf3c5fb60d4414665f2fa1050a0b /1600 raw Git blobs**, one identical test overlay; corrected product adds two declared provider overlays. ZIP/TAR index dictionaries and source-prefix lookup/release folded paths on Unix. Equal-length, equal-timestamp case-distinct archives could therefore return the other file's complete member bytes and share invalidation, retirement or capability observations. ZIP's nested spool dictionary also folded archive member names on every platform, mixing distinct filename/folder cases within one outer archive.

Filesystem index comparisons now follow the platform's filesystem safety comparer; source-prefix lookup/release follows the same comparison. ZIP nested spool keys compare exactly, as archive member names are case-sensitive. TAR's existing exact nested-spool comparison remains. A differently spelled Windows outer alias may build a second bounded nested spool; it cannot select a different member's spool.

**32 original failures /48 healthy cases /48 filesystem-specific skips** become **80 passes /48 identical skips** across the host, Ubuntu, Mac and background Windows guest. Ubuntu verifies distinct actual inode identities; Windows and the available Mac filesystem alias case-only outer names, so sixteen outer cases per producer explicitly skip. Their twelve nested-member cases still run, with four ZIP failures corrected on each platform. This is not case-sensitive Mac/Windows filesystem qualification. Same-source repeats, TAR foreign-member spooling, both selection orders, filename/folder case, cache refresh/release and ZIP read-only capability controls remain.

Every observed **4096-byte member** is independently reconstructed and checked in full. The producers record unchanged source-container hashes and complete member observations. **6718 selected affected records /129 exact skips** retain all observed predecessors and add sixty passing initial-identity controls. The separate **347-case headless App** selection retains 335 passes/twelve exact skips. Independent postchecks verify **1161 native files**, absent owned workers/temps and closed transport listeners. No host UI, physical source or persistent setting changes.

Original preparation syntax and independent-reader mistakes remain before accepted outputs. Host restoration first refused after an archived response file vanished; its cause remains unproven. The first two roots had completed restoration, and the remaining root was re-inventoried. Six idle MSBuild children, independently tied to the owned builder PID and command-time interval, later recreated empty directories; only those six verified nodes were retired before final restoration. All original archives and adverse records remain.

This corrects finite archive source/member isolation within I06 and provider provenance within I106. It does not establish stable open-handle identity, atomic replacement, topology/alias completeness, physical-source or installed-candidate acceptance. I106/I110 HOLD and human GO remain.

## Committed follow-up — 2026-10-11

[Exact committed/native qualification](E-I351-native-qualification.md) and [original green CI](E-CI-archive-native-fixtures.md) now verify 7e88ef7 separately. [I354](E-I354-archive-source-replacement.md) additionally corrects deterministic same-metadata source replacement; broader provider-handle/atomic/physical scope remains.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, original failures/skips and restoration.

| File | SHA256 |
|---|---|
| `archive-case-safety-20261011-v1/independent-final-v1.json` | `12c4a2531c0e9e50da9bf127e55df5f0c455bd78ff79d2a56c0adfdbb2a37811` |
| `archive-case-safety-20261011-v1/retained-tool-sources-v1.json` | `1ca88210eb4924e1e5da7e0b48a1a243dceb7f99b68416ca6879ccaa7fe0986f` |
