# E-I06-B1 — retained F3 picture memory

2026-10-06. Preliminary component measurements for I06/V12/V16 using the
unchanged, committed production assemblies and decoder worker from
`6215329ed67da6c8463a996eb51b12ac569194aa`. Discovery documentation HEAD is
`4e7d38dc2c83beacf471285bdfb5d0bad3e2ddee`; its only changes since that producer
are release records. No product code, budget, package or dependency changes.
**I06 remains Open. No candidate or stable GO exists.**

## Method and provenance

The existing committed Windows self-contained test payload is reused byte for
byte: 352 original file pins, 32 exact assembly references, the original source
ZIP and all 1,039 exported raw source files verify. Private probes reference
those assemblies directly; they do not rebuild FileCat. SDK 10.0.401 builds the
observer without added package dependencies. Host runs use .NET 10.0.12; a
private self-contained 10.0.12 apphost runs the same observer source in the
authorized Windows VM. Seventeen deployed observer binary/configuration pins
match their retained build outputs. These apphosts are validation tools, not
shipping FileCat artifacts.

The probes create only owned 256×256 or 4096×4096 solid-teal PNGs, real
`ViewerWindow` instances and an observed `FileContentSource`. Returning no
`LocalPath` forces the FileCat decoder route rather than the shell thumbnail
route. Avalonia headless windows use **real Skia drawing**, not the headless
drawing stub. Each locked framebuffer identifies
`Avalonia.Skia.WriteableBitmapImpl+BitmapFramebuffer`, version 12.1.1, with
the exact expected dimensions, row stride and sampled BGRA center pixel.

An independent Python reader checks both complete encoded PNG patterns using
chunk CRCs, dimensions, bounded inflation and predicted PNG filters. Wrong
dimensions with a corrected CRC and a wrong expected solid color are both
refused for each of the two distinct fixtures. This verifies the owned fixture
oracle; the product bitmap check samples its center, not every decoded pixel.

Locked retained pixel bytes are summed from actual `RowBytes × Height`.
Process private bytes, working set, managed heap, shared page-cache charges,
source reads and disposals are retained separately after every load/close.
The mode observer additionally separates retained buffers from buffers whose
picture controls are effectively visible.

## Observed memory and lifetime

All figures below are MiB (1,048,576 bytes). Private memory is sampled process
memory, not a reference-machine performance acceptance result.

| Case | Viewers / dimensions | Locked pixel bytes at full load | Private before | Private loaded | Maximum private sampled | Private after close/collection |
|---|---|---|---|---|---|---|
| Host small v3 | 1 / 256² | 0.25 | 24.52 | 38.15 | 38.15 | 36.65 |
| Host large v3 | 1 / 4096² | 64 | 25.81 | 124.94 | 124.94 | 59.32 |
| Host eight v3 | 8 / 4096² | 512 | 25.84 | 629.64 | 629.64 | 79.33 |
| Host mode transitions v5 | 8 / 4096² | 512 | 25.78 | 629.18 | 638.45 | 83.91 |
| Guest small v6 | 1 / 256² | 0.25 | 23.95 | 36.73 | 37.03 | 32.50 |
| Guest eight v6 | 8 / 4096² | 512 | 25.59 | 627.80 | 631.23 | 76.33 |

Host OS is Windows Insider 26220; guest is Windows Insider 26300 x64. All six
cases, covering 27 opened viewers, exit naturally with code zero. Every load
adds precisely its expected framebuffer bytes. Closing each viewer subtracts
those bytes, clears its public picture, makes the retained old bitmap reject
`Lock`, and disposes its source exactly once outside an active read. Final locked
pixels, shared page bytes and shared readers are zero; no later source reads
occur. Owned PNG hashes remain unchanged.

In both eight-viewer mode cases, switching all viewers to hex leaves **512 MiB
retained and zero effectively visible picture bytes**. Returning to picture mode
reuses the same bitmap objects. The small guest mode case behaves equivalently.
The observer calls production `SetMode(true)` directly through reflection and
uses public `ShowPicture`; this measures production state transitions, not
native F4 input delivery. Sources stay open until their viewers close.

The close/collection snapshot deliberately retains references to the windows,
disposed picture objects and sources; services are still alive at that checkpoint.
Allocator/framework overhead and these objects are not expected to return process
private memory to its original baseline. After the processes exit, actual host
and guest process censuses find no owned observer or FileCat worker remaining.
No persistent host/guest settings change. The guest stays running as requested.

## Failures and remaining scope

Original observer failures remain retained: v1 compile errors (namespace/timer
declarations), v2 runtime dependency binding, and v4's headless F4 visibility
assertion. That last attempt loaded all eight pictures, then failed its assertion;
its keyboard-delivery cause and close-state assertions are not qualified. V5
changes only the observer to call the production mode transition directly;
neither the failure nor the corrected measurement establishes native keyboard
behavior. Original v7 sealing also matched its own inspection PowerShell command;
a transcribed tool-failure observation is labelled as such. Corrected v8/v9
process inspection seals the original runs without rerunning them.

This is a finite retained-memory measurement, not a newly proved budget violation.
The approved numeric 64 MiB shared budget concerns content **pages**; it is not
silently extended to all decoded bitmap allocations. No separate numeric
aggregate displayed/cached picture target was established in the current
contract. Before contract freeze, retain the measured behavior when deciding
whether a picture retention/admission target is needed; any target change requires
the approved decision process. No arbitrary new cap or eviction policy is added.

Other viewer counts/formats, QuickView, borrowed icons, all-consumer strong
references, Unix bitmap allocation, GPU/native desktop frames/input/AT, reference
hardware, installed artifacts and exact-candidate qualification remain open.
The physical-source validation hold is unchanged; no physical disk is used.

Private `FileCatReleaseEvidence/picture-displayed-memory-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| original-reference-pins.json | a514c8588e96e575eb8d74f4578522bd99f05ede547b5a908eabc6cf4bb856ee |
| small-positive-v3/result.json | 3d837fb27eee20a88fc0891bae77a1338af65991bdec7934d397e931729b9dcd |
| one-large-v3/result.json | e254f6992daa958f6b3c1b5e2006f893617f0d1d0cf6814ecaaa330eb3f52ea2 |
| eight-large-v3/result.json | 660c139c9b385046aeb0dbf246149e3e609b0f24f54c18aa91e96e0e8d83c8a3 |
| eight-mode-large-v5/result.json | 256131bb19ddc1e471d8f72f4114e31ef69afc25325f971c8a104ffead576256 |
| native-windows-v6/retrieved/small/result.json | 74028bd125f3594bdd4d6b595bb23bb8293b42a2a9a42abd7aeaae82ea02b593 |
| native-windows-v6/retrieved/eight-large/result.json | 054c8789aacc7d17d16d889022bfb6eb3bfff817dc98658b8421f0d28682e438 |
| native-windows-v6/retrieved/native-result.json | 3d49a9f6d657d24e733a41320fafbe882a5f52f06ea6615c89fe42c3800a1bcd |
| native-windows-v6/outputs.zip | d88547202ef4dfad5a642764efa58bdbea7d483faef745e2fffa787cd507ce6d |
| host-process-inspection-v9.json | bdf73191fb3bd6d3895a450ea88c9032dd0de43b6d8a77f9ddc4a94d9e46c4a4 |
| independent-picture-memory-v9.json | 5dfd9551838cd1bdb48e3789403a74cce60ffa7a6be1b643986e644920aef5c8 |

The final independent seal verifies 131 retained file pins, seventeen private
probe binaries and five copies of the 352 original payload pins. Issue counts
stay 140/162 preliminary Remediated, one Closed and 21 remaining remediations;
these counts are not remaining test-item counts. All final-candidate campaigns
and the explicit human stable GO remain gated.
