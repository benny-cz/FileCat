# E-I03-RESTORE — prospective immutable NuGet restore controls

2026-10-06. Preliminary restore correction, initially investigated on exact base
source `246ce18993d9238227182e22a3150f3a032e0154`. Plan §10.3 requires a reproducible
resolved dependency graph before candidate freeze. The original repository has
no saved NuGet locks. Validated policy adoption follows below; committed hosted
source validation is pending. I03 remains Open.

An isolated raw-Git source export verifies every blob/mode/size/hash. Its derived
configuration enables lock generation, declares the four intended shipping RIDs,
uses only the official NuGet source and stores packages in an owned private cache.
All twenty-one projects restore on SDK 10.0.401 and produce lock graphs. The
initial `git archive` export fails raw-byte verification before restore; it and
the original failure remain retained. Raw Git export corrects that staging issue.

The ordinary solution locked restore passes. `dotnet restore --runtime` narrows
the declared RID list, failing NU1004 against the multi-RID graph. Using the
publish-equivalent `RuntimeIdentifier` property preserves the declaration and
passes all four App RID controls and both Windows broker SC/FDD restore modes.
Changing the direct Avalonia request refuses with NU1004; changing its lock
content hash refuses with NU1403. Restoring all twenty-one original lock files
and the exact original central-properties bytes returns the solution to healthy.

Deleting the App lock exposes a prerequisite: SDK locked mode regenerates it and
exits zero. A prospective target before each restore-graph project entry now
requires a present lock unless explicitly regenerating dependencies. Healthy
solution/restored controls pass; missing App and referenced Core locks refuse
without recreation. This guard is tested only in the isolated export. Package
updates, additional accepted script RIDs, clean cross-platform/RID publishing and
tracked policy adoption still require validation before any source change is
treated as remediated.

Sixty-three unique package archives are pinned separately from NuGet's logical
content hashes. The original observer wrongly equates `.nupkg.sha512` archive
hashes with lock content hashes, then compares restored raw central properties
with a CRLF working checkout. Both observer failures remain in tool history.
Corrected inspection, without rerunning completed controls, matches each lock
logical hash to official-source cache metadata and separately computes actual
archive SHA-256/SHA-512, matching the archive sidecar. All sixty-three differ
between logical and raw hashes. This establishes those cache/archive identities;
it does not qualify extracted build inputs, signatures, licenses, full SBOMs or
a candidate. NuGet's package reader distinguishes content and archive hashing
([upstream implementation](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs));
[Microsoft's lock-file guidance](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies)
describes locked dependency restore.

Private `FileCatReleaseEvidence/nuget-locks-20261006-v2`:

| Retained path | SHA-256 |
|---|---|
| generated-locks-v1.json | f13f53174f6d66b5569be30b70a7f13399e360e78e01cc75a752ad30dd3234eb |
| locked-controls-v2/independent-locked-controls-v3.json | 6b41851d5174e817e44f727d473ae4e5c5f69386d05aaca601f2ea9c3523ce78 |
| missing-lock-guard-v1/independent-presence-guard-v1.json | 3d40876bd1db35489b751f039d16493163671d0d56154b593dae3f8e4cd9eb4a |

## Validated policy adoption

The next raw-source derivation covers the five RIDs already accepted by the
existing publish scripts, including Linux ARM64 for restore compatibility only.
It enables default locked restore and explicit `FileCatUpdateDependencyLocks=true`
maintenance, retains the missing-lock project-entry guard and uses the official
NuGet source. All twenty-one default project restores, solution restore, five
App RID restores and four Windows helper mode restores pass. Default changed
request/content hash and missing App/referenced Core controls refuse; explicit
maintenance recreates the exact original App lock and ordinary restore is healthy.

The actual packaging icon command fails because its file-based SDK project has
no persistent lock. That original failure is retained. The same tool now has an
explicit `eng/IconFrames` project and its own empty package graph; both packaging
scripts invoke that project. Default restore and missing-tool-lock refusal pass,
and all seven generated PNG frames equal their independently parsed original ICO
payloads. No missing-lock exemption or dependency upgrade is introduced.

The actual Release solution build, five self-contained ReadyToRun App publishes
using production `-r` switches and both Windows helper SC/FDD modes pass. The
two derivation stages retain 52 command results, including five expected refusals
and the original file-based failure. Independent inspection rechecks 1,645 actual
published file hashes and all restored inputs. Cross-compilation establishes build
compatibility; it does not establish native desktop or package qualification.

Twenty-two locks, the validated props/target/source configuration and explicit
icon project are copied to the working repository based on `187fad1`. The new
`eng/Validate-DependencyRestore.ps1` passes all twenty-two tracked projects with
locked restore, verifies actual assets against each lock's package/version/logical
hash and retains every source lock, actual assets JSON and stdout/stderr. Its
working receipt is explicitly dirty and identifies that baseline plus adopted
inputs. All six CI builder definitions run it before building and always retain
the resulting evidence, including partial results on failure. Independent staged
input/graph/CI-order inspection and PowerShell syntax/whitespace checks pass.
Exact committed native CI and actual Linux/Mac packaging revalidation continue.

Private `FileCatReleaseEvidence/nuget-locks-20261006-v3`:

| Retained path | SHA-256 |
|---|---|
| generated-policy-v3.json | fc7f3b32d8b7f96e0fe147a1942fdfd518dfc980d72e5fda1677b38a6eb773e1 |
| icon-project-derivation-v4.json | be39da3165f00763f66bfdc8c120647cdf92968e68ecc6f4990b1092c6c355b4 |
| policy-controls-v2/independent-full-policy-v1.json | b1d30c188cb99f432e8e202ed4da1b5187b7daef29e26301e3ba168ca56b57f9 |
| repository-adoption-v1.json | 747f97758a1ee2013f5ba3b9579073b2c25d31bfb3c7243ee2c977a441e9c87f |
| repository-restore-v1/dependency-restore.json | 3a5dfbbae32ed2f81f04a1195dda1704d0a08dee3becbd9b9e30b65cd7379478 |
| independent-adoption-v1.json | 91cd6943e00df9810b2c151b413cf81020ba03559b42e6e9705d9046bfb43b8e |

Extracted package files, implicit SDK/runtime packs, native composition/signatures,
full license provenance, full per-artifact SBOMs and final candidate remain Open.
Counts remain 139/161 preliminary Remediated, one Closed and 21 remaining issue
remediations. No tag, release, candidate, guest/Mac setup or physical-source access
occurs in this correction. Stable remains NO-GO.
