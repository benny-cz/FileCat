# E-I274 — a connected channel is abandoned if post-connect setup fails

2026-10-09. Preliminary I06/V08/V11/V12/V23 evidence. Original is exact **1e40d82574dacc78eed88df57765071b52f175d5**. The connector has authenticated and returned a live channel before secret saving/profile notification completes. A failure from that later step releases the pool slot but loses the unadmitted channel without closing it. If the warning callback throws RemoteAuthenticationException, the original outer authentication handler also makes two unnecessary additional connections, abandoning all three. These are controlled callback failures, not observed native server/credential-store incidents.

The correction limits bounded authentication retries to the connector call. Once that call succeeds, secret saving/notification runs in a separate ownership scope: a failure closes the not-yet-admitted channel, logs any secondary close error and rethrows the original failure. The successful return transfers ownership to the existing pool path. No trust decision, secret persistence choice, ordinary authentication bound, pool capacity, session generation or healthy save/warning behavior changes.

The identical **96 controls** cover SFTP/FTP/explicit/implicit FTPS, secret-store writes, profile-change notification and the save-warning callback, three primary exception classes, two additional warning exception classes and healthy save/warning cases. Each adverse case also runs with/without a secondary close IOException. Original results are **88 failures/eight passes**; fixed results are **96 passes**. The failed original cases record **104 unclosed channel observations**, including sixteen extra successful connections across the eight misclassified callback-authentication cases. These counts are observations, not native-server incidents. Fixture cleanup separately releases all original holders.

The controlled channels wrap the existing in-memory adapter and hold actual owned read-only FileStreams. Every fixed failed setup closes its exact channel once and immediately admits an exclusive open of its holder file, preserves the original exception object/stack even if close fails, avoids a false authentication retry, excludes the failed channel from later reuse, reacquires full capacity and preserves unrelated leases and exact recovered bytes. Eight healthy controls keep their channel open for normal pool reuse. Original capacity recovery/reads were already healthy; the proved defect is orphaned channels and false retries.

The complete Remote suite uses the **same unchanged fixed compiled payload**, with no rebuild: **2228 passed/156 skipped**, 2384 actual records. Complete result/message counters retain all **2288 prior records and 156 exact skip messages** from exact ef06d54 Remote; the only 96 additions pass. All 112 preceding pool retirement/generation controls are revalidated. These private original/fixed exports use 1311 canonical Git blobs plus the explicit test/fix overlays. Exact committed/hosted correction qualification remains pending; this is not an installed candidate.

The independent reader rehashes source ZIP/Git blobs, only declared overlays, commands, streams, actual payloads, raw TRXs and all original/fixed/full observations. All 304 recorded initial-holder paths and their fixture directories are absent at seal. Six owned temporary files are archived/rechecked; three are removed and three exact compiler log/analyzer locks remain in the original namespace. Fixed/full temporary roots are absent. Earlier compiler locks retain their own separate qualifications; no global compiler/process termination occurs.

Native protocol/OS credential-store failure incidence, wider provider/account/permission/drop/second-SMB/identity/resource/consumer/reference/native/human/candidate scope remains open. No host UI, VM/Mac, persistent account/settings or physical source changes. I06 remains open; all 24 final-candidate campaigns, physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts retain complete source, command, payload, stream, raw-result and restoration inventories.

| File | SHA256 |
|---|---|
| `remote-connect274-v1/preparation-v1.json` | `7fbea84b7d44d83f5c9e85171e98ded42771cb5b7cb8b93d56078c082767c78e` |
| `remote-connect274-v1/original-Connections.cs` | `55f4511ab754f99827bef294c2fae92e630ce166db71d062ce1fd7637f245e41` |
| `remote-connect274-v1/Connections.cs` | `3e0d65e0a147c4171b95226c6da790806fa604fa62502cfe78a109a1f944d3cd` |
| `remote-connect274-v1/RemoteConnectOwnershipTests.cs` | `f9b2ec9654efaebe76d4844d2ef4ef259a64fd5fb768ec5cdc20830e1181082f` |
| `remote-connect274-v1/run-connect-controls-v1.py` | `733e63cc3158de1682519b9bb0057a72351457d05f42566704c6e168c4ac1f6b` |
| `remote-connect274-v1/run-full-connect-remote-v1.py` | `4227b96ec78adc876734e60e78c4d2892ebac7bc9b31d1b9e7a1dff8111ac044` |
| `remote-connect274-v1/seal-connect-ownership-v1.py` | `0f8bc77b8332dc199cc5c6afea1131cd1ea43235022b209a853bc52af4668438` |
| `remote-connect274-v1/independent-connect-ownership-final-v1.json` | `46a202563bfafa23cedf758dbe9036f64d400aa0cc9455fa1b138f8d86ac0e63` |
| `E:/FileCat/artifacts/release-evidence/remote-connect274-v1/original/command.json` | `02c9421ae1bc1b2ae12419397617903b6b4be475b29e8e7bbfb7ed7db9f38322` |
| `E:/FileCat/artifacts/release-evidence/remote-connect274-v1/fixed/command.json` | `2ffea50a6954fc9e25d6251628846eccbf277eb1369d650164d905d690aacb81` |
| `E:/FileCat/artifacts/release-evidence/remote-connect274-v1/fixed/full-remote-v1/command.json` | `e54b32981df3ed68bd94247d396b2735d446681ec75a6df2406b32387b6577f3` |
