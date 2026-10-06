# E-I157 — brokered read client accepts a server other than its launched helper

2026-10-06. High data-source trust defect under I17/V06/B04. Open; correction underway. No candidate or stable GO.

The owned Program Files fixture preserves every exact **ce8189e2fb3b14053aeecdd09063fba9812782e7** self-contained helper file and FileCat managed assembly. A separate private console requester named FileCat.exe calls the actual production **BrokeredDeviceSource.Open** and concurrently reads its newly created user-readable owned plan to bind the corresponding pipe. This synthetic server runs in the same requester PID, not a demonstrated separate attacker process. The original native helper is launched by the production runas route; no helper entry, plan validation or consent code is substituted.

An independent QueryDosDevice call verifies PhysicalDrive999 is absent (ERROR_FILE_NOT_FOUND). The plan names only this deliberately nonexistent device. The synthetic pipe serves metadata and bytes of one owned 32 KiB regular file, opened FileAccess.Read. The actual brokered client returns and reads **1007 exact bytes at offset 509**, with length 32768 and sector size 512, before any consented report is present. Native GetNamedPipeServerProcessId reports **4948**, the requester/control server PID; the held actual helper process handle reports **8272**. The client never checks that mismatch before sending Info/Read. This proves acceptance of a counterfeit data source, not any actual privileged device read, UAC bypass or human consent behavior. No GUI dialog is observed or interacted with.

Two independent regular-file protocol positives before/after give exact bytes and normal Close cleanup. Original helper content, payload and retained output pins, control-source immutability and owned process/protected-fixture cleanup verify. The launched helper is stopped only after image/path verification in this unique fixture; its owned nonce value is removed. No physical source, outside-fixture permissions, machine/user environment or security setting changes. Broader client/server token/path identities, limited caller, races and installed-candidate qualification remain in I17.

The correction must verify that the connected pipe's kernel-reported server PID belongs to the still-held, live process actually returned by runas **before any protocol request or reply is trusted**. Pipe-name randomness and a user-readable nonce are insufficient.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\broker-ipc-20261006`.

| Retained path | SHA-256 |
|---|---|
| baseline-v1/independent-ipc-v1.json | a26e4efde0fa7861ca86ca498821d63c1ed6bc6985bb3abb017cfc443cb185d0 |

Current 134/157 preliminary remediations, one Closed, 22 remaining issue remediations. All 24 campaigns require final qualification; NO-GO.
