# I248 — Large tool selections preserve each resolved argument position

`ToolLauncher.Plan` previously split commands over the existing 32,767-character budget by removing `{files}` and appending each file batch at the end. That changed the argument contract when file operands belonged before a later marker or option. The fallback also repeated substitution on raw tokens, so a long mixed command could send literal `{file}` and `{listfile}` arguments after already creating its real list file. Repeated `{files}` positions collapsed, and unsplittable fixed or single-file commands could return an oversized plan or silently produce no launch.

The correction resolves the original sequence once, keeps each `{files}` position, and rebuilds every bounded batch in that sequence. It refuses a command whose fixed arguments or one selected file cannot fit the existing budget. Short-command quoting, existing warning behavior, executable selection and process-launch policy remain. Severity is Low–Medium for incorrect external-tool arguments and missing or unusable invocations; these controls establish no ordinary user tool or security-boundary incidence.

## Executed original, private and committed controls

The actual completed f862 Core component has 82 pinned members. Four owned native Windows .NET apphost bodies contain two healthy short controls and two concrete large-selection failures. The ordered long command sends file operands beyond its `END` marker. The mixed long command also leaves literal focused-file and list-file tokens in the actual child arguments. Each long selection has 240 owned files and three actual child launches; each two-file short selection has one. Actual argument vectors, full input bytes and hashes, list-file bytes, known child identities and exits are retained.

A fresh byte-equal observer orders comparison by actual launch invocation rather than PID filename. The identical four-body original/private comparison retains two failures and two healthy positives on original f862, then passes all four on the declared one-file private correction. Initial native observations and their actual observer remain historical evidence. The private producer is not relabelled as a committed payload.

Eight identical portable durable bodies reproduce six failures and two healthy passes, then all eight pass on the private correction. They cover short resolved focus/list arguments, original file regions, repeated file regions, ordinary trailing files and three unsplittable-command refusals. Every case records the actual command lengths, typed error, returned plan count, full owned input hash, and list-file hash when present. The three adverse fixed controls return `ToolLaunchException` before returning a plan. These are plan-only controls and launch no native tool. The same fixed payload passes 21 unchanged affected existing tool cases plus all eight new cases, retaining prior names, outcomes and exact skip messages.

The combined committed producer is `09bccc7103949be351ab6b2bf250e35b3d0280e0`. Its immediate parent is the runtime f862 producer and its document base remains 13825900. A separate four-body native repeat uses the exact completed 09bc canonical Core DLL/PDB with the same qualified observer and no product overlay or rebuild. All 82 canonical inputs are rehashed. All four bodies pass with actual one/three/one/three child launches, resolved focused/list paths, correct ordered file regions, unchanged 128-byte file content and known child exits. The observer's original-source field denotes its historical f862 baseline; the current proof pins the actual loaded 09bc Core DLL.

## Canonical and original hosted qualification

The exact 09bc Windows canonical suites pass Core 2522/64 skips, Remote 2020/156 and App 1217/25. The independent seal rechecks 1256 original Git blobs, actual 82/41/141 payload members and 481 affected observations, including the eight plan-only controls. All predecessor outcomes and exact skip messages remain, with one declared passing PE display relocation from the owned E helper component root to the current E paired root. The unchanged fixture obtains its actual `Assembly.Location`; both truncated displays and both Core DLL pins remain. This is a source inference about location, not an unavailable full argument dump or a claim that every local raw name stayed byte-equal. The direct LF Git export and hosted native archive CRLF export retain their separate identities.

Original run 37877401877 attempt 1 at 09bc succeeds in all four required lanes. All 32 actual Tool executions pass with command-budget, resolved-argument, original-input and constructed list-file byte observations. They remain plan-only hosted controls; the separate Windows native repeat supplies child execution evidence. All 128 paired Sharp controls pass with their owned library-fault/native-file qualification. Every one of the 24,328 immediate f862 predecessor identities, outcomes and exact skip messages remains, deriving 24,488 current records across 14 original TRX inventories with no predecessor transition.

The reader verifies 21 server-digest archives and their actual expanded members, four actual toolchains, 92 locked graphs, 25 native API receipt sets and the selected eight public ARM fixture packages. Earlier paged/load/job/cleanup, SMB/TAR, Git, rename and native-overflow records retain their actual producer and precise platform skips. Earlier finite transfer or notice byte oracles are not replayed from counters. Neither complete suite counts nor plan-only records establish a desktop frame, arbitrary tool behavior, physical-source qualification, broad containment or a release candidate.

## Preserved refusals and owned restoration

The first component preparation refuses duplicate satellite basenames before any launch; fresh preparation preserves full relative paths. Its tool-only refusal remains explicitly qualified. The first observer build fails the dependency-lock check before compilation or native launch. Fresh preparation uses an owned dependency-free observer project with explicit empty net10 lock and private props/targets; repository dependency policy is unchanged. Initial PID-filename receipt ordering is preserved and corrected before the identical paired bodies. No product rerun repairs these observer refusals.

Private restoration archives and independently rehashes all 756 owned temporary files before literal-path removal. Eight exact roots are gone and all 32 recorded PIDs are absent. The current committed native repeat separately archives its 250 fixture files, removes the exact fixture and observes all nine known PIDs absent. Executed historical paths resolve through pinned archive inventories; deleted files are not claimed to remain on disk. No new residual compiler locks, process termination or global machine settings result from these controls.

The later current canonical cleanup separately archives and removes all 11 owned files; k248 is gone and its three command PIDs are absent. Nine earlier f862 compiler/analyzer locks remain individually qualified in the earlier receipt. No VM, Mac, USB, account, security-policy or global temporary-environment change is used. Broader I06/I16/I17, the physical-source HOLD, exact candidate qualification and explicit human stable 1.0.0 GO remain open.

## Exact selected provenance

The 39 selected paths below resolve from `C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/tool-batch-followup-v1`, with explicit siblings and absolute E paths. Actual source/component/native archives use the separate `E:/FileCat/artifacts/release-evidence/tool-batch-followup-v1` root. Original hosted source and expanded assets use `E:/FileCat/artifacts/release-evidence/sharp-tool248-ci-v1`.

| File | SHA256 | Qualification |
| --- | --- | --- |
| `Program.cs` | `9c1d8e4fad58394691205d7a5965a540171793fbac9ae741261a36b667aab248` | Initial native observer; historical filename-order qualification |
| `Program-v2.cs` | `340ac3bb81852b9768c40fa95e0acf7174a2d7ad4f828f3afec144eb80ff160b` | Same baseline/fixed/current native observer with actual invocation ordering |
| `preparation-v1-refusal-qualification.json` | `afcdf3c4d5aa8bc98609e29a69e48588f094782df95a6d9a0cce9b0b5344135c` | Original satellite basename-copy refusal before launch |
| `build-v2-command.json` | `25f024ab4ecf5e3e5221dbc35d8819c72906dd56a0ecc123d177827a9243a169` | Original missing-lock build refusal before native launch |
| `native-preparation-v3.json` | `7d8cc362f29563c07c352afd57711398e0e00fce4366c9e72a56f365f743766f` | Fresh private dependency-free observer and explicit empty lock |
| `native-execution-v3.json` | `b2b7790889ada19cc8d389b1f2fb8d3aecdaea53c0d97c0a5bd19f75ba19788a` | Initial original f862 four native bodies: two failures/two healthy |
| `identical-native-pair-v1.json` | `6ebd41ca60df27c41fab251ca9b4f3114ab9f71d3c3ff911326042f415dfbe2f` | Same native bodies: original two failures/two healthy to private four healthy |
| `private-production-diff-v1.txt` | `0f62a159d5a441265e736715246644ef0e33088196bb0a323d57593f1dbfdecf` | Ordered resolved-token batching and unsplittable-command refusal |
| `E:/FileCat/artifacts/release-evidence/tool-batch-followup-v1/source-fixed-tests-v1/src/FileCat.Core/Tools/ToolLauncher.cs` | `2aba1c5255c9cd3750168d3290ab35ee63bab1002cf37cdc3c18b55eb15846a9` | Exact approved private LF production input |
| `ToolBatchArgumentTests.cs` | `e96f21e9b66f6a68b07e21c73e6732c6a87f83d06644704430450bf25b54b2c3` | Exact eight portable plan-only bodies |
| `baseline-durable-v1/command.json` | `c8cddf1ebee0de43fdb7cbf40ef2b8552c425b11cf1ba3c5d8fe0f3c0b23504c` | Same-input original six failures/two healthy passes |
| `baseline-durable-v1/results/tool-batch.trx` | `ca7b6537bb10ac4ec25c8f9a04458ae201ff2e29493c83a91d5cbc61e763bc1c` | Eight original raw budget/error/list-byte observations |
| `fixed-durable-v1/command.json` | `26053df1c5b34826b8022391af53918f3198f478915b190ee57828d4b9bcf3ea` | Same-input private eight passes |
| `fixed-durable-v1/results/tool-batch.trx` | `d61ce0fbb6b3910b0abbcd518d044245a35867fce413e4f5a45f84077145f774` | Eight corrected raw budget/error/list-byte observations |
| `private-regressions-v1/command.json` | `c3f5c261a5f6d76707870688b1f29f9d81fd8dd42c2e87f8159d4481a4e44243` | Same fixed payload: 21 prior plus eight new passes |
| `private-regressions-v1/results/tools.trx` | `d71477529a16cb62cf01be117da32b000d49b647837f181e0df5bbafe0b98d07` | Affected 29-pass raw inventory |
| `independent-private-tool-controls-v1.json` | `b225244073e4f8deb6bed4ff86835a6870c2007edd9f176e7379c2b32330bf1c` | 16 durable/eight paired native actual byte/process records |
| `independent-owned-tool-final-v1.json` | `ba62e1073404024ef9f0a69c053399257540c2cf2c71b63f17ff6f5b1643d38b` | 3190 retained files and archived historical pins/restoration |
| `owned-tool-restoration-plan-v1.json` | `c670d19cfc9bdaa590ed07532868e426d7ab810c5b111989a6648ba0beccac5a` | Eight resolved roots, 756 original pins and 32 PIDs |
| `owned-tool-restoration-native-v1.json` | `141a38c0a8a49efb7bdb82bc0e661d082a5fae0f75421657d1ccbb3541bdbf6c` | 756 removed, eight roots gone and 32 PIDs absent |
| `restoration-native-v1-command.json` | `d6641a6b79082d7715605dba7cc442fc6003cb80c847ed3fb0da2a85f4bf81d2` | Closed native literal-path restoration command |
| `E:/FileCat/artifacts/release-evidence/tool-batch-followup-v1/owned-temporary-v1.zip` | `820dee9fec586c6412c29bd0e976fac2862d70736956401796b8cf0f2adef7e5` | All 756 removed private files retained as exact archive members |
| `committed-native-tool-v1.json` | `9c72d7b2479746c22b839b773215d4f2d7907b5bd35bd6b985873c1d40151d28` | Exact completed 09bc Core and four actual native argument controls |
| `independent-committed-native-tool-final-v1.json` | `2bcba621b994fac8aa80eb04c52fcb6e027c2b44725ea00831f899b4daa35687` | Four committed native records, 82 inputs and restored 250-file fixture |
| `owned-committed-native-restoration-plan-v1.json` | `d585b6cdcef4c2e458a6c9e96a441820f0e17a5627605c97164f9accb8b16335` | Current native fixture member inventory and nine known PIDs |
| `owned-committed-native-restoration-native-v1.json` | `2464b6ce5d2b80ea61b906d1a045029a46b398bdf19aee5e2d97e1304b90fe05` | 250 removed, exact fixture gone and nine PIDs absent |
| `committed-restoration-native-v1-command.json` | `415acd860e6109416f7314d869463fb1a73040a61c7c8ce37cdcbdf60be6ffbe` | Closed current native restoration command |
| `E:/FileCat/artifacts/release-evidence/tool-batch-followup-v1/committed-native-owned-temporary-v1.zip` | `518825f5a982ebee5121b4b08b07ed21c724d5a6883ee49ee8414e4d7eb4acc3` | All 250 removed current native files retained as exact archive members |
| `../paired-retirement248-v1/four-runtime-paths-v1.json` | `96fdadafed5250936cb5ba8806f6c08bc98bae938ae09f3f12a403727357434b` | Four approved Sharp/Tool source and test inputs |
| `../paired-retirement248-v1/paired-retirement-runtime-main-push-v1.json` | `01a8a49d418a284b68304b19cb1c3205c0bb7863affb90bad6d9291d4ac53b7e` | Actual runtime-parent main push/remote provenance |
| `../paired-retirement248-v1/independent-paired-retirement-canonical-v1.json` | `e3e9d1c62a89b1d2166467d5acecac98351ba945ca3ea3520a99de62f86f09a9` | 1256 blobs, three suites, 481 records and one passing PE display relocation |
| `../paired-retirement248-v1/owned-paired-retirement-canonical-restoration-v1.json` | `d396605469ee19ab121412c46948bf4e95a092c998d878d385f79a87b94c6e60` | 11 archived/removed; k248 gone and three PIDs absent |
| `../sharp-tool248-ci-v1/preparation-v1.json` | `23a707f0a9c9f88da6b1a9326f74539739a56ed0623d49a55eb53cd1075a1543` | Native archive CRLF, actual E roots and runtime/document parents |
| `../sharp-tool248-ci-v1/collector-v1-command.json` | `7165768bed5f7102d46baa06ee24c7d0630ddea1a030d1615a9c4f819008d0be` | Closed original artifact collector command |
| `../sharp-tool248-ci-v1/seal-sharp-tool-ci-v1.py` | `be43dccd8520caf445f7b349002b0388dc5348dafd9c41e745b32d70057a01d0` | Independent original four-lane semantic reader |
| `../sharp-tool248-ci-v1/closed-seal-observers-v1/command.json` | `cf085fb9c12c58d208a8b51d05d31970564eb3bff820d8610957cf93d8d9bc13` | Closed observer streams excluded from their own inputs |
| `E:/FileCat/artifacts/release-evidence/sharp-tool248-ci-v1/assets-attempt1-v1/independent-assets-ci.json` | `891a2b6843db51f1b9a384af5ec1138fa7e78857b14c475820b306a139ca55d2` | 21 original server-digest archives, expanded members, 14 TRX/four toolchains |
| `E:/FileCat/artifacts/release-evidence/sharp-tool248-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json` | `9b43616c4145f355d4a02ca6e2a08d29f7704531c699b87153d85a788b89cfde` | 92 original locked graphs/23 projects per builder |
| `../sharp-tool248-ci-v1/independent-sharp-tool-ci-final-v1.json` | `8f6f88c1a00ca730fc15616950ebbf646b0449692508eda4fabeba3ce5230057` | 128 Sharp/32 plan-only Tool passes; 24328 retained/24488 current records |
