# Held macOS directory extended-attribute inventory

On 20 November 2026, an Apple-silicon Mac running macOS 27.2.1 tested an
isolated FileCat checkout based on `1c61c234`. The fixture was on the local
APFS data volume. Apple documents descriptor-based
[`fgetxattr`](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fgetxattr.2.html)
and the [`O_EVTONLY` open flag](https://developer.apple.com/library/archive/documentation/Performance/Conceptual/FileSystem/Articles/TrackingChanges.html).
The installed macOS 26.5 SDK defines `O_DIRECTORY`, `O_NOFOLLOW`, `O_EVTONLY`
and `O_CLOEXEC`, and defines `XATTR_SHOWCOMPRESSION` as `0x0020`.

A native probe opened a FileCat-owned directory with those four flags. The
held descriptor returned 24 bytes of attribute names from both ordinary and
compression-visible `flistxattr` calls; `fgetxattr` returned the six-byte
length of the fixture attribute in both views. The same typed open against a
FIFO failed with `ENOTDIR` (20), before acquiring a FIFO descriptor. The
probe did not read attribute values. This provides a directory-specific route
that avoids the FIFO reader side effect seen with an untyped event-only open.

FileCat now uses the held directory descriptor for bounded names and lengths,
records the observed directory identity, and requests the compression-visible
view when selected. If that view is unsupported, it retries the ordinary view
on the same descriptor and says compression backing was not inventoried.
Ordinary files, symlinks and special nodes retain the partial no-follow path
view. At this inventory checkpoint, directory value, export and delete
actions remained unavailable. The saved-search predicate still
requests only its declared ordinary xattr view, including on directories.

The focused Mac Core run passed 11 tests, skipped four Linux or unprepared
fixtures, and failed none. It included the held directory, FIFO and symlink
cases plus the ordinary-view search contract. The focused App run passed
7 tests with no skips or failures. Both projects also built on Windows with
zero warnings and errors. Returned receipts are
`.tmp/mac-xattr-final-core.trx` (SHA-256
`944A5E7164686D36B2659E4B2F3D51A1532AE9C9BBB9612EA8EECA9D7CEE8CB9`)
and `.tmp/mac-xattr-final-app.trx` (SHA-256
`018E3A01C0D82EDFAD71C2794BBA392F536440B3F8C34DA952270DEAE2415770`).
The tested Core and App assemblies had SHA-256
`267D148372DAF3BB42D49CD073C9E89D26B08EC0A6959E343B2EDA2C053C703C`
and `B23C7E8E60D360F93FC14DD4BA3C8F0499A878E98AB5B254567F1F393951B506`.

This inventory checkpoint qualifies the tested local APFS directory only. The
subsequent value-action checkpoint below extends that scope; regular-file
descriptor binding, dataless File Provider callbacks, other filesystems and
Intel Macs remain open. F-EXTRA and N-MAC remain partial.

## Held-directory explicit values and export, 20 November 2026

A separate native probe on the same Apple silicon APFS data volume opened a
directory with `O_DIRECTORY | O_EVTONLY | O_NOFOLLOW | O_CLOEXEC` and read its
exact xattr value through `fgetxattr` in both ordinary and
`XATTR_SHOWCOMPRESSION` views. Both calls returned the eight-byte fixture
value. This established that the held descriptor supports explicit value
reads without opening the directory data fork.

FileCat now reacquires and checks a held directory for each explicit read. It
resolves the exact typed name on that descriptor, caps the value at 16 MiB,
observes its identity and change token around the read, and runs the entire
acquisition under the thread's no-materialization policy. The same captured
value supplies the selected-value report and F5 copy. Darwin directory xattr
deletion remains disabled; files, symlinks and special objects still have no
bound value action.

On macOS 27.2.1 arm64, the focused [Core TRX](receipts/2026-11-20-mac-held-directory-value/core.trx)
passed 1/1 with no skips or failures. It checked direct value read, report
provenance, export-source bytes and provenance, deletion refusal, symlink
refusal and reviewed-identity refusal after replacing the selected directory.
The focused [App TRX](receipts/2026-11-20-mac-held-directory-value/app.trx)
passed 1/1 with no skips or failures; its headless UI opened the directory
attribute, displayed the selected-value report, completed an F5 copy into a
file, verified the bytes and exposed no delete capability. The prior ordinary
file UI test also passed, with two Windows-only tests skipped. The Core and
App result SHA-256 digests are respectively
`0F8D9736464F20F1E4441E4CA2044D0C6C42C13F171EB90EA714F179F7C66DD1`
and `D19EF0F43E772725290A1F8F337AE91090B5FA354F3CF169A8D4AEC44A9AE133`;
the tested assemblies are
`A4C627A435BB7E068A5B7C5E384EE53C6C536907B38AE2F46C95D8BEEA5C7E0A`
and `13C1E2AB906E7638296C0F08895FE2A33ED770C031432C5C6B0EDF12043D90E7`.
Live dataless-directory callbacks, actual compressed backing values, HFS and
Intel Mac remain unqualified.

## Selection identity on explicit value reads, 20 November 2026

The supplementary listing now carries its observed native file or directory
identity into each selected value. On Unix, F3 content opens and F5 export
opens require that identity and compare it with the held source before reading.
An item reference without the captured identity fails closed and asks for a
refresh. The value report already used the reviewed identity. This closes the
case where the selected directory or file is replaced at its pathname between
listing and an explicit value read. It does not make a concurrent mutation of
the same object an atomic snapshot; the existing change-token checks still
apply.

On macOS 27.2.1 arm64, the focused [Core TRX](receipts/2026-11-20-supplementary-selection-identity/mac-core.trx)
passed 1/1. It now refuses both viewer and export opens after replacing the
selected directory, in addition to the report refusal. The focused [App TRX](receipts/2026-11-20-supplementary-selection-identity/mac-ui.trx)
passed 1/1 and completed the directory report and F5 copy through the actual
listing. The Core and App receipt SHA-256 hashes are
`EF2990ADF4498C6F45A9D24F39728B1F4772461D634D2C337BC3472D1F3D23AB`
and `2D695D6E06F976F12CD25D56A8836BB35EF1F5F3A5A886652D0E76F037FAAA87`;
the tested assembly hashes are
`8FD031F9466D3AB5DA15D2A345289143D9B6E8662A59A5D4C5F64D829E3C48EE`
and `3F9617BC2A8324054261D1DC0821A45A9A088E0A92394F58780A44F04CF25835`.
On Ubuntu 24.04.5 x64, the focused [Core TRX](receipts/2026-11-20-supplementary-selection-identity/linux-core.trx)
passed 1/1. It refused report, viewer and export opens after an inode
replacement. The focused [App TRX](receipts/2026-11-20-supplementary-selection-identity/linux-ui.trx)
passed 1/1 and completed the listed-value report, F5 copy and deletion. The
Core and App receipt SHA-256 hashes are
`9BE318D84CEB5674367D9ACCCE919284FFCF499B1492A2D4694A8874EFAC59F1`
and `2BC5739B032181E4DD1A1A0850489AA47A75C240D05A2F70223CD17DE1B38735`;
the tested assembly hashes are
`7277D4E85B20BAAE25BE80AB6F2254C9740EE90B8FE9003E11A0776BE35C5787`
and `8DFC304911068DB13DBA95771A7130436C069CD234FDE2ADAF72A5EEF3063FEB`.
The isolated source copy was removed after the run, restoring the VM's prior
free space.
