# E-CI — original all-selected-source admission, retained performance failure

Original push **run 38100449046, attempt 1, source 7a596160220899114a7715639f8156a384dfe8c4** fails Windows x64; Windows ARM64, Ubuntu 24.04 and macOS 26 pass. **32440 records: 31410 passes, 1029 explicit skips,one failure.** All **192 added recovery admission controls pass** without new skips. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38100449046).

The single failure is `DeviceReadTests.Reading_through_the_helper_costs_a_small_factor`: five pipe scans/reads take 3.122448 seconds against 0.0023167 seconds direct, exceeding direct × 20 + 2 seconds. The original cause is unproven. [I338](E-I338-device-pipe-fixture-scheduling.md) independently reproduces/corrects a blocking test-server scheduling dependency; its private controls do not reclassify this original result or establish its cause.

Independent inspection verifies **26** API digest-bound archives, fourteen TRX inventories, 1541 canonical raw Git blobs, 92 locked restore graphs and four clean SDK 10.0.401 build receipts. The failure removes one expected downstream artifact; it is not fabricated. All 1029 exact predecessor skips remain. Every other32247 predecessor outcome/message remains; the one predecessor performance pass becomes this recorded failure. The original I335 green result keeps its own identity.

This is an **adverse hosted batch**, separately from [targeted exact I336](E-I336-native-qualification.md), the [complete native Core failure](E-I336-full-native-core.md), later I337/I338 results and candidate scopes. No workflow rerun, threshold relaxation, historical failure reclassification or publication is claimed. Physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, original failures/skips, whole bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i336-ci-20261011-v1/independent-adverse-ci-final-v1.json` | `4d2be09566f29b8577e4dde96beeda4d8c0215bfca9e65afa4368464d653d091` |
