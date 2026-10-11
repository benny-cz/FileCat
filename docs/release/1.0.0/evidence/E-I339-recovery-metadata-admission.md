# E-I339 — recovery admission before origin metadata writes

2026-10-11 CEST. Baseline **9086901baffffc75f3bcfdf165d313597f492dcf /1549 canonical raw Git blobs**, with separately declared test/product overlays. This controlled High/Critical deleted-data safety gap extends I106: a late publication refusal did not prevent an earlier origin metadata write on the staged path. No physical source or workstation UI is used.

Reading source bytes, querying length or querying the final revision can change destination admission after staging starts. The baseline calls `WriteOriginMark` even when the recovery guard now answers “same source disk” or “unknown.” The product now checks every selected/traversed source immediately before that metadata call and checks the staged path against protected backing files. The metadata write is refused before calling the adapter. The ordinary download-origin propagation and existing publication checks remain.

All **eighteen local and 54 native fixed controls** pass. The same assertions first retain **six unsafe failures/twelve healthy controls** per producer on the baseline. Cases cover three change points, same/unknown/distinct answers and present/absent origin marks. Whole source-marker and healthy output bytes, exact metadata-call counts, complete staged bytes observed at each call, source closure and failed-copy byte rollback independently verify. These are actual owned staging files and recorded metadata calls using the recovery guard's controlled answers; native xattr/ADS mutations and kernel topology replacement are **not** claimed.

Every **3960 affected predecessor outcome/message and 107 exact skips** remains: 802 local passes/17 skips, 1029 Windows passes/18 skips and 1011 passes/36 skips on each Unix platform. Windows measures high RID 12288/session 0, Ubuntu UID 1000 and Mac UID 501, with pinned .NET 10.0.12. Fresh postchecks verify **1161 files** and absent owned processes/temporary roots. Windows transport listeners stop; no package or persistent setting changes.

The Windows baseline controller originally exits 1 because three files from an existing nested-archive fixture remain after its tests finish. All selected test outcomes remain retained. A fresh bounded restoration saves the three complete archive files, verifies their ZIP/TAR/7z relationships and seven reconstructable fixture images, checks exclusive opens after producer exit, and removes only those ten checked files and empty owned directories. The original cleanup failure is preserved, and a later independent postcheck verifies absence. Its underlying timing/ownership cause remains unproven; the fixed producer has normal successful restoration. An initial reader used obsolete phase names and refused before sealing; its source remains, and the corrected reader changes only those references.

Exact committed/hosted/candidate follow-up remains separate. These path/provider checks are non-atomic. Wider cleanup writes after topology changes, mount replacement, held-device identity/visibility/permission and physical-source validation remain open. Physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact source, commands, original failures/skips, whole bytes and independent restoration.

| File | SHA256 |
|---|---|
| `i339-metadata-admission-20261011-v1/independent-remediation-v1.json` | `e85928eb20df758ff07f8b9e0e49dcffd2e38650f6e085de817c238534394042` |
| `i339-metadata-admission-20261011-v1/retained-tool-sources-v1.json` | `c10afbad27942576a657b206b157fa18b67f2e6d4265c4fba1d8bc69c12750e2` |
