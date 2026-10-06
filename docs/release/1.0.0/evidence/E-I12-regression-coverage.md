# E-I12 — historical regression coverage re-audit

**I12 is Closed for preliminary regression-coverage scope. Candidate repeat remains required.** Both historical failures have durable controls at current source a0a8ecea3958f5e4d3d70b9dc57f6c5a735b3796. The original intermittent Mac failure was not forced and is not claimed caught by this rerun.

The [frozen I12 history](../FILECAT_1_0_RELEASE_ISSUE_HISTORY_20261006.md#i12--the-two-historical-failures-have-lasting-coverage) retains the original failures, fixes and earlier evidence. Windows comparison fix 62bd88fda78587bf43e99f7eee775cf7a24efa7d, Mac title-observation fix 2df5c86b287edacfd5159338c26b3e84d466c077 and lifecycle coverage 85d512d4f36fa547af6c24040cbb6e3d711c6c9d resolve in repository history. This audit follows the operational plan's historical-regression requirements; it does not turn the older red run into a current defect or count extra waiting as race coverage.

## Windows descriptor controls

Original [CI 37541603291 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37541603291) at a0a8ece passes both required tests on Windows x64 and ARM64: four distinct executions, independently reconciled to full raw TRX attributes.

- `Security_descriptors_are_compared_part_by_part_not_as_text` covers structural equality and real owner/group/DACL flag, order and entry differences, including the unmarked-inheritance vector and intentional special SACL handling.
- `As_administrator_a_DACL_stored_without_inheritance_marks_is_the_same_as_Windows_reports` creates an owned live NTFS fixture and compares FileCat's descriptor from `$Secure` with independent Windows `GetSecurityInfo` output. Its non-admin/non-NTFS branches explicitly skip; both retained executions passed.

The complete CI inventory, server digests, raw files and selected IDs remain in the [I167 seal](E-I167-git-alternate-objects.md). No physical G: source run was added.

## Mac page-title lifecycles

The unchanged current `tests/FileCat.PageEngineSmoke/Program.cs` ran twice: hosted macOS CI and the owner's physical Mac. Each run creates twelve independent engines, navigates each through both HTML and Markdown titles, disposes it and checks that no title/loading/refusal callbacks arrive during a subsequent 200 ms Cocoa run-loop interval. Both report twelve of twelve title lifecycles and zero events after disposal.

The physical run used macOS 27.0.1 arm64, ordinary benny UID 501 and existing .NET runtime 10.0.12 arm64. Windows SDK 10.0.401 produced the locked, framework-dependent osx-arm64 smoke payload from a fresh export of all 1,056 canonical source blobs. This is explicitly a Windows cross-publish, not a Mac SDK build. Its actual `FileCat.dll` SHA-256 is e328b07709cf11d6e6d7158c0f928bc33297b4225861cebb0609bc72daf635b8; the 69 produced/deployed payload pins agree before and after execution.

The process exited naturally with code zero and these exact final lines:

```text
loaded: True (); title: FileCat test page; refused: 1
markdown loaded: True (); title: README.md; refused: 0
lifecycles: 12 of 12 showed both titles; events after disposal: 0
PASS
```

A separate remote recheck verified all 69 deployed hashes, absence of the owned worker/awake PIDs and removal of all GUID test directories. Only the empty owned test parent remains. Bounded `caffeinate` exited; no persistent machine or power setting changed. The first transport attempt timed out before running the smoke; its failure receipt remains retained separately.

The earlier raw owner-Mac smoke log is copied without alteration and retains its original SHA-256. It lacks a compiled-payload pin, so it supports history only. The current hosted CI raw step is matched byte-for-byte to its original log archive; no unretained hosted compiled-payload hash is claimed.

## Independent seal and limits

The independent seal checks 54 retained files, 69 original payload files, 1,056 canonical source blobs, six relevant source blobs against Git, four Windows passing executions and both current Mac lifecycle outputs. Preliminary coverage closure does not close wider I06/I13/I25 integration, native interaction, human testing or exact-candidate qualification. No system input automation, physical source access, candidate or publication is involved.

Private `FileCatReleaseEvidence/i12-regression-audit-20261007-v1`:

| Path | SHA-256 |
|---|---|
| historical-i12-page-smoke-mac.txt | da193a7a5fffe5a7d1703c874000c6239299223e4afbcd18991f78bc68c021f2 |
| mac-publish-receipt.json | bf2d29a3a353e0885fe0b722798add492a38940f46c583961cf773ba6541a703 |
| mac-payload-pins.json | d4fb6594b3b7c330dff112076d19d1d07caedf9428f7cd8ada1c3c3bd643950a |
| native-attempt-v2/mac-native-command.json | ae999fa6022ecb5257f6bfcddcc816c4d11e24a4e0677e0dc031abd652eeb011 |
| native-attempt-v2/mac-native-stdout.txt | 01a3bd01336dac652296d793d44beb07b46e1f6f6b55d530c90a557a7eba3932 |
| native-attempt-v2/mac-runtime-info.txt | 01133323c8ad815e088782fb5c33cd89bc51058660f7a048421ff55d658242bd |
| native-attempt-v2/independent-remote-recheck.json | 78bbc73153189ed27b57378c2eee9976a7523a2357a14b989b879db7d3b77df6 |
| ci-macos-page-smoke-step.txt | d28b7d6432ca328f9029684292f6338abc1397305cb07624b2d343bc3702ec35 |
| independent-i12-coverage-v3.json | 6b26b8104aebf15951f519995eaf3d675754113aa5ac86a6bb178ce056626a9c |
