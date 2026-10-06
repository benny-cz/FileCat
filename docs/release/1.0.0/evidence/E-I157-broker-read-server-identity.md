# E-I157 — brokered read client accepts a server other than its launched helper

2026-10-06. High data-source trust defect under I17/V06/B04. Remediated preliminarily at b4b6e1b; wider I17 qualification remains open. No candidate or stable GO.

The owned Program Files fixture preserves every exact **ce8189e2fb3b14053aeecdd09063fba9812782e7** self-contained helper file and FileCat managed assembly. A separate private console requester named FileCat.exe calls the actual production **BrokeredDeviceSource.Open** and concurrently reads its newly created user-readable owned plan to bind the corresponding pipe. This synthetic server runs in the same requester PID, not a demonstrated separate attacker process. The original native helper is launched by the production runas route; no helper entry, plan validation or consent code is substituted.

An independent QueryDosDevice call verifies PhysicalDrive999 is absent (ERROR_FILE_NOT_FOUND). The plan names only this deliberately nonexistent device. The synthetic pipe serves metadata and bytes of one owned 32 KiB regular file, opened FileAccess.Read. The actual brokered client returns and reads **1007 exact bytes at offset 509**, with length 32768 and sector size 512, before any consented report is present. Native GetNamedPipeServerProcessId reports **4948**, the requester/control server PID; the held actual helper process handle reports **8272**. The client never checks that mismatch before sending Info/Read. This proves acceptance of a counterfeit data source, not any actual privileged device read, UAC bypass or human consent behavior. No GUI dialog is observed or interacted with.

Two independent regular-file protocol positives before/after give exact bytes and normal Close cleanup. Original helper content, payload and retained output pins, control-source immutability and owned process/protected-fixture cleanup verify. The launched helper is stopped only after image/path verification in this unique fixture; its owned nonce value is removed. No physical source, outside-fixture permissions, machine/user environment or security setting changes. Broader client/server token/path identities, limited caller, races and installed-candidate qualification remain in I17.

The correction must verify that the connected pipe's kernel-reported server PID belongs to the still-held, live process actually returned by runas **before any protocol request or reply is trusted**. Pipe-name randomness and a user-readable nonce are insufficient.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-ipc-20261006`.

| Retained path | SHA-256 |
|---|---|
| baseline-v1/independent-ipc-v1.json | a26e4efde0fa7861ca86ca498821d63c1ed6bc6985bb3abb017cfc443cb185d0 |

Baseline checkpoint: 134/157 preliminary remediations, one Closed, 22 remaining issue remediations. All 24 campaigns require final qualification; NO-GO.

## Correction and exact committed native/CI seal

**b4b6e1b281c24b86e2d66f5482bf3d0bfda0e140** compares GetNamedPipeServerProcessId to GetProcessId of the still-held runas process and requires its wait handle to report a live process. Failure or mismatch closes the connection/exchange before PipeDeviceSource sends Info. Holding the process handle ties the check to the actual launch rather than opening an attacker-supplied PID. This establishes that specific peer check; complete token/path identities, handle delegation and race/limited-caller/consent qualification remain in I17.

Native matching/mismatching peer and existing device/elevation host tests pass 25/1 declared environment skip/26. All four actual working native publishes/receipts verify. The working adversarial control rejects with the identity error, **zero counterfeit request and reply bytes**, no received data and two before/after exact regular-file positives. The corrected observer additionally counts protocol bytes and tolerates a partial newly-created plan until complete; original baseline probe sources/manifest pins are preserved. These are explicit observer differences, not an identical-binary claim.

Clean committed export verifies **923 raw Git blobs**, all four actual native publishes/compiler receipts and complete input/output hashes. Fresh SC/FDD probes compile the committed public control against actual published FileCat assemblies without substituting any production helper file. Both reject with the identity error before Info, with zero request/reply bytes, two exact positives, absent physical device, immutable control source and owned process/protected-fixture cleanup. Returned-source metadata is absent on rejection; the harness therefore does not obtain the private actual process handle/PID from a returned source and records zero for those fields. It independently stops only the image-matched helper in the unique owned fixture. No runtime authentication dialog is observed.

Separate SC/FDD **matching-PID positives** preserve committed native executables and client assemblies while substituting only a clearly synthetic managed helper entry. This entry verifies the owned plan hash/requester PID and serves the owned regular file; it does not run production plan validation/nonce/consent or open a device. Actual client Open accepts exact metadata/1007-byte range with native server PID equal to the held launched helper PID: SC **5836**, FDD **14120**. Synthetic helper records the correct client PID/read-only source/normal Close; no helper termination is needed and owned process/protected cleanup verify. These prove compatibility with the expected peer and are not real consent or installed-package qualification. A first private synthetic helper build missed a namespace import and failed before guest execution; its source and diagnostics remain.

[CI 37429527482 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37429527482) passes all four required lanes. Ten server digests/four build receipts/fourteen complete TRX inventories and both downloaded Windows native byte/source/compiler output receipts verify. The new kernel peer identity test passes as independently retained execution IDs on both x64 and ARM64. Platform counts are 167/33 skips/200 on x64 and 166/34/200 on ARM64. ARM64 startup/drawing/installer compile passes. Declared skips, incomplete theory display arguments and reported-only compiler/setup bytes remain explicit. No stable human GO or candidate exists.

| Retained path under FileCatReleaseEvidence | SHA-256 |
|---|---|
| broker-ipc-20261006/independent-working-v1.json | 3a6051029e8733bb4d8abbc2934ead368da2458767355599298dbda4ff6f7b9e |
| broker-ipc-20261006/independent-committed-v1.json | 15bef2a3c4266b5668dea4e16fd5e57cdbbaa84d0099700d50bb2b4ff2b59dec |
| ci-37429527482-attempt1/independent-ci-native-bootstrap-v1.json | a08f2a8fe233733951b7ab057cc5660eae9b722fb9147eeb05262ecbe7f7b037 |
| broker-ipc-20261006/independent-i157-seal-v1.json | ced767fb2127428a15ba5cb3138807a6913ffa262e6168b69718e30bfb8737b2 |

Current 135/157 preliminary remediations, one Closed, 21 remaining issue remediations; all 24 campaigns require final qualification. NO-GO.
