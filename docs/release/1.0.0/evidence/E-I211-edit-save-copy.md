# I211 — edit save-copy ownership and complete publication

**Preliminary remediation qualified at 6a9782567bccbbcb33ef9516e7277b97e2c4e1ac.** All 47 additions pass. Expanded working and exact Git-canonical clean runs each pass 564 targeted checks (96 Core, 90 Remote, 378 App), with eight explicit existing environment skips and no new skips. The full clean App suite separately passes 1011 cases with 23 explicit skips. Every previously qualified targeted/full App name and outcome is retained. These are component/headless controls with real owned files and a synthetic picker result, not native picker, hardware or candidate qualification.

| Scope | Qualified behavior |
|---|---|
| Picker and repeated requests | Demand is checked before opening and after answering. One copy owner spans the picker, capture, publication and cleanup. Cancel/nonlocal answers do not write; a subsequent request works. The returned storage item is disposed. A conflict's separate commit owner remains held. |
| Source device and lifetime | Actual registered content opens/reads on the shared source worker. Queued calls do not start after stop/close. Active calls retain ownership and source lifetime until return; later publication and notifications are suppressed. |
| Complete source | Finite 1 GiB capture requires exact length/EOF, valid read counts, stable available revisions and no missing/uncertain bytes. Missing/null/I/O/access/unknown/oversize/short/grown/changed-revision controls keep the destination and session. |
| Destination device and lifetime | Destination staging/publication uses its shared worker. Queued/active copy and move controls preserve ownership. An already entered native move can finish after stop; its task waits for return and emits no late success. |
| Exact publication | An immutable owned snapshot is copied to a unique destination sibling. Length/EOF/revision/SHA-256 must match before replacement. Failed, partial, silently truncated/corrupt/grown copies and failed moves preserve the previous destination; owned staging/snapshot cleanup is checked. |
| Current edit and persistent state | Capture uses the edit after the picker answer; later editor saves do not change captured bytes. Successful 0/4/1,048,577-byte outputs are exact and leave the persistent edit open. Selecting the normalized working path is refused with a useful message. |

## Original behavior and retained failures

The unchanged 4966004 producer fails all 47 final controls: 12 caller picker/admission/ownership failures, two existing same-path-refusal diagnostic checks, and 33 provider/native-entry checkpoints that the original direct File.Copy bypasses. The latter holds/errors were not injected into the old copy. Its three ordinary success controls emit the correct byte hashes before failing the shared-worker assertion; this does not claim those ordinary bytes were broken. The original two same-path cases already refuse on Windows; they fail the requested diagnostic, not a demonstrated overwrite.

Baseline-v1 retains the earlier 44 controls; baseline-v3 executes the final 47 on unchanged source. Working-v2 passes 42/44: negative/oversized returned counts raise InvalidDataException outside the caller's error filter. The corrected filter reports refusal and preserves both files. Working-v4 and clean-v5 qualify all 47 and the broader suites. Prepared runner v2 fails Python syntax before creating/executing a stage; v3 corrects that recipe. Raw diagnostics, source archives/overlays, command receipts, actual payload hashes and TRX remain preserved. The independent reader rechecks all of them and reconstructs the emitted worker/lifetime/file observations.

## Remaining qualification

The first cross-record audit (v131) failed a Python tuple-versus-JSON-list comparison after verifying the underlying files and raw inventories. Audit v132 normalizes the reconstructed value through a JSON round trip and retains strict equality on every field. The failed script and `cp211-v1/global-reader-guard-v1.json` are preserved; no original test/build/evidence is changed.

The synthetic picker exercises the real caller but does not validate a native dialog or its overwrite prompt. Snapshot capture writes owned temporary data while source-admitted; broader cross-volume temporary-device admission remains open. Destination replacement between picker/copy/move, path aliases/hard links, parent substitution and native atomic identity are not qualified by these byte checks. Origin-mark failure handling, crash-left sibling staging, cleanup denial/sharing and larger workload/native-frame/candidate repeats remain queued in I06/V07/V08/V11/V12/V23. No physical source, VM/Mac setup, freeze, candidate or stable publication is changed.

## Provenance

Private `FileCatReleaseEvidence/cp211-v1`:

| File | SHA-256 |
|---|---|
| baseline-v1/app-stderr.txt | 1d9b0b399aa043cd8d99e0066edc1bb16ac95d8d09d6ae3c5cf48809f79e1e16 |
| baseline-v1/app-stdout.txt | 56dd9d3a28b7d4b7f7718151586aa6976dfe6bd24c1fc2ebc1fb0ba4d436b927 |
| baseline-v1/command.json | 7d109c3b4417ed09be8e5c01deda22652f089570f093ad092f6d29c1470407ac |
| baseline-v1/source.zip | 70829bd4255ca698da1bfa65d9cbda7f711e6fd1b8ed4d0bc27d541fbfa93101 |
| baseline-v1/results/app.trx | 1766af87a66656c70f9f49f64f1acf3ace27eaba683fd73572c9b43b42e26aad |
| working-v2/app-stderr.txt | ab09aff50a68daa9e680f9314092e80aa34d5b8c6e312c94d6adb70d2fe9952e |
| working-v2/app-stdout.txt | 382a240d773e98af8fbcf93aad07b08c15bdef0720415f21741ae3555660d2b0 |
| working-v2/command.json | acf0310f88c231d75b398d7705223460e51b2127118c52ed6833167d2aff7933 |
| working-v2/source.zip | 0278aa1078bfe2950275f582b20173a82d10e69fbe11a2595586d5947a3d47e4 |
| working-v2/results/app.trx | f8bdede3929d80708064269d41606eda1cc162eb97a804de6015dd8e226653f9 |
| baseline-v3/app-stderr.txt | 12016fa08fb2506f1b18c34617eecdf181c620c1dd01db849253e424e95f57cf |
| baseline-v3/app-stdout.txt | c8864fc0610e0ba173a64764f6f2c00576eb78a805445b6a3a133fe14f0e7dbe |
| baseline-v3/command.json | 97029e7ddf8fe74e72102f9d75395fe08286a60b002994ec190fb1a1a6f926a9 |
| baseline-v3/source.zip | b245c1561e368821857a9c8275a1cc00d0ab80718e36d49dbee239fdc4c5fc42 |
| baseline-v3/results/app.trx | 0f0876f644a3a89b59d912d0a00ec2c91a6ff3550d348328a6e3538a094ddfae |
| working-v4/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/app-full-stdout.txt | f6b484de8c9ee3205bcede0dd5c2fa77f06bac03795dc6e2fcc92792c0b02966 |
| working-v4/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/app-stdout.txt | 45285ddc5a441e5129be59932b779d1bcbad35f319eecbe28cdcf1a7ebc577a5 |
| working-v4/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/core-stdout.txt | 91092278b6db7f721904375182a42139fea2ea6e64c57c9532d01ab9e2816940 |
| working-v4/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| working-v4/remote-stdout.txt | c2770ca759faa4c3093a0db8e408da7af50057e78ea39023baacfa0e04553f15 |
| working-v4/command.json | 0c38d714fbf3a1aa8f0774102c151d7b900dbad5afdef29f9b939e2459331b03 |
| working-v4/source.zip | 577307a3a791c2fcd21e38d1323a7b754aefefc4c4c97f2a252d8ca661a5bcaf |
| working-v4/results/app-full.trx | b37ffc9aee3ace3d6f564453d29f8eb23c6017e953101e7896b74e9a5392b4fa |
| working-v4/results/app.trx | 89ad3a1408f8daa77dc693c5cda8075193dd4509bf58536927369dd6486c22ee |
| working-v4/results/core.trx | b9c32d9512bd9cb04e99a2390676f085d35ba9f3e724a4646face3190d58d6fc |
| working-v4/results/remote.trx | c750bf84b559fc556838bcb117e672a88b2e7252707c5db9d636794d608b141a |
| clean-v5/app-full-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/app-full-stdout.txt | 338117d1f16fd6232392adfcc4dfcaff6add411e3d928a88b50e6fc01f835792 |
| clean-v5/app-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/app-stdout.txt | 8ee22d827d4cd2ea43afe22eb6824dfcf35eccc337a4a6a89b004bb1bec554cf |
| clean-v5/core-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/core-stdout.txt | b478d35d3df65a572a8c4cf989a055a4ed12f0f34717e7630e2120515e94e0b7 |
| clean-v5/remote-stderr.txt | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |
| clean-v5/remote-stdout.txt | bca5ca25ac468a37c2c6318c2d7948cab2d4aa6702a18c5cf8a4f6e660feb71c |
| clean-v5/command.json | 8be42afe0d4b762e7ec8f0597d170fbee077221899b063bdf8b50692149b8bbc |
| clean-v5/source.zip | c43f7706bd2f5e0101e6aaea7bacb89efb2d233b93de56c5b61f639a71526cd4 |
| clean-v5/results/app-full.trx | 7ae035cd5ee66826b4d75bcb9e514ed5c652ba388ea19ad06e9ad4c343fbf2c0 |
| clean-v5/results/app.trx | 3238f6568b6b93afa71fb1dee35e7e90729f6bda13a1966cf186433f0156f009 |
| clean-v5/results/core.trx | 6839415bda3b31f1eeda6248508dbaf95f71ba5fdf8dc5e85f7933136c801571 |
| clean-v5/results/remote.trx | 1de47ede0f76eb6679a66aa8b549518f579ef788471763e16efbf590d11e60ca |
| run-save-copy-v1.py | 5f77e35f0c2c846344109438382fd94fc5673c6f66e89846d162a17f6e3c025d |
| run-save-copy-v2.py | 960829d6c159e5fcbfc1977e16da31426882e1e312dc7ce0469aa22246765c1f |
| run-save-copy-v3.py | 45d51f0f9a38bc68560f8e1b872fa7871929271a6c711ab062832160e20e9b41 |
| seal-save-copy-v1.py | 2bb1620ba8ebbdf2c1d3fb9b291fc4976edc307be62a500b6281e60585725d10 |
| independent-save-copy-clean-v1.json | e1a799503e384c569193b34e38a3a4e2774f51f79327679ed2cdd52a250b5f4d |
| update-documents-v1.py | ce71b56599a37b17fff35cb260297038ef1042d62c8c840979039a7019e407e8 |
| document-transitions-v1.json | 3ac58c8721d42646ea915edfc9e1f1956454895aae698a0641972261a0775a4b |
