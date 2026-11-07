# RAR5 captured inputs, job passwords and native transactions

**7 November 2026.** The unregistered RAR planner now enters the shared native preparation and publication
pipeline. This is implementation and component validation; it does not enable RAR writing in the application.

`ArchiveContainerTransformPlan` carries the common frozen expectation/capture contract. TAR and RAR retain
format-specific planners and stage bounds. Shared preparation/results are now named for containers; the older
TAR-named source filenames remain to preserve existing documentation links. RAR admission retains the complete
capture owner's metadata/name/heap charges, rejects substituted owners/bindings, and requires every captured file
and directory to be used. Captured directories retain exact native metadata and independent provenance evidence.

`ArchiveJobPassword` owns one operation's validated character buffer and supplies independent one-use grants for
assessment, transform and fresh verification. Preparation borrows it; the caller disposes it when the operation
ends. Disposal zeros the owner buffer, and issued grants retain their own bounded lifetime. Persistence throws
before secret bytes are written. Password/mode/disposed-owner and fixed protocol-2 factory checks precede native
reservation and journal intent. Neither transaction facts, manifests, output receipts nor recovery catalogs retain
the password. Restoration requires a live owner again; a retained snapshot or journal is not a credential store.

Windows launch factories select protocol 1 or 2 explicitly. Application protocol-2 assess/transform/verify startup
arguments enter the existing native confinement/cancellation checks before parsing archive requests or starting
Avalonia. Existing protocol-1 routes remain covered by regression. The native test uses a real standard account,
a loaded user profile and newly published self-contained probe/application assets from the frozen source.

Native creation captures one file and one directory, records both provenance sources, rebuilds through a worker,
closes write authority, independently verifies the sealed native object, publishes its new name and rechecks the
published digest. Methods 0–5 and unencrypted/payload-encrypted/header-and-payload-encrypted modes are covered.
Windows verifies the strongest zone survives a less-restricted file and a more-restricted directory. The actual
application's three startup roles additionally perform identity-preserving NTFS content updates and explicit
fresh snapshot restoration, keeping exact rollback bytes and the stronger current origin through cleanup.

The Unix native file tests use actual held directory-relative storage/capture/sealing/publication operations and
ordinary fixed-protocol subprocess endpoints. They do **not** qualify the Unix production containment route or
Unix replacement/update. Windows AppContainer creation/update/restoration coverage does not replace the remaining
resource, adversarial, crash, fuzz, six-RID, filesystem, packaging or full application/UI gates.

The broad core archive matrix has **2,994 cases**; its corrected run has no failures:

| Platform | Passed | Skipped | Final native RAR follow-up |
|---|---:|---:|---:|
| Mac | 2,939 | 55 | 18 passed |
| Ubuntu VM | 2,952 | 42 | 18 passed |
| Windows VM, standard account | 2,756 | 238 | 24 passed |

There are 36 new core cases, including 18 Unix-native creation cases which skip on Windows. The Windows native
matrix initially completed 354 cases: 332 passed, one skipped and 21 new creation readback checks failed. All
three actual-application update/restoration cases passed. The first shared helper read the published target with
`File.ReadAllBytes`, which did not share the retained native owner's DELETE access. Its correction exposed the
same default-sharing mistake in `File.ReadAllText` against the live writable journal. Both reads now use the same
explicitly shared test reader. This changes no production exclusion or publication behavior. The final 24 RAR
native cases and ten application-entry cases all pass. Affected native creation cases repeat on Mac and Ubuntu;
the passing broad regression is retained. Both Windows readback-failure phases are frozen separately.

Each Unix follow-up has **18 independently tested native archives, 18 exact payloads and 36 exact metadata
records**. Windows has **30 archives, 33 payloads and 57 metadata records**, including source/updated/restored
versions for all three password modes. The earlier 18 shared worker workflows also retain independent checks of
36 archives, 108 payloads, 162 metadata records and 18 retained packed-stream witnesses on each platform.

The evidence verifier checks frozen file inventories and the exact current compiler-source hashes for 32 changed
Core/shared-test C# inputs on each Unix platform and 47 on Windows. The three private Mac VM bridge references have
the separate compile-only check below. Native test CLR observations are 10.0.12 on Mac and 10.0.14 on both VMs.
Final censuses have no owned FileCat/test processes and retain the Mac's until-reboot wake helper.

The initial 2,991-case core matrix had one new test failure on each platform. The test used exactly the permitted
heap ceiling for an empty plan and incorrectly expected refusal. Its corrected four cases use one byte beyond each
capture aggregate ceiling and check that capture plus a nonempty plan is counted together. No production behavior
changed for that correction. The expanded 2,994-case matrix is repeated and initial results retained separately.

Independent Python parses header CRCs, encrypted headers and every semantic metadata record. Pinned official
7-Zip tests the complete archives and extracts payloads only to bounded stdout for exact length/SHA-256 comparison.
Windows independently checks source, updated and restored archives; restoration bytes must equal the original
snapshot exactly. Runtime tools run only on the authorized Mac/VMs. Host work is source editing, compilation and
static frozen-artifact validation. No new codec/runtime dependency is introduced; the writing-only closure still
excludes the reader-only Rar15 and DiscUtils assemblies.

The private Mac VM validation bridge's common type references are updated and its host compiler build passes.
Its missing dependency lock is now recorded with the existing project/BouncyCastle closure; this is a compile-only
check, not new VM bootstrap or RAR runtime qualification.

During final evidence review, the old `FileCat.*.pdb` glob was found to omit `FileCat.pdb`. The audit tool now
includes the application, requires its PDB when its DLL is present, and checks the portable-PDB identity against
its DLL's CodeView GUID/stamp/age. A separate static audit of the same frozen Windows binaries verifies the missing
application inputs and every selected DLL/PDB pair. Missing application symbols and a deliberately mismatched
DLL/PDB pair both refuse with exit 65 and no success facts. The tool source, build, controls and snapshots are frozen;
archive tests were not repeated for this evidence-tool correction. The original narrower audit is retained rather
than retroactively described as application-inclusive.

The [checkpoint record](rar5-native-checkpoint.json) pins source/evidence hashes, exact test outcomes,
independent outputs, actual process/account observations and compiler-source checks. Native file operations and
Windows application workers advance the existing plan. Remaining work includes Unix native update/replacement,
complete production worker/resource/RID/package qualification, UI/password/edit/F6 workflows, legacy password/profile
inventories, complete 7z member graph/catalog/shared workflows and the final independent review. The goal remains active.
