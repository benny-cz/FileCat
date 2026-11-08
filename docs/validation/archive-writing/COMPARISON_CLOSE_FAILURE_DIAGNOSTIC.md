# Comparison close-failure diagnostic

The Windows `awe-storage-v21` complete Core run stopped producing results while its original process
remained alive. Read-only observations and two dumps established that this was an unhandled exception,
not a completed test batch or a reason to restart after an observation timeout.

The original runner was PID 4464, started at `2026-11-08T21:11:53.0721447Z`. Its managed stack shows
the xUnit console unhandled-exception callback waiting on a `ManualResetEventSlim`. The recorded
exception is the controlled `IOException("Owned left close failure")` from
`ComparisonContentRetirementTests.Source.Dispose`, propagated through the close helper and
`ComparisonIo.ContentEqualAsync`'s async-finally rethrow. The dump also records the continuation's
`Task.ThrowAsync` path. This evidence identifies the escaping close error; it does not establish
a particular compiler/runtime defect.

The diagnostic used `dotnet-dump` 10.0.750501 inside the guest. Process identity/start time were checked
before collection and retirement. Mini/heap analysis, tool output and the mini dump were copied into
the failed cohort before its one owned failed test runner was terminated. The heap remains in the
task's diagnostic directory with SHA-256
`E23C0DD42379EF62B402E01392D0C4126ED425BD3ED3D690E763D90753DCEDE0`.
The failed batch is sealed with zero remaining owned test processes and exact compiler/PDB/DLL bindings:
25,815,650 bytes, SHA-256
`e487807d67058bbea7ecb0541a76fb782d5712eec7192dcf0f72ad257c388095`.

Candidate `fa3975d5` moves error selection and rethrow to the comparison task's ordinary completion
path after both content owners have closed. It preserves the original operation/cancellation failure,
or the first standalone close failure in the existing right-then-left retirement order. It does not
detach an active I/O call or release its content/buffers early.

The original 104-case comparison retirement matrix remains unchanged. Four new same-/different-device,
single-/double-close-failure variants each repeat 32 deferred-opening comparisons. They prove the task
remains pending until its owned opener is released, then receives the exact expected error object while
both read-only file owners retire exactly once and file bytes stay unchanged.

Core builds without warnings/errors. The `awe-storage-v23` Windows candidate passes all 108 cases before
saved-copy preparation and the connected/full inventories. Mac/Ubuntu will receive the same candidate
after preserving their current `v22` results. Successful qualification of this correction remains pending;
the failed run is retained in the [progress checkpoint](edit-capture-recovery-progress.json).
