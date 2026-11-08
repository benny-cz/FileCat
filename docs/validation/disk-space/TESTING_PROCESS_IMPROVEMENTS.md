# Reducing qualification overhead

Use large frozen implementation batches with one build and three platform bundles. While guests run their admitted batches, continue independent code/review work. Preserve exact source and binary hashes so later results identify the tested implementation. Keep a machine's original runner until actual completion; elapsed time alone does not authorize restarting it.

Before broad Windows suites, run a short exact-count core preview and the relevant native/headless workflows. The same previews are useful on Mac and Ubuntu. Stop the batch at a failed preview. Recheck process admission before every phase and postpone if another tester is present. Never close another tester's process to obtain a test slot.

Reuse an unchanged guest build only after verifying all its cached input hashes. For test/helper-only changes, rebuild the affected test project, permit only its DLL/PDB in the delta, and run the affected workflows. Reject reuse when a product input changes. A product change still needs appropriate broad regressions; a helper fix does not by itself justify repeating all successful product checks.

Transfer one bundle per machine per candidate and collect one bounded result archive when that batch actually finishes. Avoid per-case transfers, repeated full directory copies, and repeated result downloads while a suite is still running. Keep generated fixtures and isolated guest profiles in the same batch; finish by closing task-owned processes and recording the process inventory.

Measured in this packet: the production candidate's full build took 169.70 seconds and packing exactly three archives took 23.30 seconds. The two additional indexed-source tests required a 13.25-second rendering-project build. Verified test-only bundles were about 426 KB each instead of full bundles of 42.4 MB (Mac), 39.8 MB (Ubuntu) and 78.9 MB (Windows), reducing combined transfer bytes by over 99%. The already-passing broad suites were reused; only the two new cases ran on each machine. These are observed examples, not an overall completion-time estimate.

This process reduces setup, transfer and redundant regression work. It retains actual native checks and does not substitute headless evidence for the plan's physical desktop, accessibility, display, package, resource or final independent-review requirements.
