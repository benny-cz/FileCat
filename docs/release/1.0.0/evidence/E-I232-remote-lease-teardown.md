# E-I232 — remote lease and connection teardown

Runtime/test source `6ac0b95a41299e71f5159a1a5f8df9052aed3305` fixes four proved connection-lifetime defects: a failed content close loses its lease, a failed broken-channel close loses pool capacity, a disposed manager can connect again, and shutdown stops at the first failed idle-channel close. Severity is Medium for remote availability and resource lifetime. This finite component finding does not claim an observed native server-close fault, complete I06 resource qualification or a release candidate.

## Controlled failures and correction

The final same-test-source baseline exports exact c17cac5 Git blobs and overlays only the new tests: 56 failures and four healthy passes. Sixty cases drive the real SftpConnections/SftpContentSource through owned channel adapters, using the four profile policies SFTP, FTP, explicit TLS and implicit TLS. Those policy names establish pool limits; the adapters are not actual protocol endpoints. Controls independently read the owned three-byte file, retain the original exception object, count single closes, acquire all two/four slots simultaneously and observe channels before fixture cleanup. Owned fixture channels and state directories are cleaned after every case, including assertions that fail.

Three exception families (I/O, access denied and disposed) cover content close, broken lease return and manager shutdown. Content controls include a second channel-close failure to ensure cleanup does not replace the first stream error. Manager admission is checked both after disposal and when disposal occurs during the connector callback. Healthy content/revision/full-capacity controls remain.

The corrected content path retires uncertain channels and returns its lease while preserving the stream error. Lease return releases the slot in finally, including a throwing channel close. Manager admission refuses a disposed owner, checks again after waiting/connecting and avoids recycling returned connections after shutdown. Shutdown attempts every idle close, then rethrows its first error with the original stack. Existing production connection limits, two-minute admission wait and idle timeout remain.

The complete fix passes all sixty controls. Original first 48-case results (44 failures/four positives), captured repeat and initial 48-case fix are retained. Twelve later drain controls fail on that provisional fix before the final correction; those observations are not discarded or relabelled. An initial repository guard refuses CRLF worktree bytes before mutation; a fresh guard proves CRLF-only equality to canonical Git blobs. Executed scripts, inputs and output records remain unchanged.

## Exact committed-source qualification

A clean, independently blob/archive/payload-pinned export of `6ac0b95a41299e71f5159a1a5f8df9052aed3305` has no overlays. Full Remote has 1668 passes/156 explicit skips and full App has 1217 passes/25 skips. All 2,973 immediate local predecessor names, outcomes and exact skip messages remain, and all 66 new Remote/App observations are reread. Remote additionally executes 27 existing cases excluded by the earlier maintained filter: six pass and 21 explain unavailable or opt-in native/server/benchmark fixtures. Those extra skips are not regressions or invented passes.

Original CI 37852919347, attempt 1, passes all four required lanes. The sixty new remote cases execute 240 passes; the six launch cases execute eighteen passes and six explicit off-Windows menu skips. All 21,588 immediate 8a predecessor names, outcomes and exact skip texts remain; the current fourteen inventories contain 21,852 outcomes. All 21 original archives and 2731 members, four toolchains and 92 locked graphs verify. Earlier FTP/Git/picture controls pass on every lane. Earlier transfer, notice, native-worker and server byte-oracles retain their own producer identities; aggregate counts do not relabel them as this producer.

Child-only TEMP/TMP/SYSTEMTEMP use an owned E-drive tree for local App fixture capacity. Owned-process and deletion observations are recorded in the restoration receipt; parent/machine settings, Mac, VMs and USB are unchanged. Earlier nine locked compiler files and aborted C-temp cleanup remain qualified by their original records. Broader server/account/permission/drop/atomic/native/human/reference/candidate work, physical-source HOLD and explicit human stable GO remain.

## Selected evidence

Private `FileCatReleaseEvidence/remote-lease232-v1`. Nested source, payload, command and raw-observation inventories keep all original bytes and qualifications.

| File | SHA-256 |
|---|---|
| independent-focused-lease-final-v4.json | 7ab2188b2796ac53ddd3327eaa735f920ab7d074df312e767ec2693cd61baeb1 |
| baseline-final-v4/command.json | 373e9466a268a98aff5a1d89285adb2db45226a84119c389d32569bab1710398 |
| baseline-final-v4/results/lease.trx | e74c26297a33d607a0c1dc5f964bfc937601934fc26246c5bc2f1752ef6aa7d8 |
| baseline-final-v4/observations.json | 6e979a1604d004b9a233556d9e8723936252557933e0568a4367efcf8455b77c |
| fixed-final-v4/command.json | 4dc98c849047d50603e7f1144393d23cd0536f8e50610ad68978749714b1b64e |
| fixed-final-v4/results/lease.trx | 926352a441dac4bebfb0a083d82e3f614c0fb5856d8948acf934d14813306263 |
| fixed-final-v4/observations.json | 9c15862e0090aab7e10fc06b7016fe5689b8bce900c6eca1654fe2ec32bf0379 |
| working-owner-drain-v3/command.json | 082f8ab28c1501b80a3e98b191bbe71018d80d2f9f367afc53ad8919dd19f408 |
| working-owner-drain-v3/results/lease.trx | 3b3f388655eb9677e72a7393b20b714d6cb47bbf61d089369aa9ef004cd4525b |
| independent-focused-lease-v1.json | 49b916c20d989d96ddb3ceb311132c3887628a386c1b1c0231eaa7c835862ba7 |
| baseline-v1/command.json | 796de4f0acb8b0aa6a3f29972fb9bf345026623192a5c181dea799338462d891 |
| baseline-v1/results/lease.trx | 77c3a9727efab105995bb13681e88c9f7e16f9ef052d33e9a596a2483452f4ee |
| independent-full-remote-v1.json | 592460806e6f69022066844f68d06beb0eb385fdf358ca6ecf141a965003dc48 |
| independent-canonical-batch-v1.json | 3b9d0ae7312444b158788b49bf4f1b0780639b20a168cc5bf6095f48e5a6f579 |
| canonical-batch-v1/command.json | b87821acc20ec3fe265823cddaf85bab5e94ea13574d3284b1f1b866341010b7 |
| canonical-batch-v1/results/remote.trx | 5caebec11f13a5b3d59d538c9124b1cddc834c546a370c7e0109b21df2a3b11a |
| canonical-batch-v1/results/app-full.trx | cc29b01d95f859821eda15afbd047f97f89f616afb8cc7122f81dc14913a901e |
| runtime-main-push-v1.json | fa7a67e33be323fbc3160dd302230ac976ac9bb5caf0b4f53b4700b2082bb452 |
| repository-mutation-refusal-v1.json | f5975792f278af87efde1c21e5a3eb0dd3c6472db806f82113a8fdf73f293761 |
| repository-remote-mutation-v2.json | 62d38928e72751b0ba43ff22d6cf75ba975054d4a6ff56027ebd59040ab7da1e |
| repository-final-lifecycle-mutation-v4.json | 344ba415b651e379211cd9be835f0f9415ca651a0bc8da7addd75e1c2bd73413 |
| owned-canonical-temp-restoration-v1.json | bbe2c85543b62136b7a58e31385589c97cf628cf6c4b861a07c41330db7d1b1b |
| ../runtime-lease-host233-ci-v1/independent-runtime-ci-final-v2.json | d55db9d3578eab91479fcd62a2a6901048aaf596ca63731d08a4f4941bb31b60 |
