# I208 — frozen edit commits and reviewed target revisions

**Preliminary remediation qualified at d8d81e897800381452c9feb700d197132e562cbf.** All 45 additions pass. Expanded working and exact Git-canonical clean runs each pass 390 checks (61 Core, 90 Remote, 239 App), with seven existing remote-environment skips and no new skips. The unchanged f9cb197 baseline has 36 failed controls and nine positives: 30 execute adverse behavior (29 App and one wrong committed CRC/length), while six Core controls report the unavailable snapshot API. Those six do not claim that their byte/error paths ran on the baseline.

| Confirmed gap | Resulting behavior |
|---|---|
| Archive rebase/overwrite adopts the archive revision after its confirmation, allowing a newer change to be overwritten under an old answer. | Review captures check and baseline together before approval. The queued archive plan retains that exact reviewed version; another change makes the executor refuse. |
| Queued archive/server commits read the editor's live working path after hashing it separately. Later saves can become the uploaded bytes or disagree with the recorded hash; an active upload can hold the editor's file. | One complete, bounded private commit snapshot supplies the job. SHA-256, CRC and length come from the copied bytes in one pass. The working file remains available for later saves. |
| Archive completion records CRC/length from a later working file, falsely describing the committed member. | Acknowledgment checks the actual unique archive member against the snapshot's CRC/length and saves the written hash. Later editor changes remain Modified. |
| Pending or queued commits permit a second commit or explicit discard of the same session; callbacks can submit after shutdown. | One session action owns approval, snapshot preparation, the queued/active job and acknowledgment. Competing commit/discard returns without another prompt or mutation. Shutdown checks prevent new jobs and late acknowledgment/commit notices. |
| Commit checks/copies use unrelated pool tasks and synchronous UI metadata; temporary job-source ownership is absent. | Review/stat/snapshot/acknowledgment use shared device workers. Active synchronous work retains its caller until return. Snapshot ownership transfers to the job and releases only after JobFinished, including failed/cancelled jobs and shutdown. |

## Validation and limits

The 38 App controls use actual headless dialog answers, owned real ZIPs and the existing in-memory SFTP connector. Twelve approval cases cover unchanged/newer targets, cancellation and shutdown for archive rebase, member conflict and remote conflict. Twelve queued cases verify frozen bytes, later saves, navigation/tab closure, changed targets, cancellation, competing commit and discard, and cleanup. Four pending-action cases prevent a second prompt. Stopped-service and missing/over-limit cases refuse jobs. Two controls hold the shared local device workers and verify that commit preparation waits for their admission and cannot submit after shutdown. Two held actual fake-server uploads verify that the snapshot remains while the executor is active after cancellation/shutdown, then releases when the executor returns; the working edit and previous acknowledgment remain.

Seven Core controls cover empty/four-byte/2 MiB-plus-one snapshots, exact hash/CRC/timestamp/origin propagation, later saves, repeated disposal, missing/oversized/cancelled preparation and preservation of unrelated temporary files. A real archive-job control verifies that acknowledgment describes committed bytes even when the editor has already saved new content. The broader regression run includes prior edit-copy, archive, transfer, shared-worker, viewer, unpack, link, escape and synthetic SFTP connection/workflow controls. The seven existing remote skips retain their actual reasons (six RemoteLab cases and one FTP integration name/platform case); they remain gaps.

Independent readers verify all seven local stages, canonical source ZIPs/Git blobs/modes, 1520 actual payload references, raw test definitions/counters/outcomes/skips, line-ending normalization and all 45 final observations. Earlier narrower runs remain intact. Published sessions and later working edits are never automatically deleted. Archive acknowledgment uses actual member metadata, not a new full read-back proof. Remote acknowledgment requires an available matching length, but length/time remains weak revision evidence; same-size/timestamp races and concurrent third-party changes are not an atomic source/target identity guarantee. Windows file sharing conservatively refuses an already write-held working file; Unix sharing is advisory, with before/after length/time checks. The snapshot path is owned internal data, not a boundary against a hostile process under the same account. The ZIP count guard follows the in-box central-directory allocation and does not prove bounded parser allocation.

## Remaining scope

This qualifies the edit-commit and competing-discard subset of I06/V07/V08/V11/V12/V23. Existing-session review/reopen/state hashing, watcher debounce/closure/discard demand, save-copy picker/source lifetimes, standalone discard failure/shutdown cases, interrupted run-again/staged-file identity, broader providers/workloads, real remote servers/editors/native interactions and final-candidate qualification remain. The read-only next-scope record contains hypotheses, not additional reproduced issues. No physical-source or persistent machine setup changes occur; the physical hold and explicit stable human GO remain.

The preceding [I207 original CI](E-I207-edit-preparation.md#original-four-platform-follow-up--f9cb197) is separately sealed green. Original native follow-up is retained below: three lanes passed, Windows x64 has two failures; correction follow-up remains pending. No candidate, tag or stable publication is created.

## Provenance

Private `FileCatReleaseEvidence/sc208-v1`:

| File | SHA-256 |
|---|---|
| independent-commit-clean-v1.json | 15f7fb3ed60484c69653afe8d88fe70e48545997a410ac6e02fae1621c401036 |
| seal-commit-batch-v1.py | 8b41ccb9c24c9e791db345ec4dfe4347db0fa4a6137d6a1ccb136ce30677b217 |
| update-documents-v1.py | cd6a6414e27820b39a5e0fe96b622ee424f681b7c24714bf7e67f21866135167 |
| document-transitions-v1.json | 6f7fee265542b7c83be743df184f9e57280debd59719f17a02736c8414ef274d |
| next-session-actions-readonly-v1.json | bc33315f2dc7b1a9be1e5e9ee714792048369eca678d934c74ef3c701fd62e2a |
| run-commit-v1.py | 513b2407d9e117295d44a5c771b8995d4fd22b4d8bf1338d30b88379624c51e3 |
| run-commit-v2.py | 5eba37580582ec4fd5aa7e21c64dfcc888fd1ae0ffe87ae28d932093ca09dfc8 |
| run-commit-v3.py | 4764a2df803ba557245444d313489fa2cb7280ddee82b95bfc2a790e7f5a4010 |
| run-commit-v4.py | 7986041c68598d80cf43f6fb76e5c9ca127a6a24b8e5e905b922582ba649fd92 |
| run-commit-v5.py | dd2e5f8cedb1cc73ac2120e3f6ee34c362d946a26c29e623889f0e33dcb64b89 |
| run-commit-v6.py | e2ad8b635fa9cf01ae5a083b722dc20b006d52b36d05e571f9f0ea90993a0dd6 |
| run-commit-v7.py | 000c2b575e6171569b2bead05c2ce25b42496d82da3dff84e6f16acf5d51032e |
| baseline-v6/command.json | 9d7684e4453227b7c1c57f6f7600bc0bc6f7158fb9bb459a94dd2908f67f7e85 |
| baseline-v6/source.zip | 95df334490ee50563db4fb521f5837b14c9c5ddc4dfd37c462402ebf187108f8 |
| baseline-v6/results/core.trx | f73a3696cd289151ed1bf128f1c24a23e8c387279e6acc480a5eb6b06f5ff4e5 |
| baseline-v6/results/app.trx | 6ee9a7df2efc11656245191bf8a1acce563253800f8bc37ee303f747a836466e |
| working-v5/command.json | 340d5b109af880f9175f4ba38f896e7ec03dd17b41c472da6d037b63a180d93d |
| working-v5/source.zip | fd537a5dc609723cb6b6e6c0d530734295b7c8b50b275f969d2702e3e60603d7 |
| working-v5/results/core.trx | 556b5c970aedb8f9ee7cbec4265d0bc666ff16f5a1c81b0d70ce3b9c420aa164 |
| working-v5/results/remote.trx | 11c33131807ab7d3caf834f3016e12d757c560ab58cf8e5249b1ac767e53ca93 |
| working-v5/results/app.trx | 8167bc5c13aed8dd19dbd6451444eaedcea4976a3c019df236c7bcc30a7a7094 |
| clean-v7/command.json | 454438b0d098d9b37954e787f350d314bc79a34343150c827165fa8fbe2521c0 |
| clean-v7/source.zip | 5a1d585ec2b2dd52ec1890b5bab72c3a4b98f745c0593508234186e082f1e897 |
| clean-v7/results/core.trx | 9955f0912f1fd40638679d8211f866ec18e2c55434a9dd2b123e9239e16b30be |
| clean-v7/results/remote.trx | fdec3751871fb371f38d6fb71d03eb458572593d874023179f7e803c2f7937eb |
| clean-v7/results/app.trx | 8088ba4eea53d3056f8c58cd9ec66cbed96f3db1df04d246537bc4905e37a3ea |
| baseline-v1/command.json | 139ba0e3aad9e50fe7e3b5da66c052c5c6c444caf3364be24b6019a0f4c90295 |
| working-v2/command.json | 1203b98c591ae3ded02598f1828a884dc082b711cb3bc619e59f1ae96ce0fb2e |
| working-v3/command.json | c89e80c7344b759d5c04d314f245c8c40a225f7de7b28d23ab61568e31c8bfb1 |
| baseline-v4/command.json | 37bde8ac599c264879e6063a653d16e1fa65eb661535630f40258e49e11d2e54 |

## Original four-platform follow-up — b2ec0c6

Original CI 37706345533 attempt 1 passes Windows ARM64, Ubuntu 24.04 and macOS 26. Windows x64 fails two App fixtures: the existing F4/server-edit workflow cannot find its Commit button, and the new SFTP shared-device admission case observes pending=false. Across four lanes, 179 frozen-commit additions pass and one fails; all 180 observations remain. The initial collector printed 180 under a passing label because it counted observations; raw inventories and the independent reader distinguish 179 passes/one failure. Every predecessor name remains; the existing F4 Windows outcome changes to Failed. This run is not all green.

Independent readers verify 20 server digests/every selected member, 14 full raw inventories, four builder receipts and 92 actual locked graphs. Earlier edit/mutation/metadata/archive/content/directory additions pass again (244/188/188/232/468/128). Package/draft jobs are skipped. All 801 runtime/test/eng/workflow identities equal d8d81e8; local qualification and hosted failure remain separate.

[I209](E-I209-session-review-lifetimes.md) corrects fixture checkpoints and qualifies review/watch/reopen/standalone discard separately. New hosted follow-up, real editor/network/native and candidate scopes remain pending.

Private `FileCatReleaseEvidence/ci-37706345533-assets-attempt1-v1`:

| File | SHA-256 |
|---|---|
| independent-assets-ci.json | 37779f889fb5b71d4f87faff6908e037e434a99fd13056dbd9dff931a7bc0cf6 |
| independent-restore-ci-v1.json | a1c20aca0e26889b6dc4aa9cf504fad3fa7f52665214e731c5d3b54b97d812aa |
| independent-commit-ci-audit-v1.json | 58d15c5db7567cb30bb3cc4239d7d7140c8c0e7f35b0e51518f0b51c934e599e |
| run-native-stdout | a7212f7338763686a52dd3c07ee02dd1961ffcfdd9fb3ff5b47372d119a85411 |
| jobs-native-stdout | ad36272ed82457a543b9907aa9208ba2910bf9a6a7b50920d1f645c41a787efc |
| artifacts-stdout | 00f814617941571ee8fed7b83f43a0a989a1918afc6afa6e50c651f31727b947 |

Private `FileCatReleaseEvidence/wl209-v1`:

| File | SHA-256 |
|---|---|
| collect-commit-ci-failed-v1.py | e088b0b33adb2396e3745e9f8236a1e96c03567946692ef0ee2fa1113994377e |
| seal-commit-ci-failure-v1.py | 963ca4133b3347d0c60bc83a293730bfab11d4a5de8eb76a834b84fcfdf1c6e9 |
