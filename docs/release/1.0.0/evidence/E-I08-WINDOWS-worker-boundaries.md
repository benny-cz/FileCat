# E-I08-WINDOWS — finite native worker boundaries

**Twelve owned Windows process/pipe/picture controls pass; no new product defect or remediation is established.** I06/I08 and candidate qualification remain open. No I231 issue is registered from this inspection.

## Executed identity

The 2026-10-08 19:44 UTC run uses the actual clean `9f643d9414641304ec7effd940a1827ffc1f0e6c` component payload from [I227](E-I227-upload-local-link-scope.md). Its 141 files verify against that original producer receipt before/after execution. Executed `FileCat.dll` SHA-256 is `ad5baae39aa1077fc6b01a7e8baec4fc7271808853026a0ec0219868a685283f`; `FileCat.Platform.Windows.dll` is `4bd59abd04292894abb45c5bf471ef4d4fa418b7a00268d7e43edd0c2fc93b2f`.

The canonical blobs for `PictureWorker.cs`, `PictureDecoder.cs`, `SandboxedWorker.cs` and `RestrictedProcess.cs` are byte-identical between that producer and `cae0f046d541f2d3f75f01e12ab668e72544f0f6`. Their paths/hashes are preserved in the native command receipt. This establishes applicability to those unchanged worker paths. The whole executed payload retains its original `9f643d9` identity.

The recorded host is x64, `Microsoft Windows 10.0.26220`. The private observer builds with SDK 10.0.401 and retains all 14 actual payload files; its copied Windows component agrees with the original payload. The picture cases start the actual FileCat executable in `--picture-worker` mode through `SandboxedWorker`; they do not exercise a drawn viewer or substitute a decoder.

## Actual controls

| Case | Independently inspected observation |
|---|---|
| Helper pipe roundtrip | All 8193 patterned input bytes produce the expected SHA-256. |
| Owned file write | Low-integrity helper receives `UnauthorizedAccessException`; the owned sentinel retains its original bytes. |
| Child-process attempt | Helper receives `Win32Exception`; no child PID is returned. |
| Memory attempt | A 256 MiB allocation receives `OutOfMemoryException` in the 96 MiB helper job. |
| Kill / disposal | Two separately started idle helpers exit following the respective operation. |
| PNG / clamped side | Actual worker returns exact opaque BGRA pixels for an independently encoded 32 × 24 PNG; requested side 1 clamps to 16 and returns 16 × 12, retaining source dimensions. |
| Declared pixel limit | A checksummed PNG declaring 20,000 × 20,000 pixels returns the expected size refusal. |
| Noise / empty input | 70,000 patterned bytes and zero bytes each return a bounded `FCPE` reason. |
| Oversized input | All 268,500,992 streamed bytes are written; the worker returns the 256 MiB input-size refusal. |

Grouped rows represent twelve distinct executions. Raw output headers, lengths, format, dimensions, frame/flag fields and every expected PNG pixel are checked independently. PNG chunks, CRCs and decompressed source pixels are inspected without using FileCat as their oracle.

The tool token's measured integrity RID is 8192 (medium); each of the six helper children independently reports RID 4096 (low). Native APIs confirm membership in the exact owned job, memory limit 100,663,296 bytes, active-process limit one and limit flags 9480. The six picture workers report successful low-integrity startup through the production wrapper; their token RIDs are not separately queried. Their jobs are requested with FileCat's 1536 MiB picture limit, whereas the queried 96 MiB limit belongs to the helper controls.

All six known helper PIDs are absent at the recorded checkpoint. Each actual picture worker reports `HasExited` before cleanup; no unobserved exit code is inferred. The original [worker admission/diagnostics qualification](E-I145-unix-picture-worker-diagnostics.md) keeps its own source/platform scope.

## Preserved refusal and restoration

The v1 observer failed compilation with CS0136 because a child sentinel variable shadowed the parent variable. It started no worker or fixture. Its source/build logs remain. Fresh v2 changes that variable name and selects a new directory; executed inputs and results are preserved.

The independent native seal verifies all 34 selected raw inputs/outputs, both payload inventories, source equivalence and all twelve outcomes. The restoration seal pins four additional inputs and checks those 34 files remain unchanged. The one exact owned sentinel and its empty fixture directory are removed after preserving the sentinel bytes. No account, VM, Mac, USB, host security policy or persistent machine setup changes occur.

## Remaining scope

These finite native CLI/component observations leave Unix containment, medium-integrity fallback, elevated-token behavior, parent-death handling, other formats/frames/runtime failures, identity swaps, attacker-selected network effects and protected-file boundaries unqualified. They establish no whole-platform sandbox, desktop workflow, human UX or exact-candidate closure. No product source, frozen contract, candidate or publication changes; the physical-source HOLD and explicit human GO requirement remain.

## Selected evidence

Private `FileCatReleaseEvidence/worker-parallel231-v1`; the sibling producer path resolves from this root. Pinned manifests preserve the complete nested inventories.

| File | SHA-256 |
|---|---|
| ../upload-local-link227-v1/clean-v6/command.json | a6baadf4c61163915ed6dcf92c3ceb746d4e4d9dc631b98650d41a1422cf03d3 |
| native-controls-v2/Program.cs | 5f28adbdbe42ae6d801bbb8453d80d92ce02ccbab88a6835ce5135551f77e30f |
| run-worker-controls-v2.py | 4c076872b574bcb6cfd12d04a8815ed1d60e80798f0ec72328d9f2fefdfec4bc |
| observer-compile-refusal-v1.json | d39d9b2d1d88602bacb85467380262a3500a406d16584bcf3eda185094233d20 |
| native-controls-v2/command-v1.json | 74ca35f90d52e027e18b3b02dcbf7f3374e572c2faa643fc24dc81155b996a50 |
| native-controls-v2/native-worker-observations-v1.json | 7ae9956b53ea28099e0ea84c0dbf79116b878fd655cf07133865186c2c87ea3e |
| seal-worker-controls-v1.py | 40e5d0c7c18e79867375404cdde19eeec661bfabbbc01de2aab5f8edade08d2e |
| independent-worker-controls-v1.json | 00f4de19f854fe53567413e8654f5044ad8b6125ccf5825b8813ce9678c2d04d |
| owned-fixture-restoration-v1.json | 58ba15f6f250d12161203963e6f7c31db0d8a7624755dfd6017a0fecf6428743 |
| seal-worker-restoration-v1.py | 2859cc020fd4591c3774042091142bd9ec373a514391e4041e864194aecda800 |
| independent-worker-restoration-v1.json | 8d1aacbefbf009b4c7933c83f853c4ad912154886761b6a5286239837085d9aa |
