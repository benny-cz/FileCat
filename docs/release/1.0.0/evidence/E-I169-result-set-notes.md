# E-I169 — result-set note ownership after removal and rename

**Remediated preliminarily in 5f4a636cb587140c24aa662ea69b1a834bc4a4ac.** Removing a result member now releases its note reference. Renaming a member carries its group note to the replacement; merging into an existing member preserves that member's note and releases the old one.

## Proved defect and correction

`ResultSet` stores notes by member identity. The original `Remove` and `Replace` updated the member and relative-folder collections but left the old note entry behind. A live set therefore retained a removed or merged member through its note dictionary. A unique replacement also lost the displayed group note.

Four durable Core controls use synthetic member identities without opening files. Two use weak references, a non-inlined setup helper and a positively retained live set/member to prove the removed reference becomes collectible. The other controls check note migration and ordinary unnoted replacement, including relative-folder preservation. They do not infer lifetime from dictionary reflection or native desktop behavior.

The baseline uses the original canonical 28d002f5a9efeeaa55b2baccb31789f2a98e2406 source with only the new test file overlaid. All three adverse controls fail: removed and merged members remain rooted, and unique rename returns no note. The ordinary unnoted replacement passes.

The three-line correction removes notes when a member is removed or replaced, and transfers a saved note only when the replacement is a new member. Existing replacement metadata remains authoritative when members merge. No budget or retention policy is changed.

## Revalidation and actual provenance

Working-overlay and fresh committed-source runs each pass all 25 affected Core cases without skips: the four new controls, seventeen search/criteria cases and four working-set cases. All four new controls pass. The actual correction overlay agrees byte for byte with the canonical committed module.

The clean locked run uses Windows SDK 10.0.401 and an export of 1,061 canonical Git blobs from 5f4a636. Source archive bytes, exported files, Git blob SHA-1/modes and before/after pins reconcile. Actual clean `FileCat.Core.dll` SHA-256: 5d1cf2bd233581ab1556f851c5c749c2476470fa2bae5b60bd2ba70c415ad608.

The independent seal checks all raw result attributes, the unchanged test overlay, the original 1,059-blob source copy, the single corrected module, 19 retained files and 246 actual test-payload files across baseline/working/clean runs. These are development test payloads, not selected release artifacts.

Original [CI 37546809299 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37546809299) at 5f4a636 subsequently completes green on policy/all four required lanes, ARM64 package start/draw and installer compilation. Nineteen server artifact digests/all archive members, fourteen complete raw TRX inventories, four compiler/tool receipts and 92 locked graphs reconcile. All sixteen new I169 executions pass without skips. Each complete Core name inventory equals the preceding original CI inventory plus exactly these four controls; actual outcomes remain explicit:

| Lane | Complete Core inventory | Actual outcomes |
|---|---|---|
| app-test-results-macos-26 | 875 | 43 NotExecuted, 832 Passed |
| app-test-results-ubuntu-24.04 | 875 | 42 NotExecuted, 833 Passed |
| test-results-windows | 880 | 57 NotExecuted, 823 Passed |
| test-results-windows-arm64 | 880 | 57 NotExecuted, 823 Passed |

Each App lane retains all 469 cases, including the repeated I163–I168 subsets and their explicit Unix junction skips. Packages requiring tag/manual dispatch remain skipped; no selected release package or candidate is produced. Hosted component runs do not establish native desktop, physical-source or human qualification.

Private `FileCatReleaseEvidence/ci-37546809299-assets-attempt1-v1`:

| Path | SHA-256 |
|---|---|
| independent-assets-ci.json | 28eb0b61c416a2cf489c78379d7d58cd29300dc8d39310eb8a69165059119895 |
| independent-fixture-ci-v1.json | 5354473c93cb0b0712934eaa06e8bb7f9c4930ff5f83a319a05a6b4a61ffc233 |
| independent-producer-policy-ci-v1.json | 188ece0ca07af8e38a5361f4d35d08600808f3a3f9ce4c5ce9078275ddf68f46 |
| independent-draft-guard-ci-v1.json | f70e6531d5721c8f6eee19f2f4c658347f54f8b2b572c012db14f3bcefe5d54b |
| independent-separation-ci-v1.json | b7eb19e31497aa50bb32533add6cd491ccf8ef9f6b959e55aa26084fc546c5fc |
| independent-restore-ci-v1.json | 706a9e39b4d45140fc8330b26547cff741f94d6b64358af31e0b7f38d68ece46 |
| independent-i163-ci-cases-v1.json | 4b3e4f8fcee853e0c26e75f6da5625a846ffc9b488f8b42b48117ff4f2ecc881 |
| independent-i164-ci-cases-v1.json | 013dfdd41ba257b5f4495801ea3c998ecf1f13bb9aed5bd9f5da3551ceecbbf6 |
| independent-i165-ci-cases-v1.json | 169d4ffc4f8a0bf1f76a6424ef420ea0e7f5a1c24922a122c67260ac7feca749 |
| independent-i166-ci-cases-v1.json | a49c38a0f04f280b44b1cac4f6c36592905360e16083911f8cbd026b2d7a0e62 |
| independent-i167-ci-cases-v1.json | e3f6a66705a9b3149b615f836c90843eee4ccbd18b313c7f227a1d7b9cb90b22 |
| independent-i168-ci-cases-v1.json | 8e29fdf44a78a080a18630e1e1986a60a6dbb411ede33ea74d763f3ce10ed160 |
| independent-i169-ci-cases-v1.json | 6ca49cd1086171682e9fc0553e6d3d0c1c485ae86ad4d5281e25b01171267b5f |


Broader I06 consumer/worker/frame lifetimes, wide materialized workloads and native interaction/candidate qualification remain open. No filesystem, physical source, persistent machine setting, frozen contract, candidate or publication changed.

Private `FileCatReleaseEvidence/result-set-notes-20261007-v1`:

| Path | SHA-256 |
|---|---|
| baseline-v1/command.json | 75005d21643ee7a2dd932ea12474236b72624a5c88f4fa62c07768d86b740906 |
| baseline-v1/results/baseline.trx | 45ecd692136f70c348dd271b18fbcddd875e7e35fdaf59be7e3489544013b117 |
| fixed-overlay-v2/command.json | c58e2df8ca99b6e249cc8fb3f2a1591ce8c2d81513ebcee42e022b1271186f3e |
| fixed-overlay-v2/results/fixed.trx | 9de6fc665d41914b5e6229fe1ea8326344e5ae1bd593f43864968b2b89f6bc17 |
| clean-committed-v3/command.json | 6622fe7a003ce270f7e7c60673b361f02b90dd9487fafb5f81565163b9f6cc33 |
| clean-committed-v3/results/clean.trx | 0ae18dae53fa00603c0f5853731082206552dea4d19cddba7437abbb2ef7d64a |
| independent-result-notes-v4.json | b9753484be83480bdb22197bd221cf0875fc04f532a3bd10518b08dca1915025 |
