# I209 — edit-session review, watcher and discard lifetimes

**Preliminary remediation qualified at b7f12b139d25d7204b88b734093d92bc24928922.** All 53 additions pass. Expanded working and exact Git-canonical clean runs pass 445 targeted checks (61 Core, 90 Remote, 294 App), with seven existing remote-environment skips and no new skips. The full clean App suite separately has 927 passes, 23 explicit skips and no failures (950 cases); every targeted App outcome is retained. The unchanged b2ec0c6 baseline has 51 failed controls and two positives.

| Confirmed gap | Resulting behavior |
|---|---|
| Startup, session list/detail and watcher callbacks hash working files on the UI; read failures can be called Modified. | Record reads and bounded content reviews use shared local-device workers. Only complete bytes with valid counts, exact EOF, stable available revision and no partial-content caveat establish Modified/Unchanged. Missing and unavailable remain distinct. |
| Each save starts a delayed task; an old task can announce after shutdown, window closure, unwatch or watcher replacement. | One watcher owner coalesces saves into one delayed probe and a pending flag. Owner identity and closure checks suppress obsolete callbacks. Active synchronous sources stay owned until return; accepted window closure cancels delays and disposes watchers. |
| An answer after shutdown/window closure can reopen the editor or continue another session action. | Review and postanswer actions check live window/service demand. Reopen rechecks working content on the worker. Commit/discard and finish notices use the same closure guard. |
| Standalone discard deletes a newer save made during confirmation, including creation after a previously missing copy. | Reviewed hash/state is checked again after device admission, in the same worker delegate as deletion. Changed or newly unavailable bytes preserve the edit and require a fresh review. Cancel/closure/shutdown keep the record; successful discard removes its watcher. |

## Controls and limits

Twelve held-open/read startup/list/detail controls check worker admission, ownership until return, disposal once after active reads and no late prompts. Three live controls preserve the record. Six held watcher controls and three delayed-event controls cover shutdown, actual window closure and unwatch. Two 64-save bursts check one active probe and independent-device progress. Two replacement controls suppress the old watcher and observe the replacement's next save. Six actual headless dialog answers cover reopen/commit/discard after shutdown or closure. Eleven unavailable cases cover I/O/access, invalid positive/negative counts, short EOF, unknown/over-limit length, growth, revision change, missing ranges and caveats; one missing-file case keeps its record. Seven standalone discard cases cover success, refusal, shutdown, closure, newer bytes, a newly unavailable second read and creation after the original was missing.

The fixture wraps the actual registered local provider for one owned real working file. Real FileSystemWatcher events, persistent records and headless dialog buttons are exercised; tests await actual watcher creation and retained startup tasks. No editor process, real server, physical source or native desktop input is exercised by the new controls. The legacy SFTP workflow uses its existing fake connector and exit-only editor. Every remote/full-App skip retains its actual reason. Earlier 35/117-check runs, baseline cleanup corrections, two watcher-generation timeouts and two late-dialog baseline timeouts remain in eight retained stages. Baseline admission assertions show that the old path bypassed the registered provider; they do not claim injected error/hold paths executed there. The document-writer construction SyntaxError occurred before a writer or documents existed and is retained separately; no product test failed compilation/controller execution in this batch.

The cap is 1 GiB per working-file review; record enumeration and aggregate session counts are not newly bounded. Local revision evidence is length/time, not atomic identity. Final review and deletion share one admitted delegate, but a filesystem race remains between read and deletion. Aliases, hostile replacement, access/sharing/deletion failures and larger/native workloads remain unqualified. Initially unreadable copies are conservatively kept. Native save-copy picker/source lifetimes, real editors, process-exit cleanup, additional F4/window-close creation variants and interrupted run-again/staged identity remain. Startup journal/hex discovery gains closure checks without qualifying every journal/hex workflow. No stable, candidate or physical-source qualification is claimed.

## CI correction and next work

Original [I208 CI](E-I208-edit-commit.md#original-four-platform-follow-up--b2ec0c6) has three passing lanes and two Windows x64 failures. The existing F4 fixture issued its second F4 when the record appeared, while initial preparation could still coalesce it; it now waits for actual preparation completion. The admission fixture included remote preflight and a timer while holding local workers; after eight seconds the scheduler can correctly admit a replacement. It now enters the actual copy-admission helper directly and observes its synchronous queue checkpoint before shutdown. Full commit/SFTP workflows remain in regressions. These corrections qualify intended checkpoints; clean App success does not prove every historical timing cause or a green hosted rerun. New native CI remains pending.

Save-copy picker/source lifetimes and interrupted-operation review/staged identity are next, followed by the remaining discard/provider/identity/native/candidate matrix. No persistent Mac/VM setup or physical source is changed.

## Provenance

Private `FileCatReleaseEvidence/wl209-v1`:

| File | SHA-256 |
|---|---|
| independent-session-demand-clean-v1.json | 130764c38b52cc4d0f56b3957590b27c3229cdc1f568e93225c07edaf59d68d0 |
| seal-session-demand-v1.py | 2b158e6bc9d2fc2214a3a028a464e4e5d7cdd59fb711aff67db070177962d2a2 |
| update-documents-v1.py | 0eba3bd366bc9ab8cae1978ebe19cc88d8aacc8bdb5bfe2c2bdebb7f57d6236b |
| document-transitions-v1.json | fdfecd36aa629a3caf68d956bd9fc06d2bd0abd4228d2650ab982fdedff6d6d5 |
| next-session-actions-readonly-v1.json | 2f0d77019e42544e0e57a28bdc8e96e0bbe01e60e62e1d84fa375e59956d3ac4 |
| run-demand-v1.py | 15a75f33a2667acdfd2d2a2c361f53158c343770e61f06340521dc8050a3a519 |
| run-demand-v2.py | d8b07b0ad18c3a2bbc71750d04a770f3f6b5d0a8b542b41445baf2781ea10c78 |
| run-demand-v3.py | ec6057a933a96805a58b4dfe4e8b550c36a3227633544ed6460f32fe96a592fd |
| run-demand-v4.py | 1a365c5f4163b63e142214cc3f51807fbd0e84826a527cb0584e982d2168e3d3 |
| run-demand-v5.py | f8e219544de0596e393ff38b7f8c9aa7c23724a2a6bc3bcfc304b3caf436dff4 |
| run-demand-v6.py | 8f31124415e31e16b083f66f8394d345f83eda02032d176f9a6209af0232e45e |
| run-demand-v7.py | b72770ac2c9c38ae7ec5b91b7011274e4ef036f72226998d58f2405fe80ac09d |
| run-demand-v8.py | 1cf4a71f5a3da0fc79c2082533d35d33ab33fc001fa889a24651135426e3f2e9 |
| baseline-v5/command.json | 5a588232ac494a1e584894aebc6fd49ab38a8c9fa66620e60aaf1d038c645f8e |
| baseline-v5/source.zip | 9628979041e511ab40943965b8a5143a5f799395fca5028a9585d2e41365bb03 |
| baseline-v5/results/app.trx | b2600f8902a0be853c29c971b8ca8018650796318bd271021bcb89db51028308 |
| working-v7/command.json | 64dc979236dc6a01ead658c31b97760a195081e7b157cf97f57e157cb312846d |
| working-v7/source.zip | 8da89491416370553a0da6d76ae4247a497848f0cd002b1f426141d84cb822fd |
| working-v7/results/core.trx | 78db7421e84931d5102c1c5b7d1b59363580fbc10aca0ab792c8ba068a133346 |
| working-v7/results/remote.trx | bd892a8fc59ab5283030630aa520fd49e035e85efe6b569f821d33fe7d86f438 |
| working-v7/results/app.trx | 6eae35b7fc47a6b08a87935fcf968861283cbf504a3ee65d0c14044830a13170 |
| clean-v8/command.json | d5308991279e77736fe9264e1bee3e9da545bfa70ed54fc556d75a0815919c18 |
| clean-v8/source.zip | 3b1d7714b4607b25273f69607b4d85f9b29b258d93516a49eb65e51afb9412e5 |
| clean-v8/results/core.trx | dd7c8dcae4171ce6c12d8a12ce9417eaa1045e2a1aa053fec72630093b1b7b4a |
| clean-v8/results/remote.trx | 95329c14c984c44b25be0e4033f2d533a9ea5713e4b5cde2f2e784d1ddcde13a |
| clean-v8/results/app.trx | c3ebdf65c8aa9ca438b3d8520f018f2596277ac999e98331859a5afd539513f4 |
| clean-v8/results/app-full.trx | 9d0232ddd640e7841a46dfd241d528bb95f488568d6b942ce52dc54f2d42952b |
| baseline-v1/command.json | 234aa25db04a576b1914f7d53461585a0e045090fb7e43983c0efafe69c76fce |
| working-v2/command.json | 09f45dfebf6c350de66f92bf4a349dd6fb1ed162d2f7b90963da99dbde87c54c |
| baseline-v3/command.json | 89f8f3f89acfef07d953789e5053e2e538186d5522c8f262012d1e2b95619cc1 |
| working-v4/command.json | 92d080023f6e1c0917062bd7127cbcdb39d6cf5cda6e66dcf3efe1e0afc6147f |
| working-v6/command.json | 3604013a2726b444f6b1e5a1c2022420013c31127b842d9b115c79888acb5430 |
