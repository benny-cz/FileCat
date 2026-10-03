# E-I113 — quick view retires abandoned requests and owns late readers

I113/V12/AI-03. Medium, stale preview and unbounded abandoned device work. Preliminary working-source evidence:
base `d48c1f31fc485be61dab9b624aad6d456718d5a2` plus the three-file overlay in `verified-inputs.json` (including
I114's test isolation). Insider 26220 host, standard token, cs-CZ, SDK 10.0.401. No candidate/native input/frame evidence.

Six controlled headless App cases fail against unchanged production code. A held earlier open throws after the
next file is previewed and hides that current preview with its error. Closing during a held open still reads
the abandoned source. Failure reading the initial revision leaks the opened source. Going A → B → A accepts
the first A request after the second A request, because the file-derived key matches. An empty-folder reset
retains the old reader. Ten demands on one held device start ten opens concurrently. Raw failing source,
test payloads/log/XML remain retained; the initial import compilation error remains separate.

Focus/reset/attach now immediately retire the preview request before debounce. Request identity guards success,
errors, thumbnails and decoded pictures; a repeated filename cannot revive an old request. Initial content work
uses the existing per-device scheduler and cancellation token. Source/reader ownership is explicit across
canceled task completion: queued abandoned work never opens; late sources are disposed on the worker without
a further read; successful readers transfer once. Failed revision/head-read initialization releases the source.
Shown bitmaps are disposed when retired. No scheduler, source-read or shared-budget policy changes.

All six controls pass after correction. A seventh held-read control proves UI demand completes as canceled
before the synchronous read is released, while source disposal waits until that read returns. The bounded
control holds two opens, abandons eight queued demands, previews a healthy second device before release, then
verifies exactly-once disposal and zero reads by the abandoned opens. A synchronous provider call already in
flight is not forcibly interrupted; it finishes at its safe boundary under the scheduler's existing thread cap.

Full App passes **256/277 with 21 declared skips**, including both actual helper/decoder UI-binding cases and
the busy-view closure case. The first full Core has one separate GnuPG observer failure (I114); after its test
isolation correction, Core passes **714/760 with 46 declared skips**. Independent checks verify all direct XML
inventories, 1,056 final input files, ten source copies and 3,329 retained files. These are component/headless
assertions; no rendered-pixel, AT, source-device or reference-machine qualification.

Private root `artifacts/release-evidence/i113-quick-view-lifetime-20261004`; UTC work starts October 3, local October 4.

| Evidence | SHA-256 |
|---|---|
| Six-case baseline XML | `3fab661c36989f708241712b805ac274c8364b7fb2246668d59a35c8c03003bc` |
| Seven-case corrected XML | `7eed11be52b3cd4254880770e5fe76bc3e096d442bb122f5752b45e886e10174` |
| Full App XML | `9c15d5f86b6e025ab1c05562e9054b615da115f74cd7fa583e23d14260a72625` |
| Final source/payload manifest | `6a31bde070da057ded6a94259f3951def92499d56e6131948852f9d32cd706bb` |
| Independent input/XML/skip/control inventory | `2f04e426759ee46dde8d1237dfa3b3cdf7739001aa5f9f026e6823fe889b51b5` |

Successor CI, clean native guest, candidate and appropriate native presentation/AT checks remain required.
Further viewport/page-load, picture-feed and aggregate-resource work remains open; I06 is not closed by bitmap disposal.
USB G6 quarantine and overall **NO-GO** remain.
