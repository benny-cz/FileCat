# E-I06-B3 — borrowed icon/tint buffer references

2026-10-06. Finite preliminary component observation for I06/V12/V16 on the
Windows Insider 26220 host, SDK 10.0.401 and .NET 10.0.12. Exact product source
and the original 352-file payload are 6215329ed67da6c8463a996eb51b12ac569194aa;
discovery documentation HEAD is 213de8e. Current `src` Git tree still matches
that producer exactly: 8a5c239918fe1fad88683b0bdb8bfd78d93c8b05. No product fix
or numeric aggregate policy is inferred from this measurement.

## Exercised ownership

The unchanged production `IconRequestCache`, `AsyncIconRequestCache<IImage>`
and `IconProvider` tint/clear implementations run through a private observer
with 33 original assembly references. Four deployed observer files match their
retained compiled outputs; no FileCat assembly is rebuilt. The async cache uses
an owned disposal callback; production Windows `<Plan>` loaders, native icon
workers and OS helpers are outside this component case.

Each controlled cache has capacity four and waiting capacity sixteen. These
are fixture parameters, not replacements for the production defaults of
4,096 entries and 256 waiting requests. Twelve actual 64×64 Skia-backed source
bitmaps are published sequentially and retained by twelve `Image.Source`
borrowers. Five tint-table clear rounds retain sixty additional, distinct
64×64 tinted bitmaps in Image controls. Repeated lookup within each tint
generation returns the same tint object. Original/tinted complete pixel
buffers independently match the expected teal/green BGRA hashes.

| Per-cache checkpoint | Entries / queued | Held bitmap borrowers | Locked pixel bytes |
|---|---|---|---|
| Twelve sources and five tint-clear rounds | 4 / 0 | 72 | 1.125 MiB |
| Cache and tint table cleared; borrowers retained | 0 / 0 | 72 | 1.125 MiB |
| Stale completion controls finished | 0 / 0 | 72 | 1.125 MiB |
| Image sources released; collection completed | 0 / 0 | 0; all 72 weak targets dead | 0 reachable through observed borrowers |

Across both cache types the held cohort is **24 original plus 120 tinted
bitmaps, 2.25 MiB**. Published images remain lockable with unchanged pixels after
eviction and clear, as borrowers require. A late cleared same-key completion is
refused and disposes its unpublished buffer while keeping the fresh published
replacement valid. The async retry control also disposes its unpublished buffer.
Both fresh controls are explicitly disposed after their identity check.

While held, all 144 weak targets are live, providing a positive control. After
Image sources are cleared and cache ownership is dropped, all 144 wrappers
become collectible. This establishes managed-wrapper collectibility in the
observed graph; it does not claim zero total unmanaged process memory.

The retained observer field `TotalPublishedBitmaps=144` counts the held cohort.
Its label excludes two additional fresh same-key published controls and three
disposed unpublished controls; it is not a total allocation/publication counter.
That scope is explicit in the independent proof rather than silently relabelled.
The build has zero warnings/errors; the process exits naturally with zero and
the actual owned-payload process census is empty.

## Source inventory and limits

Fourteen original source files are pinned by Git blob and canonical SHA-256.
The inventory distinguishes native caches/workers, tint/vector dictionaries,
row draw-local references, place/overflow/hidden-button Image borrowers, the
choice-dialog icon dictionary, owned F3/QuickView pictures, Shell conversions
and the About logo. It records concrete remaining scope for each consumer.
Choice rows can retain images across filtering until the dialog closes; place
buttons can retain images even when hidden. Cache entry count alone therefore
does not bound all live pixels.

Real native worker-held requests, deferred render frames, full place/dialog
lifetimes, logo/window-icon lifetimes, larger materialized workflows, Shell/DPI/
format/race variants and exact-candidate qualification remain open. The controls
here are component borrowers without native desktop input or render-frame
qualification. No physical source, credential interaction, persistent machine
setting, candidate or stable publication is used. I06 remains Open.

Private `FileCatReleaseEvidence/icon-borrowed-memory-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| host-v1/result.json | 3ae59ffac31cfb31935fc6f3e20c6826f5da836cfcf87ee0a930cb7a238a6d87 |
| host-v1-command.json | 8c31ccce78c6c79003ce562f85165f4b46f3136e594823cd3404371fe05e8066 |
| manifest.json | 585bc59271cb55f0ff2d27e9fa7886e91de2cd5d301425e133917ce5b5825311 |
| process-inspection-v1-stdout.txt | 10d43d5844154296f24af7bfe5f8adf06e2f6ca75dd2a509b2d2ae137e700c2b |
| source-consumer-inventory-v1.json | 55001beb0e3593ca30439188f76f623c751838a17f9e8a1de6f5b26a131063d5 |
| independent-icon-memory-v2.json | 3818c42fd768fe29ba154b7ae7dd187658a079153b38b4796eda8f0d181cc7b0 |

The independent seal verifies twenty retained files, both copies of four private
binary/configuration files, both original product payload copies, 33 references,
432 complete retained framebuffer hashes, two stale same-key controls, one retry
control, the held/released weak-reference controls and every recorded checkpoint.
