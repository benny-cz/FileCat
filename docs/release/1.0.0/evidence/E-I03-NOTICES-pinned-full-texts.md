# E-I03-NOTICES — pinned full dependency and runtime notice texts

2026-10-06. Preliminary I03/I10 correction for plan §10.3, working baseline
`241be6f`, committed notice correction `145f569`. Exact-source CI and actual
archive notice-byte checks below pass. I03 and I14 remain Open; no candidate or
stable publication.

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

Exact notice source `145f569a59dbad8def5a3b065eec232f4446eea8` seals original
push 37482955415 and development 37483079672, attempt 1. Both pass the policy and
four required native test lanes, including ARM64 startup/draw/installer steps.
Nineteen/twenty-five server artifact digests, four/six clean SDK receipts,
fourteen complete TRX inventories per run, all 409 App identities and picture/
reference/draft/set controls independently verify. All 23 project locks and
actual assets graphs agree in each of four/six builders: 92/138 graphs. The
development run additionally passes actual Linux and Mac package creation,
install/version startup, icon and Mac ad-hoc signature checks; retrieved package
manifests agree with all final file hashes. Draft publication skips. Two bounded
native-API status-read timeouts are retained; later original-attempt reads pass.

The actual authoritative Windows packager runs x64 and ARM64 from a separately
verified raw Git export of that commit with development-only version
`0.0.0-i03noticecheck`. All four SC/FDD payloads and four portable/FDD ZIPs retain
the exact 50 committed notice files. Portable ZIPs exclude the administrator
helper; FDD ZIPs include it. Both actual SDK package inventories parse. Input
bytes stay unchanged. This host run does not compile/install an installer or
qualify native desktop behavior.

Independent archive reading checks the actual development tarball, Debian
package, AppImage and Mac ZIP against the 50 original Git blobs: 200 exact
file-byte matches, with all package hashes unchanged. Debian's nested AR/ZSTD/TAR
and the AppImage SquashFS are read by pinned installed 7-Zip 24.01; no package is
executed in this inspection. Reader v1 assumes the wrong Debian prefix; v2 then
assumes slash paths from Windows 7-Zip. Both failed observations remain. Fresh
v3 uses actual `opt/filecat` Debian members, normalizes only AppImage listing
comparisons and reads each original member name. All four archives pass.

Private `FileCatReleaseEvidence/ci-37482955415-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 3708961fc287cab28160feb44384057b5a0b6f05a505ce51d40dbe60ffd39e61 |
| independent-fixture-ci-v1.json | 2af1e20066037680df3a5f0127645ffdfb4425e7f78bc046d35d649cb9ac4117 |
| independent-producer-policy-ci-v1.json | a8002eb44664c24cf6f640d45de6e29f465898569f518c1266ebce303c359622 |
| independent-draft-guard-ci-v1.json | 0b8f7576c6f7cba6fa21194ce9d59e6f9f3cb944cac626c4cf5a377f62dfc2d4 |
| independent-separation-ci-v1.json | 775c9025bd6d9f0b5285e846e1bf8bcc367f5d977a97e07280167995c4511cd0 |
| independent-restore-ci-v1.json | 7e1a50e8e24f78f6bcda5913b778ab4e0ad10007eb1a87aa6ce99a8a0f1a3063 |

Private `FileCatReleaseEvidence/ci-37483079672-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | f29b6413ffc6da80b4171181848f74125c6992d7e263107f73194bcd372e9cc2 |
| independent-fixture-ci-v1.json | 9e2df657d01db1cf98100a2900ca3a2cd4a77764af8c2ba1cbc40b76114ade6b |
| independent-producer-policy-ci-v1.json | b6c2cfbdda5d8532aa50138d1f11370e0fe990a2c1f0e396f4b03c0c9bd3abf2 |
| independent-draft-guard-ci-v1.json | d626400391e2b318f6d49a2d07fcd11db35e754bf8d125c534b1f166b0130228 |
| independent-separation-ci-v1.json | 2de4a6241e988f5e7d08865564ed1b504353ae6fed0ba90b24b6123dd905cfc3 |
| independent-restore-ci-v1.json | 6e8afba8c8b55ea9297233fda62509466cce592654ab0a655577b9a54092087c |

Private `FileCatReleaseEvidence/notices-production-windows-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-windows-notice-packaging-v1.json | bc16fba077e23d181e7e07ee6dc4b12202be772b9eab2b8f3f589d015fd8c26d |

Private `FileCatReleaseEvidence/notice-package-inspection-20261006-v3`:

| Retained path | SHA-256 |
|---|---|
| independent-package-notice-bytes-v1.json | 90ed7debbfa9a22816ebe32cd780ef40652934c56fe83a86d921b793194544aa |

These source/text changes invalidate previous package-specific qualification;
the new evidence is preliminary development evidence. Full source/native composition,
signatures, implicit runtime input inventory, LTRData/RAR/AppImage obligations,
full SBOMs and candidate remain Open. Counts remain 139/161 preliminary Remediated,
one Closed and 21 remaining issue remediations. No physical-source, guest/Mac
setup, tag, public release or human GO occurs in this correction.
