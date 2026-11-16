# O4 bounded performance diagnosis and Owner-on criteria

This method is recorded **before** the diagnostic/Owner-on measurements. It addresses the three original O1
comparison misses; those raw results remain unchanged. No favorable replacement of them is selected.

One control/candidate pair uses the original production baseline 20899295 in detached worktree
E:\FileCat\.tmp\worktrees\column-ownership-baseline, and current O4 code in the implementation worktree.
Only the identical benchmark test file is copied to the control. Build both Release with SDK 10.0.101 on the
same machine/runtime. Run the control, then the candidate, sequentially, without concurrent builds/tests.
Each retains one warm-up and five timed samples. Diagnostic mode keeps the original PF1 1M × 4 / 240-frame
workload and the two missed 100k PF3 scenarios (one tab, two tabs with superseding request); it omits the
already-passed PF3 variants. CPU time, GC pause/counts and allocations are recorded before the forced memory sample.

Apply the **same pre-existing differential rules** to this paired control: PF1 elapsed +15%, median per-run p95
list render +0.5 ms, allocations +5%; PF3 elapsed max(+15%, +50 ms), allocations +5%. Record original outcomes
and paired outcomes separately. If this pair still misses, inspect measured hot paths/allocations and fix a
concrete regression; do not widen the allowance or rerun just to get a pass. This headless Skia evidence does not
claim native input/compositor acceptance. PF3 snapshot/retention costs remain O5 work.

PF2 uses four 1M-row synthetic listings, the same 1920×900 / 240-frame scrolling pattern, and a fixed fake ownership
source with four principals. The Owner column is appended to Details and scrolled into view; geometry and source
counts are recorded. Measure first paint/demand separately, then one warm-up and five steady-state samples.
No real filesystem owner/name reads occur. Compare against the candidate PF1 reference and also compare a cached,
identical Owner geometry with a plain fixed metadata field, to distinguish metadata presentation cost from layout.

Owner-on allowance, set now: per-list median render increase at most 0.5 ms and per-list p95 increase at most 1 ms
over the geometry-matched plain field; median render at most 2 ms and p95 at most 8 ms. These leave room in the
existing 16.7 ms frame budget (tail pauses are reported, not interpreted as compositor frames). Incremental process
allocation over geometry-matched plain metadata is at most 64 KiB per scripted four-panel frame; incremental process
allocation over PF1 is at most 192 KiB/frame. The latter conservatively allows ~1 KiB for each of up to ~160 visible
rows' item/path/cache-key/context/detail allocations plus 32 KiB headroom. Extra first-demand allocations are reported
separately and are bounded by visible-row demand, the existing metadata backlog, and the resolver's queue/cache limits.
PF2's process peak and retained memory are reported; O5 must separately enforce charged shared-byte reservations.
