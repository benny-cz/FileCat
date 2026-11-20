# Archive validation cadence

**9 November follow-up.** The remaining measurable handoff opportunity is between
multi-hour guest campaigns. Ubuntu now has guarded user-service queues for post-fuzz
adoption, strict 7z/RAR5 campaigns and the next gzip/BZip2/Zstd campaigns. Each queue
waits for its predecessor's passing status, checks for other guest test processes and
preserves separate raw runs and evidence seals. This can remove unattended idle gaps;
it does not shorten any fuzz floor, overlap CPU-intensive tests or reduce qualification
scope. The actual time saved has not been measured. Mac replay grouped all three wrapper
formats into one idle guest visit with one isolated build and one sealed evidence bundle;
Windows repeated that three-format batch in one guest visit and ended without test processes.
Independent frozen-source guest cohorts may run concurrently when each machine passes its
own idle and ownership checks. The 9 November Mac ZIP/security and Windows backing-source
security cohorts did so while Ubuntu's guarded fuzz/recovery chain continued. Each guest
built in a separate task directory and sealed its own raw results; a failed assertion on
one guest did not change another guest's compiler inputs. This removes cross-platform
waiting, but does not make any platform result stand in for another.
Ubuntu's same-source follow-on batch is queued behind the active fuzz chain with a
passing-predecessor check and an idle wait. This uses that VM as soon as the existing
campaigns and other testers release it, without overlapping CPU-heavy runs.

## Current execution decision, 9 November

The remaining saving is to apply the cadence consistently. The storage-admission Windows corrections
should not trigger another complete Unix cycle after the running `v31` milestone. The next `v33`
candidate runs fresh affected gates on Mac/Ubuntu and retries the affected gates plus complete inventories
on Windows. Its runner pins the actual sealed predecessor separately: `v32` on Windows and `v31` on Unix.
The verifier enforces those platform-specific scopes; a focused run explicitly leaves full suites unrun.
Earlier full results continue to describe only their own candidate, and final qualification still requires
the final runtime on all three systems.

After this correction, freeze the next connected recovery workflow rather than each helper: coordinate
native saved-copy/cleanup mutations with admission, complete safe discard/recovery decisions and their
user actions, then integrate the qualified store. Run affected failure/lifetime/security checks during
implementation. Use a completed workflow, a finding needing wider coverage, or final qualification as
the next full-inventory trigger. Implement the next independent part while frozen guest tests run;
never change their compiler inputs or restart them merely because observation timed out.

Keep one concise current status and record each new verified bundle once. Preserve raw failed evidence,
source/binary binding and the final inherited-evidence audit. Avoid a general harness rewrite, extra
timing studies or repetitive historical documentation now: their likely saving is smaller than finishing
the product integration. No deadline, corpus, platform, isolation, resource or final-review gate changes.

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

## 9 November follow-up

The next connected candidate now includes the native Save copy session link, destination adapters and fresh
read-only recovery, rather than qualifying each helper independently. Both test projects compile cleanly on
the host; runtime validation remains guest-only. This implementation proceeds while the unchanged previous
C# candidate completes its full `v28` Core/Remote/App milestone on all three systems. A focused workflow cohort
will precede its next full milestone. Passing the previous candidate never qualifies the newer runtime bytes.
Windows will run complete inventories on the connected next candidate as the required retry of its failed
`v28` Core run; the new native focused prerequisites run first.

Keep the existing test cadence. Another harness refactor, increased concurrency within a shared guest, or
shortened adverse-condition deadlines has no demonstrated benefit sufficient to justify its cost here.
The small remaining improvement is compact read-only observation:
`eng/validation/archive-writing/observe_edit_storage_batch.py TASK awe-storage-v28` reads timing events,
passed-gate counters, full-suite attempts, completion/census records and sealed transport metadata in one call.
An unfinished timing event is explicitly an unconfirmed command, not proof of a live process. Partial records
produce warnings. The helper runs only Python standard-library file reads and grants neither machine idleness
nor qualification; the existing process checks and sealed source/binary/raw-result verifier remain required.
This avoids repeated log discovery and large status dumps without changing the frozen running cohorts.

The observer was checked with synthetic missing, active, terminal, partial/corrupt and malformed/path inputs,
then used on all three live cohorts. It helped identify a Windows capacity failure: a mini dump records
`ERROR_DISK_FULL` (`80070070`) allocating the ordinary cancellation test's destination. Its existing loop waited
for copied bytes while the job waited for an error decision, without a deadline. The runner's source path/start
time, exception, native stacks and space observations were preserved before retiring that exact owned failed
process. Its full Core result remains unsuccessful; independent Remote/App collection continues.

Capacity repair removed 76 duplicate guest archives, 7,903,851,449 bytes, only after every size/SHA-256 matched
a preserved host copy. Sources, extracted evidence, active cohort data and other testers' files remain. The
observed free space rose from about 1.2 GB to 9,120,296,960 bytes. Future cohorts record an 8 GiB working reserve
check for build/output/default temporary/native edit fixture filesystems before builds, focused tests and each
independent full suite. This operational threshold does not replace product quota/no-space tests or promise
that a volume cannot fill later. The intentionally small separate case-sensitivity probe volume is unchanged.

The real 200 MiB/rate-limited cancellation case still reaches actual copy progress before cancellation; its
startup wait now has a deadline, reports unexpected decisions and always cancels/retires the job. A new forced
startup-error case proves that path fails promptly and leaves its source intact. The next guest batch runs both
cases before the connected existing regressions. Both cases and the complete connected focused gates now pass
on Mac/Ubuntu, with sealed raw/source/binary evidence verified and zero owned instances. Windows still requires
its affected/full retry. Static resource boundary and eight orchestration scenarios also pass without running
product code on the host. No test deadline asserting
product behavior, corpus, format/platform, sharing check, source binding or final QA gate was reduced.
