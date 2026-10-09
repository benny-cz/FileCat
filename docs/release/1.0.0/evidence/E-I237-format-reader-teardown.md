# I237 — format-reader teardown and opening-error preservation

Updated 2026-10-09. **Remediated preliminarily.** Exact runtime/test source `c412bd04fa26a26d306dbff291462c98a2d58405`; original CI `37861720364`, attempt 1. Parent runtime/documentation state is `b442a2c`. This is a controlled component correction within I06/V07/V12/V23. All twenty broader unresolved issue statuses and all twenty-four final-candidate campaigns remain.

## Defect and correction

`SingleStreamReader` closed its decompression stream before its actual file stream. A throwing first close skipped the file close and left both references in place. A later member open could fail on the retained stream. If creation of the decompression stream itself failed, the newly opened source file remained open.

The correction retires both references before closing, attempts both closes, preserves the first exception and its stack, and logs a secondary close failure. Opening catches a factory/opening failure, attempts cleanup and rethrows the original exception. Disposal uses that same close path. Public format limits, operation deadlines and the owner-disposal contract are unchanged.

## Observed controls

The same twenty-five final cases produce **eighteen failures/seven passes before the correction and twenty-five passes afterward**. Twenty-four owned fault-adapter cases cover dispose/reopen, failures before/after closing, and opening failures with/without a preceding healthy member. They read actual owned file handles and check exact bytes, primary exception identity/type/message, retired references, handle closure, recovery, repeated disposal and the unchanged input hash before emergency fixture cleanup. These adapters do not establish native decompressor-fault incidence.

The separate real gzip positive uses the public format reader and `System.IO.Compression`, reads all 4096 bytes exactly and observes the captured source file handle closed. The input pattern `(n * 29 + 7) & 255` hashes to `4bdb590eaadb6efc9fc001b29f09b2af9edf289898cd204289fcf5557d97cb87`; its first sixteen bytes are `0724415E7B98B5D2EF0C294663809DBA`. This is an owned-file native-format positive, without a device or physical source.

Original v1/v2 baselines each retain eighteen failures/seven passes but only nineteen structured records: six reopened members threw again before writing their observation. Those missing observations are not reconstructed. Fresh v3 records the six recovery errors before asserting and retains all twenty-five original failures/positives. Fixed v2/v3 retain all twenty-five passing observations. All **113 actual structured records** across the five runs are preserved and independently read. Strengthened gzip handle capture and the original unsuccessful recovery-capture preparation remain separate inputs. Existing affected cases add 129 earlier passes: the same final private payload passes 154 cases in total, including the earlier 72 I235 controls.

## Exact committed and hosted validation

The clean committed export reads every one of the 1238 Git blobs directly, preserving their exact LF bytes. Full Core is **2315 passed/61 explicit skips**, Remote **1828/156**, and App **1217/25**. All previous local case-name/outcome/exact-skip multiplicities remain; duplicated truncated names are compared with multiplicity. Core/Remote predecessors retain their `05832bf` producer and App its `6ac0b95` producer. All eighty-one new canonical observations are independently checked, including I236's exact recovered `one` bytes, full pool capacity and primary errors.

Original CI passes all new controlled cases on four lanes: **100 I237 and 224 I236 executions**. Its overall result is **failed**: Windows x64 has one older overflow test failure and the other three required lanes pass. All 22556 immediate `05832bf` predecessor case identities and exact skip texts remain, with that original single pass-to-fail transition explicitly preserved; fourteen original inventories contain 22880 current case records. Its 21 original server-digest archives, 2731 extracted members, four toolchains, 92 locked restore graphs and 25 native API command/receipt sets verify. [I239](E-I239-overflow-final-state-oracle.md) retains that failure and independently disproves the timestamp oracle; its later source qualification remains separate. The earlier 704 I234/I235 primary-error/cleanup records are rechecked with only actual stack presentation and owned fixture paths excluded from cross-run equality. Earlier helper/notice/byte-oracle producer qualifications retain their original scope.

The sibling native `git archive` export uses CRLF for the affected files. Its separate line-ending qualification and original exact-byte-comparison refusal are retained in I236. It is not relabelled as this direct-blob LF export. No native decompressor or wire-protocol fault incidence, native desktop frame, physical-source comparison, candidate or stable qualification is inferred from component counters.

## Restoration, capacity and presentation correction

The six private-run temp files are archived, rehashed and removed; `E:/FileCat/obj/ar237` is absent and all six recorded command PIDs were absent before cleanup. The exact-source cleanup archives and verifies 11 files, removes 11, and retains 0 locked files with original hashes and refusal messages. Its owned `E:/FileCat/obj/k237` root is absent. All three exact-source command PIDs were absent before cleanup. No unrelated process was killed and no Mac/VM/USB/account/network/security setting changed. Earlier independently recorded compiler locks and aborted namespaces remain qualified at their own producers.

Five completed retrieved Mac trace logs are compressed transparently in place with explicit-file ordinary NTFS compression. All 6005024336 logical bytes, SHA-256 hashes, timestamps and original paths remain. `GetCompressedFileSizeW` reports a storage-size reduction of 4448712272 bytes; observed C: free space rises from 822943744 to 5275561984 bytes. No evidence is deleted, moved or renamed; active outputs, CI collection and NuGet roots are excluded. No directory inheritance or global system setting changes.

Two literal I234 paths had gained a space during the preceding prose presentation cleanup. This batch removes only those two inserted spaces. The original leaf bytes and Git blob, original writer and exact replacement receipt are preserved; every other prior evidence leaf remains byte-for-byte unchanged. Referenced raw files, selected hashes and producer qualifications are unchanged. Original unsuccessful document-baseline preparation is retained rather than overwritten.

## Selected private evidence

Paths below belong to private `FileCatReleaseEvidence/archive-reader237-v1` unless a sibling is stated. These hashes are a selected input/output inventory; the independently sealed commands and original CI archives contain the full payload, source and raw-member lists.

| Record | SHA-256 |
|---|---|
| Original format implementation (`archive-reader237-v1/ArchiveFormats-original-b442.cs`) | d04be61453bc9e0459158826c485d286088a87cc3fb6f8241b92bcbfd9cab2f9 |
| Applied format implementation (`archive-reader237-v1/ArchiveFormats-fixed-v1.cs`) | a5347ff264214f83db874b656906280e68e8d5055164d5fd30cb469fa04e9bf2 |
| Original lifecycle controls (`archive-reader237-v1/ArchiveReaderCloseFailureTests.cs`) | fc0d6d353a85c6c2efc8259c3f925408e2c5662111efbdad47b7e9c8b9406f53 |
| Strengthened native positive (`archive-reader237-v1/ArchiveReaderCloseFailureTests-v2.cs`) | 5aa73ed6f6d23e260b2cde1da383954c141dcc8637da39b977799db87a687601 |
| Final recovery-observation controls (`archive-reader237-v1/ArchiveReaderCloseFailureTests-v3.cs`) | e6966bcd5f056bc279b8feb4d011dba528aad63c402abc8113e5499ad4c92a7d |
| Native positive preparation (`archive-reader237-v1/native-positive-input-preparation-v2.json`) | f89be48f91a70f040be62723742dc3c208981b66c15ceba286d136b8da8b2bd8 |
| Original recovery-capture refusal (`archive-reader237-v1/recovery-observation-capture-refusal-v2.json`) | cca5f20b86eac3b73d6c5b26a8132d015ca73169a902a1ce68a613d51fe213bf |
| All five private runs independently read (`archive-reader237-v1/independent-reader-controls-v1.json`) | c16afcce60ef13e64b8e39003be7b18c893596602d1361dbcffc889dd6df24d5 |
| Baseline v1 command (`archive-reader237-v1/baseline-v1/command.json`) | 034d865b62e1eefb5949c3a92818de3bab10ceb012fa5407e95277453f475c52 |
| Baseline v1 TRX (`archive-reader237-v1/baseline-v1/results/reader.trx`) | 085d8b4ca11bf1e71569cb2295584ab366a9d37aefae95ba67d93561ebcbab5f |
| Baseline v2 command (`archive-reader237-v1/baseline-v2/command.json`) | 7fc6d7e9fc48e4c233a3177618b6b97902d0682b8d573e8cfd6bd8bdb0759dbb |
| Baseline v2 TRX (`archive-reader237-v1/baseline-v2/results/reader.trx`) | 4a1ba480b53a92d2d9847be2e4089bb12585d574bd0be9ea96cb1c625e27df7d |
| Fixed v2 command (`archive-reader237-v1/fixed-v2/command.json`) | 69baf5b5f6c53956d07d606b585b6ec32c2618a8b6945aca4b49b5c4fbfbf582 |
| Fixed v2 TRX (`archive-reader237-v1/fixed-v2/results/reader.trx`) | 72de16ea18f7cad5f0ab0a17cde44eebdf7ad65699404c3d7b6e86437d73cbf5 |
| Final baseline v3 command (`archive-reader237-v1/baseline-v3/command.json`) | 3a165607592b5949327e5485e9b9e8bf05c35a2bce4dd655a21c0078c1c12fed |
| Final baseline v3 TRX (`archive-reader237-v1/baseline-v3/results/reader.trx`) | 806ebdf8b32d833073199d19b0b9279e8c90517ce641ed4f722c762068cff0d3 |
| Final fixed v3 command (`archive-reader237-v1/fixed-v3/command.json`) | c667d334c8bf2a1df54930a8ce5d13ac96a88c4e23bd70cc8a1da98f1b89931e |
| Final fixed v3 TRX (`archive-reader237-v1/fixed-v3/results/reader.trx`) | 1d800cc28f7f8edcbbdd5e8614a5c8648af642922aa2b70f6199d73cd8780e62 |
| Existing affected selection command (`archive-reader237-v1/affected-existing-v1/command.json`) | 89a9d94440bab57b98a76ea2b8e853ba455accc337f270a861ba645b64de5174 |
| Existing affected selection TRX (`archive-reader237-v1/affected-existing-v1/results/archive-existing.trx`) | 2c45801379a74bdeeca1e96512b4715af6c845e64f77597b6a3c318d66389e8c |
| Exclusive guarded application (`archive-reader237-v1/applied-exclusive-reader-fix-v1.json`) | db73e587bdc42e62f291ef350e91c87d9a95d2527525602632f2396c6da221ae |
| Combined runtime commit and push (`archive-reader237-v1/combined-runtime-main-push-v1.json`) | dc0259a51bf94300baa65c701b7d6e7bd0faea50d3e31d8700590d83dc6bda7e |
| All three exact-source commands (`archive-reader237-v1/canonical-v1/command.json`) | 983fafb9a655f0af70a92e93138fb646b15b5087387681fac833e1026532a07c |
| Exact-source Core TRX (`archive-reader237-v1/canonical-v1/results/core.trx`) | ebe7f0e97a2bb3dd50a1adeb227daca72f9488d99e3a683b7a9e46470cb392b3 |
| Exact-source Remote TRX (`archive-reader237-v1/canonical-v1/results/remote.trx`) | 9c0ee546b71cf017db2d31a41fcc3ab0f4fe6fe35efd9de0a950cc5d154d2e56 |
| Exact-source App TRX (`archive-reader237-v1/canonical-v1/results/app-full.trx`) | 61ab54ef78bd864d45f009f3240f99e139616fba96dfcde1f1d02e0f4af4a4e6 |
| Independent canonical inventory and raw observations (`archive-reader237-v1/independent-canonical-reader-batch-v1.json`) | 8148c74f6aa48a7a9e8efde9ad9ee59c1c37e2e12d3fb6b75ed8e89caaf73848 |
| Original four-platform CI and predecessor comparison (`listing-reader236-ci-v1/independent-list-reader-ci-final-v2.json`) | dd19dbcb0cc640fe95162a194ff4dcce5a05310d3d4287184aa58fc1d4857b02 |
| Original private temp restoration (`archive-reader237-v1/owned-reader-private-restoration-v1.json`) | 3125ad47a810cb2db9397003924b9ff9f329238d6f6ecbf0de60fe5a53311563 |
| Exact-source temp restoration (`archive-reader237-v1/owned-reader-canonical-restoration-v1.json`) | 27a6502c377ee6b5704d8d55b591854c6f2bbd1fff5f4803c15ba0690d33250b |
| Completed trace compression and exact byte preservation (`archive-reader237-v1/completed-trace-text-compression-v1.json`) | 9db35f97892416053fd46cfdcd4d4ef55d6d2f651c3edb5f038d00f36648f8af |
| Original native compression journal (`archive-reader237-v1/completed-trace-text-compression-native-v1.jsonl`) | 2f465e847b4b3e506f772dfc6bd33918ee3e9561784e4e2c383c6666b4b71c99 |
| Original I234 display mistake preserved (`archive-reader237-v1/original-I234-display-path-mistake-preservation-v1.json`) | 63d6109ee059332453ec5464c1bb6597953cc0f2a517f1ca634bad3e55233a71 |
| Exact two-path presentation correction (`archive-reader237-v1/I234-two-literal-path-display-repair-v1.json`) | fee21e1ef4340bcbd6c65a4ec512aeca131303b080a944c4cdb2702c0fba7b9b |
