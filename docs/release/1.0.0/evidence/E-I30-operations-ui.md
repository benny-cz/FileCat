# E-I30 — how a running operation shows

Issue: [I30](../FILECAT_1_0_RELEASE_ISSUES.md#i30--running-operations-should-show-what-happens-in-the-best-possible-way).
Owner's requests (2026-09-30, middle priority): "when user moves his data, he needs to know what is happening and have
it visualized in the best possible way"; "user wants to see the progress in the best possible UX/UI way".

## Before (pictures from the new screenshot mode, `d40fda0`)

The Operations strip was one line of text ("Title — Running · 477 MB of 709 MB · 0 of 201 items · 57,3 MB/s · about
30 s left") over a 4-pixel bar and the current file's name; no percentage, no progress of a large file, no phase; the
details drawer listed jobs, and its right half stayed empty until one was picked.

## Change `67f70f9`

- **Strip:** the phase instead of the bare state ("Copying", "Verifying", "Finishing", "Waiting for your decision",
  "Paused", "Counting what to do"); a 6-pixel bar with its percentage beside it (never 100% before the end, I26); the
  current file on its own line, and for a file of 16 MB or more its current step with a bar of its own — "holiday.mkv ·
  660 MB of 700 MB" while copying, "holiday.mkv · verifying, 45%" while reading it back.
- **Details:** opening them selects the running operation (or the latest), and shows at a glance: from → to; phase and
  percentage; the file in hand; "Time left: about 25 s · running for 11 s" (or "Took 15 s"); items with skipped and
  failed counts; data copied and, with verification, verified; speed now and on average; and a graph of the speed along
  the operation (across: how far it is; up: its speed then) that fills from the left and stays after the end.
- **Taskbar (Windows):** the window's taskbar button shows the running operation's progress (`ITaskbarList3`), yellow
  while it is paused or waits for an answer, red for a moment after one failed; nothing when idle.
- **Tests:** `JobProgressViewTests` — a verified copy's phases (Copying → Verifying), the large file's line, timing
  ("running for" → "Took"), items and data texts, 100% only at the end; opening the details selects the running
  operation; the taskbar state is Normal while it runs, Paused while paused, None after it is cancelled.
- **Regression:** host, all four suites (run `run-i30b`): Core 555, Remote 43, Platform.Windows 111, App 173 — 0 failed.
- **Picture:** `i30-operation-details.png` `91b2ba110136cb98937d2da1c78cfce10569339fd3bbd2456280ca4b6901b7c0` (a
  verified, speed-limited copy 10 s in, details open).

## E-I30-V2 — the taskbar on a real desktop (lent Windows 11 VM, Insider 26300)

A small harness window (`taskbarcheck`, WinForms, referencing FileCat.Platform.Windows at `67f70f9`) called FileCat's
`TaskbarProgress.Set` with each state for six seconds on the logged-on desktop, unelevated, while the host captured the
VM's screen every 2–3 s (`vmrun captureScreen`). The harness log (`taskbar-log.txt`
`f98e31c5892c62344fdbefc0a4de304a1453693b3fd28dc92480c1368830940c`) and the captures agree: Normal 0.4 drew a blue bar
at 40% under the window's taskbar button (`shot-07-15s.png`
`7a6e6b48f0fe197d7ae212eaa9c4bbe4d4a222b20a54d4203576909b0ad883c7`), Paused 0.6 an amber bar at 60% (`shot-10-22s.png`
`e0e13df263fb9837602763d3c8f5b2e75a5b36b7cbad642808c0f29afc15bce2`), Error a full red bar (`shot-12-27s.png`
`bf095708e95f3567b2803ec1b0fb24813bd875b60fa894377251e3c4b1ac0f02`), Indeterminate the moving marquee, None only the
running-window underline; no exception. The eight button strips side by side: `taskbar-strip.png`
`46760b707ec56ee864974d56fad2bf78f2e21a3ba15c276026ae4841a7da2497`.

## Still to do

- The whole app driving the taskbar during a real copy (the harness exercised FileCat's call, not the app's wiring,
  which the view-model test covers).
- A person's judgement of the new strip and details (V17 usability sessions).
