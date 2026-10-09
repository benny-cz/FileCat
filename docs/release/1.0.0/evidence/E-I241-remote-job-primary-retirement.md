# I241 — preserve remote job errors during connection retirement

Updated 2026-10-09. **Remediated preliminarily.** Exact committed source `720354b406e14086ca1e017c09fdea12c73497a5`, parent documentation `6fceb75`; original CI `37867481621`, attempt 1. This is a controlled I06/V08/V12/V23 correction. Twenty broader unresolved issue statuses and all twenty-four final-candidate campaigns remain.

## Reproduced defect and correction

`SftpExecutorBase.Execute` released its connection lease in `finally`. If the original job work failed and a broken or closed-owner channel also threw while retiring, that secondary close replaced the actual original work exception. The correction records the primary failure, attempts the same lease return/close, logs a secondary close error and rethrows the original object and stack. Successful work still exposes an original standalone close failure; pool capacity, ownership, operation deadlines and retry behavior are unchanged.

Identical 192 controls against an exact private `e4cf592` source export plus the new test, and the same export plus the one-file correction, yield **72 failed/120 healthy passes → 192 passed**. They cover four logical profile labels, idle return versus broken/closed-owner retirement, successful work and three primary failure types, and successful closing plus three secondary close errors. The controlled subclass exercises the actual production executor/connection-pool/lease path. Its work deliberately raises owned failures after reading actual `one` bytes from the owned in-memory channel fixture. This is component error/resource qualification, rather than four native wire servers or an end-to-end transfer fault incident.

All 384 original/fixed structured observations are independently checked. Primary exception object/type/message/stack, exact input/recovered bytes (`6f6e65`), one close for retired channels, unchanged healthy idle return, full reacquired capacity and no new connection after owner closure verify before fixture cleanup. Cancellation retains the original `OperationCanceledException`. Source review shows the job manager distinguishes that exception from other failures; these controls do not claim an observed desktop job-state change. The real owned journal is closed and has a positive observed length, but no journal-content/durability oracle is inferred from that length. The two-second reacquisition bound is a test fixture limit.

The same fixed private payload passes the full Remote suite without rebuilding: **2020 passed/156 explicit skips**. Every one of the 1984 prior `e4cf592` Remote case-name/outcome/exact-skip multiplicities remains. All 192 new raw records are rechecked against their actual original fixed records, excluding only stack presentation and owned fixture paths. Private component DLLs retain their actual e4cf-plus-declared-overlay producer; they are not relabelled as committed 720354 DLLs.

## Committed and original hosted evidence

The four approved runtime/test inputs for this correction and [I240](E-I240-paged-load-cancellation-retirement.md) were committed/pushed together. All 1245 direct Git-blob LF inputs and all actual Core/Remote/App payload members verify. Full committed Core is **2444 passed/61 explicit skips**, Remote **2020/156**, and App **1217/25**. Every prior e4cf local name/outcome/exact-skip multiplicity remains. The independent reader inspects all 403 affected observations: 192 current job controls, 48 current page-load controls and the earlier 163 listing/format-reader/active-use/native-overflow records. Existing handled-read and standalone close contracts remain; the native overflow byte predicates retain their actual committed producer.

Original CI `37867481621`, attempt 1, fails: Windows x64 reports a timeout in the preceding held-writer overflow observer; the other three required lanes pass. The original Windows failure log, failed case and absent structured observation remain preserved. No rerun or assumed historical scheduling cause replaces that evidence. All **768 I241 and 192 I240 executions** pass with actual error/resource/byte/order fields inspected. All 23,204 immediate e4cf names and exact skips remain. Exactly one predecessor outcome changes from pass to fail for the held-writer observer; all other 23,203 identities/outcomes/exact skips remain. Current inventories add the 960 new passing executions. Original server-digest archives, raw TRX, toolchains, locked graphs and native API receipts are retained at their actual source. Earlier cleanup/active-use raw records and the three available actual Windows overflow observations are independently rechecked; the timed-out case did not emit its structured observation. The completion-deadline boundary is queued for separate qualification; these two corrections do not close the current CI failure. The older failed c412 attempt, its corrected oracle, earlier native/worker/notice/transfer evidence and distinct CRLF native Git archive remain qualified separately.

Broader native protocol/server fault incidence, actual dropped-transfer/permission/retry behavior, native interaction, reference acceptance, source identity races, installed candidate and stable release remain unqualified by these finite controls. Physical-source HOLD and explicit human GO remain.

## Owned restoration

Three completed private SDK/temp files under `E:/FileCat/obj/rj240` are archived, rehashed and removed, with all three recorded command PIDs observed absent. The actual namespace name remains rj240 even though this record is I241. All 384 private fixtures/journals and the full suite's owned fixtures are gone. The committed `E:/FileCat/obj/k241` root is archived/rehashed and removed after its three recorded command PIDs are absent. Earlier locks and cleanup limits retain their own inventories. No unrelated process termination, machine policy, Mac/VM/USB/account/network change or physical-source validation occurs.

## Selected private evidence

Paths below are relative to private `FileCatReleaseEvidence`. Commands and independent seals contain full source/payload/raw-member inventories.

| Record | SHA-256 |
|---|---|
| Original e4cf job source (`remote-job-retirement-followup-v1/SftpJobs-original-e4cf.cs`) | 032f5f5162a1cb6a6b6e49cbe1809c0b5fa0e9720ce1be0d90b49f3a9c0547e1 |
| Executed private correction (`remote-job-retirement-followup-v1/fixed-candidate-v1/src/FileCat.Remote/Sftp/SftpJobs.cs`) | 37e6d67aa56073ccff64ba034eeba6f977b34d978e8e943a1bb0fa9b96cfac41 |
| Identical final 192 controls (`remote-job-retirement-followup-v1/RemoteJobRetirementFailureTests.cs`) | 087a5f87f3f357dae6d068739bbf2e419faf8d9c1ba12496986e37616fd6ca58 |
| Private preparation (`remote-job-retirement-followup-v1/private-job-preparation-v1.json`) | 98386b479079479d4ec67fa019f6525cfcd75cbcf432a0b24a4eb03b35f733c4 |
| Original baseline command (`remote-job-retirement-followup-v1/baseline-v1/command.json`) | ccfc6186d60b2bc97ced133dffe218ce622a21ec7c1c6c52c2676767a496b759 |
| Original baseline TRX (`remote-job-retirement-followup-v1/baseline-v1/results/job.trx`) | 7e41d39a5f331e90f9f9a7c1a998f8cbc84d0d71ade978f1098c57bf4b04d12b |
| Fixed private command (`remote-job-retirement-followup-v1/fixed-v1/command.json`) | d117dd0c12d57722698e141d45f1dd8337134d42d5e1f05f0cf0b5d8ce027679 |
| Fixed private TRX (`remote-job-retirement-followup-v1/fixed-v1/results/job.trx`) | d716eef53f82fcb5469994bc9faab39246341ce2200cde0824580fd053d4bb49 |
| All 384 private raw observations (`remote-job-retirement-followup-v1/independent-private-job-controls-v1.json`) | 550aa1a07c93b63ea802079c634bc5bc4a217a0338386d55d47f17456f32705c |
| Same-payload full Remote command (`remote-job-retirement-followup-v1/full-remote-v1/command.json`) | 954f812ae3cfc50a9355d908a3e41d3a2308080f6fdaa93c99ce5bce09bfe152 |
| Same-payload full Remote TRX (`remote-job-retirement-followup-v1/full-remote-v1/results/full-remote.trx`) | b245a2df556d64dd80e909071bb82d29c9ecb4e9e4dd9c260cc3bc3f7ed66143 |
| Full Remote prior-outcome and raw recheck (`remote-job-retirement-followup-v1/independent-private-job-full-v1.json`) | 344678b63e09b6c7149b94b3c6911b00fe8d7cc2701db97a3a3a342f8e6f83e4 |
| Owned private temp restoration (`remote-job-retirement-followup-v1/owned-job-private-restoration-v1.json`) | 21ced9bc79847fdda45917aebba193937f3c1fc3b09901781a76995df962f70e |
| Original private temp archive (`remote-job-retirement-followup-v1/owned-job-private-temp-files-v1.zip`) | 87c7f0ca2fc28ba16d3494c479f7426da587dd9646f447ee7f41479305c4a11e |
| Guarded exclusive application (`remote-job-retirement-followup-v1/applied-exclusive-job-retirement-v1.json`) | cbea2d5ccb121d8b8f37d1ff3c2a82071c14a0741f9729b9695a50b6e88b0a92 |
| Paired runtime commit and push (`remote-job-retirement-followup-v1/paired-load-job-runtime-main-push-v1.json`) | a3090d320fc399be0db88c0574e35ad34a5b3409454b7be51484c5e55bc4d7d7 |
| All three committed-suite commands (`remote-job-retirement-followup-v1/canonical-v1/command.json`) | 63bd55652176ded3787c41e6f72dee16760d60408b0b5304662ac28bb7916677 |
| Committed Core TRX (`remote-job-retirement-followup-v1/canonical-v1/results/core.trx`) | e6c24021933a6b21f48941052c03552c553438a40d9f69055cf2b13f80eba102 |
| Committed Remote TRX (`remote-job-retirement-followup-v1/canonical-v1/results/remote.trx`) | c7cdd936c6533eeacba1109a7c96bb683be7fbfda229908eab976a9b3a6f3a3b |
| Committed App TRX (`remote-job-retirement-followup-v1/canonical-v1/results/app-full.trx`) | 96f051938517a7284b3c452877261b9ec11bf9af48c8b8b34662049865ae38ae |
| Independent 1245 source/403 raw canonical records (`remote-job-retirement-followup-v1/independent-paired-canonical-v1.json`) | f143b2dffd4927ce71623e42b0a6357675fd1ef63a70c1fbb435363a830c065a |
| Committed temp restoration (`remote-job-retirement-followup-v1/owned-paired-canonical-restoration-v1.json`) | bb4d4ebbee3e51b8a149658fe93a96ef1e1a15da9b1f87492af48b6ddee4e3e5 |
| Original four-lane artifact collection (`paged-job240-ci-v1/assets-attempt1-v1/independent-assets-ci.json`) | 9deacfd5d03cbff8049d80a73b3bdbd40e2fc0c02de4f475509918976fe54916 |
| Original four toolchains/92 locked graphs (`paged-job240-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json`) | 97b31f5a1a38b4bf213571c5b93b20b8cf702077b5a31c9a628d8ebed8625ed6 |
| Independent original hosted case/raw qualification (`paged-job240-ci-v1/independent-paged-job-ci-final-v2.json`) | 42faddc0488e2c870a2cbc232439e5d31d360025c3994319267165565c359b26 |
