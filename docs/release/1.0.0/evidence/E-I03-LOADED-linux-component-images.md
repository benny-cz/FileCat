# E-I03-LOADED — actual mapped Linux component libraries

2026-10-06. Preliminary I03/V20 observation using the exact original
6215329ed67da6c8463a996eb51b12ac569194aa Linux payload. Current dialog correction
1669cb6 is separate; this does not relabel or qualify its rebuilt App artifacts.
Ubuntu 26.04.1 x64, ordinary UID 1000, .NET 10.0.12. No product rebuild or
persistent setup change is required.

The private observer uses 33 exact original assembly references, real Skia and
headless production QuickView APIs. Four panes/five replacement rounds complete
24 decodes and close 32 readers with zero final pixel/page/admission charges.
The child exits naturally with zero. Its own `/proc/self/maps` is captured
before the workload, while four pictures are held, and after closing.

## Observed file images

Across the three snapshots, **69 distinct files have executable mappings**:

| Origin | Native ELF x64 | PE with CLR directory | Total |
|---|---|---|---|
| Exact original product payload | 8 | 25 | 33 |
| Private observer apphost | 1 | 0 | 1 |
| OS-installed libraries | 35 | 0 | 35 |
| Total | 44 | 25 | 69 |

The eight original native libraries are SkiaSharp, HarfBuzzSharp, CoreCLR,
clrjit, hostfxr, hostpolicy, System.Native and the OpenSSL adapter. All eight
retained file bytes match members of three original owned NuGet archives:
SkiaSharp native assets 3.119.4, HarfBuzz native assets 8.3.1.3 and Linux x64
.NET runtime 10.0.12. The original 348-file payload and five private observer
files verify before/after; compiled/deployed private copies remain pinned.

All 35 OS libraries have retained dpkg ownership/version results and file hashes.
Examples include glibc 2.43-2ubuntu2.4, OpenSSL 3.5.5-1ubuntu3.7, ICU
78.2-2ubuntu1, fontconfig 2.17.1-3ubuntu1, FreeType 2.14.2+dfsg-1ubuntu0.1,
GLib 2.88.0-1ubuntu0.1 and libsecret 0.21.7-2build1. These installed inputs are
distinguished from shipped payload components and from AppImage static libraries.
These are observed installed versions, not support or advisory dispositions.

Raw mapping device/inode/path values agree with the inspected guest files.
All 69 complete file snapshots are retained for independent SHA-256 inspection.
The native reader parses ELF program headers; a separate host reader parses
section tables and agrees on all 44 files' `DT_NEEDED` and RPATH/RUNPATH values.
One-byte file changes fail each of the 44 retained hash controls. Dependency
names describe declared dynamic links; they are not proof of every reachable
load path, static component, license obligation or memory byte.

## Retained observer failures

The first reader rejects .NET's deleted `memfd:doublemapper` mapping after the
successful QuickView child. Its supervisor exits one; that original failure
is preserved. V2 distinguishes mappings with no current ordinary file, but
then incorrectly assumes every executable file mapping is ELF. It stops on
a managed PE/CLR file. V3 classifies both formats, reads the same captured maps
and rechecks all original/private payload hashes and absence of owned payload
processes. **FileCat is not rerun after either reader correction.** The original
nullable-key build warning CS8714 is retained.

Private `FileCatReleaseEvidence/loaded-native-images-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| native-linux-v6/manifest.json | dd0e787dbc07d08f5a50e15174370e8fccbaca842a6999fe5710cdda74a3a770 |
| native-linux-v6/retrieved/replacement-command.json | 3bf35e0cc44a9eb309840476d7fc8bff35b8f0c5fe906fedf1511534ada41043 |
| native-linux-v6/retrieved/loaded-images-stderr.txt | 8883004e3c488d0c9f19f4f9e0a35b38fcb39098a14bb47d92e284dc50aadb8c |
| recovery-reader-v2/reader-v2-native-stderr.txt | 50f270ecc629bbc38ea2d1501ec961c1314f8b038352c545e873f87b97a8d9ee |
| recovery-reader-v3/retrieved/replacement/loaded-images-v3.json | 2c92aa0f2d566fccb107c69c0badea12a46e15ec883065b33f3120696b961efd |
| recovery-reader-v3/retrieved/native-reader-proof-v3.json | 0dff9d7b0b0fa2a4e4e0261357bd0d2fbba06b91fd232f4816e670e0f6728d8f |
| recovery-reader-v3/host-verification-v3.json | d71e9c067e74b3448ac39f550371480bd670ab731f6d6883f4d98fd18bf57c0e |
| recovery-reader-v3/outputs-v3.zip | 6218c1ff3386252e2bd533056871a4c8d7076ba9202965ab22091d852addccaf |
| independent-loaded-images-v4.json | 836897599706a3f6b3b3dc3a8ecb444dcd876bcad3464d3e670e09ebb0a3d8bc |

The independent seal verifies 218 retained files, all 69 mapped-file snapshots,
both copies of five private observer files, original/reference pins, native
transport archives, eight exact NuGet member matches and all raw checkpoints.

## Remaining scope

This establishes current-file provenance for mappings in a finite headless
QuickView process. It does not establish mapping-time memory-byte authenticity,
an entire process dependency closure, dynamic loading in child workers, the full
native desktop or other platforms, static/native source/license composition,
legal eligibility or a complete per-artifact SBOM. Deleted/anonymous mappings
have no ordinary current-file hash and remain explicitly classified. Required
installed-candidate workflows and native platform qualification remain open.
I03 stays Partial audit; no physical source, human prompt, candidate,
tag, release or publication is used.
