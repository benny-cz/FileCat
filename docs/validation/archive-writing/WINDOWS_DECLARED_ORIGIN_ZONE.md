# Declared Windows origin-zone checkpoint

2026-11-05, over `f395542e`. This implements a closed bounded interpretation and byte-preserving increase of captured
Windows zone declarations. It does not implement effective Windows trust/policy, strongest-input aggregation, complete
input provenance sidecars, native Unix mark handling or production dispatch. No new write profile is enabled.

## Contract and native correction

`ArchiveWindowsOriginZone` accepts positively captured absence or a present mark with exactly one `ZoneTransfer`
section and one unambiguous decimal `ZoneId` digit zero through four. Keys are bounded ASCII identifiers; whitespace is
explicit ASCII space/tab. Comments, blank lines, CR/LF/CRLF, case-insensitive known names and opaque fields such as URLs
are retained. Unknown/duplicate sections, duplicate zone keys, malformed/unknown values, control characters and lossy
encoding are controlled refusals. Empty presence, unreadable and foreign-format evidence never become unmarked.

The declared values are the standard
[Windows URLZONE values](https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/ms537175%28v%3Dvs.85%29).
This does not evaluate the file's effective zone, global policy or URLs. It performs no IO, URL mapping, account lookup
or settings change. A declaration is not an authenticity/trust verdict, and custom/enhanced zones remain unsupported.

Initial general text round trips admitted four encoding candidates. The independent Windows 11 system
`PersistentZoneIdentifier` rejected UTF-8 with a BOM and UTF-16 big-endian during `IPersistFile.Load`, returning
`E_ACCESSDENIED` (`0x80070005`). The production interpretation now refuses those forms before transforming them. Only
strict UTF-8 without a BOM and strict UTF-16 little-endian with a BOM are admitted. UTF-32, malformed/truncated Unicode
and decoding replacement are refused. This was found by the native oracle, not by tests that repeated the same decoder.

`RaiseTo` never lowers a declaration. It returns the original immutable snapshot if no increase is needed; otherwise
it changes exactly the one existing ASCII digit byte in its supported encoding. Every other byte, including URL and
opaque evidence, BOM, whitespace, ordering and line endings, remains unchanged. `ZoneOnly` emits a culture-independent
ASCII declaration with no invented URL. Any multi-input caller must separately retain the captured source evidence
and prove it used every required origin; this parser alone does not satisfy aggregation or publication admission.

The native test-only oracle uses the installed Windows class and SDK `IZoneIdentifier`/`IPersistFile` contracts. It
creates a fresh COM object for each read, confirms declared 3 before and 4 after transformation in both supported
encodings, and pins the observed refusals for the two rejected candidates. It calls no Save/SetId/Remove and changes no
system policy. This introduces no production COM/parser dependency. One further native case captures a real source
mark, raises it, copies it to the sealed owned stage, verifies retained URL bytes and payload hash, and leaves the
source's declaration unchanged.

## Validation

Thirty-nine new portable cases cover supported encodings with all line endings and multibyte prefixes, exact-byte
updates, no decreases, absence versus present zero, unavailable/foreign evidence, all five zone-only values, ambiguous
sections/keys/numbers, invalid encodings, maximum 64-KiB input and non-Latin numeric culture. The Core filter also
includes the three existing typed-snapshot cases (42 total). Five new Windows cases cover the independent encoding
oracle and held-stage integration.

The native filter is the origin/storage/replacement filter (111 cases), not the separate ten-case metadata class
already qualified in [the metadata checkpoint](WINDOWS_ARCHIVE_METADATA.md).

| Environment | Core parser/snapshot filter | Native/platform filter | Evidence |
|---|---|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14, medium integrity | 42 passed, 0 skipped/failed | 109 passed, 2 skipped, 0 failed | `archive-zone-core-vm2.trx`, `archive-zone-native-vm2.trx` |
| macOS ARM64 / APFS, source built, SDK 10.0.101 | 42 passed, 0 skipped/failed | 24 passed, 87 Windows-only skips, 0 failed | `archive-zone-core-mac.trx`, `archive-zone-native-mac.trx` |
| Ubuntu x64 / ext4, source built, SDK 10.0.101 | 42 passed, 0 skipped/failed | 24 passed, 87 Windows-only skips, 0 failed | `archive-zone-core-ubuntu.trx`, `archive-zone-native-ubuntu.trx` |

All 39 new Core cases execute on all three platforms; all five native cases execute on Windows. The two Windows skips
remain the separately tested elevated-refusal case and an existing physical FAT case. Windows executes host-built
assemblies, not a guest source build or ordinary filtered-user/installed-package qualification. Core launcher parent/
child PIDs 6628/6168; native 3628/6556; both runtime 10.0.14, integrity 12288 before / 8192 after, exit 0. Builds finish
with zero warnings/errors. No unrelated full Core codec suite is repeated for this additive parser.

Exact three-file source delta applied over the metadata-updated owned Unix trees:
`B056B4603937F90E0178DD5ABBFB61EA6B331257A7417E220ECE735165493A9C`.
Final Windows binary/symbol transfer archive:
`FAEFD51A6C58505DBB057AE14200938CADDAA0E7F161196CD4EED055F277E7AB`.

| Artifact | SHA-256 |
|---|---|
| FileCat.Core.Tests.dll | `912FA5FA533CF9E6582D97F3A3798EEF8A84E38D3C7AC74A995759B23387982D` |
| FileCat.Core.dll | `7796452B5E62C028E99ECC2FD95E1C67C5E2A989ED8141502C96FDB551A7F2FB` |
| FileCat.Archives.dll | `F5C5B809FFD111575C22DDABA28D874CBC1989E39EDF476C128E5DBAEC591830` |
| FileCat.Platform.Windows.Tests.dll | `5F145604CB99C6B8D1B89B316EEC73D9CF039340FFFEC72E4053D7074721BA14` |
| FileCat.Platform.Windows.dll | `7936D48DF260C56C5871F51A26EEAD8B8D6D29F5BB40D34692B796D2CBA1AC0E` |

TRX/facts, including the initial oracle disagreements, remain in ignored evidence. All guest FileCat/test processes
exited; Mac keep-awake assertions remain active. Shared `E:\FileCat` stays tracked-clean on `main`. No dependency or
third-party implementation source was added. Full implementation/qualification, independent final review and the
conditional RAR follow-up remain pending.
