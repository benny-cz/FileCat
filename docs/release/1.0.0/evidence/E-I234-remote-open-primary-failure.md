# I234 — Remote opening retains its primary failure

Executed evidence for a reproduced remote-opening error-masking defect, its committed correction and exact-source local/hosted revalidation. The finite component scope does not close the wider release gates.

`SftpProvider.OpenContent` previously called `lease.Dispose()` inside its error handler. If a channel became disconnected and then threw while closing, that secondary exception replaced the source's original `Stat` or `OpenRead` failure. The correction attempts the same lease retirement, logs a secondary close failure, and rethrows the original exception. Existing slot-return behavior is retained. Severity is Low–Medium: the reproduced result is a changed error diagnosis during failed source opening, not a native protocol or security incident.

## Executed controls

The unchanged production baseline is `6ac0b95a41299e71f5159a1a5f8df9052aed3305`. Identical 104 controls run against that export and the one-file private correction yielded **72 failed/32 passed →104 passed**. These use actual production `SftpConnections` and `SftpProvider` with owned `ISftpConnector`/channel fault adapters and four logical profile labels (`sftp`, `ftp`, `ftpes`, `ftps`). They exercise both opening stages, three primary exception types, three secondary close faults plus healthy closing, and eight healthy source/idle-return controls. They do not use four actual wire servers or establish native fault incidence.

All 208 raw observations were independently inspected. The corrected controls retain the actual original exception object, type, message and stack; failed channels close once and leave zero active channels; every case regains all configured lease slots and reads the exact three recovery bytes `6f6e65` (`one`). The two-second fixture acquisition bound does not change production timeouts. Full private Remote validation on the same fixed payload passed **1772 cases, 156 unavailable**, retaining every 1824 immediate canonical 6ac Remote name/outcome and all 156 exact skip messages. Current local full App and release-candidate qualification are not established by that run.

Only the provider and new Remote test were applied, then included by the parent in committed source `05832bf6e90ee2877ec728bbf27b91b2204b4533` alongside a separate Core correction. Git bytes of those two files were independently compared with the approved applied bytes; the test's executed CRLF input and committed LF input were explicitly normalized for that comparison. The private rebuilt DLLs retain their actual controlled-overlay producer and are not relabelled as a committed 058 payload.

Fresh canonical 058 Git-blob export qualification passed **full Remote 1772/156 unavailable** and **full Core 2290/61 unavailable**, with all 176 new I234/I235 raw controls independently reread. All 1234 source files and both actual payload inventories were rehashed. The original long-home Core run had one real native GPG fixture failure; the same canonical DLLs passed that existing case and then full Core with a shorter child-only temp path. Fourteen owned native GPG controls independently establish safe and adverse home lengths for the installed tool context; they establish no archive product regression or security-policy failure. A reader's duplicated truncated Core display-name refusal is preserved; the fresh reader reconciles stable test IDs on the same payload without changing raw results. Earlier local full App 1217/25 retains its actual 6ac producer.

Original hosted **CI 37857868887 attempt 1** passed all four required lanes on exact 058 source. All 104 I234 cases passed in every lane (**416 actual executions**), with actual original error objects/types/messages/stacks, close/active-channel counts, full capacity and exact recovery bytes independently inspected; all 72 separate I235 cases also passed (**288 executions**). The proof preserves 21 original server-digest artifact ZIPs, every extracted member, 14 raw TRX, four toolchain receipts, 92 actual locked restore graphs and the ARM fixture's actual pip report. Every 21852 immediate 6ac case name/outcome and exact skip text remains unchanged; current totals 22556 derive from actual raw inventories. The first hosted reader refused 67 passing Windows observations because it assumed a helper stack frame; that refusal and all actual traces remain preserved. Its fresh version checks the actual nonempty traces and primary object/type/message without demanding release/JIT frame presentation. Prior finite transfer/notice byte-oracle qualifications retain their earlier producers; these are not replayed merely from counters. No stable tag or release was published.


## Preserved refusals and restoration

The first actual 104-case attempt is unqualified because the fixture guard appended an extra trailing separator to the Windows temp path; all104 original cases failed that guard. Its raw source, command, TRX, observations and 104 owned fixture directories remain preserved. A fresh input version corrected only the fixture-prefix calculation before the reported same-input baseline/fix runs. An independent reader first expected a literal secondary `ObjectDisposedException` message; a fresh reader checks its observed constructor wrapper without rerunning or changing the product/test input. Two PowerShell/Python preparation quoting refusals are also retained; they did not execute additional product controls.

All four recorded test PIDs were actually absent during bounded restoration. Seven owned temp files were archived and rehashed, four SDK logs removed, and the complete `remote-open234-temp-v3` tree removed. Three compiler-locked files remain pinned under the exact old `remote-open234-temp-v2` root; this record makes no complete restoration claim. No process was terminated, no global temporary environment changed, and no machine/VM/Mac/USB/account/policy setting changed. The 52-input final manifest preserves executed sources, commands, observations, refusals, archives, restoration and source/payload receipts.

## Exact selected provenance

The first 20 paths in this table are relative to the actual private root:

`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/remote-open234-v1`.

| File | SHA256 | Qualification |
| --- | --- | --- |
| `inputs-v2/RemoteOpenFailureTests.cs` | `67bf02e962cfadd1b5945b282736b6c56f28c041d2de40a056174e6de0b58d5d` | Final unchanged baseline/fix control input |
| `fixed-candidate-v1/src/FileCat.Remote/Sftp/SftpProvider.cs` | `43e480183abaa8ba65f5f0933ac12f60b198769ae70300069421b83fb10e51ec` | Private minimal provider candidate |
| `private-fix-preparation-v1.json` | `380ca8bae484b8fc77cbe676ba6949ae1c029d0b8f76397dafc225f938657309` | Original6ac source and private candidate identity |
| `baseline-v3/command.json` | `cd8bbd04925fc800a169f97d75193d51f68ed99cd7965c656527e4488a5289b3` | Actual baseline command, source export and41 payload pins |
| `baseline-v3/results/open.trx` | `e91de8669028d97547e8e58de32d77365da2a1502f3a24525e496bd0d985eec8` | Actual72 failed32 passed baseline controls |
| `fixed-v3/command.json` | `9d8adddb360cc836f778804d408e7ebe6331242c2a822b8642626871917d6f9b` | Actual fixed command, source export and41 payload pins |
| `fixed-v3/results/open.trx` | `d7831a477e19fe728d4da5b54d5a607e705d6e16766bf2470686f4bb89ac8219` | Actual104 passing fixed controls |
| `independent-open-controls-v2.json` | `df7f670540835e604a3b1df2c3a1cc8ee86ed8de64fc5801f3b6762dbb1ea14c` | Independent same-input208 raw-observation reconciliation |
| `fixed-full-v1/command.json` | `19ad79ae538736551dca0c06eeaccceb61c30b29adfae31fcebd6b669eb06e04` | Actual full private Remote command and source/payload pins |
| `fixed-full-v1/results/remote-full.trx` | `afc1bc5074872a6b2100697e1e31a931b1e005aa48fa5dfad872d5a40e68a69c` | Actual1772 passed156 unavailable private Remote cases |
| `independent-full-remote-v1.json` | `6037422cbd5061e6ab8138e1fb4596dcbde3e0ee3728549372a0fe4984933647` | All1824 prior names/outcomes and156 exact skip texts retained |
| `fixture-prefix-guard-refusal-v2.json` | `772447e533fa52fa2bd536e821309b303fa5b395529fb50fc0c1f7df3f4af4e9` | Original unqualified fixture-prefix refusal and correction |
| `object-disposed-message-reader-refusal-v1.json` | `a595f38f32d72c29750804ebe240c842304c004aef7e10d8534462a7bf635a93` | Original reader-message/quoting refusals and correction |
| `shell-runner-preparation-refusal-v1.json` | `7f976ba331bd60be9ead9004de75e06573f687d9186ab4479d075043bc42417c` | Original runner preparation parser refusal |
| `original-guard-fixtures-v2.zip` | `7731dc38774b6cb1b081a6108f25bd9811dafcb254d70012017ad65d24e61dfe` | Preserved104 owned original fixture directories |
| `owned-open-restoration-v1.json` | `2aa6470b710d45cadcf992bf1ecfae6b137e54e603a9df7fddf60d579d825eb3` | Actual four absent known PIDs and bounded temp restoration |
| `owned-temporary-files-v1.zip` | `f97aa84bb74d4023a43a8301068e41de92cf57b083908035cacaf7b0a92f6c5e` | Seven preserved owned temp files |
| `applied-exclusive-open-fix-v1.json` | `8a054adf3b77071ad5bb31a9380d301ece8c937812cae9acb6cdd796af36f595` | Approved two-file apply and CRLF/LF identity receipt |
| `independent-private-open-final-v1.json` | `99a2203c96249e1def1e50ebdfe86fda5280b9f6d3fe315c70c2ff1fd2a4f03a` | Final52 raw inputs, source/payload/restore and058 file identity |
| `seal-private-open-evidence-v1.py` | `fc3fc84f89e2bb567e20d4f2fdcdfb1be42166c9c2b6736ba5bee71224a65176` | Independent final private observer source |
| `progressive-close235-v1/independent-canonical-close-batch-v2.json` | `646f770b41ab79bcf9563b42129e6a0ee6a228f01dd2b21d09451f15bdfb56d0` | Exact058 canonicalCore/Remote source,payload,176 raw-control and originalGPG refusal reconciliation |
| `open-archive234-ci-v1/assets-attempt1-v1/independent-assets-ci.json` | `d26df1d3e7b4c67051465a5f659ca8cd813ab3d7535629a0d57a517f964f523d` | Original058 official artifacts,digests,rawTRX and four-lane controls |
| `open-archive234-ci-v1/assets-attempt1-v1/independent-restore-ci-v1.json` | `da55f87195dd66c62a0ab29f4346916b2dfcac82ddbc6eb91332af797dfd5dce` | Original four-builder92 locked restore graphs |
| `open-archive234-ci-v1/independent-open-archive-ci-final-v2.json` | `f88f27540ea2d78153d077ebeb43265d56960f8f6f5ad235fd4b15a99f1ea940` | Independent416 I234/288 I235 hosted passes and21852 exact predecessor cases |

The final four paths are relative to its parent `FileCatReleaseEvidence` root.

The wider I06/I16 failure/launch boundaries, physical-source HOLD, candidate/artifact qualification and explicit human stable1.0.0 GO remain separate. Native FTP/SFTP/TLS fault incidence, desktop UI, whole containment and whole release readiness are not established by these finite component controls.
