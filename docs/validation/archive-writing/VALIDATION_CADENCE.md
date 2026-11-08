# Archive validation cadence

The 8 November reassessment uses preserved guest logs and TRX timings, not estimates of compiler or test speed.
[Timing data](edit-storage-timings.json) records the exact raw bundle hashes and the reproducible summary from
`eng/validation/archive-writing/summarize_storage_timings.py`. The summarizer reads evidence without running
product code or changing it. Suite wall times include runner initialization; individual cases may overlap and
their durations must not be summed as wall time.

| Preserved cohort | Builds | Focused suites | Full Core/Remote/App suites |
|---|---:|---:|---:|
| Ubuntu `awe-storage-v22` | 69.26 s | 174.18 s | 1,357.31 s |
| Mac `awe-storage-v21` | 27.27 s | 183.82 s | 1,886.96 s |

These observations put full inventories at about 85% and 90% of the measured build/test time. They support
larger connected implementation batches; they do not predict a percentage reduction in the overall project.
Implementation and defect diagnosis also consume time, and source auditing, setup, transport and sealing are
outside those totals. Windows' failed `v21` run is deliberately not presented as a completed timing sample.

The practical changes are:

1. Freeze connected workflow candidates rather than running complete inventories after each small intermediate
   change. Run fresh affected native/error/lifetime/UI checks as implementation proceeds, and complete inventories
   at workflow milestones, when a finding requires broader coverage, and on the final qualification candidate.
   Intermediate evidence explicitly cannot qualify changed runtime bytes or count unrun suites as passing.
2. Collect the independent full Remote and App suites after an ordinary terminal Core failure. The runner first
   checks machine idleness before each suite; a surviving process, another tester or unexpected infrastructure
   failure stops execution. Ordinary test/required-inventory failures remain recorded, every remaining independent
   suite can expose its own finding, and the aggregate exits unsuccessfully. Focused prerequisites remain fail-fast.
3. Write `execution-timings.jsonl` start/end/exit records and `full-regression-attempts.json` in future cohorts.
   Preserve raw logs/TRX and compiler/binary bindings as before. Timings grant no completion or reuse authority.
4. Verify new raw bundles once on receipt, then audit the complete inherited evidence at milestones/final
   qualification. Rechecking unchanged historical bundles after each prose change adds no defect coverage.
5. Continue independent implementation during frozen guest runs, without changing their sources or runner.
   Keep the sharing checks, standard-user environment, owned-process cleanup and Mac wake assertion.

The runner change is for a future frozen cohort. The active `v23` Windows/Ubuntu and `v22` Mac runners remain
unchanged. The complete worker/crash, hostile-input, fuzz CPU, package/RID, desktop/accessibility, independent-reader
and final-review requirements remain open until their original evidence exists.

A static orchestration self-check executes only synthetic callbacks, with no product code or processes.
Its seven scenarios cover success, each ordinary full-suite failure, missing Core inventory, a sharing refusal
after a failed suite, and an unexpected infrastructure error. Independent ordinary failures collect the other
suites and remain unsuccessful overall; sharing/infrastructure failures stop. Python syntax and diff checks pass.
The next frozen guest cohort still has to exercise the instrumented runner with actual product tests.

Deliberate one-minute process deadlines and expensive codec/corpus cases appear among the longest Core cases.
Their time is part of the asserted behavior or coverage, so this reassessment does not shorten their deadlines,
reduce corpora, raise concurrency on shared machines or trim formats/platforms to improve the totals. Grouping
focused filters into fewer processes might save additional seconds, but would need a new raw case-to-gate proof;
the measured cost does not justify that harness work before the remaining product integration.
