# E-I06-B2 — QuickView picture replacement and detach memory

2026-10-06. Preliminary component observation for I06/V12/V16 on the Windows
Insider 26220 host, SDK 10.0.401 and .NET 10.0.12. Exact unchanged production
assemblies and decoder worker are from
`6215329ed67da6c8463a996eb51b12ac569194aa`; discovery documentation HEAD
`0b2cfd3458b21d42875b38996862ba65059d2df7` differs only in release records.
No product code, package, dependency or persistent setting changes.

## Executed scope

A private probe directly references the previously sealed 352-file committed
Windows payload. Its 32 original references plus the original
`CommunityToolkit.Mvvm.dll` verify before/after; the latter is needed to compile
against real tab view models. All four deployed observer binary/configuration
files match retained build outputs. No FileCat assembly is rebuilt.

Four actual `QuickViewPane` controls each attach to a real tab/listing in a
headless window with real Skia drawing. An owned provider serves two 4096×4096
solid-color PNGs, a deliberately malformed PNG header and one text file.
Provider item paths and sources have no local filesystem path, and isolated
Shell-picture settings are disabled, selecting FileCat's own decoder route.
The panes use the observed render scaling of 1; this is not a high-DPI result.
The existing private-load/debounce mechanism used by
`QuickViewLifetimeTests` invokes the production loader.

Each initial large PNG becomes an actual 256×256 Skia-backed framebuffer.
The probe verifies dimensions, stride and the expected teal or red center pixel.
Five rounds alternate all four focused items, for **24 decoded pictures**.
Immediately after each focus change, the old Image source is null and its
retained old bitmap rejects locking. The next visible picture has the correct
current color, rather than a stale completion.

| Checkpoint | Retained decoded pixels | Retired buffers | Source opens / disposals |
|---|---|---|---|
| Four initial previews | 1 MiB | 0 | 4 / 0 |
| After five replacement rounds | 1 MiB | 20 | 24 / 20 |
| All malformed-image fallbacks | 0 | 24 | 28 / 24 |
| All text fallbacks | 0 | 24 | 32 / 28 |
| All panes detached, windows and tabs closed | 0 | 24 | 32 / 32 |

Malformed pictures retain the hex fallback; text files show the text control.
Both fallbacks have no Image source. All 32 source readers dispose exactly once
outside an active read; the 24 retired bitmaps remain unusable despite retained
references. Final picture bytes, shared page bytes/readers and decoder admission
requests are zero. No reads follow closure; all four owned fixture hashes remain
unchanged. The process exits naturally with zero; an actual owned-process census
finds no observer or FileCat worker remaining.

Sampled process private memory is 26.05 MiB initially, at most 50.23 MiB at the
recorded checkpoints and 33.66 MiB after closure/collection. Samples do not
measure transient worker peaks or establish reference-machine/native-frame
performance. Remaining objects and allocator overhead are distinct from the
one-MiB framebuffer sum.

The independent reader verifies every snapshot's exact pixel/source/admission
arithmetic, all 24 bitmap identities, all payload/reference/build/output pins,
both complete encoded PNG patterns and four wrong-dimension/color refusal
controls. The malformed header and text bytes are checked independently.
The original private build's one CS8714 dictionary-key nullability warning is
retained; compilation has zero errors. No warning is silently removed and no
successful measurement is rerun to change it.

## Limits and retained evidence

This qualifies the exercised component replacement and detach lifetimes only.
Main-window panel integration, more counts/formats, pending/held decoder races,
Shell thumbnail/borrowed-icon routes, higher DPI, Linux repeats, native
desktop input/frames, reference hardware and exact-candidate artifacts remain
open. Existing held-read tests provide separate evidence at their own identities.
No new aggregate bitmap policy or budget violation is claimed. I06 remains Open.
No physical source, credential interaction, candidate or stable publication is used.

Private `FileCatReleaseEvidence/quickview-picture-memory-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| host-v1/result.json | b0237b2bccbe10afee7d99a51452f8533cebb0c6823a75473c1276513109f8b2 |
| host-v1-command.json | bf15089a7509f18a07d081c7aeec8dadfe928efa23f71bffdab43d2f10eaf811 |
| additional-reference-pin.json | c28164c9cb6e67b23d62b90348832f5bb6f2a17c0a91a4b63ad707098d5b91f6 |
| host-process-inspection-v2.json | 739ec23dbb451f74ef20d420d9c47d64bc6da0f2c3d95e1d625e267c6518ef16 |
| independent-quickview-memory-v2.json | c855f6a3dd4bc8fb4d6bb4baa8395d59acc7aff86f1746b3e6d48adb61f0f9e3 |

The final independent seal verifies twenty retained files and both compiled/
deployed copies of four private binary/configuration files. Counts remain
140/162 preliminary Remediated, one Closed and 21 remaining issue remediations.
These are not remaining test counts; all final-candidate campaigns and explicit
human stable GO remain gated.

## Native Windows and Mac follow-up

The same C# observer source now passes in the Windows Insider 26300 VM and on
the physical Apple Silicon Mac, macOS 27.0.1, ordinary UID 501. Each uses its own
original committed self-contained product/runtime payload: 352 Windows files or
348 Mac files, with 33 exact local assembly references per probe. FileCat is
not rebuilt. New private apphosts use self-contained .NET 10.0.12; the Mac
validation apphost alone receives an ad-hoc signature. Its before/after hashes,
native strict signature check and the actual retrieved signed bytes are retained;
the other four private files and every product file remain unchanged.

Each native repeat independently verifies four controls/five replacement rounds,
24 correctly colored/dimensioned framebuffers, a 1 MiB retained pixel plateau,
24 immediately disposed old buffers, malformed/text fallbacks, 32 source disposals
outside reads, sixty total reads and zero final pixel/page/admission charges.
Both exit naturally with zero and leave no owned payload process. Across the host,
VM and Mac, the completed scope is 72 decoded pictures and 96 source readers.
All raw output, transport, manifest, private binary and fixture oracles independently
verify. The same original nullable-key observer warning is retained.

Mac reports zero for every `PrivateBytes` sample; that metric is **unavailable
through this observer API**, not zero actual process memory. Mac sampled working
set is 220.75 MiB before, at most 269 MiB at recorded checkpoints and 269 MiB
after closure/collection. Windows guest sampled private memory is 25.52 MiB
before, at most 48.09 MiB and 34.24 MiB after closure/collection. These finite
snapshots do not measure transient worker peaks or qualify performance targets.

Mac SSH/runtime preflight finds no `dotnet` on its PATH; the private self-contained
apphost permits the repeat without installation or root authentication.
Temporary `caffeinate -i` is bounded by the native runner's command lifetime;
no persistent sleep, remote-control or other machine preference is changed.
Linux, Shell/DPI/main-window/races/other formats/native desktop/reference/candidate
scope remains open. No physical-source or stable-publishing policy changes.

Private `FileCatReleaseEvidence/quickview-picture-memory-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| native-windows-v3/retrieved/replacement/result.json | 66d4acd0b53f931e215d2fd64d11df81dd834c99177cc9346dcc606cf4c7a272 |
| native-windows-v3/retrieved/native-result.json | d30f48797a329957139cb940fed7a9abcd7eaf6189a65c7c9e6d1efac3a7260a |
| native-windows-v3/outputs.zip | 109d9ae85d1f13f7c6dd8cbb6809ac481a8731a04e071bce3bb456e22008a03d |
| native-mac-v4/retrieved/replacement/result.json | 4d500840908d3f152eeeb09878c6ccdc7fb12c079ba163b83e265dd898b68be8 |
| native-mac-v4/retrieved/native-result.json | 8d79124dfe1d9810f44f4edf9cf93d27ae68ae1452f46d37fffb63eec1c9646f |
| native-mac-v4/outputs.zip | 6e0e3ea1e9bdf9a258322fbda231bc6019fcad0cba8013d80181b64ffe941ab8 |
| independent-native-quickview-memory-v5.json | 897be7ac86c27265633e2cd569a5aed4f9cd9ef06052c89095cf87a1d0f133e5 |

The native seal verifies 117 retained files and ten private deployed/published
binary pins, in addition to the original host seal. Native Mac private-memory
counter availability remains a measurement limitation; no pass is invented.
