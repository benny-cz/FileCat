# E-I148 — legacy RAR secondary-volume discovery

Classification: preliminary committed remediation at `9da573893a3ec9dffe7ffefe87658941c21c7d79`;
controlled/full host, clean three-platform native and exact-source CI qualification pass.

Actual clean 6cf17e5 component APIs fail all six secondary entry points of a complete,
unchanged seven-volume legacy RAR fixture. Its primary `.rar` lists three members and
returns the independently verified 100,926 bytes correctly. The old discovery code
starts with the selected secondary path, omits the primary, then adds that secondary
again while walking a prefix. This is I148, Medium functional correctness, Must fix V13.
Explicit forced-format opening is tested; automatic discovery of `.r00` names has
not been added or claimed.

The correction discovers the primary once, orders the available legacy siblings by
their extension numbers and retains later volumes after a gap with an explicit
missing-volume warning. The modern numbered-volume branch is preserved. All seven
complete entry points verify member identities, sizes and exact hashes in reverse
and forward order; three missing middle/last/primary controls require a warning or
refusal and unchanged source bytes. Six pure UDF revision controls supply positives.
With the identical final test DLL and only FileCat.Archives.dll changed, the original
reader fails six legacy cases and passes ten controls; the correction passes all 16.
Every payload pin remains unchanged and the exact test process is waited to exit.

The first controlled observer stopped after the correct 16-case baseline and payload
checks because its strict temp test counted the known empty `filecat-tests` container.
Original XML/TRX and that failure are retained. A fresh observer permits only
nonrecursive removal of this verified empty fixture container; no test file is deleted
or exempted. Both fresh before/after temp directories are then empty. Affected host
63/0 skips and full Core host 804/56 declared skips/860 unique cases independently
verify, including all 16 new cases. Native UI and candidate qualification are pending.

Fixture provenance: unchanged SharpCompress MIT test files from tag 0.50.4, annotated
tag object `a94a325a06492ea4ffa282ed88f9052025bf4c2d`, commit
`c083c6efd843a844b0c8f7878787360e815be781`. Every raw download matches the pinned Git
blob and retained SHA-256. Independent 7-Zip 24.01 primary extraction establishes all
member hashes. Its secondary extraction can omit earlier volumes and fail; the
original failed oracle assumption is retained, and no secondary-tool capability is
claimed. The final oracle uses the complete primary extraction, not FileCat output.
This does not resolve I14's decoder distribution/legal decision.

Private evidence root: the authorized second workspace's
`FileCatReleaseEvidence/archive-variants-20261006`.

| Retained item | SHA-256 |
|---|---|
| `retrieval-proof-v1.json` | `0bcb651567cb89285cd3e627078299fcfce425a905b1b937602b02099e57400b` |
| `native-final-v1/verified.json` | `dac3154ab176a4c5a33a8bc1446aaaf22b7ff1b00507abb120c798274021b3e9` |
| `udf-corpus-v2/independent-proof.json` | `8162c4304671fa1417cd6304ee0a408ebbc411d1f78efe61751667bd7669306b` |
| `udf-corpus-v2/fixture-oracle.json` | `e077c1aba180b0170b4b110e5ae8f4ee7cad6df48cbc00192122ab61b53703c2` |
| `udf-corpus-v2/observations.json` | `9ff9675738708f3fb95e9485354d6838fb6aedaf2089e80be8583105ca0760b5` |
| `legacy-upstream-v1/provenance.json` | `43d74dd0618a281516e0d99ff126b0d8bd429e94bad0d6beffdd3c5d0aa00698` |
| `legacy-corpus-v2/fixture-oracle.json` | `0a0445b2469ee78126edf009e653c2d562a00f752793b056f55bfc48bfdc2b3c` |
| `legacy-corpus-v2/observations.json` | `dc825f3a2d66006dd330bf2fafb82851e91ac6aa5af578f26937e420cbe62cae` |
| `controlled-baseline-v2/receipt.json` | `ca343db517425a51f8a9c829075319c0cb7a04de61b01e27e5b2eb2e0cb23abb` |
| `controlled-corrected-v2/receipt.json` | `1891e3c8a610b578ed684e1b6ee076d8f23a4063d15573ba401bbadc9164c3fa` |
| `controlled-comparison-v2.json` | `cf65ae4d75cb39d7d36e770b432aef6b4f28f404d4b37936e761030e673ca8f4` |
| `controlled-observer-failure-v1.json` | `4e8d8c18f9a304173654b17dec162f09fa4c88ea9d11333943d9c1270debe4f6` |
| `working-v2/results.trx` | `ddad2990e0deff574755861980f04c733728f4ac3e4eb075822099ee7dd207f5` |
| `full-working-v1/results.trx` | `f0682fc3dda0611738c79f19481109a924398e5ace876c5a1fcc214510ac5748` |

Clean committed three-platform publication/native controls and exact-source CI are
next. Broader archive variants, drawn navigation/Find/extraction, physical/reference
hardware and exact-candidate evidence remain. Mac awake v3 is active, restoration
due; both VMs remain running and G: is untouched/HOLD. Owner interactions remain
queued until 2026-10-06 08:40 CEST. No candidate, no human GO; overall NO-GO.

## Clean native and CI qualification

All 882 raw Git source blobs/modes at 9da5738 verify before clean self-contained
publication. Windows, Mac UID 501 and Ubuntu UID 1000 each pass all 63 affected
archive controls, with no skips; all sixteen new legacy/UDF controls pass. All
331 Windows/330 Mac/331 Ubuntu payload pins, owned temp cleanup and test-process
absence checkpoints independently verify. Windows observes all owned payload
processes absent. This is component validation; no drawn desktop is observed.

[CI 37393570643 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37393570643)
passes all four required lanes. Four server ZIP digests/six full per-case inventories
verify: Windows Core 803/57 declared skips/860 cases, Platform 166/33, Remote 88/28,
App 371/17/388; Mac App 340/48/388 and Ubuntu App 338/50/388. All 63 Windows affected
archive case names match exactly and pass, including all 16 new cases. All 388 App
names match exactly on each TRX lane. Of 860 Core names, 854 match exactly; six PE
cross-check inputs embed the native Windows system-path casing or native test-assembly
location. Their exact names remain retained, without normalization; source PeFiles()
declares these actual native inputs. This is an explicit inventory difference, not a
missing case. ARM64 Core 803/57/860 and App 371/17/388/drawing/installer pass by logs;
no ARM64 or Unix Core per-case TRX/physical qualification is inferred.

The first collector rejects the six machine-specific path names. All original raw
downloads remain, along with the original strict comparison. Successor read-only
observer setup failures (wrong execution root, eight source-declared PE cases versus
six differing paths, and a mutated intermediate comparison) remain in tool outputs
and retained scripts. The final independent audit verifies every server byte and
every complete inventory, with the six exact native differences stated explicitly.
No product case fails or is rerun by this reconciliation.

The follow-up private forced-format probe passes complete legacy member reads, but
its signature-based format check still fails. This separately exposes I149: the
signature detector compares seven bytes to a six-byte prefix. I148's entry ordering
is qualified; signature-based opening was not supplied by the earlier forced-format
controls. [E-I149](E-I149-rar-signature-detection.md) records the separate correction.
No broad V13/candidate closure follows from I148. Overall NO-GO.

| Retained item under archive-variants-20261006 | SHA-256 |
|---|---|
| `clean-v1/source.zip` | `e1b98ae1726c61eac885cb10f0b8a1889d7dcec748fb6446e4e6a93a27b6099a` |
| `clean-v1/producer.json` | `6ec09469fb974a0905bcedebe7bebc64dfa2ace9075000caff5c2f157f4bbf81` |
| `clean-v1/windows-executed/independent-guest-v1.json` | `2a165477b40b12d4692ec88ffc925ea7a860907f857408918a14bce0d6576c1f` |
| `clean-v1/mac-executed/independent-native-v1.json` | `88736e9252140c9859634c8b81d394d104c59e948664dab9a9d73d43d64d1698` |
| `clean-v1/linux-executed/independent-guest-v1.json` | `2487725e94e67dd63dd96d18fd06fd66ba88a5dcc95e06c13ea33edbdbc9c54b` |
| `signature-baseline-v2/results.trx` | `176ded5534f471096c1035157287e804b455b570df0dc3d8338b0cbce76a9f62` |
| `signature-working-v1/results.trx` | `b1072394c11f8876485f5f9e459574f4b8b3d928f5e7263364caacf0b6d8c5b3` |
| `signature-full-v1/results.trx` | `c4e0664b1f9ffa85e1b83a7f2cd1f70216e84ca304e8470ca4d51e7177a989b6` |
| `controlled-baseline-signature-v1/receipt.json` | `3dc78d7ceb3ed559ac3767ec9fca34bda951a1722a97c831fb0b05b0dbb88dae` |
| `controlled-corrected-signature-v1/receipt.json` | `3de54f6ace503e454d67295fe9772ad9f7da0a28f4e3697fbc822e41b67371ac` |
| `signature-controlled-comparison-v1.json` | `72631dd30c57b0eafd3c67e75c86d732c20ae932ef3bf6023e82c355e8514980` |
| `signature-test-compile-failure-v1.json` | `9e1bc2ffb2bbd6e64bb4101b767fa15f3cddadc2dddb62dfefa24755a0e2c143` |
| `../ci-37393570643-attempt1/independent-ci.json` | `c9ac4eb7869b6ed935b8f2859b3bf348674bfabf4f66d73e76ab70f848a2ba14` |
