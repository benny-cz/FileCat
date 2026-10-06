# E-I146 — Held picture fixture crosses the real watchdog

Classification: preliminary committed validation repair; controlled failure-before/
pass-after, clean native and four-lane CI verify at cb85f0a. Production remains unchanged
by this fixture correction.

Exact f017a94 CI 37384217205 attempt one has one Windows failure in
Picture_viewers_share_the_provider_device_limit_and_other_devices_complete: the
earlier checkpoint has two held reads, but the third feed has entered by the later
healthy-result assertion. Case duration is 10.8037486 seconds; production's watchdog
threshold is eight seconds. That timing suggests legitimate watchdog replacement,
but the original case did not retain a watchdog event/timeline: historical cause
remains an inference. Mac/Ubuntu/ARM64 lanes pass. Four server ZIP digests and all six
complete per-case TRX inventories independently verify; the Windows failure stays
failed and all six new decoder-admission controls pass per available App inventory.

An owned controlled copy of the exact old fixture delays healthy completion nine
seconds after its two-read checkpoint. Default production correctly replaces held
workers; the old late assertion fails, with the quick-view positive still passing.
The corrected fixture gives its own scheduler a one-minute threshold, explicitly
round-tripped in the test, keeping this normal-admission scenario separate from the
real watchdog tests. The identical controlled delay yields two passes. Only test
DLL/PDB differ; every production payload file is byte-identical before/after. Every
source read/cancel/device limit/healthy dimensions/deadline/byte/lifetime assertion
is retained; neither production defaults nor the watchdog/hard-cap controls change.

Private FileCatReleaseEvidence/picture-admission-20261006 root:

| Retained item | SHA-256 |
|---|---|
| `i146-fixture-v1/baseline/receipt.json` | `e8cfa4168aac3c9a8f730c818be394bf4da345194a519d02d375b7e8cdd8d4d9` |
| `i146-fixture-v1/baseline/results.xml` | `a869be983f5bb288fe0243eba4ce9df72e635620c534ac81d2ac8c2f0bdb4f17` |
| `i146-fixture-v1/corrected/receipt.json` | `43e22442679be7e88627deedcd06d3f8cdc83333c8f85d8c0a08e390f51a45ad` |
| `i146-fixture-v1/corrected/results.xml` | `dab8b5bcf3ee281692763fd54302364f28b56f1a842bd9f3eb4da62d452f7973` |
| `i146-fixture-v1/independent-fixture-control-v1.json` | `b408f48a2139b4d9b64ddec6c2aa2d118c3cf120dfc0bd22e9ddf0e36c234574` |
| `corrected-v3/receipt.json` | `6c7b468444b9a2b3fc264b4923e1c02d8246d7a17c4483b2fb1ddaff8dc77e24` |
| `corrected-v3/results.trx` | `b714424ea4271fe847c12bf889a04c8af91cfe1894900118495e887aac4c128c` |
| `../ci-37384217205-attempt1/independent-ci.json` | `cbdd1122c5948fc271afe9cbcf6a28f3155951596bc294b5d07b8ef41bc30fd9` |

This supplies a reproduced fixture flaw, not exact historical attribution or native
desktop/frame/candidate qualification. Fresh committed native and four-lane CI
qualification remain necessary after the correction.

## Committed successor qualification (2026-10-06)

The combined I145/I146 correction is committed and pushed at
`cb85f0a43566874c44fc0ec29f5e2d24b0ff8504`. The 867 raw Git blobs, modes and
SHA-256 source manifest verify before clean self-contained publication; all five
affected source files are included. Native Windows guest: 25 pass/zero skips, 354
payload files unchanged before/after. Ubuntu 26.04.1 and macOS 27.0.1 each: 24 pass,
one explicit Windows-only Shell thumbnail skip, 350 payload files unchanged. All
six decoder-admission controls pass on each target. The owned temporary directories
are empty automatically except the expected empty test containers, which the
observer removes. No debugger FIFO is exempted or manually removed in these passes.
The test process is absent at the native checkpoint; Windows also checks all owned
payload processes. These are finite component/headless observations.

The original corrected Mac v2 capture passes its tests and temp checks, then its
collector shadows the process variable with an empty-container path while creating
the command receipt. That failed driver and all original results are retained;
its test PID was not retained. Independent readback verifies unchanged payloads,
results and an empty temp folder. A fresh v3 capture fixes only that variable name
and records the original command/PID with a complete independently verified receipt.
Neither failed capture nor original f017a94 cleanup/CI results are overwritten.
The new Mac baseline metadata identifies c022f79 as the Windows baseline and does
not assert that a Mac baseline ran; UID keys are preserved correctly.

Affected working host: 25 pass. Full host App: 357 pass/23 declared skips/380 cases,
with exactly the same tested DLL/PDB hashes as the affected run. Exact-source CI
[37387554116 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37387554116)
passes all four required lanes. Four server ZIP digests and six full per-case TRX
inventories independently verify: Windows App 363/17, macOS App 337/43, Ubuntu App
335/45 (pass/declared skip, 380 exact case names); Windows Core 787/57, Platform
166/33 and Remote 88/28. Affected picture cases are 25/0 on Windows and 24/1 on each
Unix CI lane; every new admission case passes. ARM64 App logs report 363/17/380,
and package-start/draw and installer compilation pass; no ARM64 per-case TRX or
physical-device qualification is inferred.

I144, I145 and I146 are preliminarily remediated at this exact source identity.
All-consumer/displayed bitmap memory, Unix containment, wider native UI/frame/AT,
reference hardware and exact-candidate qualification remain. No candidate or human
GO exists; release recommendation remains NO-GO.

| Successor retained item under the same private root | SHA-256 |
|---|---|
| `full-v2/receipt.json` | `a7079499665549a629bff9b772f9ecbf860cda0d5f7e82faf600c848af8966ed` |
| `full-v2/results.trx` | `591c98ff47e3bac0fbe675eda5f3dcc2fbd01d2bd62edb233e4eb2289a485b86` |
| `clean-v2/source.zip` | `2d7684984ae32550b65cba289c5bf7e81dca2240ab80220d0fc5effb4cd55d84` |
| `clean-v2/producer.json` | `fedc9dc5cdf0fd02193edda29c4ccc00a57d6e66dce919a5f44271537b09bc05` |
| `clean-v2/windows-executed/independent-guest-v1.json` | `3835393c62fb9c79fa49c55278ec27d39a45876abbc553dd6e6b99b74ade3c8e` |
| `clean-v2/linux-executed/independent-guest-v1.json` | `ce30fdc806fc01cf3403ada21a6dea3f09493e4a641e34ce6d9756d052c5483e` |
| `clean-v2/mac-executed-v3/independent-native-v1.json` | `ae58ff2a87760f900bdf096189dc44cb2e2a5057018f96507705f4928e563737` |
| `clean-v2/mac-executed/independent-observer-failure-v2.json` | `6e6bf952333b01824eb4d1e08c43c63111b86f27d9f713bc8c5c068df7906099` |
| `../ci-37387554116-attempt1/independent-ci.json` | `9fe191738c2f3fbea6c178a4059993f68f255615d25b77339bec5561a9624607` |

## Separate cold-pool checkpoint follow-up (2026-10-06)

Exact b9526b9 main CI 37454794034 remains failed on one unscheduled eight-picture
admission case: four first feeds do not reach their ten-second checkpoint. All
four selector-control receipts and the other three lanes pass. Independent
development dispatch 37455247699 on the same source passes all four test lanes
and Linux/Mac package jobs. Original logs, fourteen full inventories per run and
all fourteen/eighteen selected server artifact digests verify (E-I18-A1).
This is a different test/checkpoint from the earlier watchdog correction above.

Sixteen fresh one-CPU host processes each run all four unchanged admission cases:
64 pass, with first-case median 6.974 seconds. The test-only correction temporarily
reserves at least twelve pool workers for four blocking feeders/four blocking pipe
readers plus runner/cancellation continuations. Process-wide minima are restored
and independently asserted after all work drains. The ten-second checkpoint and
every admission/cancel/source/healthy assertion remain; diagnostics now report
thread count, pending work, request states/exceptions and first-feed timing.

An identical observer/common production payload with only test DLL/PDB replaced
passes another 64 cases; first-case median 1.004 seconds. Both controlled sides
pass, so no controlled failure-before claim is made. Cold injection is a plausible
historical contributor, not established attribution. Full working App passes
386/23/409. Committed successor CI remains necessary; no new issue closure,
native GUI, physical source or candidate qualification is inferred.

The following pins are under private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained item | SHA-256 |
|---|---|
| picture-cold-start-v1/independent-cold-start-v1.json | 08b244062ad5dd6abb7cdadb8c632001ba95dbb7dd94a01b8eefc668e7bc7688 |
| picture-capacity-v1/independent-cold-start-v1.json | 815124225a4020806ffdd5fbdc6f520a2d6f161ab2308a780710aa19c46b070a |
| picture-full-working-v1/app-full.trx | 8c1d38243cf7fb0f5fefb897beebf11fd4238c3b5f14f20dd4d295b013b414ff |
| independent-package-fixture-seal-v1.json | 9e9fdefa3ab57f3515e00bbaaede8e725b4a41fa37e63a5613e9a300b82fa81e |
