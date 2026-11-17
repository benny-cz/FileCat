# EFS zero-byte setup and actual associations — 17 November 2026

Status: **implemented internal candidate; partial N-KEY/E-KEY evidence**. This qualifies zero-byte setup and actual certificate/key observations on the supplied Windows 11 x64 VM. Product EFS remains unavailable. Recovery choices, parented export/provider interaction, unexpected-key stop behavior, recursive integration, output routes and wider UI/native gates remain open.

## Behavior

`IStorageEfsSetup` is an E-only seam. An explicit setup choice is required before resolving the workspace, claiming native resources or creating an artifact. Setup uses the normal invoking SID and the active worker's existing native allowance. There is no default-key getter/setter, arbitrary EFS-EKU selection, account switching or implicit enrollment during availability/C inspection.

`WindowsStorageTransforms.Setup` walks the configured application base with bounded, case-aware no-follow opens, actual local NTFS eligibility and the protected-Windows anchor. It promotes the verified ancestors to continuously held read/list guards excluding ordinary rename/deletion. The application base must be user-owned without another ordinary writer. A dedicated `FileCat.EfsSetup` child must have a protected user/SYSTEM-only DACL; an existing unqualified child is refused. Only newly created owned objects receive these private permissions; selected-item ACLs are not edited.

Setup creates one unpredictable `CREATE_NEW` zero-byte file with `FILE_ATTRIBUTE_ENCRYPTED`, relative to the held private directory. It writes no payload. It holds that created object, validates its identity, encrypted state, zero EOF, single-link/non-reparse state, private owner/DACL, exact captured spelling and complete bounded stream inventory. The only accepted inventory is the empty unnamed data stream. Cancellation after creation still retires the owned artifact; Windows-created keys/certificates remain.

`WindowsEfsAssociations` uses supported `QueryUsersOnEncryptedFile` and frees its returned native list. It refuses an empty, partial, invalid or over-256-entry list; certificate hashes must be exact 20-byte SHA-1 identities. It probes only the actual associations naming the invoking SID, through the previously qualified fresh, silent/no-healing CAPI/CNG key-comparison service. Recovery agents and arbitrary store certificates are not substituted. The detached snapshot separates all reported users from current-user key evidence; a matched key is neither backup proof nor proof of file decryptability.

The pathname query is enclosed by validation of the caller's continuously held binding. It retains the approved cooperative-namespace limitation: before/after checks do not prove continuous identity during an equal-privilege in-place link conversion. Actual user-file queries require the original live EFS binding from the same adapter; a detached ID/path is not enough. This checkpoint implements post-encryption association acquisition, not the recovery-coverage policy or unexpected-key stop.

Cleanup uses only the exact created file handle and only after rechecking its known identity, zero EOF, one link and complete empty main-stream-only inventory. It never reopens a filename for deletion, recursively cleans a workspace or deletes a certificate. Added stream data is retained with a cleanup problem and its last captured path/identity. The result records an attempted setup path even when native creation fails, without claiming ownership of an unreturned object. Native metadata failures become structured setup/cleanup problems after handle retirement.

## Verification

The platform/native tests built with zero warnings/errors. No tests ran on the workstation.

| Target and exact scope | Result |
|---|---|
| Windows VM, normal interactive `admin`, `WindowsEfsSetupTests` + `WindowsEfsPathTests` + `WindowsStorageInspectionTests` | **26 passed, 0 failed, 1 deliberate fresh-account skip** (27 cases). |
| Windows VM, new standard account `fcEfs_1117c`, first-use method only, explicitly loaded profile, `FILECAT_TEST_EFS=1` and `FILECAT_TEST_EFS_FIRST_USE=1` | **1 passed, 0 failed, 0 skipped**. The skipped method above passed here: 27 distinct Windows cases passed across the two runs. |
| Ubuntu VM, `WindowsEfsSetupTests` without native opt-in | **1 passed, 0 failed, 8 native skips**. Only explicit-consent admission is portable. |
| Mac, same portable scope | **1 passed, 0 failed, 8 native skips**. Mac remained awake and unlocked. |

Setup tests cover an actual zero-byte association/fresh matching key; ordinary rename exclusion; cancellation after creation; a native metadata failure; rejection of an existing broadly writable application namespace before provisioning; private-child permission drift; retention of injected ADS payload; and actual post-encryption user-file association through the original live binding. The latter verifies unchanged synthetic payload. Existing EFS path-call/stream/race fixtures and C inspection fixtures regress the shared captured-spelling helper.

The separate first-use run used SID `S-1-5-21-1278333194-2778939646-3566894036-1005`, medium integrity, its real `C:\Users\fcEfs_1117c\AppData\Local` directory, and no preexisting EFS certificate. Declining setup left that count at zero. Explicit setup created one actual EFS certificate, obtained one fresh matching current-user key, and deleted the zero-byte artifact:

```text
EFS_FIRST_USE account=S-1-5-21-1278333194-2778939646-3566894036-1005 beforeEfsCertificates=0 afterEfsCertificates=1 matchedKeys=1 cleanup=True
```

The new account, profile and actual EFS key/certificate are retained in the disposable VM for later recovery qualification. The test launcher loads the profile; FileCat does not create/switch identities or load another user's profile.

Earlier evidence is retained rather than overwritten. The initial setup fixture requested SACL access unnecessarily and failed before setup; it was corrected to request owner/DACL only. A direct VMware-created fresh-account process returned access denied during encrypted creation, and a documented Win32 encrypted-creation control did too; neither created an EFS certificate. A separate fresh account with `Start-Process -Credential -LoadUserProfile` passed, followed by the final-build fresh-account run above. This isolates a launcher/logon-context dependency; it does not establish every internal cause of Windows error 5, justify a production workaround or weaken the failure result. The never-started password task was verified as never run and disabled before another launcher was used.

## Inputs and artifacts

SHA-256:

- `StorageEfsAssociations.cs`: `781B06794F363D45CB42E72D1BD4CA86F0FDE9652300EAF9687F4F903D5E13BD`
- `StorageEfsSetup.cs`: `DA962BA4E7352C175223FBDEB000CBAF0A487ED53174F6458D606130AB160D93`
- `WindowsEfsAssociations.cs`: `DF85880BBA7E9CFA5AD0FC425A2EA4BDCDAF6412090EE0F71F034EDCD2204F85`
- `WindowsStorageTransforms.Setup.cs`: `143952FAF066E66CDCD00F41682CBA3C380606B7264997B5703F61B0B6919A4B`
- `WindowsEfsSetupTests.cs`: `A5350E8245B08B3E4136F833DA654AB727A0734CD13F2811A2EF8A6978AA0DAE`
- `.tmp/filecat-efs-setup-final-native.tar.gz`: `0E2424FB36AB1ECA0758F744A1DBE995B095E743845C76F56947B4BE6426A445`
- `.tmp/filecat-efs-setup-final-portable.tar.gz`: `7627FAA4B3F8D65703863DF5455B20BC50B1FD01515F6338F494382F41A7F840`

Ignored retained artifacts include `.tmp/filecat-efs-setup-final-results.log`, `...-tests.xml`, final launch scripts, `.tmp/filecat-efs-first-use-profile-c-results.log`, `...-tests.xml`, and its profile-launcher log. Both final Windows logs end with exit 0/completion markers. Earlier ACL/expanded/first-use/control evidence is separately named. Portable XML is `/tmp/filecat-efs-setup-final-portable-tests.xml` on each supplied Unix machine.

Remaining qualification includes namespace/case/cloud/native-policy breadth, cancellation and resource limits across the complete workflow, hardware/provider UI, each relevant certificate's explicit recovery choice/export, unexpected post-call key change and durable recovery evidence, recursive integration, all enabled E-OUT routes, sparse preservation and the complete product UI/architecture gates. These component passes do not enable product mutation or claim recoverability.
