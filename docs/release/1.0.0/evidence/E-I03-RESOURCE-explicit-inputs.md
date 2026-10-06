# E-I03-RESOURCE — explicit native resource inputs

2026-10-06. Source baseline `0d61dbb`, working recipe correction for the remaining
§10.3 native input inventory. I03 remains Open.

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

Committed full packaging/hosted revalidation continue. This is a resource-input
reduction and provenance improvement; full native byte-read trace, all tool/runtime
composition, licensing and candidate gates remain Open. No source device, host/
guest/Mac setting, helper execution, publication, candidate or human GO.

The exact 8502983 workflow still excludes the new RC/object from both Windows
test-result upload lists. Independent selection against four actual local
production files confirms the omission before remediation. CI adds only the two
resource file globs to each existing upload; downloaded original/successor archive
validation continues. No job, permission, package or execution behavior changes.

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
