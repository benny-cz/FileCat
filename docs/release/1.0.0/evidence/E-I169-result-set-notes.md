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

Original [CI 37546809299 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37546809299) at 5f4a636 is pending at this local seal. Hosted results will be recorded only after the original attempt completes and its raw inventories and artifacts are reconciled.

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
