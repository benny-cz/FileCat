# Legacy ZIP publication extraction checkpoint

Date: 5 November 2026. This is the narrow compatibility extraction required by AW2 before moving ZIP transformation
into a worker. It does not qualify a new publication adapter or enable a new profile.

`LegacyZipPublicationTransaction` now owns the existing stage naming, source digest, final baseline/mark checks,
publication, retired-original reconciliation, rollback, journal outcome and cleanup. `ZipUpdateExecutor` retains the
codec, planning and stage verification. The source stream is closed at the same point before publication. Publication
still commits all roots together. The extracted preparation and publication bodies were compared with the prior
source; the codec/planning/verification portion is unchanged. The only additions to publication behavior are internal
phase guards and refusal of every explicit versioned profile before a legacy journal intent is written.

The old pathname operations and origin-mark API are deliberately retained for compatibility. They are not evidence
of object-bound cleanup, typed provenance, durable archive recovery or per-directory capabilities. Explicit profiles
remain refused by production dispatch and by the extracted boundary itself. ZIP worker migration is a separate change.

## Regression evidence

Filter: `FullyQualifiedName~ArchivePublicationCompatibilityTests|FullyQualifiedName~ArchiveUpdateTests|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`.

| Environment | Result | Evidence |
|---|---|---|
| Windows host, SDK 10.0.101 | 52 passed, 8 skipped, 0 failed | `.tmp/test-results/archive-writing/legacy-publication-host-guard.trx` |
| Windows 11 x64 VM, runtime 10.0.14 | 52 passed, 8 skipped, 0 failed | `results/legacy-publication-vm1.trx` in the owned VM validation root; copied to the host evidence directory |
| macOS ARM64 / APFS, SDK 10.0.101 | 60 passed, 0 skipped, 0 failed | `assessment-results/legacy-publication-mac.trx` in the owned Mac validation root |
| Ubuntu | Postponed | Another tester's pre-existing FileCat process 65891 remains active |

The eight Windows skips are existing Unix-specific race/mark controls. The Mac replay executes them. These results
cover creation/update, mark/identity races, retained-backup behavior and edit sessions; they are neither GUI nor
installed-package qualification. The Windows VM uses host-built assemblies and the test-only medium-integrity
launcher. Its facts are parent PID 4112, child PID 2000, integrity 12288 before / 8192 after, exit 0. No FileCat,
testhost, VSTest or test-run processes remained on the two used guests after the runs. Mac keep-awake assertions remain.

SHA-256 of the Windows assemblies used in these runs:

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.dll | `B1AA54085A1C71FD47292B6D7F51B4D006C64E5980BEA42032D8B61902D44E78` |
| FileCat.Core.Tests.dll | `3D549550048047FD388BF0A36C51E08D486713DDFEEC0A782849A79A4A6A7A76` |

AW2 remains incomplete. Native object-bound publication, transaction records/sidecars and recovery, provenance,
worker-backed ZIP and the full crash/concurrency matrix still need implementation and qualification.
