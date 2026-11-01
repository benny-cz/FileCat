# I596 — broken file link reported as a removed item

## User consequence and distinct scope

In a native Mac file panel, an owned `alias.txt → missing.txt` remained listed and Properties correctly identified it as a symbolic link with target `missing.txt`. **File → View** then said **The item no longer exists or its folder was removed**. The link and its containing folder still existed; only the target was absent. This directs the user toward the wrong recovery action. Prior link/Properties and missing-item tests did not cover the native Properties → View error handoff for a dangling link.

The neighboring `neighbor.txt` had SHA-256 `7ac59857c529f0872e1826c943b4eb5f8a05855b89a8a0288f98aa931cadd5fb`; the owned target folder was empty. The original source was `a4a93528ebaec3d071b00d281737e71c84407ff4`. The before-state isolated Mac process was stopped.

## Correction and bounded verification

Product `08ec46647a4921b826238db26749aec761867a23` uses the captured file-system link flag when a viewer open fails with a not-found error. It now says **The link, its target, or its folder no longer exists. Check the link target in Properties.** Ordinary missing files retain their existing message, and cancellation retains **canceled**. The message does not claim which component disappeared after the listing was captured. No link is followed merely to create the explanation.

Focused App test `ViewerLinkErrorTests` passed **1P/0F/0S** on Windows x64. A fresh exact-source Mac ARM64 package ZIP had SHA-256 `ad16f5fc8a2c0c6d01691393163a8a6470c0034016981182c112b72d606d1cfd`; the extracted app passed strict deep code-signature verification. In an isolated instance of that package, the same `alias.txt` row and **File → View** displayed the link-aware message. Independent `readlink` still returned `missing.txt`, the neighboring hash remained exact, and the target stayed empty. Both isolated test processes were stopped.

## Disposition and finite candidate rule

Medium navigation/recovery clarity issue, **remediated preliminarily**. On the exact installed 1.0.0 candidate, create one owned dangling local file link and a neighboring known-hash file. Require Properties to identify the link and its stored target, View to describe the missing link/target/folder possibility and point to Properties, and an ordinary missing-file control to retain its generic message. Check that neither action changes the link or neighbor. Stop after this affected path and latest-product five-lane CI with skip reconciliation; do not rerun broad link safety or viewer provider suites without a new consequential anomaly. Public preview.5 predates I596; stable 1.0.0 remains **NO-GO** until all candidate gates and explicit owner GO. This finding resets the exploratory clean streak to **0/20**.
