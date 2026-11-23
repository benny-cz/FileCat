# Windows on Arm SMB case-distinct listing — 21 November 2026

This receipt closes N-ENUM's last open case. The native Windows on Arm SMB client must list names that differ only in case identically on the native and managed routes. With it, FileCat now uses the native tag-bearing listing (F4) on Windows on Arm, as it already does on x64.

## Why a different server

The Windows SMB server refuses to list a folder whose names differ only in case: it returns error 442, as the [ARM64 loopback fixture](2026-11-18-storage-capacity-refs-smb.md#windows-smb-loopback-fixture) recorded. The x64 case-distinct evidence used Samba on the Ubuntu VM. Hosted runners have no Samba and accept no inbound connections, so a hosted Windows on Arm runner cannot reach the VM either.

Windows 11 24H2 can map a share on an alternative TCP port (`New-SmbMapping -TcpPort`). [`Test-NativeSmbCaseListing.ps1`](../../../eng/validation/Test-NativeSmbCaseListing.ps1) uses this:

- it serves a case-sensitive NTFS folder from a throwaway user-mode SMB server (impacket 0.13.1 `smbserver.py`) on 127.0.0.1, port 4445;
- the server runs on the x64 Python build under emulation, because its `cryptography` dependency has no ARM64 wheel; the SMB client and the test process are native ARM64;
- the throwaway server does not sign, so the client's signing requirement is relaxed for the run and restored afterwards;
- `Native_listing_matches_managed_listing_on_a_prepared_mapped_and_unc_smb_folder` then runs at medium integrity in its case-distinct mode.

## Runs on the hosted `windows-11-arm` runner

| Run | Result |
|---|---|
| [44943868913](https://github.com/benny-cz/FileCat/actions/runs/44943868913) (probe) | The ARM64 Python could not install impacket: `cryptography` failed to build. |
| [44944053710](https://github.com/benny-cz/FileCat/actions/runs/44944053710) (probe) | With x64 Python, the mapped drive, a `net use` mapping and the UNC path each listed `Case.txt`, `case.txt` and `owned-folder` (SMB dialect 2.0.2). |
| [44944493485](https://github.com/benny-cz/FileCat/actions/runs/44944493485) | impacket 0.12.0 failed to start: it imports `pkg_resources`, which Python 3.12 does not ship. Pinned 0.13.1. |
| [44945257651](https://github.com/benny-cz/FileCat/actions/runs/44945257651) | The share listed identically on both routes, but a missing folder listed as empty on the native route instead of failing as on the managed route. |
| [44946011122](https://github.com/benny-cz/FileCat/actions/runs/44946011122) | Passed after the fix below. |

## The bug the fixture found

For a missing folder, `FindFirstFileExW` returned `ERROR_FILE_NOT_FOUND` (2) on this server. The Windows server and Samba return path-not-found instead. The native route read error 2 as "empty folder", which it can also mean for an empty share or FAT root. So a deleted folder on such a server listed as empty. Commit `44642d114` hands error 2 to the managed route before anything is published. The managed route opens the folder first and tells empty from missing. `A_no_match_answer_is_settled_by_the_managed_route_before_any_publication` covers both outcomes.

## Result

At commit `44642d114` on Windows 11 Enterprise build 26200, ARM64, medium integrity, the test executed and passed, 1/1 ([TRX](receipts/2026-11-21-windows-arm64-smb-case/native-listing-smb-case.trx), `c081280c…`; [fixture record](receipts/2026-11-21-windows-arm64-smb-case/smb-case-listing.json); [case-sensitivity record](receipts/2026-11-21-windows-arm64-smb-case/case-sensitivity.txt)):

- the mapped `Z:\` and the UNC `\\127.0.0.1\FILECATCASE` each listed 4 entries, and the real Win32 find cursor completed;
- the entries were `Case.txt`, `case.txt`, a Unicode name and an empty folder;
- names, kinds, attributes, flags, sizes and both times were equal on the native and managed routes;
- both routes listed the empty folder as empty and refused the missing folder with `DirectoryNotFoundException`.

The fixture record's `serverPython` field reports the OS architecture (`ARM64`), not the interpreter's. The runs used the x64 build; the script now records the interpreter's build tag instead.

## Enablement

With this case closed, `WindowsPlatform` uses the native listing whenever the process runs natively on x64 or on Windows on Arm. A process emulated on another architecture keeps the managed route. On Windows on Arm, listings now carry reparse and Cloud Files subtypes; the [Cloud Files exposure](2026-11-21-windows-cloud-exposure.md#windows-on-arm-x-arm64) child had already qualified the exposed native route there.
