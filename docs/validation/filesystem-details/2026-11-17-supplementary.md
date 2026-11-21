# Supplementary inventory implementation checkpoint

Implementation remains in progress on `main`, following the [foundation receipt](2026-11-17-foundation.md). This checkpoint implements the bounded supplementary-data route and part of its qualification. It does not close F5, F6, NTFS compression, or EFS.

## Windows EFS output follow-up, 18 November 2026

The supplementary provider reads output through a separate admission path on Windows. It records the held file identity, revision and protection from the actual selected stream or the held file used by the filtered EA query. Each stream read checks the same handle around acquisition. The EA query checks the held file before and after the native buffer arrives. Observed EFS or unknown protection refuses derived output. The existing explicit viewer can still read the selected value when the invoking account has access.

All 12 focused Windows native cases passed at medium integrity. The tests exercised actual clear and encrypted ADS and EA values and an EFS change after opening a previously clear stream for output. The next read refused before returning bytes. All 11 focused application cases passed on the same VM. FileCat's F5 action completed an ordinary export and refused an EFS-protected stream before creating a destination file or stage. The saved source stream stayed encrypted with its original bytes. Details report ownership and ordinary inventory regressions also passed. See `.tmp/filecat-hidden-efs-output-tests.xml` and `.tmp/filecat-hidden-efs-output-app-tests.xml`.

This verifies the tested supplementary F5 portion of E-OUT12. It leaves the report's explicit payload route, additional sinks and wider native qualification open.

## Implemented behavior

The existing supplementary tab now inventories names, typed categories and lengths without previews. It retains per-category Complete/Partial/Not inspected/Denied/Unsupported/Changed/Failed state, observation time, scope and the exact bound/failure reason. Limits are 1,024 retained entries, 1 MiB retained name metadata, bounded native acquisitions/retries, and cancellation between calls. Windows inventories named ADS by default; its separate header action explicitly admits the native EA buffer containing names and values. Values are discarded and the buffer cleared. EA enumeration requires a terminal exhaustion result rather than treating one successful chunk as complete.

The header and EA action remain accessible with no rows. Header, status, empty text and the renamed **Extra data** column use the typed inventory. The column retains ID `hidden`, uses exact numeric sort keys only for complete declared categories, refreshes after 20 seconds, and is invalidated on filesystem listing generations. Ordinary summary acquisition still performs no inventory. Explicit inventory and native column production share the summary reader's process/device admission and owned scheduler lane. Native metadata analysis is unavailable outside ordinary filesystem panels until those original-subject adapters are qualified.

Supplementary references use the fixed category ordinals 1 Stream, 2 Attribute, 3 NtfsAttribute and 4 ResourceFork. Refresh and the loading/restart path retain full provider references, including marks and focus, through split/reversed arrival. A disappeared focused category returns focus to the parent rather than the same-name surviving category. Missing/legacy category IDs refuse; no name-only fallback remains. Opening an ADS does not issue a combined EA inventory.

Windows actions resolve an exact native name on a held no-follow file and open ADS relative to that object; opened identities are compared. EAs use a held descriptor and filtered native-name queries. Native EA bytes and flags are retained. Required (`FILE_NEED_EA`), recognized security/OS and storage-backing metadata refuse generic deletion after fresh native resolution. Linux pins with `O_PATH`, verifies an ordinary inode, reopens that held inode through procfs, compares identity, and uses descriptor xattr APIs. Symlinks/FIFOs/devices are refused before a data descriptor is opened. These value actions remain subject to the separately unfinished protection/output work; this receipt does not qualify EFS-derived exports.

macOS currently retains an explicitly ordinary no-follow path view, reports compression backing **not inventoried**, and refuses value/export/delete routes without a qualified bound adapter. It does not substitute `O_EVTONLY` for a safe universal inode pin. Compression-visible inventory and a bound macOS action adapter remain open. decmpfs decoding now describes header claims, preserves unknown/dataless context as uninterpreted, and checks unsigned size bounds.

Find now uses the same bounded inventory. Windows scope is explicitly ADS only, excluding EAs; Unix scope names the visible API view. Download-mark exclusions use platform, category and native-name rules. A known ordinary item can match; an incomplete negative is counted as unexamined and leaves coverage incomplete, with bounded diagnostics. Saved broad or different-platform predicates require explicit replacement or clearing before execution.

## Native finding: transparent WOF backing

On the disposable Windows build 26340 guest, `compact /C /EXE:LZX` produced transparent compression while ordinary attributes and the reparse query did not reveal a reparse point. A synthetic `WofCompressedData` stream could not be created because Windows reserves it. The fixture now uses actual compact compression and queries **FSCTL_GET_EXTERNAL_BACKING** on the held metadata handle. Positive/unknown external backing refuses generic supplementary actions. The successful fixture observed provider 2 and retained the original payload after refusal. Microsoft documents the held query and its explicit non-external/unsupported outcomes in [FSCTL_GET_EXTERNAL_BACKING](https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/fsctl-get-external-backing).

The native Windows x64 full metadata report now identifies the same transparent file-provider backing as LZX. Its NTFS compression attribute remained clear, the held file ID matched the summary and the report omitted payload bytes and ordinary main-stream placement. The test read the original 256 KiB back exactly after inspection and the refused deletion. The ordinary unbacked control omitted the Windows Overlay Filter section. All 32 focused metadata, extent, range, hard-link and supplementary cases executed and passed at medium integrity. The full suite `wof-details-r1.trx` has SHA-256 `F97F76CA46D3BEC9734862D8D571284DAC40AF2DE132066A09840EF93C4F4E19` and its test assembly has SHA-256 `08813F05CC0C06DCA5D2A3DA15DF56FFE9F25BBFA5B72F474CACE8E7804888F7`. [Microsoft file backing metadata](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_provider_external_info_v1)

The transparent LZX case also executed and passed on the native Windows ARM64 runner. All 19 cases in `native-file-details.trx` passed. The receipt SHA-256 is `44CA558D1CA7BD588B327E3F15A3DD4E68A2929F06A7CD3AD82F491F0E679111`. [Native ARM64 details receipts](https://github.com/benny-cz/FileCat/actions/runs/44644665012)

## Verification

Pinned SDK builds of Core, Windows tests and App tests passed with zero warnings/errors. Native tests ran in the user-authorized disposable environments:

- Ubuntu 24.04 x64: **49 Core tests passed**, including the real xattr read/export/delete route, FIFO/device/symlink refusals, category refresh/restart, scoped Find and metadata ownership/refresh. **12 App tests passed**, including real supplementary export/delete, empty-inventory EA review, chooser labels and details/report ownership.
- Apple silicon macOS/Darwin 27.2.0: **47 Core tests passed**, with the Linux-only FIFO summary test skipped. The corresponding Core and App builds passed. **12 App tests passed**, including the explicitly unavailable unbound action route.
- Windows x64 build 26340: **11 selected native/parser tests passed**, with one separately opt-in scale test skipped. Includes same-name ADS/EA values, exact EA disappearance, directory ADS, a named `$DATA` stream, required-EA native flags and fresh deletion guard, native terminal EA exhaustion, and actual WOF backing refusal. **13 App tests passed**, including native supplementary export/delete, empty-tab EA admission, Find's captured-source forwarding, chooser labels and details/report ownership. This run caught and verified the repair of a command-availability mismatch: availability now uses the same registered supplementary provider as execution.
- Host deterministic Core subset: **39 tests passed**, zero failed/skipped. Earlier build-failure or stale-assembly invocations are not acceptance evidence.

Source SHA-256 at the native checkpoint:

| Source | SHA-256 |
|---|---|
| `HiddenInventory.cs` | `38DB60C352CD31CAD62C148D40492F17B86CDD25FC7F458A5AF11C1693F90CE4` |
| `WindowsHiddenData.Inventory.cs` | `73F81C9A6898E9FEB5C8FD4E066DDF5F948F6B6FD98666F2DA05D6C0CF89BA93` |
| `WindowsStorageBacking.cs` | `B38F9281DA547620F37ADDDBFAE1A7DEB45AF08809A405BB136713F893F2FD9C` |
| `UnixHiddenData.Inventory.cs` | `2AEA8C39278A4A1BF3CAB2058756003C7E46570C4CA11F913ACCA00D864CDCB0` |

Logs and transfer/build scripts are task scratch under `.tmp`; they are not product dependencies. Tests do not disable protection on the workstation. Windows Update/Defender setup was confined to the disposable guest as recorded in the foundation receipt.

## Unix inventory growth, 18 November 2026

When a Unix attribute-name list outgrows its native buffer, FileCat makes at most two retries. If the third fill also returns `ERANGE`, the inventory reports Partial and explains that the names kept growing. It has no complete names for that attempt and issues no attribute-length queries. The fixture injected `ERANGE` for all three fills and verified exactly three size probes and three fills.

Ubuntu x64 passed 22 focused tests with two Darwin-only skips. Apple silicon passed 21 with two Linux-only skips and one opt-in compressed-file skip. Both ran the growth fixture. Ubuntu exercised bound xattr operations and special-file refusal. The Mac exercised the native resource-fork inventory. Results are `.tmp/filecat-unix-xattr-growth-90208329-ubuntu.trx` and `.tmp/filecat-unix-xattr-growth-90208329-mac.trx`. The tested `UnixHiddenData.Inventory.cs` SHA-256 is `DEF377988BA0DD7EDD4B3486BA2F0CDC6C3770D12802F0979E895581C6EB3502`.

## Delete confirmation retains the inventory identity

Supplementary deletion captures the listed file identity before confirmation. The Linux adapter verifies it on the descriptor used to remove the attribute. The Windows paths verify it on the held file before resolving the exact ADS or EA for deletion. Removing the extra inventory lookup also avoids reopening the pathname between current category resolution and mutation.

Ubuntu x64 exercised a real xattr on a replacement inode with the same name. The existing confirmation failed even after the tab had refreshed to the new inode, and both files kept their attributes. A newly reviewed removal succeeded in the Core fixture. The regular app confirmation still removed the selected attribute and refreshed the parent column after returning. The focused batch had 64 Core and 11 App tests pass, with three platform-specific skips. Results are `core.trx` and `app.trx` under `~/filecat-supplementary-delete-8eeeca7e-r3`.

Windows 11 x64 passed 12 native supplementary tests. Both replacement fixtures put an ADS and an EA of the same name on both files. The old identity refused deletion and a fresh identity permitted deleting just the selected category. The other category, the main bytes and both values on the original file were preserved. The suite also exercised native EFS-derived output refusal. See `.tmp/filecat-supplementary-delete-windows-r3.xml`.

## Linux inventory without opening the data fork

On Linux x64 kernels with the xattrat syscalls, FileCat inventories names and lengths through the numeric entry for its live `O_PATH` pin. The lookup is relative to an open, verified procfs descriptor directory. `listxattrat` requests names and `getxattrat` uses a zero-length request. On Ubuntu kernel `7.0.0-43-generic`, `AT_EMPTY_PATH` on the `O_PATH` pin returned `EBADF`. The relative procfs route listed names even on a file whose mode denied a data open. The denied length stayed unknown and the inventory was partial. [Linux xattr syscall implementation](https://github.com/torvalds/linux/blob/master/fs/xattr.c) · [Linux x64 syscall table](https://github.com/torvalds/linux/blob/master/arch/x86/entry/syscalls/syscall_64.tbl)

A separate syscall capability check distinguishes kernel `ENOSYS` from a failed item lookup. Older kernels and other process architectures retain the descriptor-based inventory. That path, explicit value actions, and the bounded FIEMAP collector share the procfs directory check. The legacy descriptor remains subject to data-open permissions. [Linux procfs descriptor semantics](https://man7.org/linux/man-pages/man5/proc_pid_fd.5.html)

The Ubuntu x64 suite passed 57 modern-route Core tests, including the existing ext4 and XFS extent checks. A filter confined to a separate test process simulated kernel `ENOSYS`. Its 20 passing tests exercised the legacy fallback and the unchanged permission refusal on a mode-zero file. A second simulation returned `ENOSYS` only for the actual item lookup and verified that FileCat surfaced the error without reopening. Nine App tests also passed. There were 13 platform or opt-in skips across these runs. The results are `modern.trx`, `legacy.trx`, `refused.trx`, and `app.trx` in `~/FileCatValidation/filecat-linux-inventory-28dcc768-r2`.

## Remaining gates

F5 remains partial: qualify broader denial/change/native-budget and action/output cases, macOS bound/compression-visible inventory, and restore independently bounded hard-link/range/FIEMAP/retrieval-pointer collectors. Metadata-only reports continue to omit those collectors before acquisition. The [optional-column checkpoint](2026-11-17-columns.md) records subsequent F6 ownership/freshness work; native search-mask validation and wider qualification remain unfinished. Do not interpret these counts as complete plan acceptance.

EFS remains required by the user's instruction. The documented FSCTL table is not a supported shortcut around the rejected path protocol: Microsoft's [product-behavior appendix, notes 64 and 70–71](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/d4bc551b-7aaf-4b4f-ba0e-3a75e7c528f0) identifies SET_ENCRYPTION as a private EFS request whose necessary private data is unavailable to ordinary callers. A replacement binding/conflict protocol and the key/output safeguards are still required. No EFS mutation/key qualification is claimed by this checkpoint.

## Combined Windows inventory bound, 21 November 2026

When an explicit EA read follows the stream inventory, FileCat now retains the EA prefix that fits the shared 1,024-entry/1 MiB metadata limit. It reports the EA category as Partial when further returned EAs do not fit, while preserving a prior native Failed/Denied/Changed state and reason. The value-bearing-query flag remains set when the EA API was called. Three focused parser/merge tests passed on Windows x64; the new case checks retained rows, partial state and an existing native failure reason. This is deterministic merge evidence, not a native large-inventory fixture.
