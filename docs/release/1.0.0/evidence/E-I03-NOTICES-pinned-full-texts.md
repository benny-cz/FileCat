# E-I03-NOTICES — pinned full dependency and runtime notice texts

2026-10-06. Preliminary I03/I10 correction for plan §10.3, working baseline
`241be6f`. Committed native/package revalidation is pending. I03 and I14 remain
Open; no candidate or stable publication.

The current packagers copy a summary table but omit the full license/NOTICE
texts supplied by actual dependencies. The table additionally asserts completed
OSI-only eligibility while the release register explicitly retains RAR/native/
AppImage audit gates, and uses the former non-RID inventory filename.
[E-I03-RESTORE](E-I03-RESTORE-prospective-locks.md) retains the original source,
63 exact NuGet archives/1,644 archive-matched extracted inputs and nine verified
Git blobs at the immutable revisions declared by packages. Source metadata is
not a binary reproducibility or complete license-provenance conclusion.

The frozen snapshot now retains **48 original license/notice texts**, plus its
README and provenance index: 29 archive-provided App graph texts, nine declared
upstream texts and ten exact .NET runtime pack texts. All five actual SC publish
dependency files identify .NET 10.0.12; their archive hashes, raw SHA-512 sidecars,
official-source metadata and exact ZIP/extracted notice bytes verify. The snapshot
maps 38 App graph packages and five runtime packs. It includes build-only/RID
alternatives and does not claim that every mapped component ships in each artifact.
One full-text gap remains explicit: LTRData.Extensions 1.0.23 declares MIT, but
its complete declared source tree contains no license-named file. That bounded
search does not prove the absence of all possible licensing statements.

The package-only `eng/DependencyNotices` tool uses an empty, five-RID NuGet lock.
It checks indexed text bytes, missing/extra/duplicate/escaping paths, exact App
package version/logical-hash identities and the actual published runtime-pack
identity before creating output. It refuses existing output directories and
linked source entries, then copies the validated bytes and checks the copies.
This is a consistency check; it does not determine legal eligibility.

Seven positive controls use the five retained actual cross-RID SC publish
dependency files and both b9526b9 actual FDD package dependency files. The FDD
ZIPs remain unchanged; those controls use retrieved metadata and do not qualify
the whole old package against new source. All fifty copied file hashes per
positive match their independently frozen originals. Ten original refusals
cover malformed/missing index, extra file, duplicate/escaping path, changed
package version/hash, schema, unknown runtime patch and preserved existing output.
Inspection finds that the original changed/missing-text fixtures target index
JSON because Windows path sorting places it first. Two additional fresh cases
target a pinned license itself; both refuse before output. All nineteen original
results remain retained; no false license-byte coverage is inferred from the
earlier index controls. Source snapshot, locks, tool and original payloads remain
unchanged by those controls.

Windows SC/FDD packaging now checks/copies `licenses/dependencies` before ZIP
creation; installer recursive payload inclusion carries the same tree. Linux
checks/copies before tar/deb/AppImage creation. Mac checks/copies into Resources
before ad-hoc signing/ZIP creation. Their existing SDK runs the explicit tool;
no new Python/PowerShell dependency is added to Unix packaging. The summary now
records exact ANGLE/runtime versions, current inventory filenames, external
WebView2 ownership and pending provenance instead of asserting eligibility.

Independent adoption inspection checks all fifty working/raw-staged Git file
hashes, every package/runtime notice reference and invocation ordering. C# build
passes without warnings; PowerShell and both shell scripts parse. Generic
whitespace checking flags untouched upstream whitespace and frozen CRLF metadata;
the original 4,970,701-byte diagnostic is retained. A narrow snapshot
`-text -whitespace` attribute preserves original bytes and ordinary checking
passes elsewhere. Adoption v2's field name uses 58 total paths for 57 unchanged
paths; v3 corrects the label without rerunning controls or changing text bytes.

Private `FileCatReleaseEvidence/nuget-locks-20261006-v3`:

| Retained path | SHA-256 |
|---|---|
| notice-bundle-v2/independent-notice-bundle-v2.json | 7ec4def283ebe0482e88bab114b50be37b0936cb18123cb112fb3ad65c7a49d2 |
| notice-controls-v1/independent-notice-controls-v1.json | e62491c03fc66110d774a54aee6f88ddb7600401f9efe1bb06ff5fef15791b6c |
| notice-byte-controls-v2/independent-notice-byte-controls-v2.json | ccb3c8dd02891af4b79fb96409a52eb2e2b98ec3d2caee13b4d430fe28480aa6 |
| notice-adoption-v1/independent-notice-adoption-v1.json | 5999bef5b4594ca394346e584b0a728eafcfb85eb356b56f4245d15b45aa24b2 |
| notice-adoption-v1/independent-notice-adoption-v2.json | 8763e379a942e211e2e6e25b18761dafb26fa12dfbc9dbfd7660c074dcdee628 |
| notice-adoption-v1/independent-notice-adoption-v3.json | 90f9c19ef1033a6270558dabc68fcf31b041f7f91159c7f68e75895822abcf91 |

Actual changed production packaging, committed hosted graphs/tests and final
archive notice-byte inspection continue. These source/text changes invalidate
previous package-specific qualification. Full source/native composition,
signatures, implicit runtime input inventory, LTRData/RAR/AppImage obligations,
full SBOMs and candidate remain Open. Counts remain 139/161 preliminary Remediated,
one Closed and 21 remaining issue remediations. No physical-source, guest/Mac
setup, tag, public release or human GO occurs in this correction.
