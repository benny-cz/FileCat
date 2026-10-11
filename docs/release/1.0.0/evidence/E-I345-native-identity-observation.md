# E-I345 — observe native identity before path canonicalization

The original **I343 push run 38130627026 attempt 1 at fd851d1c363a21870a1c3e08364973c6d127613d** reports twelve Mac Core failures at the new fixture's non-empty original-identity assertion. The complete original job log remains retained. Its output shows non-empty native identities for canonical `/private/var` paths, complete source-image protection and healthy output bytes, but a null fixture field: the observer assigned that field only when the queried path string equaled its `/var` input. Full-run collection remains separate from this job observation. [Original workflow](https://github.com/benny-cz/FileCat/actions/runs/38130627026).

A fresh ordinary-user Mac control uses actual `/tmp` → `/private/tmp` canonicalization with the exact unmodified fd851d1 fixture. **All twelve original observation assertions fail**, while independent complete-byte checks verify all six source protections and six healthy extraction routes. Native file identities are present in the queries; the fixture's original field remains null. All 1065 affected predecessor outcomes/messages and 36 exact skips remain.

The fixture now captures the identity through the exact owned source pathname before the job can rename or canonicalize it. The identical native alias control passes all twelve cases, with non-empty native identities, complete protected/healthy bytes, closure, progress and publication counts independently verified. The [combined cleanup/identity audit](E-I344-temp-directory-retirement.md) passes 24 local/36 native controls on Windows/Ubuntu/Mac, preserves 4046 predecessors/108 exact skips, adds 88 listing results and verifies all 1327 native files/restoration. Only two test fixture files change; product source protection is unchanged.

The actual hosted failure is explained by its captured alias/query/field mismatch and separately reproduced. Original failure outcomes stay failed. Exact committed/hosted follow-up for the corrections and installed-candidate qualification remain. Physical-source HOLD, owner decisions and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, original failures/skips, complete bytes and restoration.

| File | SHA256 |
|---|---|
| `i343-ci-20261011-v1/job-114444044877-adverse-v2.log` | `a8ffb8ca2ec6338baf5ec442a2bb93c1c5767abbc0b6bc5a95ec2605f08de9ee` |

[Exact committed/native follow-up](E-I344-I345-native-qualification.md) and [original four-lane CI](E-CI-cleanup-identity-observation.md) pass at d909333. The [original I343 CI](E-CI-backing-staging-original-failure.md) remains adverse with twelve failures/two unavailable inventories.
