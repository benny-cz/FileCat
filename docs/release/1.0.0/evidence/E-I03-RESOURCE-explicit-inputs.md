# E-I03-RESOURCE — explicit native resource inputs

2026-10-06. Source baseline `0d61dbb`, recipe correction `850298359fc16cf28503cea42f3ba9c0b1599240`,
CI retention correction `5e11f60b08ebc534be1b70f2c9dc65e1ea19a3fb`.
Preliminary §10.3 native input inventory improvement. I03 remains Open.

The native resource recipe includes `windows.h` to obtain two VERSIONINFO
constants, while its receipt explicitly leaves resource header dependencies
unverified. The installed SDK 10.0.26100.0 `verrsrc.h` independently supplies
VOS_NT_WINDOWS32 = 0x00040004L and VFT_APP = 0x00000001L. Its original bytes are
pinned in the proof. A separate derived recipe removes the header include and
uses those exact fixed values. Four actual native builds (original/derived on
x64 and ARM64), with identical version/revision/source/icon/manifest, produce
byte-identical complete helper executables per architecture. No helper is run.

The repository recipe adopts those constants, rejects any future generated
header include until its dependency inventory is reviewed, records the generated
RC and actual copied icon as explicit file inputs, and retains/hashes the compiled
`.res`. Its resource-header list is empty and verified at this limited scope.
Existing C++ reported-header/link-library/compiler-component records remain.
The receipt still explicitly records post-compilation hash timing, no individual
compiler read trace and incomplete library/license classification.

Two further actual working builds use **Windows PowerShell 5.1**, matching the
MSBuild target's host. Both complete native binaries remain exactly equal to the
original controls. Independent inspection verifies two resource input pins,
empty header list, eight file pins, retained `.res` bytes and every declared
evidence file. Two derived controls adding direct/spaced header includes fail
before compiler logs, executable or success receipt appear. Original project/
script/icon/manifest pins stay unchanged through these controls. PowerShell
parsing, whitespace checking and staged Git/working source consistency pass.

Committed source 8502983 passes the actual Windows x64/ARM64 production packager
in an owned raw-Git export. All source blobs, four complete payload inventories,
four ZIPs' 50 original notice files each, two inventories' 38 locked identities
and four final build/publish native receipts verify independently. Each retained
RC/resource/report/map pin matches actual bytes; both SC/FDD helper outputs per
architecture match their final native receipts. The raw export has no Git context
and its native embedded source revision is empty; the external exact-source proof
does not turn these local artifacts into candidates. No helper is executed.

Original push/development CI 37496707218/37497325774 attempt 1 pass all four
required test lanes, policy/ARM package startup/drawing/installer checks and actual
Linux/Mac package install/version/icon/ad-hoc-signature steps. Nineteen/twenty-five
server digests, four/six clean build receipts, fourteen complete TRX inventories
per run, all hosted controls and 92/138 actual locked project graphs verify.
Expected skips retain their reasons. The four fresh Unix archives independently
match 200 original App notice files plus seven AppImage wrapper files; every
downloaded package hash agrees with its manifest. No tag/package publication runs.

The exact 8502983 workflow excludes the new RC/object from both Windows
test-result upload lists. Independent selection against four actual local
production files confirms the omission before remediation; original push and
development artifacts each independently confirm both declared files are absent
in each Windows lane. Correction 5e11f60 adds only four upload lines. Every other
workflow byte and all production/test source blobs remain unchanged. Eight actual
local build/publish resource files match the corrected globs.

Corrected push CI 37498145660 attempt 1 passes all four lanes/ARM package checks,
nineteen server digests/four clean receipts/fourteen full inventories/all hosted
controls and 92 actual graphs. Both downloaded Windows artifacts now include
the complete declared native evidence set. RC/object hashes, exact source/icon/
manifest/output pins, embedded version/revision and reported header/library sets
independently agree. Hosted compiler/header/library bytes are producer observations
and are not independently retrieved. The first private observer compared doubled
backslash linker paths to normalized receipt paths; v2 changes only comparison
normalization and inspects the same original bytes without a build/test rerun.

This is a resource-input reduction and provenance improvement; full native
byte-read tracing, all tool/runtime composition, licensing and candidate gates
remain Open. No host/guest/Mac setting, physical source access, helper execution,
publication, candidate or human GO in this slice.

Private `FileCatReleaseEvidence/native-resource-inputs-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-prospective-resource-inputs-v1.json | 6a1f8d9202d46d48faab5e5b77458b9bb4ec8f3a9b74da4bee270e2578a71c18 |
| working-recipe-v2/independent-working-resource-recipe-v2.json | 3427cd7ed7e579c30391efc7cd811785d5c73a4ace31f15f0e1b07d7dcaaaf27 |
| working-recipe-v2/independent-resource-source-adoption-v3.json | 9ca8349b8d49bec61c84ccd6b18cf1630aac74beec4bcba390d79caf715e6a5e |

Private `FileCatReleaseEvidence/resource-ci-retention-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-original-resource-retention-v1.json | e0ac05afe9230584bcaab935b90256a81d45cfaa42316d631d36a7df64fdbd5d |
| independent-retention-source-adoption-v1.json | c72938b1823c8bdfd054e4914c6fc74712602827e0bfc76ce44b28240c2eb2e6 |

Private `FileCatReleaseEvidence/resource-production-windows-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-windows-resource-regression-v1.json | b249ddb5448d6a992ba3731d4f268045921e9cf951ad2b236666015e561a0764 |

Private `FileCatReleaseEvidence/resource-package-inspection-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-package-notice-bytes-v1.json | 05f1d7fdff9fb582ba2ac08d9519831c7c1561f7a71144051750daf4fc7062af |

Private `FileCatReleaseEvidence/ci-37496707218-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | b99a8ed20e96aa9236606cd552d6fdd576bb7ec95930191aeed87d2866bac740 |
| independent-fixture-ci-v1.json | 78aae08cbf691ae78ebdc4c6db0270ca9fe32b6816392733e6edf9c9b29bf957 |
| independent-producer-policy-ci-v1.json | f4078aa3950997e24dc0c0c17c045fc5f29907b6edb4ebc77662022ddda76b0b |
| independent-draft-guard-ci-v1.json | 73feb0ea91d33ca1a0b45f215895653852b1253251cab8f8b2ec418b27dcba74 |
| independent-separation-ci-v1.json | fc1dca4436d2a6bca8a2a8c28e7b91eabfc399f28cae3d206eb997a769c99fcb |
| independent-restore-ci-v1.json | 93246421f2d0c739f97500bdb5812ca4cbfc1b3299c9a0005548afe50a415b42 |
| independent-native-resources-v2.json | f960e20ef5e847ff3a3edb215a7215f11e87d5891bdc0a33ba1552bfde992453 |

Private `FileCatReleaseEvidence/ci-37497325774-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 779f3947a62de8c6a191d7102947e275b927e04cc1895a84cb501999e5cfd136 |
| independent-fixture-ci-v1.json | 968d17b0a9802892bcbf5007ca9633998c0a1fbe5b4f5060e558a525d8d95ab0 |
| independent-producer-policy-ci-v1.json | eb5d5bd47b4ec554a9b04ffe6a10b1e303380f5e8d9d8c5d88185df8728e207b |
| independent-draft-guard-ci-v1.json | 2cde7b6d9b2372ba95d51103b1ea75d2572d47a988662086e7e06c746ebaf88e |
| independent-separation-ci-v1.json | 432eeebba793516b2c92f42b4877820caaf7a190aecaf4f5ad2ecf5e5c4b30ca |
| independent-restore-ci-v1.json | 08135636c596116bbe041970fca3250d7f44b231eabf69041c3d96dd333c5cb6 |
| independent-native-resources-v2.json | 27bb1ca316ca638b18e5c97aca7c991d1ba11cebf212b743afd047d552829375 |

Private `FileCatReleaseEvidence/ci-37498145660-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 7ae35adb1d4082fdd3e097fdbb3e1b38e33eee8f02d4ce60a7676891f8879123 |
| independent-fixture-ci-v1.json | 91a07c9c29d8a594a2eb54c0117c8d55da3891563ef6fd7e1156ae6a5f985588 |
| independent-producer-policy-ci-v1.json | 647d2f7dcad411116d98ab21694d9dacd20195acc4e2ff1514b96b965ff1c5c5 |
| independent-draft-guard-ci-v1.json | 568a0298d673365043e061bdcf19073bd68479ecfe0d839fd0d0c8408b089b6e |
| independent-separation-ci-v1.json | c7a8a002ae2e136ec5c389c4b91524fc71d094e6f86432dd7365e3449a17b2cc |
| independent-restore-ci-v1.json | 0309db748d5ee6fa1c09dc4ecfbd60d36219f8d932f2e567698ea42e2e2765f8 |
| independent-native-resources-v2.json | 801340a4f1846a09336abb987048e57bc0cf315c4536f04ad4db95eb305cb3a0 |
