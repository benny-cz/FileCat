# E-I146 — Held picture fixture crosses the real watchdog

Classification: controlled validation repair; working tests pass, successor native/CI
pending. Production remains unchanged by this fixture correction.

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
