# E-I268–I271 — remote pool retirement and reconnect faults

2026-10-09. Preliminary I06/V08/V12/V23 evidence. Original production is exact **15dec4411439b58735982ef8e3917d94806bd74f**. Two production files are corrected together with one new 64-case regression fixture in the commit containing this record. Local validation uses independently exported original Git blobs plus the explicitly pinned test/fix overlays; it is not represented as an already committed build or an installed candidate. The exact committed cb37ed3 Remote build also passes 2084/156, independently retaining every private-equivalent identity/outcome/exact skip. The later [1b8b501 hosted attempt](E-CI-remote-lifetimes-smb-startup.md) independently passes all four required lanes, including all 256 retirement controls and 192 later generation controls. Wider native/account/candidate qualification remains.

## Four reproduced defects and corrections

| Issue | Original behavior | Correction |
|---|---|---|
| I268 | The idle sweep closes entries before removing them. A close error escapes the callback, leaves other stale connections open and retains the failed entry for another close. | Pop every stale entry before closing; log individual close errors and continue draining. Preserve the ordering and lifetime of fresh idle entries. The scheduled timer invokes this method; process termination from a real native close fault was not induced. |
| I269 | Disconnect stops at the first close error, leaving the other idle connections open. A second call closes another entry and can fail again. | Drain every idle entry, then rethrow the first original exception with its stack; log later close failures. Leased connections keep their existing return/closed-owner behavior. |
| I270 | Reusing a pool with disconnected idle entries fails immediately if retiring one stale channel throws, without admitting a healthy new connection. | Remove and retire each stale entry, log cleanup errors and continue to a healthy/new channel. Capacity/trust/authentication rules are unchanged. |
| I271 | After the user chooses Retry on a disconnected operation, old-channel cleanup can replace that retry with its own exception before the executor clears its lease/listing state. | Detach the broken lease and forget cached listings before retirement, log the cleanup error, then let the next attempt acquire a fresh connection. |

The identical 64 controls cover four logical protocols (SFTP, FTP, explicit and implicit FTPS), the four operations above, and healthy/IOException/UnauthorizedAccessException/ObjectDisposedException retirement. Original results are **48 failed/16 passed**; fixed results are **64 passed**, with every actual test identity retained. Each operation contributes twelve original failures and four healthy passes.

The channels wrap the existing controlled in-memory adapter and hold actual owned read-only FileStreams. Independent raw observations show **54 unclosed-holder observations** across original cases; these are observations, not 54 unique product/server incidents. Fixed controls close every retired channel exactly once, release the real holder for immediate exclusive opening, retain the first standalone Disconnect exception object, avoid repeated close, preserve the unrelated leased channel and exact bytes, and recover full per-profile capacity. The job case exercises the actual Remote error/decision/Retry path: one Retry and two attempts recover the exact file bytes after retiring the old lease. Fixture cleanup separately releases all owned holders, including original failed-case leftovers.

The complete Remote suite runs against the same unchanged compiled fixed payload: **2084 passed/156 skipped**. Its 2240 actual identities retain all 2176 preceding dc1a86e Remote identities/outcomes and all 156 exact skip reasons; the only additions are the 64 passing controls. No existing assertion or skip is weakened. Native server fault incidence, actual scheduled-process death, account/permission/second-SMB behavior, native GUI and candidate qualification remain outside this subset. Core/App/Platform and prior CI keep their own exact producers.

One restricted-runtime fixed attempt exits during restore after only “Determining projects to restore”; it emits no TRX or compiled payload. Its command/streams remain retained and are not a test failure/pass claim. A fresh successful command uses the exact same verified exported source and overlays. An intervening automatic approval routing 401 prevented one command from executing; this was an approval-service failure, not a determination that the authorized validation was unsafe.

All original/fixed inputs, canonical source ZIP members, raw TRX/streams, actual payload pins and independently decoded control rows are retained. Seven remaining owned temporary files are archived and rehashed before cleanup: four workload logs are removed, the fixed/full roots are absent, and three exact compiler analyzer files remain locked in the original private root. They remain pinned in the restoration receipt; no compiler/user process is killed and no global restoration is claimed. No host UI, Mac/VM, physical source, persistent setting or ordinary user data is changed. The physical-source HOLD and explicit human stable GO remain; no candidate exists.

The successful commit/push is separately sealed. A post-push raw-equality assertion exposes the repository’s unchanged `.cs` CRLF-to-LF clean filter in the two production files; the original controller/streams remain, both exact byte forms and 430/1284 converted line endings are recorded, and the other seven files are byte-identical. This is not a push failure or a claim of raw equality. The subsequent complete build uses only exact committed Git blobs without overlays; all 2240 cases pass/skip as above. Its owned full-run root is archived/rehashed and removed without locks.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts retain the full source/build/input/control/temporary-file inventories.

| File | SHA256 |
|---|---|
| `pool-retirement268-v1/RemotePoolRetirementTests.cs` | `5a804ddcb11ad17c7e3f30c4dd09dd342b9c18c50516dca7b5ea6dfbe6ebdca2` |
| `pool-retirement268-v1/Connections.cs` | `c9c923c8e879fee978b9fc781a9aa39991762483e6ad1d61adc68e2086d98eda` |
| `pool-retirement268-v1/SftpJobs.cs` | `92fca7915ca7995f77c10e269b9f7b3d16afc600f5bc538b16e7b3962a0ecff0` |
| `pool-retirement268-v1/prepare-pool-fix-v1.py` | `03b4ac813395d96fc4240db2b7f71d3d647518c1f197a08526753f92f5305710` |
| `pool-retirement268-v1/run-pool-controls-v1.py` | `aa4ca2ece0bcf48deeca286f356ddf266a07b6a57b30fcc8dbdb3c12397576d7` |
| `pool-retirement268-v1/resume-fixed-validation-v2.py` | `3a49ed4fd75244220cee6327b9fcdaca1f35e6ad1bfa2750124db56121f1ab63` |
| `pool-retirement268-v1/run-full-remote-v1.py` | `5255f248e4293cdb0f027ee0d113f46f888b9784bc03a3cd05df6eafd4ded448` |
| `pool-retirement268-v1/seal-pool-controls-v1.py` | `cf77d18fde17bf1b111000e4dd6f2c2ddefd10d882f711ab28c4419fe75ea563` |
| `pool-retirement268-v1/independent-pool-retirement-final-v1.json` | `1eb72db33461a5a1e589e5ab8424402f31ab92ace159c03385f948a41852ef3c` |
| `pool-retirement268-v1/owned-pool-restoration-v1.json` | `721d4c92362d67388c27f17072decc7aeb5f9f3a880078775620fcb3cbf6dbb7` |
| `E:/FileCat/artifacts/release-evidence/pool-retirement268-v1/original/command.json` | `b92c3cc58eece22cf769f479f477597f25c7507b0b0c4600ab92c75527908f48` |
| `E:/FileCat/artifacts/release-evidence/pool-retirement268-v1/fixed/command.json` | `c6d1ee7d5d51beb3d4fb7f231b16142d240da7a48a57ac0958b430903d75fe37` |
| `E:/FileCat/artifacts/release-evidence/pool-retirement268-v1/fixed/completed-v2/command.json` | `ec8d6ca76e6f03070f82ef55429514065188b90932fa25131fd526ba8753d46e` |
| `E:/FileCat/artifacts/release-evidence/pool-retirement268-v1/fixed/full-remote-v1/command.json` | `452d2723a581c97c31804dc48dbec62c64f9485ba891b6f908bee53ebede485b` |
| `pool-retirement268-v1/seal-committed-pool-push-v2.py` | `9ca47b32f6b603422a3b53fb77eaf7f8a4e1be1eb66b37f06327531b0eb39a7d` |
| `pool-retirement268-v1/reviewed-pool-main-push-v2.json` | `246dddd28769f12eee923f9edb1750b5ba28cef70e7837717f1ed45d03f974d4` |
| `pool-retirement268-v1/independent-committed-pool-full-v1.json` | `d90bb9f055fd8e853596f372f1b71fc60b7763cd2dbd00e7ad017c45ca6c2c0a` |
| `E:/FileCat/artifacts/release-evidence/pool-retirement268-v1/full/command.json` | `9265d95e58787d72ceb0488c2d17659371fc08ea3941d47ead1784fd2870d368` |
