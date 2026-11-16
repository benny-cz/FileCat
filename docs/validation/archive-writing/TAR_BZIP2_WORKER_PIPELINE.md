# TAR.BZ2 worker pipeline, 5 November 2026

Development checkpoint over `4e06980a`. This extends the unregistered TAR pipeline; it does not complete the
[archive-writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) or enable a production profile.

## Implemented behavior

Assessment, transformation and separate fresh verification now support TAR.BZ2. Canonical features `[3, dialect,
blockSize]` distinguish it from plain TAR and gzip; dialect is USTAR/PAX and block size is 1–9 in 100,000-byte units.
Creation defaults to block size 9. Updates preserve the source block size and dialect, including exact duplicate
occurrence edits. An empty TAR remains USTAR-observed while retaining its compression setting.

The strict wrapper validates `BZh` and the block-size digit before block-dependent allocation. It requires complete
block/combined CRC validation, a real footer, zero final padding bits and exact physical EOF. Concatenation, trailing
bytes, truncation and malformed framing refuse. Expanded bytes are capped and the existing 1000:1 policy after
64 MiB applies even to FileCat-produced data. Codec format exceptions become controlled archive refusals.

The existing Apache-derived decoder now exposes an internal checked-footer/end observation for synchronous and async
decoding. Tolerant/concatenated modes cannot satisfy it; their existing reader behavior is unchanged. Its existing
copyright/license notices are retained with explicit modification comments and an entry in the fork change inventory.
The already-present encoder's bounded block-size constructor is used; no upstream files or package were added/upgraded.

Compressed input buffering tracks actual codec consumption separately from physical read-ahead, so trailing bytes
cannot be hidden in a buffer. A worker-local raw TAR catalog is reused, with independent runtime-decoder comparison
and checked copy passes. Updates allow four physical compressed-source passes, without a payload spool or filesystem
authority in the worker. The parent derives the complete expected manifest and compares the fresh verifier's output,
including compression features and every member's content/metadata.

The encoder's individual byte writes are coalesced into bounded proxy-sized chunks. Otherwise valid large data would
consume one IPC frame per encoded byte. A 2 MiB incompressible multi-block payload passes through separate real
transform/verifier processes. Failed/aborted encoders cannot forward buffered bytes or a trailer during disposal;
healthy finalizer IO failures still fail the job. The stage remains parent-owned.

The output bound covers the encoder's first-RLE 5/4 growth, at-most-20-bit Huffman codes, and 64 KiB of table/selector/
framing room per minimum-size block. Four bytes per admitted TAR byte plus this block overhead is conservative;
the absolute 80 GiB stage policy remains enforced. The generic receipt minimum is 14 bytes, with additional
format-specific parent checks and mandatory complete fresh verification. Full bound/scale/fuzz qualification remains
a release gate, not inferred from small tests.

## Validation

| Environment | Broad Core replay | UI | Additional native replay |
|---|---|---|---|
| Windows 11 x64 VM / NTFS | 1,235 passed, 176 skipped (1,411 total) | 219 passed | Storage 103 passed, 34 skipped; actual LPAC 46 passed |
| macOS ARM64 / APFS | 1,355 passed, 56 skipped (1,411 total) | 219 passed | Native retained-file preparation/create/update/publication cases included in Core |
| Ubuntu x64 VM / ext4 | 1,368 passed, 43 skipped (1,411 total) | 219 passed | Actual Linux confinement 53 passed, one platform skip |

All final replays have zero failures. The native lanes add BZip2 creation/update through actual Windows LPAC probe
and application entries and the Linux cgroup/Landlock/seccomp probe. Creation passes through the existing private,
unmarked publication transaction; updates remain verified but unpublished. Mac controlled children do not qualify
a hard-memory-confined worker. Windows medium-integrity facts show 12288 before / 8192 after, exit 0; retained admin
membership does not qualify a normal filtered-user installation. SDK 10.0.101; actual runtimes Windows/Ubuntu 10.0.14,
Mac 10.0.12. Builds completed with zero warnings/errors.

Final native process checks found no FileCat, dotnet or testhost instance left on any guest. Mac idle-display/system
sleep prevention and user-active assertions remain set until reboot. Shared `E:\FileCat` remains on `main`, tracked-clean.

The focused host replay passes 89 cases, including 37 new BZip2 cases. They cover empty/multi-block raw streams,
one-byte fragmented reads, every truncation, CRC/header/block-size/padding/concatenation/trailing corruption, expansion,
abort/finalizer faults, bounded replay/physical reads, separate processes, exact duplicate edits, remove-all, wrong-wrapper
fresh verification and controlled corrupt-worker refusal. An initial instrumentation test double-counted bytes because
derived MemoryStream dispatched its span read through its array override; the counter now wraps a stream and the
four-pass assertion passes. That failed test evidence is retained separately from passing replays.

Independent Python `bz2`/`tarfile` fixtures exercise USTAR/PAX duplicate occurrences and exact mode/owner/nanosecond
metadata in both directions. Each platform's eleven BZip2 outputs, eleven neighboring gzip outputs and eight exact-time
TAR outputs pass independent readers without extraction. Native Unix C metadata/xattr/capacity observers remain enabled.

The Windows development application payload also passes DependencyNotices: 125 verified notice files. The updated
ArtifactSbom description names the reachable BZip2 encoder/raw DEFLATE decoder rather than calling the fork reader-only.
SBOM generation validates 261 exact payload files, 34 reviewed NuGet packages and one runtime pack, with 235 restored
files matching their package archives. It records the base commit plus dirty-source SHA; this is development-payload
evidence, not an installed-package or all-RID release gate. Native probes and application bundles include the changed
fork DLL/PDB explicitly; the managed Windows overlay grows from 84 to 88 files for those four assets.

No GUI acceptance, production registration, complete metadata/marked-input publication, update replacement/recovery,
cooperative cancellation, maximum-scale/global-budget, coverage-guided fuzz or all-RID/package qualification is claimed.
The main implementation loop remains active. The RAR phase follows that loop and aims for feasible license-compatible
implementation/validation; the independent UX/UI/functional/security review follows RAR.

## Evidence

Ignored raw logs/TRX/launch facts/child facts, fixtures and oracle outputs are retained in
`.tmp/test-results/archive-writing/bzip2-v1`, the host smoke/focused directories, `bzip2-artifacts-v1`, and owned guest
roots. The deployed 21-file source overlay is over `4e06980a`; subsequent report/status text does not change tested code.

| Archive/artifact | SHA-256 |
|---|---|
| Deployed source overlay | `4C15A869452BFC5F05A8D0F2A19676AC936DF990A34C4F6BDBB3E62AC5F753CF` |
| Windows 88-file managed overlay | `1E01C11E5893BD9B4F4C4C711D2CEA67E3385787585EBB7DFA9DD9D2A631FC3E` |
| Independent Windows input fixtures | `C7EA1B13BA48E58967EBA3AD2E2F2D759C70C1D21B966DD4B6C0D0A3667B412A` |
| Mac results/fixtures/logs | `A17D611A9D2D7E5557E2783FDB8D5B0AB9B896F0D24BA4717D1577E8B2A08C84` |
| Ubuntu results/fixtures/logs | `0492DCE1F2AACA06A1339D89D553C949FAF8CF3BB4B95D827B21057FDF9CF630` |
| Windows results/fixtures/launch facts | `B01C479BE396BAA5333B1D46D3F9DF2EC1CEF002ACC27EF27CF112DF4C951854` |
| Development application SPDX SBOM | `1CA983BB7396287D3809EB4B69423E90C868023C5B7ACFF9D3F161B73CF0A6C2` |
