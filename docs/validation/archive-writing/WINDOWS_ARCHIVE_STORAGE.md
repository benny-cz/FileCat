# Windows archive storage checkpoint

2026-11-05. This is an unregistered native foundation over `5e8f30bd`; no new write profile is enabled. It implements
private stages/manifest files, held regular-file inputs, bounded streams and exact-object namespace primitives.
It does not implement the journaled publication/reconciliation protocol, outer metadata/provenance preservation,
directory-entry durability, production service dispatch or archive recovery actions. Other platforms do not acquire
Windows storage authority. Their source-built checks below exercise portable filename validation and refusal.

## Native contract

`WindowsArchiveDirectory` refuses elevated/unknown Windows authority and non-Windows entry. It retains the existing
no-link directory-chain lease, including all ancestors. All child opens/creates name one validated component relative
to the held final directory. Current strict admission refuses every reparse type, including cloud placeholders; it
does not silently hydrate or traverse them. Alias resolution above a chosen source root is still future work.

Volume observations come from the held directory: `NtQueryVolumeInformationFile(FileFsDeviceInformation)` checks
underlying local disk media and refuses the remote-device flag; `GetVolumeInformationByHandleW` requires writable NTFS
with persistent ACLs. These observations are not release capabilities, proof of filesystem crash durability or a ReFS
qualification. The media type is `FILE_DEVICE_DISK` (7), as specified by
[Microsoft's filesystem algorithm](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fsa/538c23e1-397b-41ca-b8e3-61874fbdc5fe).

Random stage/manifest names use `FILE_CREATE`, never open-or-overwrite. A protected DACL granting only the caller and
SYSTEM full control is supplied in the creation call, then independently read through the new handle and checked.
No inherited/package/Everyone grant is accepted. The optional security-descriptor argument to existing `NtFile` opens
defaults to null, preserving existing callers. Applying creation security in that call follows the
[NtCreateFile contract](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile).
Post-create admission failures close the handle, retain the artifact and report its exact evidence path through a
specific exception; that path is not cleanup authority. Disposing a normal lease also never deletes evidence.

Sources and reviewed targets open the actual non-reparse disk file before checking type, size and stable volume/file
identity. Incompatible existing/future data writers and external delete/rename handles are refused by Windows sharing
checks. This is not a guarantee against a preexisting writable memory mapping or an equivalent-privilege attacker.
Provenance ADS changes require separate rereads. Ordinary inputs have no namespace authority; reviewed targets do not
bypass read-only attributes. Known hard-link counts are exposed; moving/deleting the held name leaves other links
attached to the old object. Cross-input alias/self-inclusion checks and the eventual UI warning are not implemented here.

`WindowsArchiveFile` supplies a bounded stream over the held object without exposing its native handle to codecs.
Position, length and write bounds are checked before filesystem IO. Sealing permanently closes write access at the
stream boundary, flushes the actual file and computes a complete SHA-256 version; failed/canceled sealing cannot resume
writes. Manifest storage is capped at 128 MiB and stage storage cannot exceed the physical policy. A directory permits
at most sixteen outstanding child leases; job-wide accounting remains a separate integration requirement. Disposing
the directory owner keeps its pinned handles alive until all children close.

Rename always uses the held source and destination directory, with no replacement. Delete uses only the reviewed handle
and strict POSIX disposition; there is no pathname reopen, read-only override or weaker fallback. Both require the
expected identity, full length/SHA-256 reread and (for artifacts) still-private permissions. The deleting handle closes
before success is returned: Windows removes that name when the POSIX delete handle closes while existing readers can
retain the old bytes, per the
[disposition contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex).
Their caller must first persist a durable journal intent. These internal primitives alone do not authorize production
publication or recovery, and file flush success does not claim that directory entries survive power loss.

## Validation

The new suite contains 37 cases: private ACLs at creation/recovery; exact identities and full hashes; byte/seek/length
bounds; cancellation; source writer exclusion; directory/file redirects; readonly and live ACL races; hard links; late
file/directory/link occupants; unchanged unrelated occupants during cleanup; deletion with an independent reader;
directory/child lifetime; bounded lease counts; invalid/device/ADS/Unicode names; and a real manifest sidecar written,
sealed, reopened, fully verified and deleted through its exact native object.

Two implementation bugs were found and fixed before the final runs: the volume check used driver type 8 instead of
media type 7, and cleanup initially left its deleting handle open after setting disposition. Both failed real VM tests;
the final suite includes the specific behavior that exposed each failure. Raw failed runs remain in ignored evidence.

Affected regression filter includes `WindowsArchiveStorageTests`, `BoundReplacementTests`, `BoundReplacementJobTests`,
`WindowsBackingIdentityReplacementTests`, `WindowsBackingStagingReplacementTests`, `OpenTargetReplaceTests` and
`WindowsFileOperationsTests` (95 cases).

| Environment | Result | Evidence |
|---|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14, medium integrity | 93 passed, 2 skipped, 0 failed | `.tmp/test-results/archive-writing/archive-storage-vm4.trx` |
| Same Windows VM, elevated refusal lane | 1 passed, 0 skipped/failed | `archive-storage-high.trx` |
| macOS ARM64 / APFS, source built with SDK 10.0.101 | 24 passed, 71 Windows-only skips, 0 failed | `archive-storage-mac.trx` |
| Ubuntu x64 / ext4, source built with SDK 10.0.101 | 24 passed, 71 Windows-only skips, 0 failed | `archive-storage-ubuntu.trx` |

The Windows medium skips are the elevated-refusal case (separately run above) and an existing physical FAT-volume case.
All 36 medium-lane new cases execute on Windows. The medium launcher retains the administrative account's group/privilege
properties: parent PID 6980, child 4396, runtime 10.0.14, integrity 12288 before / 8192 after, exit 0. This is not an
ordinary filtered-user or installed-package qualification. Windows executes host-built binaries; it is not a guest
source build. The host had other test activity, so no host test run was started. Build: zero warnings/errors.

Exact five-file source delta SHA-256, applied over the previously sidecar-updated owned Unix trees:
`9B520DD87C86AA6C3A8C083C540A8D3316112B2B332B10E6B1F6C0B7FE378CFD`.
Final Windows binary/symbol transfer archive: `8E2A4965413F9941E5809D3AB4CD4B7EA5BCB349B285C6B35523B36713F1388D`.

| Artifact | SHA-256 |
|---|---|
| FileCat.Platform.Windows.dll | `376270F3E08AB9A034082876B9F37DC6C458D59684856DC03654475DC30118DE` |
| FileCat.Platform.Windows.Tests.dll | `643AC4D2C91338240E23716273A3B282C083A1B5611006D79DF66A052D11314F` |
| FileCat.Core.dll | `A47AEFC8CEF5B66B0B2287D1C732CA4216C08D8C56CE0AEB3FBEB21A8D1BAA16` |
| FileCat.Archives.dll | `A14CCA4A135A475651DAC29C5DAE08A73331059FA46D10A942B1D936E946798D` |

Every used guest had no FileCat/test processes after testing; Mac until-reboot `caffeinate` assertions remain active.
Shared `E:\FileCat` remains on `main`, with other implementors' files untouched. No dependency/third-party source was
added. The independent final review gate and conditional RAR follow-up remain pending after the full goal.
