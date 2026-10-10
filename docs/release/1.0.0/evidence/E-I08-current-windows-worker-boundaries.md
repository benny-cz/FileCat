# E-I08-CURRENT-WINDOWS — medium and elevated native worker boundaries

2026-10-10 CEST. **All 24 finite controls pass** in the disposable Windows 11 Insider 26300 VM. The exact product remains **0aec7a7b4c68d3dd142e0d34c23d0e2fdd6f68f6**, from the independently verified no-overlay export. The later 069f772 changes test fixtures/docs only. These observations extend the [older Windows controls](E-I08-WINDOWS-worker-boundaries.md) without relabelling their producers or closing I08.

## Two measured parent identities

The interactive Admin route yields parent integrity **8192 (medium)**. A separate noninteractive Admin route yields **12288 (high)**. Native token queries establish these identities; an administrator login alone was not treated as proof of elevation. Both cases use all 193 unchanged private .NET 10.0.12 runtime files and pinned product/observer bytes. The second private observer adds only read-only token/PID/job queries for the actual decoder children.

Each parent executes twelve controls: helper echo, owned-file write, child start, memory allocation, kill and disposal; actual FileCat picture-worker normal PNG, clamped requested side, oversized declared dimensions, patterned noise, empty input and oversized streamed input. No product executable is rebuilt, no desktop input is sent and no consent dialog or physical source is opened.

| Native observation | Medium parent | High parent |
|---|---|---|
| Six helper child token RIDs | 4096, queried by each helper | 4096, queried by each helper |
| Helper job membership /memory /active processes | Exact owned job /96 MiB /one | Exact owned job /96 MiB /one |
| Owned sentinel write /child start /256 MiB allocation | Access denied /start denied /OutOfMemoryException | Access denied /start denied /OutOfMemoryException |
| Six actual decoder token RIDs and job queries | Startup reports low integrity; token RID not separately queried | All six RIDs 4096; exact owned job, 1536 MiB and one active process queried |
| Exact PNG pixels /clamp /finite refusals | All pass | All pass |

All 8193 helper bytes match their independent hash. PNG signatures/chunks/CRCs/decompressed pixels and every returned BGRA pixel are independently checked. The 32×24 image returns all expected pixels; requested side one clamps to 16×12 while preserving source dimensions. The declared 20,000² image is refused. All 268,500,992 streamed oversized bytes are accepted by the pipe before the expected 256 MiB input refusal. Noise and empty cases return bounded FCPE errors. Observed job settings are not whole-system or peak-allocation proofs.

All twelve helper PIDs are absent at their recorded checkpoints; actual decoder observations report exit, with no invented unobserved exit code. Native post-run queries find no process under either owned payload root. Both exact sentinel files and empty temporary directories are removed after the complete output archives and sentinel bytes are retained and rechecked. Product/observer/runtime hashes remain unchanged. Payload/result roots remain owned campaign evidence and require final cleanup.

The first transport runner refuses its absent local evidence parent before any guest/network command. Its immutable source and refusal are retained; a fresh runner corrects only that directory precondition. No account, firewall, policy, system runtime or host UI changes occur. Broader standard-account/fallback/permissions/network/identity races, parent-death causation, Unix policy and installed-candidate scopes remain open. A low integrity RID alone is not a whole-platform sandbox claim. Physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve exact source, commands, raw results, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i08-windows-current-20261010-v1/independent-controls-v1.json` | `f5ca522cc44cee0e08ce676a35506a370bbdf7e7558c00fc519ffeef1f816557` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/seal-controls-v1.py` | `a5a4819d43afab21a3a331027a7bd02017716689fee78a88afdcced66efa9204` |
| `i08-windows-current-20261010-v1/independent-restoration-v1.json` | `2f06e4178bfab642a9c0becb9a4ff76f0e7da8d121ddf5c271d755393b0faa0c` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/restoration-v1.json` | `d5ceee498095d9c6a2d46f13c00867c8f6e71a5ff6ee960dc24209dbd7fd9ff2` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/restore-owned-v1.py` | `9020dbab4247be7f224e75fb2f01906326465244797003b6adb63d4a131ecdf5` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/restore-owned-v1.ps1` | `9282f4baa91ea54c1b540e1013a8594f3ba121b5b9de7eeebfbc5174c6322436` |
| `i08-windows-current-20261010-v1/runner-v1-refusal.json` | `0b6ff31190b61473218673db97222bb661889a535ca70c47402136cbac4b6e15` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/build-command-v1.json` | `ef4bc46c8e965dca4ca724d61b8a5db186a6ea626f6c271d8d9c4ae57712d6e8` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/build-elevated-command-v1.json` | `44932e36ca710c0334182f748e2b9a076fe59af5c480c504189642d6a910aa67` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/observer-source-v2/Program.cs` | `e82a254f9a070fedd151f03fcc65ae35994dd30437f29d38c49fb046206e5ec8` |
| `i08-windows-current-20261010-v1/windows/transport-final-v1.json` | `5bfc6cc51af9389fd477a9ae739453e1cc65e49c341a1b19fcce5c894f1bef89` |
| `i08-windows-current-20261010-v1/windows-elevated/transport-final-v1.json` | `21dcfa1d243ccf473f9fd9bfeaaf68d434a5e945795d9d9d73142ae6c125e50b` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/windows-v2/retrieved/native-worker-observations-v1.json` | `c6a2eafaf879187d4127f3d59b9bb1895b052dfb41c92f959558fe6d086ff7fe` |
| `E:/FileCat/artifacts/release-evidence/i08-windows-current-20261010-v1/windows-v3/retrieved/native-worker-observations-v1.json` | `15bb3019775dc81cb91182aea21cc71a643509bd8479575e4817403d187c78af` |
