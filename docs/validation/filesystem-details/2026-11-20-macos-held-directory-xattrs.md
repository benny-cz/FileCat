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
view. Directory value, export and delete actions remain unavailable until
their own bound action protocol is qualified. The saved-search predicate still
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

This qualifies the tested local APFS directory inventory only. It does not
qualify a regular-file descriptor, compression-visible file attributes,
value actions, dataless File Provider callbacks, other filesystems, or Intel
Macs. F-EXTRA and N-MAC remain partial.
