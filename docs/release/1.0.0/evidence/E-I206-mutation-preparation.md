# I206 — archive approval and link preparation

**Preliminary remediation qualified at 23b77b63762520a6a951305b0a8e36f68b26ed20.** The related batch passes 159 working and canonical clean checks (142 App/17 Core), including 47 additions, without skips. Forty distinct baseline controls fail and seven are positive. Original four-platform CI is sealed green below; exact-candidate qualification remains.

| Confirmed gap | Resulting behavior |
|---|---|
| Archive delete, inline rename and folder creation capture a fresh baseline after approval; duplicate deletion recounts names after approval. | The revision and member counts are prepared together before the prompt. A changed archive is refused by the existing executor; selected duplicate ordinals remain exact. |
| Archive metadata and duplicate queries run on the UI; add preparation uses an unbounded pool task. Pack probes the destination synchronously. | These preparations use shared device workers, check shutdown and retain active-call ownership until return. Revision checks bracket the central-directory read; entry and retained-name limits fail explicitly. |
| Creating an archive folder reports a completed change index against an empty source list, throwing during UI completion. | Jobs with no source items skip source unmarking; the created folder and completion UI remain usable. |
| Link capability, free-name and preview callbacks bypass shared admission; the target is read after capability preparation. | The source selection, target and relative-path context are captured before preparation. Metadata and volume callbacks run on their actual path's shared device worker. |
| Closed or superseded link previews can publish; edits accumulate pending requests. | A dialog owns one preview task. Edits replace its pending request; generation/closure/shutdown checks suppress late output. Closing retains the owner until an active call returns. |
| Pack/link/Test Archive materialization retains selection snapshots, and batch limits are checked after conversion. | Unsubmitted or converted captures are released; the existing pack/link/archive-delete limits precede materialization. Explicitly approved jobs retain the captured source and destination. |

## Validation and limits

The actual headless commands exercise ZIP delete, nested folder creation, inline rename, Pack and Create Link. Add preparation is invoked through its existing private method with an owned file and ZIP. Real duplicate ZIP members, byte/hash checks and real hard-link writes verify mutation behavior. ZIP contents are checked using System.IO.Compression against known literal bytes; this is not a separate external-format qualification.

Held/error metadata and volume controls explicitly replace AppServices.Platform's file-operation dependency. Ordinary metadata delegates to the owned files; volume/link-right controls are synthetic. A registered filesystem wrapper supplies a disclosed common device key. Other-device progress, nominal/hard worker caps, shutdown ownership, captured targets, relative paths, invalid previews and sixteen coalesced edits are checked. No physical source, native desktop interaction or real network server is exercised.

The fixture explicitly releases its owned ZIP browsing cache before atomic external replacement, because the original browsing handle prevents replacement on Windows. This models a version change while the prompt is open without claiming that an unrelated Windows process bypasses sharing rights. On the baseline, changed ZIPs are adopted and altered; corrected jobs refuse and preserve the replacement hash. Revision checks use the existing length/modification-time baseline, not an atomic or cryptographic filesystem snapshot.

The final unchanged 4a4f4ef baseline has 47 distinct controls: 40 fail and seven remain positive. Its raw TRX has 50 results (42 Failed/eight Passed), including three additional xUnit cleanup failures from the archive-folder UI exception; these are retained, not counted as three extra controls. Working/canonical runs each pass all 159 tests without cleanup failures or skips.

All 21 raw stages, source ZIP/Git blob/mode identities, 3056 actual payload references, raw TRX definitions/outcomes and 47 final JSON observations are independently verified. Initial compilation errors, blocked replacement attempts, cleanup-controller mistakes, the premature concurrent archive hash and the fixed-delay preview observation are retained. The final fixture waits for the edited preview's actual publication instead of treating a delay or zero active callbacks as completion. The earlier unused prepared verifier remains on disk; the executed v2 verifier identifies the exact final stages.

## Remaining work

This closes this preliminary archive/link preparation subset of I06/V02/V07/V11/V12/V17/V23. External edit-session preparation, other operation/provider admission and lifetime paths, larger workloads, native desktop and exact-candidate qualification remain. No claim is made that every mutation or parser allocation is bounded. In particular, the in-box ZIP reader can allocate its central directory before the entry limit is checked. The read-only next-batch source inventory records hypotheses, not additional reproduced findings.

The preceding [I205 native CI](E-I205-metadata-preparation.md#original-four-platform-follow-up--4a4f4ef) is separately sealed green on all four lanes. The physical-source validation hold, owner/resource/participant gates and explicit stable human GO remain. No candidate, tag, publication or borrowed-machine setting changes.

## Provenance and record preservation

Private `FileCatReleaseEvidence/ap206-v1`:

| File | SHA-256 |
|---|---|
| independent-mutation-clean-v2.json | d565548cefa3f3acb1817caf35b24ce3a263d48a3a7e7fceee8456c0f8ef95d7 |
| seal-mutation-batch-v2.py | 82eee51cb7f7090f2b35730611e4ec6352f2c9527b9250db9e1a3b1e00e01c3c |
| run-mutation-v19.py | 87c4ea406480c123ecc130a6748a89cba70514f84b9778488d62a9a90a38e93d |
| run-mutation-v20.py | 45e25205377a8b9487b98fd4b928e5837f9cfab1dbe26af9bc57733c13b56df0 |
| run-mutation-v21.py | bc05a71887039c2bc7025c6a710e37e207cb599b4a847beebffb61c5cdefeb6e |
| baseline-v20/command.json | 5632bcd7d4266019c8e558e5d7e3165746e4c1ef5e59997e39e291142a9655ed |
| baseline-v20/results/app.trx | 4b4edcd9e4f1e0b1feb63c45c40096250c414c6610f381d82dd171273aec3dee |
| baseline-v20/source.zip | 5a369c5c81a3552352e1adeb96b4fd5a5eaabc9eb6879b4e03f9c2b213cfd295 |
| working-v19/command.json | a58411f7d18ecea7eaabd87db06ca64e47bb398fc37f2d53c2c75d3f4928937e |
| working-v19/results/app.trx | 3a75d83341da6bf286e342c87675e4f39623b706275ebe4339d71195147b4061 |
| working-v19/results/core.trx | a0914790e68575608ee9f894ac0899bdab11a2c7cfd4e55fc84898f06eeca8e1 |
| clean-v21/command.json | 28e6f95d68c4f06dded6fa621f728a72318160e4c22dd1ecee9fd867da30a1e1 |
| clean-v21/results/app.trx | 8c30d6d3940d1947e5be68e8153a74cb81cff2e6b9502fd9071dae09db0d4b38 |
| clean-v21/results/core.trx | c019171a1e04bdc0f10786db575131de5960460d3eae742f9dc580c9104946b3 |
| clean-v21/source.zip | a9aa29690ee08297fb32c05a6defca71ae84db3d9b67ff6aab5f37910b3550b1 |
| next-edit-preparation-readonly-v1.json | 09a699bc0c7dcc4b02b4da7d14109c708b123bfc079d885b7af02df6a0d7138f |
| baseline-v1/command.json | b0f88309301d71967b850f90baa5225722ff004987ab684739852ba174ee40e7 |
| baseline-v2/command.json | 3a644a6cdbfa5fac731795ff08ce73cca3681002d2812d555dfcb4df445095ba |
| working-v3/command.json | 45e11b5cfeaa4e9b68bc05cc54f56deb39f194da09f8ac29e789d7d3491569a9 |
| working-v4/command.json | a8727f8542cd9705e910fd7719b4acecdf3ef7c3b85bf182521af366227a3e8e |
| baseline-v5/command.json | 55dbb3d8a9663cd346544b1cd9f9e1b6771a7812970d086102e781a335f7dbf0 |
| working-v6/command.json | ed94bdb2aa4331115f7edccb7c0e9fea0fd729bb43943d9cad6b4302551e06c8 |
| baseline-v7/command.json | 1eb8de31dbd8ad7f25dce09d90fa5a3bfa3f06c0f25ad68b64bd7c193ca6fc52 |
| working-v8/command.json | 635f93221d144119e0c8868e9d569430a1126113051f7321a8d03b59df395801 |
| working-v9/command.json | 1fae3fa749df9123ac0abf1ee69f26ef9be3c843e0f424a9f4f38f8318994b0e |
| baseline-v10/command.json | dd541014e468f5171b0493bddb89cd2b4acde7aee749dca040d122fab97bd548 |
| working-v11/command.json | 738a6e30742b29fc607e478412cd03f46087384c345a5bd01c2f2046f7bd13dd |
| baseline-v12/command.json | e74ca2fe6496e0ad06cc28350357fdf7dfa705eb4466183b9186d5dfd0cc20db |
| working-v13/command.json | ff37c3e2bd51de5e238cffc5c86fb686a0c3183601cb5a4fa4590b5ffd32c48c |
| baseline-v14/command.json | 71270885979d7bf181c43c34408ae8bea9ca042fb63a287de0182998975b7e89 |
| working-v15/command.json | 5bdfdf5004133c3996e12b32cad6666e76770daf940129614fb20419a2d96e5e |
| baseline-v16/command.json | f9018b6fac1ac60511dc5a35ab23ce6abf32e925389b15876af14733106ab815 |
| working-v17/command.json | 706c3b01ad9cb5574bc8103549d10d932a0e9dc0c5b0c1007b93f94f1d110106 |
| baseline-v18/command.json | f1e060184a569330fc1963de11381e7b20228457a8012522eb305c9befd35f02 |
| baseline-v2/results/app.trx | aade12b02d924b5c168055dc201580ee93dc08d98290d9de8ac7262f9156fc72 |
| working-v4/results/app.trx | 0443ebdf3b2d90a311b3f0c9469e2bc7d490a3f63a144443da0ea0d60746f682 |
| working-v8/results/app.trx | ca4b1a3103d8491a35f022b3ce788887634cdab9344fc7f2e401cecff9ffa9c4 |
| working-v15/results/app.trx | 5ec91c4e61c4b8c09a07dd08e119880880c87a250c745c869578a8b78b4b7a02 |

## Original four-platform follow-up — a96b598

Original CI 37701094259 attempt 1 passes Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26, including all 188 executions of the 47 mutation additions. Every available predecessor name/outcome remains. Independent readers recheck 20 server artifact digests/every selected member, 14 full raw TRX inventories, four builder receipts and 92 actual locked restore graphs. Metadata/archive/content/directory additions pass again (188/232/468/128). Native Ubuntu dependency preparation and nine mirror/five launcher controls succeed. Packaging/draft jobs are skipped on the main push; installed GUI, hardware and candidate qualification remain.

The CI source is a96b5981a3a69d817fd7e7d785edba9e5dd56116; its 796 runtime/test/eng/workflow Git identities match the locally qualified 23b77b6 product. Local and hosted producer identities remain distinct. Current campaign mappings now use the final plan's archive/local-operation/tool/lifetime scopes (V02/V07/V11/V12/V17/V23), correcting the previous V05/V15 labels; the complete prior records and rows are preserved in this batch's before snapshots.

Private `FileCatReleaseEvidence/ci-37701094259-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 4516747c100cdcd6cbbc0bf2aed754ec7733cc9c7a9e534a97fd4680aaccbd01 |
| independent-restore-ci-v1.json | 31084129177d637976210dba7da586b53ca9e19d1348ceb375e6e1f0c99a59ef |
| independent-mutation-ci-audit-v1.json | f52d2f55333645f75ce5fe74836d771e40671dba3a053ee84ab1ec7db3d748f0 |
| run-native-stdout | 3aeaf2b333a7fb4bad62264843f8355e6652d3696e28a475082601d1982d04c1 |
| jobs-native-stdout | 4043aff46a51519fc7c3cd7128f9ac80a6859cdf7cecb6afde3093947da456f1 |
| artifacts-stdout | 7530e5991d0988a16191e629c305d1ae3486a0c085aa57d837ae44e2c4ce8e55 |

Private `FileCatReleaseEvidence/release-assets-20261006`:

| File | SHA-256 |
|---|---|
| collect-i206-green-ci-v1.py | cec9cf2e173d5fe7f500d0e9161b3faa63335dceba80a5a34e368070792108f8 |

Private `FileCatReleaseEvidence/ap206-v1`:

| File | SHA-256 |
|---|---|
| seal-mutation-ci-v1.py | cad390481985db5980325b7d78e62612d73e5d24c692921922eb01d96ca88873 |
