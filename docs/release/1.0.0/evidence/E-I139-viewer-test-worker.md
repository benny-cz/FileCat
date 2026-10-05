# E-I139 — blocking viewer control depends on shared worker scheduling

Validation repair, 2026-10-05. Baseline source is
`63e7af87fe7baea233421cfdf7371e6833d93e7f`; no production behavior changes.

## Failure and controlled reproduction

[CI run 37293936000](https://github.com/benny-cz/FileCat/actions/runs/37293936000)
fails its native Windows ARM64 App lane: 346 passed, one failed, 17 skipped, 364 total.
The Markdown/close-after-read case in `ViewerDirectContentLifetimeTests` times out waiting
ten seconds for the deliberately held read to enter. The failure precedes close/disposal
assertions. Three other required lanes pass; package jobs skip. The failed ARM log and
the other lanes' three server ZIP digests/six complete TRX inventories remain retained.
All eight lifetime cases pass in each of those three lanes.

A private source export of the exact baseline retains only that original case and adds an
owned scheduling gate. With the pool limited to four workers, all available workers are
held for twelve seconds after the warm-up. An independent thread checks read entry at
ten seconds. The original `Task.Run` worker reproduces the same entry timeout; changing
only that worker to `Task.Factory.StartNew` with `LongRunning` and the default scheduler
passes under the same gate and enters within the original ten-second checkpoint.
The successful case returns 102,486 HTML bytes, disposes exactly once after the read,
has no disposal during a read and preserves the owned Markdown file's exact SHA-256.

This proves a scheduling dependency in the validation control. The historical CI run
does not contain scheduler diagnostics; its exact scheduling state remains unknown.
An earlier one-worker probe hangs before the case starts. It is retained as a failed
instrument setup; its two identity-checked owned processes are terminated and absence
verified. It is not counted as a reproduced test failure.

## Correction and validation

Only the HTML/Markdown test worker uses a dedicated thread. The test intentionally blocks
that synchronous read until the test releases it. All existing lifetime, byte, cancellation,
cache, disposal and timing assertions remain, including ten-second checkpoints, the
25-second held-read limit and the three-second close limit. Production code, dependencies,
build configuration and CI workflow are unchanged.

On the Windows host, all **eight affected cases pass**. The full App suite passes
**341/364**, with **23 declared skips**, zero failures and command exit zero. The first
full-suite supervisor had a three-minute limit: its command exit was not captured, although
the surviving test process subsequently produced the same passing complete TRX. That
attempt is retained separately. A fresh repeat with sufficient command supervision passes;
no test deadline is increased. Full case IDs, output and skip reasons are independently
checked. Native ARM64 successor CI is still pending at this checkpoint.

Private base: authorized second workspace's `FileCatReleaseEvidence/mac-resume-20261005`.

- `../ci-37293936000/independent-failure-v1.json` SHA-256
  `46159f7a84f71675e0cbc94ecdda6c3c4141798c411463eae0c7a93e51abbf30`:
  original CI failure, other-lane server digests and full case inventories.
- `i139-independent-host-v1.json` SHA-256
  `c2c47000c4410e79b24db5af93ab9060a293fbfadba393f83eedcef798fb01ba`:
  42 retained pins, controlled baseline/correction, eight affected cases and the complete
  host App inventory, including separate supervisor/setup failures.
- Private controlled test before/after SHA-256:
  `2efc8a6038be408119ba8ae00ccd8caf5641465461a6280c53df8532340bdec7` /
  `23ee82cac17fba051667e6d1fea15e0c6dc3e233f0270e8dbe72c928f540528d`.

I139 is remediated preliminarily, not Closed. No native desktop, physical device or release
candidate qualification is claimed. Stable publication remains **NO-GO**.
