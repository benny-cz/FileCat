# E-I148 — legacy RAR secondary-volume discovery

Classification: preliminary committed remediation at `9da573893a3ec9dffe7ffefe87658941c21c7d79`;
controlled and full host qualification passes. Clean native and exact-source CI pending.

Actual clean 6cf17e5 component APIs fail all six secondary entry points of a complete,
unchanged seven-volume legacy RAR fixture. Its primary `.rar` lists three members and
returns the independently verified 100,926 bytes correctly. The old discovery code
starts with the selected secondary path, omits the primary, then adds that secondary
again while walking a prefix. This is I148, Medium functional correctness, Must fix V13.
Explicit signature/format opening is tested; automatic discovery of `.r00` names has
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
