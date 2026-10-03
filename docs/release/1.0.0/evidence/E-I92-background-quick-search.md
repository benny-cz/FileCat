# E-I92 — responsive name search in large listings

I92/V12/V16. Preliminary working-source validation: base `324ed29da43a292c73f9369fa713de1b0e1e38b9`
plus the exact twelve-file overlay in `verified-inputs.json`. Windows 11 Insider 26220, standard token,
.NET SDK 10.0.401, cs-CZ; this developer host is not the reference performance machine. No candidate exists.

A fresh baseline at clean `1bd931bbfb523ed11ce701af7b4bece1b7f372f3` reproduces the recorded delay:
one million synthetic 240-character names, five scans each. Synchronous misses take 202–345 ms ordinally
and 737–1,168 ms with culture-aware matching. These are model costs, not native presentation measurements.

Above 4,096 visible rows, quick search now scans on a worker. Its immutable display order and entry store
are leased; it does not copy a million names into strings. Keys remain ordered, including rejected text,
Backspace and both cycle directions. Pending text and a searching caption acknowledge input. A replacement
view causes the same key to be checked against current rows; stale row indexes never move focus. Escape,
user cursor movement, navigation and tab closure cancel queued keys. Small listings retain immediate behavior.
The native benchmark distinguishes its synchronous primitive control from the actual quick-search handler.

Nine controlled Core cases verify in-memory/spilled/external-index order, wrapping, parent exclusion, marks
and focus, a held scan leaving the UI free, cancellation within 128 names, and safe retirement/cleanup.
Seven new headless App cases verify rapid keys, miss feedback, Escape routing, navigation/closure,
wildcards/non-ASCII/anywhere matching and a filter replacing the view while a long-name scan is pending.
The existing small-folder keyboard case also passes. These are model/UI-binding checks, not native frames or AT.

After the affected regression fixes I111/I112, full Core passes **714/760, 46 skips**, full App **249/270,
21 skips**. Targeted Core passes 16/16 (including streaming controls); targeted App passes 8/8.
Exact source, 790 retained input files, direct XML/skip inventories and 2,480 retained files independently verify.
Initial non-filesystem spill-fixture failures, a compilation import error and the initial Escape observer failure
remain retained. The Escape observer now snapshots focus immediately before cancellation; it does not require
undoing earlier input. The exact cause of its earlier row change was not independently traced.

One serialized successor ListingScale run, five samples per mode, keeps the complete spill at 503.5 MiB and
38.1 MiB of reserved indexes. First rows appear in 73 ms; enumeration finishes in 1.833 s.

| Model scan | Acknowledgement | Completion |
|---|---|---|
| Ordinal miss | 0.017–0.494 ms | 215–246 ms |
| Culture-aware miss | 0.018–0.024 ms | 748–828 ms |

The synchronous control still costs 195–233 / 743–920 ms in this run. Moving that work off the UI thread
addresses the recorded blocking cause; OS input-to-frame and the reference-machine acceptance protocol remain
required. A concurrent earlier successor run is separately retained and is not treated as an equivalent load profile.

Private root `artifacts/release-evidence/i92-quick-search-20261003` (UTC October 3; local work spans October 3–4).

| Retained evidence | SHA-256 |
|---|---|
| Twelve-file source/payload manifest | `d22f56a3e151e5e8b4d5e59f6d2fd4398c642a56fc79e35ec7e3546b9e3a914e` |
| Full Core XML | `a10c65e040ca76c25be6e7dad79ba5e34b87847beee9db9ee8fe0a5d97ff8876` |
| Full App XML | `2a56d6528ed0d4f14fe8337031cfdc7a0d3aade756f75a6a0974f28399bfc227` |
| Targeted Core XML | `2d63a949a7badf06ac443661929cdd927a154ce7e8b4cfe514b4d056dc172e0f` |
| Targeted App XML | `c77a85485cdbdbbc64d03e2b94d0a56f5cf06a5fa64814526bfa0cd7d616bfb1` |
| Fresh baseline scale log | `82947eda34d53725cdbdfd140a466eb48f146176389b4afb253033059418b53b` |
| Serialized successor scale log | `41451f498f4f7ff1b79c98d0a98581ab5465f107d292c56111f793d4ac946a00` |
| Independent source/XML/skip/payload/metric inventory | `1689a3b6c3ad3019f0a56c63f3fab3868384c3a6b274f20d20306d0a1d37e759` |
| Native UI initialization failure | `a084cf66d3533e092f859887a51e728ff7ac921575aa79baa21f6d8b3e16262e` |

The computer-use skill was reread with its guidance/API/confirmation policy. Initialization exits before
application selection with the same Windows sandbox setup-refresh error; no agent native app input was sent.
Successor CI, native/candidate input/frame and appropriate AT checks remain open. No USB access or source-safety
qualification; G6 quarantine and overall **NO-GO** remain.
