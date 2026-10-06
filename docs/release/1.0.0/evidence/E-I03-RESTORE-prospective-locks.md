# E-I03-RESTORE — prospective immutable NuGet restore controls

2026-10-06. Preliminary restore correction, initially investigated on exact base
source `246ce18993d9238227182e22a3150f3a032e0154`. Plan §10.3 requires a reproducible
resolved dependency graph before candidate freeze. The original repository has
no saved NuGet locks. Validated policy adoption follows below, committed at
`a7a70ae2b1e27c5dce6f7e8f0790daa2a2ae9f53`. Both original push and independent
development-package CI attempts are now sealed. I03 remains Open.

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
Exact committed native CI and actual Linux/Mac packaging are sealed below.

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

## Committed-source CI and development packages

Source `a7a70ae2b1e27c5dce6f7e8f0790daa2a2ae9f53` passes original push
[37476206680](https://github.com/benny-cz/FileCat/actions/runs/37476206680) and
independent development dispatch
[37476271305](https://github.com/benny-cz/FileCat/actions/runs/37476271305), both
attempt 1. Each seals policy/four required native test lanes/ARM64 startup, draw
and installer checks; nineteen/twenty-five downloaded artifact ZIP digests,
four/six clean exact-SDK build receipts and fourteen full TRX inventories verify.
All 409 App identities, sixteen picture cases, sixteen policy, 128 draft and 52
package-set controls per run match source/run identity. Expected platform skips
retain their reasons and are not passes or final qualification.

Four/six restore receipts retain 88/132 actual project-assets graphs, all source
locks and restore logs. Independent inspection compares every package/version/
logical hash, input/validator/source-lock byte identity and original run identity.
The native API transport retains original-attempt metadata/logs and all server
digest evidence after bounded CLI connection timeouts. No test is rerun for that
transport correction. Actual development Linux tar/deb/AppImage installation,
startup/icons and Mac ad-hoc signature/start/icons pass; retrieved manifests match
all actual package file hashes. The Windows tagged package and draft jobs skip.
No tag, public preview, stable publication, native desktop qualification or
candidate is created. Earlier failed runs remain retained.

Private `FileCatReleaseEvidence/ci-37476206680-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | c079addd1eb2928ea264a54d0a888bf6d6a41637669b4ea89031aecf192160d8 |
| independent-fixture-ci-v1.json | ab120936c391deabaefe6507a878270a1e0ff47c5de42d7469da6338fb30f9b8 |
| independent-producer-policy-ci-v1.json | ef4aad3a2625855d14b0383aad767808cddf25702ff11ed6a8c28a6f486154ee |
| independent-draft-guard-ci-v1.json | c3e83a6b59a9ff61a105aa6642d8a4ddb2ba93ce7ce9c0f2533c2f19c7da9d46 |
| independent-separation-ci-v1.json | 076d360d5e38580a5158921942866994599195eadd366e0b12c524a53fff3aa5 |
| independent-restore-ci-v1.json | c0a98933378c94461e6c8f72247c514b31b569d8c1ab0ad18529b0b45af848e8 |

Private `FileCatReleaseEvidence/ci-37476271305-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | f0e349c3ce222771080f1660d5ab3c2a41143cb65264ff1381a5650336196ffe |
| independent-fixture-ci-v1.json | 7e13f5eb5b665ffdf1eb037663b554bec3fa8820992265cc1fc6cc8275e6f3da |
| independent-producer-policy-ci-v1.json | 0f329ed3b18afd45d15b452832894d3e5d4bfe2d9789c81a817d7f9413ef02bd |
| independent-draft-guard-ci-v1.json | 0ab4d85f95d8b163c8e9b27e3abfdf73eaf7eac37298e502441827e9a454af17 |
| independent-separation-ci-v1.json | 49f803d890586bbaab2b79ac605672fddc3cea62897862353facca334aa5fe6f |
| independent-restore-ci-v1.json | 447af4a6f71f4d44589b046bb3a7d93b7d3bd0db5be537d7d15eaeed691120b0 |

## Owned extracted-input and full-text preparation

Read-only comparison of the owned cache associated with the raw-source derivation
and dirty working restore verifies **1,644 extracted inputs across 63 packages**
against the previously pinned NuGet archives. Actual assets declarations, generated
metadata, original archive identities, byte-preserving nuspec case normalization
and non-input archive metadata remain separately recorded. The original observer
uses the wrong archive-hash field name and fails before comparison; corrected v2
uses the retained schema, without modifying the cache or rerunning FileCat.
This covers the observed owned cache. It does not prove all compiler reads,
implicit SDK/runtime packs or the hosted runners' extracted input bytes.

Nine full license/notice Git blobs are retrieved at immutable source revisions
declared by the exact packages and independently verify their Git SHA-1, length
and SHA-256. Thirty-eight original full texts are staged with a package/file/hash
index for the thirty-eight resolved App graph packages, including build-only/RID
alternatives. Their presence does not establish that all those components ship.
The staged bundle explicitly records one full-text gap: LTRData.Extensions
1.0.23 declares MIT, but its complete, nontruncated declared source tree contains
no license-named file. That finite search does not establish the absence of all
possible licensing statements. No license eligibility or RAR/native/runtime/
AppImage obligation conclusion is inferred from metadata or root licenses.
The bundle is still private preparation; package source changes and actual
archive notice-byte validation remain the next executable work.

Private `FileCatReleaseEvidence/nuget-locks-20261006-v3`:

| Retained path | SHA-256 |
|---|---|
| extracted-inputs-v2/independent-extracted-inputs-v2.json | 819cd3fc0b74b1fe1770981e9743338ff0ecb02296f1aea8c57049c5748f4acd |
| upstream-license-roots-v1/pinned-license-roots-v1.json | e03ebc141e97d8e2fc9ba9ff391b2f4fe096a0686c750071523e0596865b8c58 |
| upstream-license-texts-v1/independent-upstream-texts-v1.json | 57c5c5429bf7cdab84a5c4722085ce13324d62b8567d4a203c1744cbd4121d5f |
| notice-bundle-v1/independent-notice-bundle-v1.json | 8a582177baaa5df8eddec9846880a87c74034d0e720a24fa413f281928bd0515 |
