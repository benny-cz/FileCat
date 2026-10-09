# E-I275 — a display-profile lookup can abandon remote content's opened stream

2026-10-09. Preliminary I06/V08/V11/V12/V23 evidence at exact original **5aee22c720bf9f8ed15f0dfdd280e5b8d20403f1**, plus declared test/fix overlays. OpenContent evaluates OpenRead before GetDisplayPath in the content constructor arguments. If the profile callback used to build that label throws, no content object receives the stream. The existing outer handler returns/retires the channel lease and preserves the primary error, but never closes that stream. This is a proved controlled callback path, **not an observed native-server or concurrent settings-list incident**.

The correction resolves the display label after stat validation and before acquiring the stream. A label failure therefore opens no stream, while healthy content still owns its stream and connection lease. Existing stat/open-read/lease retirement handlers, returned labels and healthy content bytes stay covered by the complete suite.

All **48 identical controls** cover four logical protocols (SFTP, FTP, explicit and implicit FTPS), five profile-lookup exception classes and two secondary channel-close configurations, plus eight healthy controls. The lookup is armed deterministically after a successful stat; it does not claim a real network or settings race. Original results are **40 failed/eight passed**, recording forty unclosed stream observations. Each original adverse case opens one actual owned read-only FileStream, records zero stream closes and fails immediate exclusive access. Fixture cleanup independently releases those original holders. Other pool capacity, primary exception and recovery byte observations were already healthy.

Fixed results are **48 passed**. Every adverse label failure records zero stream opens. Original exception objects/stacks remain, including the disconnected-primary cases with a secondary channel-close IOException; those channels close once. Non-disconnect errors keep the healthy channel reusable. Each healthy content owns one stream, blocks exclusive access while open, closes it once, releases its holder and retains the expected display label. Full capacity, unrelated leases and exact recovered bytes pass. Secondary close injection is exercised when retirement actually occurs; other cases do not claim a close call.

The **same unchanged corrected compiled payload**, without rebuilding, passes the complete Remote suite: **2276 passed/156 exact skips**, 2432 actual records. Every one of the **2384 preceding result/message multiplicities and 156 exact skip messages** from the private I274 full-suite producer remains; only the 48 passing new cases are added. Source ZIPs contain 1314 canonical original Git blobs plus only the declared fixture and one production-file overlay. Exact committed and hosted correction qualification remains pending; this is not an installed candidate.

The independent reader rehashes source blobs/ZIPs, declared overlays, command/raw streams, actual compiled payloads, raw TRXs and every original/fixed/full observation. All **64 recorded initial stream-holder paths and their fixture directories are absent** at seal. Six owned temporary files are archived/rechecked; three are removed and three exact compiler log/analyzer locks remain in the original namespace. Fixed/full temporary roots are absent. Older compiler locks keep their separate qualifications; no global process termination or restoration claim.

No workstation UI, VM/Mac, persistent account/settings, physical source or stable publication changes. Native/account/permission/provider/drop/second-SMB/resource/identity/consumer/human/candidate scope remains. I06, all twenty broader unresolved scopes, all 24 final-candidate campaigns, physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts retain complete inputs, commands, payloads, raw streams and restoration inventories.

| File | SHA256 |
|---|---|
| `remote-open275-v1/preparation-v1.json` | `d0be0bd3c2de83b3a9124e5c05140b5e7674a2693e48db2c7a6218e7c80d86f0` |
| `remote-open275-v1/original-SftpProvider.cs` | `7f1f0edca3746aaa71f92aed2918a36c5387b3a26e7cd9260eaa9c7250dadaa7` |
| `remote-open275-v1/SftpProvider.cs` | `d3a488ec5f93147c24db70783b173b1d05a4b1f195b383de503dbacb13b8a17f` |
| `remote-open275-v1/RemoteOpenOwnershipTests.cs` | `82b107ceb5c5a026f673ee3e722de7c3065be4fdb3ce12830902c8f2f0ec4468` |
| `remote-open275-v1/run-open-controls-v1.py` | `c439d5526d461829e1f27a2ac82207271edd6de99de238d7d95dfe9df1a6e909` |
| `remote-open275-v1/run-full-open-remote-v1.py` | `1eb30b652fa702ce5d6be3ad9bcbe14b28804b932c8fdf6928c130df34e22ab8` |
| `remote-open275-v1/seal-open-ownership-v1.py` | `e26ad522479a0e288efc31ee8a7e1b34173bbb9545d8e42df0264ebc3f56102d` |
| `remote-open275-v1/independent-open-ownership-final-v1.json` | `9c44aeab8959caa749767772b45c008e16d90b9a6cf30750a418bab271c9324d` |
| `E:/FileCat/artifacts/release-evidence/remote-open275-v1/original/command.json` | `87b6cf63810064588731ff2a8bafeac79c61bccb40fdc0b5b9d7e0648278f46a` |
| `E:/FileCat/artifacts/release-evidence/remote-open275-v1/fixed/command.json` | `5430a578ec42c7f11b84e1dd43bbdee025a91f42b6553630a6f83f41eff1633e` |
| `E:/FileCat/artifacts/release-evidence/remote-open275-v1/fixed/full-remote-v1/command.json` | `9d0c915c5add3d74cbe61c66f3d719d3c882b88ca64798a4a6ed9a45e5c7c681` |
