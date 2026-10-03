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
Native/candidate input/frame and appropriate AT checks remain open.
No USB access or source-safety qualification; G6 quarantine and overall **NO-GO** remain.

Clean successor `b7d2e80422bee5b43debae1ebb28a157ffcf05ee` passes all four required
[CI lanes](https://github.com/benny-cz/FileCat/actions/runs/37158664701); three tag/manual package jobs skip.
Retained full metadata/log and direct Windows XML independently verify all four inventories: Core 713 pass/47
skips, App 255/15, Windows platform 165/33, Remote 88/28. All 39 affected listing/cache/search/progress/thumbnail/
process-identity cases pass. Different host/CI skip counts remain explicit, not converted to passes.

The clean source's self-contained x64 Core/App test payload also passes in the running Windows VM (Insider
26300, elevated token, UUID `9D224D56-1161-A849-ABA7-2581A980895C`): **20 Core and seven headless App cases,
zero failures/skips**, UTC 2026-10-03 22:43. Independent verification checks all 684 payload files, 685 ZIP members,
the exact raw source copies against the build checkout and canonical source content against the twelve committed
blobs. Retained hashes preserve the checkout's mixed LF/CRLF bytes; initial line-ending observer failures are
retained. Controller 8940 and test workers 8944/7980 are absent at 22:50:56 UTC; the owned temp folder is empty.
This is native OS component execution with headless UI assertions, not native input/frame or reference-machine evidence.
The initial guest-directory command failed before transfer; the corrected VMware directory API succeeded.

Private roots: `artifacts/release-evidence/ci-37158664701` and this record's root under `clean-b7d2e8`.

| Retained successor evidence | SHA-256 |
|---|---|
| CI metadata / complete log | `c80a861d3deb4afe8bb2a086db0085ee90b66c9b0dc8a46e47f53aa3a88158c1` / `3bcd4f9fb9655bd60a09cb12bb5397492e64278cf31747d627115a44a0fe7eb5` |
| Independent CI inventory | `47d12a441b980710b0dd9384dabe805365ed28a6ef4e3529b6e66f9b441d49bb` |
| Guest input ZIP / manifest | `856827fafcfd2ca1acd42adb296efb9be75b1fc17e9708a71da579255704e516` / `53934a31f2c3012e8e9c50aac7a878d3afd00a37c193d4f0aa53f70b1ade192c` |
| Guest runner | `174602b26282033a01b82b360643cd3a3e6c1bea8d849492a7e1dbbb4f4a1d4c` |
| Guest Core / App XML | `78a83c5c4245d34cd0de1224b99b78ab8cc881df1326c3b52630e278d3f47864` / `30cf202689916add0f279533ec9c9976edc0206ae6177cdb1e5c49f78ca6ca52` |
| Cleanup observer / result | `7f44f8096edbc272ce23e939d625772ad1b1c28f70623e712227c591772173b5` / `0c7f7143fa5bc857b3777b3cc38644fa9dc74a6ec89b60df173a4a0ca33b6237` |
| Independent native inventory | `c614dd536aef9e8acd2b2e854cad9713c59adc2f126315e2a528449e8bab757a` |

I92's production blocking cause is remediated and verified preliminarily. Candidate/native frame/AT/reference
acceptance remains required; neither the CI package skips nor this test payload supplies a candidate identity.
