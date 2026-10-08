# I217 — icon checkpoint double sampling and an unreached eviction boundary

Test-only icon correction `a1e6879692bac8b6b4d532ad85499e2dcebe443e`; final lifetime-only fixture producer `2dc1417804d8aa3936089bdd10a90e67565d401b`. All production and workflow files remain identical to I216's `3f121edbf14b3f7abadfb9c4e5103a0bebf586f6`. One new Windows-only control, five retained Windows cases, **full canonical App 1150 passes/23 existing explicit skips**. Original CI 37736670942 retains one unrelated Windows lifetime timeout; corrected original CI 37738707924 passes all four required lanes: the new control passes on Windows x64 and ARM64, with two explicit Windows-required skips on Ubuntu and macOS. All six Windows cases pass on both Windows lanes; their twelve other-platform skips remain explicit. Real-capacity eviction observations are retained on both Windows architectures.

## Original failure, forced mechanism and corrected coverage

I216's exact 3f121ed full App run retains 1,148 passes, one failure and 23 skips. The existing eviction case failed its Active==0 assertion after 0.1102825 seconds; the helper's 15-second deadline was not reached. Its loop sampled the predicate successfully, then sampled it again for the assertion while another admitted load could start. The historical failing run has no main observation, so its precise scheduler interleaving is unproved. It is retained rather than dismissed as a flake.

The new bounded control uses the actual NativeIconSource request path and a controlled load result. It observes idle, starts one held follow-up load before returning that observed sample, and records the active load. The unchanged helper takes a second non-idle sample and fails; the corrected helper accepts the successful single sample. Baseline-v1 is one failure; working-v2 passes all six Windows controls. Clean-v3 independently verifies the canonical Git source, actual payload, raw output and full inventory. The original existing icon failure becomes Passed; every other prior App name, outcome and skip remains.

The old eviction case flooded the held queue, which admits at most 260 entries; it never reached the production cache's 4,096-entry capacity. The strengthened case retains its original name, holds one old request, publishes 4,096 distinct fresh entries and verifies the old key is actually absent under the cache gate. After releasing the held request, its unpublished image is disposed and the old key stays absent; count stays exactly 4,096. The cross-worker disposal flag is volatile. Working, clean and both native Windows observations retain exact starts, membership, counts and disposal. No cache capacity or production behavior is reduced or changed.

Each original native attempt retains twenty server-digested archives/every member, fourteen full test inventories, four build receipts, 92 actual locked graphs, nine owned mirror controls and five launcher controls. The original reader checks the actual I216 predecessor with the one explicit lifetime Passed-to-Failed transition retained. The [I215 follow-up](E-I215-comparison-warmup-checkpoint.md) checks that failed producer and retains the explicit Failed-to-Passed correction and every other old name, outcome and skip, all 260 I216 additions, four comparison controls and earlier additions. NativeIconSource executes on real Windows platforms with controlled results; these controls do not qualify the native helper, Shell parsing, drawn desktop, DPI/frame behavior, physical sources or a release candidate. I06 and all 24 candidate campaigns remain open.

## Provenance

Private `FileCatReleaseEvidence/icon217-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/command.json | 995a91fc72a387c16d17ec58511231558d8661d84afdae13db53fef47e82e03e |
| baseline-v1/source.zip | b1a8b6b10a9fb34a14bd8355c019bebdea2e0c0105f1e3c9e77d42cf81c056c9 |
| baseline-v1/app-stderr.txt | 4535110e821b79b4e7b5f350a6a12d7354e64efc7141a7dcc20402c9392cf06f |
| baseline-v1/app-stdout.txt | a33d23eef44bfad24ada45623d7e03787f7fa9e9792e0d7a7deac223a38a7c2b |
| baseline-v1/results/app.trx | 8e565e0a264c55a6c70e09bd4ea218a7d65bfe10d6c29371751bfff7bbcb313e |
| working-v2/command.json | 93d4985a09595de8351d291e9527955279cc20382c992b4e9a45d01967f9c3a8 |
| working-v2/source.zip | d3136ab4491026759b436233e9faf5c0dc4982298632bdb3bc626d3d04b70ee7 |
| working-v2/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v2/app-stdout.txt | ad9b83d6895fa0e419dbe69a491a87e8852f9b9c280a200397dcb90f39f23bf2 |
| working-v2/results/app.trx | fb1c5d016193de1e69dead96af06bfe1a38e914b8434248db68376c20775b8ea |
| clean-v3/command.json | ba02b7dce64e94d852938bb895c801ce6ea020e3043f9edf8d0331138edb013c |
| clean-v3/source.zip | 5ff818ddfd5e035cda0b2ff06d931885503c735a11c8523746f5748e1b686391 |
| clean-v3/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v3/app-full-stdout.txt | 0bbbc92a4e85abdef325f566e6558bb1b4522161e966b03f76a46f9b6f371d25 |
| clean-v3/results/app-full.trx | 159b3cd93d60100fe7fc32ce573c4c27816684963adec016c5d2e809fb47f875 |
| capture-original-failure-v1.py | d5aff5bb55949ec716430e90b58440fae789c3b2ca4a20d1b55ee7a099521dc0 |
| original-icon-checkpoint-failure-v1.json | 2d526e2ab93a30dece67e3538ae0e278d5e336f353673dbd51a2bacb6c882cc0 |
| prepare-diagnostic-v1.py | 48598dac4525353aa5578fb188773688bd5fcfe8d55767937a739b7bc364c005 |
| WindowsIconDemandTests-diagnostic-v1.cs | d90f6d6de1fee705511bb61ae31e9cda6dbd49d25a605f305438dcde1641d71b |
| run-icon-v1.py | 449add84ddf3eb0f3f4f642a89549cef4635184d513ce3bae27f8eae8a174450 |
| install-fixture-v1.py | b3f9ba185dc9b81e78defe8fcf59beeb60fbb48e3e47d17a6741ce2e999b436c |
| fixture-transition-v1.json | 1ebe14ffd304e6ace3291a55a34a7818c2c5a37da1aa5f92fa6a41f5b5a68879 |
| seal-icon-v1.py | da10c6b4b2134c5cda5ea9f618752b4f54373c28420d61815af8ee9ae289e59f |
| independent-icon-clean-v1.json | 1b192c15311b61094d2a378511cb1649f41651f4e1aa703062ebcb83667a6b2a |
| query-ci-v1.py | aee8901412cd1d1f8948a35632bf90b448ffc4ebe56f5c50f1f0eb3c028c5371 |
| ci-query-v1/observation.json | 90c8ee9b4f9191596172e0ce36ad495910af048af12e877706202854055813f2 |
| prepare-native-v1.py | cf147cf1f41e2b26836ec504424b5ab29d65784e9b30b618a18a31e323932b67 |
| collect-icon-ci-v1.py | ea88875d9cefefac21847ba5911bcbd76bba9ff4148cf1b47b3c81558a8a71df |
| seal-icon-ci-v1.py | bc38c0f0d087520d3c5769a476d34ce70ca3f75ea8946b2bf892cafdc2301dea |
| query-ci-v2.py | 77a39db472b8b0b17f36d3fd090515164e0f0b09aeffe55344140c43b3ef4ab0 |
| ci-query-v2/observation.json | dd74d6f7ac39bd321a2bb776cc8311ce5a0c2ca5cd782ebf3e73d5b8af5ed303 |
| seal-native-results-v1.py | ebb8884a1e3415d03f005fdfa95f5fe8f78d3188a8495e6dea5baf95c3749669 |
| prepare-query-v2.py | a1909252bd1ec2264d4f94ad2033a68750ed98846d07e159017f2099ac9c7ff0 |
| prepare-query-v3.py | d88de243b475ce07ff2e5dd1e5489c9ff657cda57eaf274c2e5fb62b77e9cbde |
| query-ci-v3.py | 7c14be00cdf2e67f3668f409b2e78cc1008ad431b9d32dbd2386b1e35adf8e98 |
| ci-query-v3/observation.json | 60538ee57334bf5a3742ca56e89707566d9fea8b4df27c00de1dd72ab21131c2 |
| inspect-original-failure-v1.py | c5b34efedef1b887cd3b18d60b78670201fa247498caf1adcc1d0895a0a0a940 |
| original-ci-failure-v1/observations.json | d44370da9bedc91d95fabc01ae9e009d0bba11e73df573f165dc737250de1f4b |
| original-ci-failure-v1/original-run-logs-stdout | f73f3537d764ef744081b7ed0edfdf308a226f095972f35cac9ae26ec2d097b9 |
| prepare-original-native-v2.py | a26b1abce3b3dcd911d8530465f76bbca5bc9bdc8083c26fe86f1b4291ca86ea |
| collect-original-icon-ci-v2.py | 00715de85b52f38af71e3537b6f2e54bd58cfe67742a3ea0e08fd0bde33f0d32 |
| seal-original-icon-ci-v2.py | 3335b8ebdabb0e40f5d6a484d1631d8776b4cd6b86f204ad7dd3ab6174c95419 |
| prepare-original-native-v3.py | 4dbddb7380f458b4331fa5dfb787da5752b5f25291d5ea367253981d5feed7a3 |
| collect-original-icon-ci-v3.py | 9a2b0b89473315ebc2cee3857c47ce98a2f5854724f7e977583df238bbe5061f |
| seal-original-icon-ci-v3.py | f714a37018f9ca1ceaf6bdf6746fd1f5264b35bfb2aff3c7b3ce69af2a5f4bbe |
| seal-original-native-results-v3.py | 35af90e9d2b67ae4872d847f6f88d89c1b90e3ac2bb5f5dc6fd8b25188579c3c |
| seal-original-native-results-v4.py | 5e5cbcad549fa1cd738a10c979a0abc1cc6c4a0e7bc3fc1655ae91cac4aca6eb |
| original-native-inventory-guard-v2.json | 933e769aff1f5588a10f8741a72a77c3cd980c415cf87603344786870cd1da8c |
| original-native-seal-command-v4.json | e954e1fe1bf0bcb06acdda83a27c16e661d7aaef7e88270ae1fa78226f75a66b |

Private `FileCatReleaseEvidence/ci-37736670942-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 79bf9783c1d75ad4d9ac1c989c6821980bd7cdb889018dc2866a358c79141cd0 |
| independent-restore-ci-v1.json | 6d7bb1ea71a8f23c176ecc331b73fb4afd893eb6ed75bae7a41ed581a5a1ee9d |
| run-native-stdout | 2eaf3fb32a60f5fe648a5d4fdd8156d621159efcdb4f5f07f04da977dc96a304 |
| jobs-native-stdout | d76d6fc52d0438d09840a8e29d4801a9173cf11b0f94c02393619a04cd602131 |
| artifacts-stdout | 5b9ef9dc62955d97d49ee3138ae183cb3fb286ebb9779ed3ca03099661fc476b |
| independent-original-icon-ci-audit-v3.json | 9160885d257d3eb2a82e324f04e6f8d9959dafe2e66a7a7b7fa8405b3df8a3a3 |
