# Bounded C# RAR5 codec and creation foundation

**7 November 2026.** The independent compression route now has an original stream-only C# implementation,
not just the earlier Python feasibility experiment. This checkpoint does **not** enable a production RAR profile.
Password/data/header encryption, strict source admission/update/verification, worker protocol integration and complete
workflows remain required under the controlling plan. 7z implementation and final independent review remain open.

## Implemented behavior

- `Rar5Encoder` writes algorithm-version-0 RAR5 blocks with complete fixed canonical Huffman tables and a bounded
  greedy hash-chain matcher. Store plus methods 1–5 work. Compressed presets search 1/2/4/8/16 candidates; a targeted
  collision fixture proves different search work **and smaller actual compressed payload**, not merely method relabeling.
- Independently reset 32 KiB blocks use a 128 KiB advertised dictionary. The input buffer, hash heads, position chain
  and packed block buffer have constant bounds. Maximum block allocation is `ceil((1800 + 9*32768)/8) + 4` bytes;
  comparisons are checked against `4*depth*blockLength`. Cancellation is checked through reads, tokens and long matches.
  Empty inputs avoid allocating a full codec workspace. These are design bounds, not completed whole-worker resource qualification.
- `Rar5ArchiveWriter` accepts only numeric input grants and an empty seekable stage. It freezes member/descriptor
  collections, preflights names, exact times, modes, namespace collisions, aggregate retained metadata and logical sizes.
  It has no path, publication, journal, recovery or deletion authority.
- Each file gets an exact counting/SHA-256/IEEE-CRC32 pass before its header and a fresh second lease for encoding.
  Both results must match each other and the captured digest, including final EOF. Truncation, extension and changed
  content refuse; partial stages remain unpublished. This avoids whole-file buffers and padded variable integers.
- Creation writes Unicode names, regular files, explicit directories, empty files and nested members. Ordinary Unix
  modes and one authoritative high-precision mtime record are serialized. Unix seconds through `uint.MaxValue`
  retain arbitrary nanoseconds; other representable times use FILETIME exactly. Pre-1601 or out-of-Unix-range
  non-100-ns times refuse explicitly. Source creation/access/other metadata preservation is not implemented here.
- New CRC code is original FileCat code. Compression follows the independently authored prototype and existing
  permitted format knowledge recorded in [the feasibility ledger](RAR5_FEASIBILITY.md). No new third-party codec,
  restricted source, native runtime dependency or reader-only RAR1.5 dependency was added to the writing assembly.

Compression is deliberately forced for each nonempty compressed-method fixture, including noise. Per-file automatic
store selection and product defaults still need integration/qualification; these tests must not hide an encoder gap
behind an incompressible-data fallback. RAR7, larger/fractional dictionaries, filters, legacy encoders, solid/multipart,
service/recovery preservation and required password modes are not proved by this implementation.

## Live batch results

All execution was on the disposable Windows VM, Mac or Ubuntu VM. The host only edited/compiled code and inspected
source/artifacts. One complete source snapshot was prepared for the consolidated builds/regressions/native checks.
No host FileCat or test process was run.

| Machine | Passing tests | Explicit platform skips | New RAR cases | Independent native record readbacks |
|---|---:|---:|---:|---:|
| Mac | 442 | 2 | 23 passed | 84 exact |
| Ubuntu | 441 | 3 | 23 passed | 84 exact |
| Windows, actual loaded standard account | 323 | 121 Unix-only | 23 passed | 84 exact |

Each new suite includes all six methods, 14-member archives, Unicode/nested names, directories, binary/text/noise,
tiny/empty/periodic data, Unix nanoseconds and pre-epoch/post-2106 FILETIME. One case additionally executes **90**
length/preset combinations around literal and 32 KiB block boundaries. Negative cases cover invalid sizes/names,
file/folder collisions, unrepresentable times, changed two-pass content, disposal and cancellation before IO.
Existing RAR-reader, archive-boundary, capture/directory, transform, creation-origin and Unix-storage regressions also run.
This is not the complete fault, scale, fuzz, UI or resource matrix.

Native readers hash every regular payload and independently check names, kind, length, ordinary mode and exact
seconds/nanoseconds. Directories have zero payload; the API must not be asked to decompress them. A single appended
directory slash is an allowed native presentation difference; file names are exact. Mac and Ubuntu use private pinned
libarchive 3.8.11. Windows uses its actual `archiveint.dll` libarchive 3.8.9 through UTF-8 C APIs, without extraction
or changing global locale. System DLL SHA-256: `7316291C7DDA7C48EAF07BC5C5B0F87C9E483A9D9C7354A39E0DD06EF7B4172C`.
It is a test-machine OS component, not a FileCat dependency or redistributed codec.

All six produced archive SHA-256 values match across all three platforms:

| Method | SHA-256 |
|---|---|
| 0 | `68B627579434072F2717097934F151529C738D3C18E47B5880F9E92740B50ED9` |
| 1 | `DF63ED69622B1EE9D211E2101D881399F7C4F7D655411E013AA3F85E7739AED7` |
| 2 | `D4113F9E82711E30987E110B7D5A94E6653AE7AFF50EEE41FD1FA9C0163DC73C` |
| 3 | `A2642AE00EF6415D03DB97EFD19575CBD6DBF5816E9B6E2E05F1C3B59B627625` |
| 4 | `9D72C63AFD7AC00710D71D93C9C8B3260817A5FB2073ADDCCD823593B5157CBC` |
| 5 | `E31876D40C8BEEB9FDFCF22C88DD7FB8E1A6588CE26DA9E8FBBB8987CB01C4A5` |

## Evidence and harness corrections

Complete source packet: **3,970 files**, **30,328,014 bytes** compressed, SHA-256
`4AF5E376ACA382AE67A8B69824882C16DF86638DD2BF4E88DF462015BCB8EA76`.
The earlier six-file delta failed because old test checkouts lacked the dedicated writing project. Fresh complete
source roots remove that stale-checkout dependency. The packet includes the actual production/test source bytes;
later harness corrections are captured separately and are not backdated into the compiler source evidence.

Compiler-source audits match **1,280 document instances** per platform: **12,215,882 bytes** each on Mac/Ubuntu,
**12,230,546 bytes** on Windows. Counts include linked sources in multiple assemblies and generated test content,
not 1,280 unique source files. An explicit optional NuGet cache root admits the compiler's xUnit runner content;
every document still requires the exact PDB checksum and is snapshotted. Default production audits retain their
checkout-only policy. Windows requires the exact cache path spelling recorded in its PDB. This proves actual compiled
bytes, not upstream package provenance by itself; dependency locks remain with the source packet.

The following harness/environment failures were retained and corrected without repeating passing codec tests:
directory payload API misuse; missing Ubuntu `make` (installed as a test build tool); xUnit content outside the default
audit root; Windows `tar` narrow-locale Unicode presentation; the standard helper initially placed outside its granted
tree; Windows cache path spelling; and PowerShell's default ANSI serialization. The corrected native witness and
file inventory use explicit UTF-8. The original ANSI bundle is retained as a failed evidence version, not counted as
a fully verified final bundle. No runtime-discovered C# encoder change was needed after the complete source packet.

Final bundle identities and full manifest checks are recorded in `rar5-csharp-checkpoint.json` beside this report.
Each final bundle includes actual assemblies/PDBs, source snapshots, result logs, generated archives, native witnesses,
executed helper sources, failed attempts and a final process census. All owned FileCat/test workers exited. The Mac's
existing until-reboot wake helper is retained. Resource observations here do not close the still-open aggregate Mac
host-memory envelope or six-RID/package/fuzz gates.

## Next implementation chunk

Continue the needed 7z container/codecs/filters and required RAR/7z password primitives/source closure before finalizing
shared interfaces/UI. Then finish strict raw catalogs/preservation manifests, secret grants and fresh worker verification,
creation/update/publication, common capture/snapshot/global-resource ownership and full workflows, including safe move-into.
No profile is enabled by this checkpoint. The goal and final independent UX/UI/function/security review remain open.
