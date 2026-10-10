# E-I17-STANDARD — native standard-account pre-elevation boundaries

2026-10-10 CEST. **Twenty native controls pass** in the disposable Windows 11 Insider 26300 guest. Exact product **0aec7a7b4c68d3dd142e0d34c23d0e2fdd6f68f6** supplies the unchanged Windows/Core assemblies; later 069f772 changes tests/docs and d880b99 changes docs only. A private observer references those actual assemblies without rebuilding product code. This extends [I17 consent evidence](E-I17-consent.md) and the earlier broker trust work; it does not qualify installed-candidate consent or close I17.

## Measured account and ACL matrix

An owned temporary local account is a member of Users and absent from Administrators. Its actual observer SID matches the created account, and native WindowsPrincipal reports neither Administrator nor LocalSystem. The observer invokes the production helper trust check by read-only reflection and the public pre-launch refusal path. All executable fixtures are one owned zero byte, containing no executable code. The accepted protected fixture is never launched.

| Owned fixture | Actual trust /pre-launch result |
|---|---|
| Administrator-owned file and parent, standard Users read/execute | Trusted; actual standard-account append is denied and original byte/hash remains. |
| Explicit standard-account Modify on file | Refused before elevation; a separate native write succeeds and its complete changed bytes/hash verify. |
| Standard-account owner, with no explicit user write grant | Refused before elevation. |
| Administrator-owned/read-only child below a user-writable parent | Refused before elevation; the child itself has no user write grant. |
| Owned file outside Program Files /missing file | Both refused before elevation. |
| Owned symbolic link outside Program Files to the protected file | Resolved location is Program Files, but the original alias is refused before elevation. |

Seven complete native security descriptors are independently inspected for owner SID, allow masks and inheritance. The temporary create-file permission is removed before the final observations. All six untrusted cases produce the production IOException stating that nothing was run; no UAC, broker, consent dialog or plan execution occurs. The volume-GUID path returns to the original protected display path. Portable mode and the missing installed helper retain their distinct refusal explanations. These are finite pre-elevation/component observations, not a signed installation or complete TOCTOU/loader/IPC matrix.

## Original failure and restoration

The first attempt creates its owned account/fixtures, then the standard-account PowerShell ownership step returns exit one before any FileCat check runs. VMware Tools retains only the nonzero-exit report; the underlying native error is unobserved and no cause is inferred. Its account/fixtures are cleaned up and those raw receipts remain. A fresh case creates the owned file as the standard account, allowing Windows to assign the owner, then removes the temporary grant before checking trust. It reuses the exact same compiled observer; no product correction or new defect is claimed.

Both distinct test-account SIDs, their profiles and Users memberships are independently confirmed absent. The owned Program Files tree, alias and fixture roots are removed; only owned setup was changed. All seven product/observer files match before/after, and all 193 private runtime files match the pinned prior and actual post-run inventories for both cases. No owned process remains. Raw outputs, commands, ACLs and retained payload/results remain available for campaign custody/final cleanup. No workstation UI, physical source, contract freeze, candidate or stable publication changes.

Broader I17 still requires installed-candidate UAC/consent/token/path/lifetime and human qualification. This standard-account subset does not waive those gates.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain exact assemblies, commands, ACLs, failures, raw observations and restoration.

| File | SHA256 |
|---|---|
| `i17-native-permissions-20261010-v1/independent-final-v1.json` | `2c3658bf4448831c5d35ddabb2bf17f37c6d6a39a631128072a5298dbf7a2a83` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/seal-v1.py` | `e1eaf756f99f79e21d5919f3af4153e5190ff810100c6ef188e1e6cc00a0cab8` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/build-command-v1.json` | `cca93e7966990949709133b07884a70dae4e3638fd6f5379143961a734120013` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/Program.cs` | `f9c9c11311b3557b38bcc749077d387defa25e0605fd9cc5f5e0dad487d8f265` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/run-native-v1.py` | `4ac4340b19a60b1fa5bbb8c0fe6238fa6b285564afa4e3a4cb6ec7241b7c2653` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/run-native-v2.py` | `e8ee024484bff378a23d1ec9fdd14bf3da174dc30abee946cd5cb6343dac347e` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/setup-v1.ps1` | `a16fd1464b37cd5d21d7c42712dc45d91d0625417169aaa2e14c65f56e1293dc` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/setup-v2.ps1` | `8b863c3275603938bdebaa3c1e2e3b5005125d4e3c239c3244291846d9e0a9e9` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/finalize-v2.ps1` | `4e1554761180561afecd64b933688d8f3b13534661fce515a8cfbd70afd28bef` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/run-v2.ps1` | `b679bee33e0c2d4095a82ed84cfc9aae117dfd851c1f5ad819856015d1a96b71` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/cleanup-v2.ps1` | `b4dd6cf02c8c6d988a7622cf79a636f75533758394e9e157195e852e00a12107` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/post-v1.ps1` | `786b452df8436df5ede40d1db2d3fd7d27671a12bb602e301c4cdf2a59a97fa8` |
| `i17-native-permissions-20261010-v1/standard-owner-v1-command.json` | `aa49b15cf3527909483db5971db77328969e0d606909c754f56f69fcd4797740` |
| `i17-native-permissions-20261010-v1/attempt-v2/transport-final-v1.json` | `03854836f3a898412058dbce7920ef57d93c4c22cdc592d772eaf99bdc907390` |
| `i17-native-permissions-20261010-v1/post-command-v1.json` | `8fb593b43cc75e691e5767273768ce358f809d461403b2d464e806e84c3c93e8` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/retrieved-v2/acl-before-v1.json` | `d701da81fb3347d4c603c7c3972cde2f0edf3a03cc3f91ee0f238cc606d6c139` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/retrieved-v2/cleanup-v1.json` | `05cf5f8ce8ac5f6c7ee2ef062741629eb9868839418770fecb023471de80ae26` |
| `E:/FileCat/artifacts/release-evidence/i17-native-permissions-20261010-v1/original-retrieved-v1/cleanup-v1.json` | `2856ea9a15165e401c54de582e9c8b925fdeaf644dbb34a8e997daaa8f2bb826` |
