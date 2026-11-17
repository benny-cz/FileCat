# Runtime capacity and binding health qualification

The internal runtime view fixes passed native filesystem API and Avalonia headless checks on
Windows, Mac and Ubuntu. Exact source changes, build inputs, XML results and clear post-run
process inventories are retained in [the r1253 packet](source-manifest-r1253-runtime-capacity-health.json).

- Rescan availability now updates across windows when another window acquires or actually
  releases an analysis place. Failed-close notices also propagate. Windows detach the observer
  when closed; posted notifications check window lifetime before touching controls.
- Admission changes notify outside the ownership lock. A failing observer cannot turn a
  successful acquisition or close into a stranded reservation or suppress other observers.
- A memory result adopted from a completed acquisition shows captured-result status instead
  of the initial choose-locations prompt.
- Indexed rescan binding tracks the current private/source preparation device. An actual I/O
  watchdog notification cancels preparation and explains that outstanding work remains owned.
  Late notifications cannot cancel an unrelated rescan. The subscription ends with the binding
  operation. The existing scheduler's health and cleanup policy remains authoritative.

| Executed suite | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Focused lifecycle and watchdog checks | 29 passed | 29 passed | 29 passed |
| Full analyzer rendering suite | 813 passed, 1 declared skip | 814 passed | 814 passed |

The focused run includes both the existing native preparation watchdog and the new indexed
binding watchdog. The new test occupies the device's actual worker lanes, waits for the real
watchdog notification, releases the work, and verifies no successor epoch was published.
Cross-window capacity is checked before and after actual closure. Existing owner handoff,
late return, refusal/retry, marks/basket, focus, private exclusion and native-handle cleanup
controls are included. All Core source hashes remain identical to their previous qualification,
so unchanged Core tests were reused.

The native million-file runs use a separate frozen batch and are not claimed by this packet.
The public command, progressive indexed startup, branch refresh/watchers, remaining resource
and feature gates, physical desktop/accessibility matrix and final independent review remain
unfinished. This packet does not mark W1 or the complete product goal finished.
