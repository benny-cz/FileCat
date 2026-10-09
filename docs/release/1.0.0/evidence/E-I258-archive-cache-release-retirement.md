# E-I258 — retire every matching archive index after close failures

2026-10-09. Preliminary I06/V07/V23 remediation at **e686431568c851b0ee907f760e9ebaec294acb6e**, combined with I259. The original runtime is **5cd9a6a1de088ad4f51b12e3504b229678eeec12**. No candidate or stable GO.

ArchiveProvider.Release previously stopped at the first index close exception. Cache-miss invalidation used a separate loop with the same stop behavior. A cache containing three matching real indexes could therefore retain later indexes and their actual source handles. The correction removes and attempts every matching entry, retains the first exception object/stack, logs secondary types, and rethrows only after retirement finishes. Cache-miss invalidation uses the same Release path. Cache-hit and source-identity/race policies remain outside this correction.

The original ten compiled Windows public-Release controls construct actual Tar indexes and read four complete 4096-byte positives before attaching controlled errors after real source closure. Six first/middle-error cases leave later sources open; four healthy/last-error cases retire all three. These are two deliberately added real indexes and controlled stale/overlapping keys, not an observed concurrent cache race or a natural native-library failure. The recorded key vector is a pre-operation snapshot; its inferred iteration order is not an independently captured per-Dispose timeline.

Twenty-two identical durable bodies cover public Release and cache-miss OpenContent, healthy/IO/denied/disposed errors at three positions, and two close failures. They change **14 failures/eight healthy passes to 22 passes**. The fixed bodies retire all three targets, preserve the unrelated source, preserve the first exception, avoid duplicate retirement, and restore complete retry reads where applicable. The actual member pattern `(n*31+13)%256` has SHA-256 `f8d749036d5f1bc689a03838e7bfec01c538b386f787e832b3c799169b1d0f07`. All ten unchanged compiled native bodies also pass on the private correction.

All **351 unchanged compiled predecessor cases** selected by Archive/Tar/Disc/ProgressiveContent class-name fragments retain their 349 passes/two exact skips. The selector also includes matching discovery classes; this is not a claim that all 351 are archive-decoder tests. All 107 affected finite raw observations are independently rechecked: Tar 26, format 25, index listing 44, spool eviction eight, disc retirement four. The private run overlays only the declared Archive DLL/PDB; it is not a whole committed payload. Independent source/test review is complete.

The original private reader assumes a second operation succeeds after two original close failures and refuses the actual retained second error. Fresh v2 reads the unchanged closed rows and preserves both failures without execution replay. A separate review reader initially expects the wrong TRX field and retains that schema refusal. Private restoration rechecks all **4392 inputs** (4372 live/20 archived), archives all 45 owned files, removes 43, leaves two individually archived compiler locks under ar258n1, removes six/seven exact roots and observes all six recorded command IDs absent. The 40 original/private native Tar fixtures are first independently byte-checked at that restoration; no initial filesystem pin is invented.

One four-path main commit applies both approved fixes. The immutable Git-blob export has **1280** source members. Full Core **2687 passed/64 skipped**, Remote **2020/156**, App **1217/25** pass. All **646 affected raw observations** verify, retaining all 618 preceding finite controls and adding these 22 plus I259's six. Every immediate 5cd local identity/outcome/exact skip remains, with one declared passing PE assembly-location display relocation. All other identities are exact; earlier producer qualifications remain. The first current reader expects one older-2b relocation, but those actual truncated archive-prefix displays are identical. Its assertion/diagnostic remain; fresh v2 keeps older-2b identities exact and verifies the one actual immediate-5cd relocation from the unchanged closed evidence, with full untruncated arguments unavailable.

Ten fresh native controls use all **82 exact current payload files plus five unchanged compiled observer files**, without product/observer rebuilds or overlays. Actual loaded Archive hashes, complete positive bytes, target retirement, unrelated-source survival, first-error retention and repeated-release behavior verify. Twenty owned Tar fixtures are independently read and archived with exact byte/hash provenance before removal.

Original [CI 37897376332 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37897376332) passes all four required lanes. Its **25148 records** retain all 25036 preceding identities/outcomes/exact skip messages. I258 gives **88 passes** and I259 **24 passes**, including both Unix lanes; no new skips. Original artifact digests/members, full TRX, graphs, APIs and toolchains remain pinned. No historical failures are relabelled or tests replayed to repair evidence readers.

Shared current restoration archives/rechecks **31 files**, removes **31**, and retains **0 individually pinned files** across three exact owned roots. Earlier locks and unavailable inventories remain qualified; there is no global restoration claim. Leased-member/concurrent identity policy, other formats/providers/native workflows and exact candidate qualification remain. Twenty broader unresolved scopes, all 24 final-candidate campaigns, the physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to `C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence` unless explicit. Receipts retain all nested original input/payload/stream/archived-fixture pins.

| File | SHA256 |
|---|---|
| `archive-release-followup-v1/Program-v1.cs` | `6ecd90a27d2134dda502f0698e5c011825cd6b2b9dffb3ea5fd9d2d8ed966274` |
| `archive-release-followup-v1/original-public-release-v1.json` | `cb8297a413d316774c893ae8f0d5346fca9cdffcb0db3d8b70dcbc54bfd2a997` |
| `archive-release-followup-v1/private-cache-release-inputs-v1.json` | `aeb8c228b0355f3a6f41571a0381b603e7160ad115ccc177ad3aa81939bae4a9` |
| `archive-release-followup-v1/ArchiveCacheReleaseFailureTests.cs` | `32912acca5e73a857bc142034438771c23002c980855552105d252889cecf6e9` |
| `archive-release-followup-v1/same-cache-release-tests-v1.json` | `378e1c013723dfaa9be8d9dd5265d09f9f7b7f60e2ba591b82ef88f78ab5f3d7` |
| `archive-release-followup-v1/fixed-native-and-prior-archive-controls-v1.json` | `abc0765b3d94682099ef4c43f992dbc2ec7877babb9cf2da4218a7f6e9e1964e` |
| `archive-release-followup-v1/original-private-multiple-retry-reader-refusal-v1.json` | `d3ac2f870509944e5e590492e7d6df8d8db77fd6fffb287f25b031dc2e3c90c8` |
| `archive-release-followup-v1/seal-private-cache-release-controls-v2.py` | `1694335a5e2ef92c2032c74a94b2c822d0c5891dab671bee93c2190d42eae478` |
| `archive-release-followup-v1/independent-private-cache-release-controls-v1.json` | `4e4291eaa28f6d2d508b689a2378a4ad34f839cf844ec7049c95c92b0df99b8a` |
| `archive-release-followup-v1/owned-cache-release-private-restoration-v1.json` | `e8d212c83d48eb0b5d57eab08c634d8669b3671031fa5a31bb0880b9ff5409f0` |
| `archive-release-followup-v1/independent-owned-cache-release-final-v1.json` | `c4addf05475e71d1467013819123067c77b8a64efe9f828e9835ae525907eb56` |
| `archive-release-followup-v1/root-cache-release-ready-inputs-v1.json` | `e1e22cddc1ada949556f63a77f89a3b4f252c45303e79f9ac7d464e396e8ea51` |
| `archive-release-followup-v1/next-archive-document-baseline-v1.json` | `b630ca0c20313b0232eda9258a7095eed3e1544b566ed0b65534b289d5bcea69` |
| `archive-release-followup-v1/applied-archive-drag-batch-v2.json` | `401400428792f383a0e896521ca80740c1827f9761249393721c0590d460a2a0` |
| `archive-release-followup-v1/archive-drag-runtime-main-push-v2.json` | `d10a053613c4d7114327c8c7168fc161efff62c12fba207531ac72e5042fa0a0` |
| `archive-release-followup-v1/canonical-v1/command.json` | `c2ed7a640f201b2640a79b3b275c5750d4f64e287a2db471efddf864e7647a8c` |
| `archive-release-followup-v1/independent-committed-cache-release-controls-v1.json` | `3535949e8c65955f8959fea7f7beeb355349756c3102d381adbfbb6e8237ad53` |
| `archive-release-followup-v1/independent-prior-command-budgets-on-combined-v1.json` | `77fae36b790fb819ea732daa167cb4d73ea774693472aea1ca204069f55e8a1e` |
| `archive-release-followup-v1/independent-all-new-since2b-controls-v1.json` | `6fc7893707f2ef7dd83394633e107f872764551521ed97e6312df2b8a794c08a` |
| `archive-release-followup-v1/independent-archive-drag-canonical-v1.json` | `e77e832754fe6883a22fbc8e802e8992dbb592c1da33282235cb1cada27e14db` |
| `archive-release-followup-v1/original-canonical-display-counter-refusal-v1.json` | `a414c4e635bef0634ad5a78a149fbc51e1b379ecd1c230a309378463a27dd2bf` |
| `archive-release-followup-v1/seal-archive-drag-canonical-v2.py` | `22f6d962a9f53851c62dff6b8ed5c39b55185085067391a6b4495b661773b84e` |
| `archive-release-followup-v1/independent-committed-cache-release-native-v1.json` | `fb2974e94bbff7e39332562f557dc6f43dcf57774bd112da2a189387d9715145` |
| `archive-release-followup-v1/committed-release-native-v1-command.json` | `5c278a8156c12e578151bc1fc5b7de6bce648fb2f32f45d85c2683d387da0c4d` |
| `archive-release-followup-v1/owned-archive-drag-canonical-native-restoration-v1.json` | `c5db3602d75f53fd0509f2dcc1c6054d622962bf6c9cbd8f16eec2fa7085874c` |
| `archive-release-followup-v1/independent-owned-committed-cache-release-final-v1.json` | `a8a56005e191b8b033e8d7187e91c026bccb456667963328b54cf6d569c45f91` |
| `apply-named-batch-followup-v1/next-cache-release-independent-review-v2.json` | `6e46996da056589c39c686ffcda1f47f69796902e9b23fef9e123878c20342e1` |
| `apply-named-batch-followup-v1/next-cache-release-review-schema-refusal-v1.json` | `4958542edd245dcbe1a4ffd442e289f6e1eed66f5f24f876d68cbad57ac92b2b` |
| `archive-drag259-ci-v1/preparation-v1.json` | `5489152e918a6f0818e5f96965f31e0c26375f278062d74a957b513629be4329` |
| `archive-drag259-ci-v1/independent-archive-drag-ci-final-v1.json` | `1e7dd8703e20b6f9719aa09d64d9545423c2f761a61c9de745fb016f64c99df4` |
