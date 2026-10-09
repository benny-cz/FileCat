# E-I282 — follow file-link contents without losing the compared target

2026-10-09. Preliminary I06/V02/V13/V23 evidence at original **b3edd9197db3c10b758f5fd57aad9c8d92546c3c**, with exact original source exports and declared fixtures/production overlay. If literal link creation fails and the user chooses **Follow link**, the fallback used the link's own size as the referent's size, kept CopyLinkAsLink enabled, and dropped ExpectedTargets. It could therefore recreate a link instead of copying contents, fail verification, or replace a target changed after synchronization comparison.

The correction resolves ordinary-file referent metadata after the explicit answer, copies bytes with CopyLinkAsLink disabled through both staged and fast routes, carries the compared-target guard through fallback and retries, and rechecks before the fallback conflict decision. Following an admitted link adds content work without counting a second item. The source path remains the original link. A cross-volume move that follows it copies the contents, reports that the source link and referent were kept, and skips source removal; it never substitutes the resolved referent as the item to delete. Directory-link fallback is outside this correction.

**160 synchronization controls** run the real TreeCompare, SyncPlanner, JobManager and portable file operations over actual owned symbolic links. Update/Mirror, both directions, root/nested paths, four timing boundaries and unchanged/length/time/removal/link/directory targets retain the earlier matrix. Literal-link creation is deliberately refused and Follow link selected; the first publication failure/retry is controlled. Corrected original results are **136 failures/24 passes**: healthy cases still publish links, changed targets can be replaced/recreated, and substituted directories lack the required comparison refusal. Final **160/160 pass**, healthy destinations are ordinary 17-byte files, and every changed target retains its actual injected snapshot.

**32 ordinary copy/move controls** add Native/ReadBack verification, 17-byte and 1,048,577-byte referents, free/existing destinations and controlled metadata/volume profiles that include the fast path. Original **32 fail**; final **32 pass**. Exact destination/source/referent hashes, one admitted item, transferred bytes, CopyLinkAsLink=false, absence of source deletes, source links, sentinels and destination names verify. Volume routing and literal-copy failure are synthetic controls; the links, files, byte copies and deletion observations are actual. Moves explicitly retain both source link and referent.

Full Core on the unchanged compiled control payload passes **3309/64 exact skips**. All **3181 preceding logical Core outcome/message multiplicities** remain, with one verified PE-inspector assembly-path label adaptation. The **300 selected headless App edit/comparison/sync cases** retain all preceding outcomes/messages. All **576 recorded original/final/full-Core fixture/source/target/referent sets are absent**. 24 owned temporary files are archived/rechecked, 15 removed and 9 exact locks retained; eight root-absence flags are false, true, true, true, true, true, true, true.

Both final fixture exports retain **1332 canonical original Git blobs** and only the declared test/production overlays. Initial raw synchronization controls record 160 failures because the first fixture expected a Follow link question even for 24 targets rejected before copying; the corrected fixture requires zero questions there, retaining all other assertions. A preparer then rejects an overly broad expression count before preparing any run. Additional-file fixture compilation initially fails on a required EntryKind and a nullable Assert.Equal overload; zero tests run. The original failed build, fixture, output and versioned corrections remain. These are harness defects, not fabricated product failures. Corrected original controls independently establish the product defects before final passes.

Independent reader v1 refuses because several existing PE-inspector cases share the method prefix; reader v2 isolates the exact one removed/added payload-path label and requires the same method. It then refuses a string input path passed to a Path-only loader. Reader v3 normalizes the loader argument; both returned tool errors remain as explicitly transcribed summaries, with no product replay or cleanup before either refusal. All semantic, source and hash assertions remain. The first raw run's additional 160 fixture/source/target/referent sets are also checked absent.

This does not qualify directory links, native Windows copy engines, atomic compare-and-replace, same-size/reverted/post-check mutations, wider provider/account/resource/consumer/native/reference/human/candidate scope, a full App/Remote replay, or the physical source. Exact I282 committed/hosted follow-ups remain. Twenty broader unresolved entries, all 24 final-candidate campaigns, I106/I110 HOLD and owner/GO gates remain. No host UI, VM/Mac, device or policy change occurs.


## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute; nested receipts retain source, raw commands, payloads, failures and cleanup.

| File | SHA256 |
|---|---|
| `follow-link282-file-v2/preparation-v1.json` | `67d9dfcac72e034aaed30565478b46e94445a372ff193cbcb7a669d438c8a0fb` |
| `follow-link282-file-v2/regression-preparation-v1.json` | `d2d237c2d900c23d251e2858a05931de7bbf2fbba208e29d5e8729728992b4a6` |
| `follow-link282-file-v2/Executors.cs` | `bcb67b9e40bf30bb8733477f510537ac59f62d37905cb302673abaad59a8632b` |
| `follow-link282-file-v2/SyncFollowLinkPublicationTests.cs` | `95d10fe29017ec57407ffbc01fff6fdf8d0b80e6b2d981adae21d95ae867e8ec` |
| `follow-link282-file-v2/FollowLinkFileCopyTests.cs` | `85478366196cdc648d8cca11bddd774df5ebbb60d813c1a15c1fa690c8cdddda` |
| `follow-link282-file-v2/run-controls-v1.py` | `0c81ce98f85f12fe45eebb4cf92a8224569b2950c91d8aa94e0025e70ceb450c` |
| `follow-link282-file-v2/run-regressions-v1.py` | `e9dccf615009a7100da65cba2e4d5c8dec6111619ec8869e81a7f72811529307` |
| `follow-link282-file-v2/seal-controls-v1.py` | `fafb5b39c33f2696bcf20711810589877a6a90a0e09a3678b2f5478ee1abf5a4` |
| `follow-link282-file-v2/independent-follow-link-final-v1.json` | `49f7f9ead2a6a3376402aa17481afd3f96bdf15287417088a040ff5ecfd8b122` |
| `follow-link282-v2/preparation-v1.json` | `29bbd493078980949f55f8d8f19bcb9c78760fd907d7ae4d062e2745c50617f1` |
| `follow-link282-v2/fix-preparation-v1.json` | `7f3e47762ff234870ed5a3e5d232d6c74ae6c62eb75a1f1fec4944d3d4535112` |
| `follow-link282-v2/file-preparation-v1.json` | `5a0a70052fb09619b6c96ce593875e739ab3bbe5dfd49b6d6d3e11a4c3e57ba6` |
| `follow-link282-v2/run-controls-v1.py` | `4070f300f7049ca211e1e88c768fd3b5149897779e8ddf06ffb7d47066d3b430` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-v2/original/command.json` | `6c0a738c9a2bc338b268d5ab70f7c0acad73ad70abb101b66c8eba506b0073aa` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-v2/fixed/command.json` | `b5b97dfa9cdfc885df170396ad43594ae8527d259e35966e69f089de4e42e7cf` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-file-v2/original/command.json` | `6411a23c21629e72b24d6625ce4bab5e173d87a8b936802ce49a4fad61d305e6` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-file-v2/fixed/command.json` | `d5006d74bece0d8b78d7759fe48deeed0596cf5de39ba76a7793799860397335` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-file-v2/fixed/app-regression-v1/command.json` | `9d09e3e076930991b1662ceb024b213336542ffe6da8d5cb1f07489362a8e763` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-file-v2/fixed/core-regression-v1/command.json` | `724f038e57fb8b29d5de870682064ca5d894e470d29c448967a9eb2ce878ff1f` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-v1/original/command.json` | `904b84dd7d775228de33f32fff515c1b8411f9d9d6be8093cb93c7cbb445b337` |
| `E:/FileCat/artifacts/release-evidence/follow-link282-file-v1/original/command.json` | `a2414875ba0b3ddbf4b21f8c1a4a7ec1e2b68f9bd563c579de3263e5afd0235c` |
| `follow-link282-file-v2/seal-controls-v2.py` | `f5dfb77b6aa914fb859ca7994d48e46562b79a1f8885f9899738047558e38988` |
| `follow-link282-file-v2/seal-controls-v3.py` | `6970f3a6876127eb62ebd50cbe966867ca8f3427aee9f01e667cccd3d7568338` |
| `follow-link282-file-v2/reader-refusal-preparation-v2.json` | `1113f8d82489e0f8970f9f98a4f79481064c5726657a406d461bb7b69f9c31a1` |
| `follow-link282-file-v2/reader-refusal-preparation-v3.json` | `307670f1b6a9ea418cab86ff3528a4a6fca66aeb4892651ffd593c06d39c7d38` |
