# E-I272 — leases survive Disconnect and reenter a new connection session

2026-10-09. Preliminary I06/V08/V12/V23 evidence. Original source is exact **cb37ed397544566534937fd32cdea275657e656e**, including the preceding four retirement corrections. One connection-pool file is corrected and 48 controls are added to the existing test fixture. Local original/fixed runs use canonical original Git exports with explicit pinned test/fix overlays. The subsequent exact committed ef06d54 Remote run passes 2132/156 without overlays; all 2288 private-equivalent identities/outcomes and 156 exact skip reasons are retained. The exact main push and canonical committed blobs are sealed. The later [1b8b501 hosted attempt](E-CI-remote-lifetimes-smb-startup.md) independently passes all four required lanes, including 192 generation controls and all 256 earlier retirement controls. Candidate qualification remains incomplete; no candidate exists.

Disconnect marks the pool closed, but a later new lease reopens the shared flag. Returning an older lease then puts its still-open channel back into the new session. A connection that was still being established when Disconnect occurred can itself reopen the pool on completion. This violates the documented lifetime rule that already leased connections close when their current work returns. No different-account credential compromise or real server incident is inferred from this controlled race.

The correction records a pool generation under its existing lock, advances it on Disconnect, and associates every admitted lease with that generation. An old in-flight completion can finish its existing work but cannot reopen the pool. A return from an old generation closes its channel, even after a new session is opened. New leases, idle reuse, capacity, owner disposal, authentication/trust and original standalone close-error propagation keep their existing behavior.

The identical 48 controls cover SFTP/FTP/explicit/implicit FTPS, healthy and three close-error classes, and three timings: an already leased channel before Disconnect/reopen; Disconnect during the connector’s return before pool admission; and the healthy case without an intervening reopen. Original results are **32 failures/16 passes**; fixed results are **48 passes**. The exact prior lease closes once, its actual owned native file holder admits exclusive reopening, the new session reuses its own channel, the first standalone error object survives, full capacity recovers, unrelated leases remain open and complete recovered byte hashes match.

The same unchanged fixed compiled payload passes the complete Remote suite: **2132 passed/156 skipped**. All 2240 cb37ed3 identities/outcomes and all 156 exact skip reasons are retained; the only 48 additions pass. This also revalidates the preceding 64 retirement controls. The callbacks force the stated race deterministically using the existing controlled adapter; no native network/desktop/account incident or candidate result is claimed.

[The original combined hosted attempt](E-I273-smb-child-startup-fixture.md) passes all 112 pool controls on three lanes, 336 executions, but ARM64 stops at an earlier unrelated Core fixture failure. Its missing later inventories are unavailable. Every canonical source ZIP member, overlay, actual payload, command stream, raw TRX and decoded control row is rechecked. Three owned workload logs are archived/rehashed/removed and all three current private temporary roots are absent, with no new locks or process kills. The preceding pool batch’s three pinned analyzer locks retain their separate restoration qualification. No host UI, VM/Mac, physical device, persistent setting or ordinary user data is changed. Physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute; nested receipts retain the complete inventories and raw results.

| File | SHA256 |
|---|---|
| `pool-disconnect272-v1/RemotePoolRetirementTests.cs` | `48ff261f38b953c4a164ec593ab0c044df161ac632d3bfbf4f0a177b23f237f2` |
| `pool-disconnect272-v1/Connections.cs` | `bf6057ce58a90a44910d12269dd17d2e3e40aa8cfba5b838b1e9f42c16070504` |
| `pool-disconnect272-v1/prepare-disconnect-controls-v1.py` | `5f775ac2107ee059212d44d96681f0cc4fdbe46ee2e9e362c853b52bdb8274fa` |
| `pool-disconnect272-v1/prepare-generation-fix-v1.py` | `227e86752dfdc41c23ea2cd9d66ac7335ef198d1da167919a96db6f9cb2f11a3` |
| `pool-disconnect272-v1/run-disconnect-controls-v1.py` | `946df79b093b4d3a057ae431c5661f239ee27b9a7744429075cf8240d141b7d5` |
| `pool-disconnect272-v1/run-full-disconnect-remote-v1.py` | `65496a8e60a676f8442aa34a2700de8852d64ae48e1edf14239d306bb5a5a6f3` |
| `pool-disconnect272-v1/seal-disconnect-controls-v1.py` | `3594eca3abcdf096ad928c4c5e5d04377c46f297deeb0f3d2e7cb50a4b44bac0` |
| `pool-disconnect272-v1/independent-disconnect-generation-final-v1.json` | `ec9716a0975a48756acecaf6120b48935521d4255b18ca428f3ccbbcfb00f24d` |
| `E:/FileCat/artifacts/release-evidence/pool-disconnect272-v1/original/command.json` | `5b289db49bc5d2ba25f9af395a1ffb44567ff4fa8d8fec870a5d0ee8866743c0` |
| `E:/FileCat/artifacts/release-evidence/pool-disconnect272-v1/fixed/command.json` | `850cc1aa719bd59a2ec54baeb89a02545d79ed6a26465304cace7be862f96fbc` |
| `E:/FileCat/artifacts/release-evidence/pool-disconnect272-v1/fixed/full-remote-v1/command.json` | `70b336f70615306eb183fef1e592c62db1277003fd395729bd153f51a19e310a` |
| `pool-disconnect272-v1/reviewed-generation-main-push-v1.json` | `a39a315adcb7ecd1b6f68cd6e3c8165e7d51963be43072a9bfa94cc543b5befc` |
| `pool-disconnect272-v1/seal-committed-generation-full-v1.py` | `2a868d47b35d8b381927115a885aff0c3eb374c945b5cbb8e09658e6dadc7754` |
| `pool-disconnect272-v1/independent-committed-generation-full-v1.json` | `2ee659e5c50b0c4b0e908b36e0a153f989b0f99d53d9c98930c990549a450371` |
| `E:/FileCat/artifacts/release-evidence/pool-disconnect272-v1/full/command.json` | `0279f0332e0d8bb16e497c63a491350c2c4f6faa3bb1544fa5d62650600345d1` |
