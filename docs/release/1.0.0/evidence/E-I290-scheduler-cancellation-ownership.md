# E-I290 — cancelled device work retires captured payloads

2026-10-09. Exact original/correction base **96977d456767389424c07a8fde10e278c95615de**. Both complete 1358-blob exports contain only the declared fixture; the correction also overlays DeviceIoScheduler.

A cancelled queued WorkItem completed its task while retaining its callback and captured data. Behind a held device call, already-cancelled and newly cancelled requests kept their payloads until the queue drained. Shutdown removed queued slots but left their registrations in live, uncancelled external cancellation sources, retaining the callbacks even after drain. This is a controlled reachable retention result, not a measured historical UI incidence rate.

Cancellation now atomically drops the queued callback and unregisters its notification without waiting. Execute transfers its callback to a local owner, keeping data alive until a running synchronous call reaches its safe boundary. Registration handles the already-cancelled constructor case too. Existing task cancellation, callback exceptions, worker/watchdog/admission and quarantine behavior remain subject to the retained suite controls. Queue slots can remain behind held workers; this fix retires their payloads and does not establish a queue-count or whole-process bound.

Fifteen public scheduler controls read one actual owned 32 KiB file into independently captured payloads. Queued cancellation, already-cancelled admission, shutdown and live demand each run with 1/16/64 requests; running cancellation repeats 1/4/16 times. The bounded explicit-collection observer keeps the scheduler, returned tasks and external cancellation sources alive. All source bytes/hash remain unchanged. The original yields **nine failures/six positive passes**. All cancelled queued payloads remain reachable while held; shutdown payloads remain after drain. Live callbacks and running cancellation correctly verify every expected byte. The correction passes **all fifteen**: cancelled queued payloads collect while the device remains held, shutdown leaves none after drain, live callbacks execute, and running callbacks retain their payloads until returning. Reachability/counts are not process memory peaks, throughput, native allocation or arbitrary-schedule qualification.

The affected App selection passes **121**. The unchanged compiled full Core/App suites run concurrently and pass **3424/64 exact skips** and **1398/25 exact skips**. Every one of the preceding **3473 Core** and **1423 App** outcome/message records and exact skip reasons remains; one Core PE display path is adapted only after verifying the same method. Full-suite payload bytes remain unchanged. Exact committed/original hosted checks, aggregate queue/job/result/provider memory, wider races, native/process/frame/reference/human and candidate scope remain. No new memory budget, host UI, physical source, freeze or publication occurs. I106/I110 HOLD and owner decisions/human GO remain.

The final proof inventories every archived/rehashed/removed/locked file under both exact owned temporary roots, including the explicitly declared short root. Earlier restoration and locked compiler files remain at their own producer.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute; nested receipts retain complete source, commands, payloads, raw failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i06-scheduler-cancel-ownership-20261009-v2/SchedulerCancellationOwnershipTests.cs` | `828af8ded8190d8960ff2beb9df1e99d2876a48503e743b7693cf3666915cf4e` |
| `i06-scheduler-cancel-ownership-20261009-v2/DeviceIoScheduler.cs` | `85c71b9a9a0fd98d37173eb7270814255f8fa8e62731beb5e75135086bdb60b5` |
| `E:/FileCat/artifacts/release-evidence/i06-scheduler-cancel-ownership-20261009-v1/original/core-controls/command.json` | `fa4092af0effc392be4c9fb8611c355e6ec1fb7f3d476d99eac615844a0ac126` |
| `E:/FileCat/artifacts/release-evidence/i06-scheduler-cancel-ownership-20261009-v2/fixed/core-controls/command.json` | `653152c3affaee1c60362b6f907f0af622d5dca15a5a764118e349486362a7fb` |
| `E:/FileCat/artifacts/release-evidence/i06-scheduler-cancel-ownership-20261009-v2/fixed/scheduler-app-controls/command.json` | `c4b627e7aa2b96661192cc839125d57030a5e6075eb825b994906155c1594078` |
| `E:/FileCat/artifacts/release-evidence/i06-scheduler-cancel-ownership-20261009-v2/fixed/full-core/command.json` | `8a1dc7d98462cc4daefe1e0ea8e06e4b3d843f1309477036f5d67f7a16c30021` |
| `E:/FileCat/artifacts/release-evidence/i06-scheduler-cancel-ownership-20261009-v2/fixed/full-app/command.json` | `ce9a44763678740fd01f7056d7a6743727c5eee81de9d534061d6190cfe37946` |
| `i06-scheduler-cancel-ownership-20261009-v2/independent-original-diagnosis-v1.json` | `6cdf01dba93c8a8d3eae8f207dd6378bbe8d0441ec131ba158f9b83e447db766` |
| `i06-scheduler-cancel-ownership-20261009-v2/seal-scheduler-batch-v1.py` | `e932b8bbdd7586208f1e7b7c5f07e00461e0414d52f5899dd8da537c94961200` |
| `i06-scheduler-cancel-ownership-20261009-v2/independent-scheduler-batch-final-v1.json` | `2dda8a77fe39c7c3438fd2005f6030798b58c527ddc382b3a88e61006c3b9e1d` |
| `E:/FileCat/artifacts/release-evidence/i06-scheduler-cancel-ownership-20261009-v2/owned-temporary-files-v1.zip` | `c3b67254ff922c1471dc6b14a062511c4eac7e89451e90aeed0c5e3677383550` |
