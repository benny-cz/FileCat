# I364 — deep archive names must not consume the indexer's call stack

**Status:** remediated preliminarily at `165085a2f4050c970b1dbaf11ab121ec6b9d0ac0`. Exact candidate, broad archive-format, native UI and release qualification remain open.

The ZIP and TAR index builders used recursive `EnsureDir` calls while creating missing ancestors for an archive member. A valid ZIP/TAR member with 4,096 `d/` path segments exhausts an owned child's measured 192 KiB stack. On baseline `aeaea5cbd38c4dc22d853993ba733b6cb8578abf` / 1,642 raw Git blobs, four deep ZIP/TAR cases exit with Windows stack-overflow status `0xC00000FD`; two shallow controls pass. Each original archive is retained byte for byte in the test output. This is a bounded-stack reproduction, not a claim that the default application stack necessarily overflows at that depth.

Both builders now walk missing ancestors iteratively, parent to child. The TAR builder preserves the explicit directory's timestamp on the requested node. The six new controls read a known 4,096-byte member in full through the real provider, verify root and leaf listings, and delete the owned scratch directory after the isolated child exits. All six proposal controls pass locally and on Windows 11, Ubuntu and macOS. The local affected selection has **428 passes / 18 unchanged skips**; the native selections have **1,546 passes / 77 unchanged skips** across 1,623 records. All 1,605 predecessor native names, outcomes and skip reasons remain, with six new passing controls on each guest. The original four stack-overflow exits, two healthy controls and two Unix preparer errors remain retained.

The exact committed `165085a` source is independently exported as **1,643 raw Git blobs, with no overlays**. Its six local controls and 428-pass/18-skip affected selection pass; all 446 proposal names, outcomes, messages and skips remain. The exact Windows, Ubuntu and Mac guests pass the same six whole-byte controls and affected selections. Fresh postchecks verify **907 staged/runtime files** (Windows 279, Ubuntu 277, Mac 351), with no owned test process or temporary fixture left. The first exact export reader compared CRLF working files directly with raw LF Git blobs and stopped before compilation; its failure is retained. The corrected reader compares normalized bytes while building and testing only raw committed blobs. Guest payloads and provenance remain separately pinned; no host UI or physical source was used.

This correction removes recursive stack consumption in these two index paths. It does not qualify every archive parser or member depth/length budget, full application's default-stack behavior, candidate package, or physical-source behavior. The I106/I110 physical-source HOLD and owner GO remain unchanged. [Original-push CI](E-CI-archive-depth-and-budget.md) now passes all four lanes at `165085a`, with 24 new deep-path passes and every predecessor result/skip retained.

## Selected immutable receipts

Paths are relative to the private `FileCatReleaseEvidence` root unless they begin with `V:`. Nested receipts pin full commands, original input bytes, result archives, exact sources, failure outputs, skips and cleanup.

| File | SHA-256 |
|---|---|
| `deep-path-host-qualification-20261011-v1/independent-final-v1.json` | `7b110a8e3f7bcdd2f277b0d042dad0b129c0a4cdd0eb95776898ca9685cdd076` |
| `deep-path-native-qualification-20261011-v3/independent-final-v1.json` | `7f64c4825e67edcf3ba0256a3aeac7591f1ccbd34a63cc26a01b551d5d45d418` |
| `i364-exact-host-qualification-20261011-v1/independent-final-v1.json` | `f36430824ed90860df8012e996c263acb50da2833d92b01ef91bceed0ff5e1d4` |
| `i364-exact-qualification-20261011-v1/independent-final-v1.json` | `db251681b501543ed15c097a269d1b4172a5dbc13001faa3537508e202ccd95c` |

No receipt from a proposal overlay is treated as an exact committed source or a completed release candidate.
