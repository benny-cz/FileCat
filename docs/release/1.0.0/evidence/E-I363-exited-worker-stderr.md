# I363 — retire an exited Unix decoder without waiting for descendant stderr

Baseline **a8f7159ecba9e7fc72ce478e482e118ff6a48029 / 1,639 canonical Git blobs**, with one new test overlay. The real private decoder launch wrapper starts an owned shell root and a finite child. The root exits successfully; the child remains alive until the fixture releases it. When the child inherits stderr, both direct disposal and Kill-then-disposal stay pending. Closing only the child's stderr makes both controls complete. The corrected native baselines retain **four passes and four failures** across Ubuntu and Mac.

The root's actual exit now controls retirement; the parent then disposes its pipe reader. The existing live-tree stop and dedicated launching-parent channel remain. This avoids coupling admission to descendant pipe EOF: [.NET's process implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.cs#L1451) also drains asynchronous stderr in WaitForExitAsync. No PID is signalled after the root exits. **This correction does not contain or remove the surviving child.** The owned test child is explicitly still alive at successful product retirement and is released by the fixture afterward. Wider Unix authority, detached/exited-root descendants and I08 remain open.

All **eight final native controls pass** with the identical corrected regression. The native affected selection has **220 records: 214 passes and six explicit skips**; the Windows headless host selection has **100 passes and ten skips**, including four Unix-only new cases. All forty unchanged attached-child, parent-channel and public decoder-request cases pass on the native platforms. Twenty-eight complete pixel observations verify expected output bytes or every interior checkerboard pixel. The pixel fidelity cases exercise native decoding in process; public request cases separately exercise the child decoder. These are not native window or full containment acceptance.

Independent postchecks verify **1,306 distinct native input/runtime files**, no owned workers or scratch, and three restored host roots/four retained scratch files. No persistent setting changes, host UI, physical source, candidate or publication work occurs. Exact committed and original-push qualification follow separately.

The first fixture queried ExitCode on a Process acquired by PID, which is unsupported on these Unix routes. Its eight failures remain with the original observations; cleanup masked the product assertion. Only that cleanup query changes in the corrected fixture, which then gives the paired four-failure/four-healthy baseline. Both versions, every source overlay, native command, assertion message and owned cleanup observation are retained; no old run is reclassified.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, byte observations and restoration.

| File | SHA256 |
|---|---|
| `exited-root-qualification-20261011-v1/independent-final-v1.json` | `4f7a5190d7529a29bafcfe0c0834d15028bd6d7e0baeb83cab0beaed5d7b67b5` |
| `exited-root-qualification-20261011-v1/retained-tool-sources-v1.json` | `67582c79b1996e60a227b4a494815a3ff41227605a1d4ffa3b1a3122a22ff082` |
