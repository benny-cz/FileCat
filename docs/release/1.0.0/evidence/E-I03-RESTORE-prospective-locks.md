# E-I03-RESTORE — prospective immutable NuGet restore controls

2026-10-06. Preliminary investigation, exact base source
`246ce18993d9238227182e22a3150f3a032e0154`. No repository restore policy has been
changed yet. Plan §10.3 requires a reproducible resolved dependency graph before
candidate freeze. Current repository inspection finds no saved NuGet locks.

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

I03 remains Open. Counts remain 139/161 preliminary Remediated, one Closed and
21 remaining issue remediations. No tag, release, candidate, guest/Mac setup or
physical-source access occurs in this investigation.
