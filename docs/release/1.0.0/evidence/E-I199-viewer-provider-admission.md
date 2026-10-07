# E-I199 — F3 provider-open device admission

Recorded 2026-10-07. **Preliminary remediation; broader I06 qualification remains open.**
Original product ba05bf2195ecfe3018c6b1cadaadeceff67c3fc6; discovery documentation ccc1e717856795013dfa9113f5e8033c786ae427 /discovery record commit 1f5035ec8d259cbd87d608577d58866a3cb6f250; corrected producer 419a358c54db4ed50705d6cfdfac00b95f199e26.

The ordinary F3 `MainViewModel.ViewItemAsync` provider-open path used unbounded thread-pool work before the already bounded viewer metadata admission. Seven requests against one held provider all started concurrently. A request made after scheduler shutdown also opened content, then discarded it during later viewer admission. Quick View already uses the device scheduler.

Six preliminary headless controls exercise the actual F3 method and an owned provider exposing a real 31-byte FileContentSource. Four repeated-open variants return content, unsupported/null, I/O failure or access denial. The separate already-stopped case and a responsive opening/closing control verify admission and ownership. The original producer has five failures and one positive pass. All held calls run off the UI on pool workers; the UI marker and another device remain responsive. All original returned sources close once after their held calls return, without metadata/read work after shutdown or a late viewer. Before/after file hashes agree with independently reconstructed known bytes. This finding concerns admission bounds and late opens; it does not claim a reproduced use-after-dispose.

The F3 provider now opens on the existing interactive device queue. This uses the same worker/watchdog bounds as other device demand; shutdown cancels queued requests while an active synchronous callback finishes safely. In the finite seven-request controls, two initial device workers enter and five queued opens are canceled at shutdown. No provider opens after shutdown. Returned sources stay alive until their callbacks return, then close without a metadata read or late viewer. Normal opening still shows the actual file and closes it. This does not qualify all hang/quarantine, provider, native-frame, worker or candidate cases, or assert a bound on the number of queued requests.

Working and exact clean producer pass all 147 affected App cases with no skips, preserving all 141 preceding names/outcomes plus six. Independent v8 verifies the original 1123 and corrected 1126 Git blobs/modes/archive members, exactly two product/test changed paths, original and working overlays, 423 actual payload references, 29 retained private files and eighteen raw provider/thread/shutdown/ownership/known-input observations. The owned-process query finds no stage executable. Original five failures and one positive remain unchanged. Original [producer CI 37655285201](https://github.com/benny-cz/FileCat/actions/runs/37655285201), attempt 1 at 419a358, is sealed green on all four lanes. Each complete 642-case App inventory preserves all preceding 636 names/outcomes/skips plus six; all 24 new executions pass without skips. Every Core/Remote/Windows Platform name/outcome/skip remains. Nineteen official artifact digests/every member, fourteen complete raw inventories, four compiler receipts, 92 actual locked graphs and all seven CI seals are independently verified. The separate reader reconstructs all 24 provider admission/thread/shutdown/ownership/known-input observations from the saved raw results. Producer reference policy passes; package/draft jobs are skipped. No original run was rerun or substituted.

| Original CI lane | App Passed / NotExecuted |
|---|---|
| macOS 26 | 558 /84 |
| Ubuntu 24.04 | 556 /86 |
| Windows x64 | 625 /17 |
| Windows ARM64 | 625 /17 |

A generated clean runner failed before export/build when an overly broad replacement changed the commit-length assertion to 60; its source and empty stage remain. A seal reader then assumed 1125 source blobs instead of the actual 1126, omitting the earlier I198 evidence document added after original product ba05bf2. Its failed reader and count guard remain; the fresh reader verifies every raw blob rather than changing the source or results.

Private `FileCatReleaseEvidence/po199-v1`:

| Retained path | SHA-256 |
|---|---|
| I199-discovery-v3.json | e1b81d66601ea3c306f3ad744c96368fd4ee4abe90f3875846b7e5aeae5e7078 |
| baseline-v1/command.json | 57e9741b25f9843745a4a6f9f5fa7d303b5068653dfabf34aefa9e947bd4446a |
| baseline-v1/results/app.trx | 60611dde97060f0fc0dc9335671453177c807564861c00997f34842cae4bd24b |
| working-v4/command.json | 4be7ba3d10c88dc6f40e3500f92cbb090abed4a4d3ce3ef1e885fd032dd46608 |
| working-v4/results/app.trx | 302c7a2122ccdf9b30f741bfa44970da93224e16a5807036b4a42e490385232d |
| clean-v6/command.json | e9d6f19c72509ecbe3398541037e61620ca8b8b489ee7b8123e449a4fe9c93d5 |
| clean-v6/results/app.trx | 5d7a1e5f3740c93d89b3326996752b84e5a2fbac4e14cfb6e6065f1f78f74e1f |
| ViewerProviderAdmissionTests-v1.cs | 55389c8f490a83d5f38d51121f021a30dd6e97b4c581ac1dcf160dad14afa4c3 |
| MainViewModel.Operations-working-v4.cs | d4cb96584e58e870ecaa0909a3c6eaf6a2388bb2f4f7bc724bded5614c7d8e97 |
| independent-provider-observation-reader-v3.py | d4c16f89bd5281043f322316416408f6034c5ef73bcbad0f711f42f4d753cac1 |
| run-provider-baseline-v1.py | d4ee2752c88b1f160c118c552593f78f1994b3519dc7b01ceb467ae7b0fce174 |
| run-provider-working-v4.py | 693277382872aca717748296721be6e8f7fc8bc6b5a7adf77c6f0c2b8ac8c1f3 |
| run-provider-clean-v5.py | 475cf8471cb4247ad58460dd2466ad23e1875082d4422ac1f7347ae5b46795c6 |
| run-provider-clean-v6.py | c009cb9ab033b5f70fa77333ece3e0b77cbebda9ab94df410be3494d76f685dd |
| seal-provider-admission-v7.py | 38a74171e889af6ad2631e0b05f2fc3c87bbc73b8609878b6a38a2c10a8c5667 |
| seal-provider-admission-v8.py | cf2b344832c83b7af4ef852621b3aae820757953357c20fce392305a6704a92c |
| clean-runner-guard-v6.json | 38c8e261878cda6b7f18aa076922cdb493338043fa15de334d36092e9682b047 |
| seal-count-guard-v8.json | dcd1c59bf8208c2534f732bfe4402446007ef8b1b2820065ebc9ef80abe0ba11 |
| owned-process-absence-v8.json | 70939388598d3b2df08bace7f8f4a61db6d449599d8ae43fdcdc6ea44c173384 |
| independent-provider-admission-v8.json | 83e39564abf02a17b5e0f9802dca8d42b7cc8c43497917825965ab883ef82ff6 |

The first discovery preparation compared a lower-case Python digest to uppercase C# output and failed before writing evidence or changing product files. The fresh independent reader normalizes the known input's representation; original source/build/results remain intact.

No native desktop/input, human, physical/reference or candidate qualification is claimed. Physical-source/USB hold, contract freeze, candidate formation and explicit human GO/publication gates remain. No persistent machine setup or physical source changed.

Private `FileCatReleaseEvidence/ci-37655285201-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 912bee5ebac31030e7cecfad8759dee517f43b84f2c72e2f3fc4357795c6e9b5 |
| independent-draft-guard-ci-v1.json | 586fb6b4d51260b20b0602fba655ecacbd619c8ca68ed9a2c121c4d7ae30d2e3 |
| independent-fixture-ci-v1.json | 331179dc21fbd893856c35f728ff740bf8fa9c4154bed39f6bd9cf2d4e163dac |
| independent-i199-ci-audit-v1.json | aa7c5be36bc5843de50b992bc9ce32122e1214812173608c443014da1fc5435f |
| independent-i199-ci-cases-v1.json | 67dd69f6014346057b311ecf7009ff31354bd05f8135ff79c43a1777f2e4f809 |
| independent-producer-policy-ci-v1.json | ed07de962e36abb1bd2c1365342a8c20e1b1c05c2a3d5d267b3f0e346df6a644 |
| independent-restore-ci-v1.json | 490deae95a300365bf67d0e4b299fcdede95261e6751a1c9927e5908277c41f8 |
| independent-separation-ci-v1.json | ef49287e22c2d44c36b695f43945aad32059b387c4304a41eaaf3cb24bf91e46 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| collect-i199-ci-v1.py | 5f23b22b5e4a7c718c44125f7cc0017feaeb3a61657a8179adc1962ea490b358 |
| verify-i199-ci-v1.py | 2a7907598d5e105231031484771ee3801d4f204d416df1d81ad5fc2c0383b3fe |
