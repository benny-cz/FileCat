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

## Still to do

- The taskbar progress on a real desktop (the headless tests have no window handle): checked in the lent Windows VM
  (see below when recorded).
- A person's judgement of the new strip and details (V17 usability sessions).
