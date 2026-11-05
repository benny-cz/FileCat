# Streamed manifest sidecar checkpoint

Date: 5 November 2026. AW2 foundation; no new archive writer is enabled. This provides bounded stream serialization and
review for the manifest references already present in archive journal facts. Native private-file creation/permissions,
object leases, durable file/directory flush, transaction integration and object-bound cleanup remain separate work.

## Contract and binary format

The trusted parent supplies a stream, operation nonce, exact profile/options and purpose: AssessedSource (1),
ExpectedOutput (2) or VerifiedOutput (3). The API chooses no pathname, opens no file and never publishes/deletes.
Its returned `ArchiveManifestSidecarInfo` is encoded length/SHA-256 evidence, not a file identity or publication receipt.
The native owner must create/open a private exclusive regular file, retain its identity, durably flush it and record
its reference before the corresponding publication mutation. The sidecar does not establish original-data authenticity.

Schema 1 uses the following layout. Integers are little-endian; the 16-byte nonce uses RFC 4122 byte order.

| Offset | Field |
|---:|---|
| 0 | Eight bytes `FCAMSC01` |
| 8 | UInt16 schema 1; byte purpose; byte reserved 0 |
| 12 | Operation nonce, 16 bytes |
| 28 | Four Int32 fields: format ID, profile version, compression option, dictionary bytes |
| 44 | Int32 member count |
| 48 | Int64 logical byte total |
| 56 | Int32 feature-blob length F |
| 60 | F feature bytes |
| 60 + F | Three 32-byte canonical full/content/metadata digests |
| 156 + F | One Int32 encoded-record length followed by the existing schema-1 assessment member record, for each member |
| End | Four bytes `FCME`, then physical EOF |

The external SHA-256 covers every encoded byte, including nonce/purpose/header, ordered records and terminal marker.
There is no host path/provider/engine object in the encoding. Physical source revision remains separate in the journal
and source assessment contracts. The three canonical semantic hashes retain their existing schema-1 encoding; the
in-memory and streamed paths now share one incremental digest builder. Independent pre-existing-schema golden vectors
pin all three hashes, so the refactor cannot silently redefine persisted catalog digests.

Writer output must be empty, seekable and at position zero; it preflights lengths, leaves caller streams open, and
returns no checksum after failure/cancellation. It does not truncate a partial output for retry. Its ordinary stream
flush is not a claim of native file/directory durability. A fresh held-file read is still required by integration.

Reader input must be seekable, at position zero, and the exact externally expected length. Nonce, purpose and profile
are checked before member decoding. Each record is bounded before its byte-buffer allocation, then names/types,
occurrences, logical bytes and metadata are validated and charged. Members returned before completion are unverified
evidence. `Complete`, or `ReadNext` returning null, requires every record, exact duplicate occurrences, all logical
bytes, terminal marker, EOF, all three recomputed semantic digests and the external file checksum. Early completion,
IO failure, corruption or cancellation poisons the reader; it cannot resume or expose verified digests.

Review retains bounded names/occurrence counters and incremental hashes, not all decoded DTOs/metadata blobs. A weak
reference test proves decoded objects can be collected during a completed streaming scan. Physical encoded size is
capped at 128 MiB, a complete entry at 1 MiB, names at 32,000 UTF-16 units each, member count at 100,000, logical bytes at
64 GiB and conservatively charged heap at 192 MiB. The shared budget now explicitly enforces the plan's existing
64 Mi-character aggregate member-name ceiling; TAR assessment/planning and wire/sidecar decoding charge it. This restores
the specified ceiling, rather than raising a profile limit. Generated-parent-name accounting in future planners is
still a separate required gate.

## Checks and evidence

The 61 new cases cover every explicit format/purpose, empty output, complete-EOF verification, golden hash compatibility,
scope mismatch, repaired-checksum structural/semantic mutations, every truncation of a small fixture, truncation after
open, independent file checksum failure, cancellation/IO poisoning, partial-output retention, pre-allocation bounds,
streaming DTO collection, aggregate name charging while discarding entries, and a persisted file reopened through a
fresh handle. A virtual 128 MB sidecar exceeds the aggregate name budget without retaining the whole encoded file.

The first real-file fixture attempted overwriting an open destination with `File.Move(overwrite:true)`; Windows refused
it. The portable fixture instead renames the held original and installs a new occupant at the old name. The reader
validates the old held stream, while the later pathname occupant remains untouched. This demonstrates why a matching
sidecar checksum cannot authorize pathname cleanup. It is not an atomic-replacement or object-bound-cleanup qualification.

Affected filter is the broader archive/edit/journal/job/undo filter recorded in
[the journal checkpoint](ARCHIVE_TRANSACTION_JOURNAL.md).

| Environment | Result | Evidence |
|---|---|---|
| Windows x64 host, SDK 10.0.101 | 757 passed, 30 skipped, 0 failed (787 total) | `.tmp/test-results/archive-writing/archive-sidecar-host-final.trx` |
| Windows 11 x64 VM, runtime 10.0.14 | 757 passed, 30 skipped, 0 failed (787 total) | Owned guest `results/archive-sidecar-vm1.trx`, copied to host evidence |
| macOS ARM64 / APFS, SDK 10.0.101 | 766 passed, 21 skipped, 0 failed (787 total) | Owned Mac `assessment-results/archive-sidecar-mac.trx` |
| Ubuntu x64 / ext4, SDK 10.0.101 | 782 passed, 5 skipped, 0 failed (787 total) | Owned Ubuntu `results/archive-sidecar-ubuntu.trx` |

All 61 new cases execute in each environment. Existing platform-only/opt-in skips remain. These are component/native
filesystem regressions, not the full archive UI, installed-package, sandbox or actual publication crash matrix. The
Windows VM uses host-built assemblies and the test-only medium-integrity launcher; facts: parent PID 2604, child PID
4600, runtime 10.0.14, integrity 12288 before / 8192 after, exit 0.

The exact seven-file source delta over `c2585c27` was applied to both owned Unix source trees. Its tar.gz SHA-256 is
`8F03271B57E7AFD21980A27E73E33CB6EB5BB06808344EF87AD55AD88DB565FA`.

Final Windows assembly SHA-256:

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.dll | `BCF91C55793174AECFA14795C08A05795D4837CF8542299352107876A4677D5F` |
| FileCat.Archives.dll | `9F79EA617E239058BE4B34F5D08ED20F7AB55FD012F5EFF1EB2868ED5B934F90` |
| FileCat.Core.Tests.dll | `62DEB3305E776172DD361E9D490CBEB5A526CD8561E2F516B9AD6D2AA9F7415D` |

All used guests had no FileCat/test processes after the runs. Mac `caffeinate` remains until reboot as requested.
No package/dependency/third-party source was added. This does not satisfy coverage-guided fuzz CPU floors, private
sidecar/native transaction integration, provenance, worker ZIP migration or the final independent review gate.
