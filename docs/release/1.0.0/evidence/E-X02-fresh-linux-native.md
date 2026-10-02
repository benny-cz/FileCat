# E-X02 — native suites on the fresh Linux matrix

Preliminary execution on 2026-10-02. Ubuntu 24.04 results below; 26.04 pending. No frozen candidate or final
qualification. Package-specific checks are in [E-V19-P2](E-V19-P2-linux-matrix.md); OS/disk identity in E-ENV-07.

## Inputs and setup

Source `ecf5349eb1c81035e44b20c911b3a25955ed9915`, git archive SHA-256
`94f4f4b97acc27fc479944b59130283d0bd918955b1e9a906d00cc5fa7af7731`. Release solution build succeeds: zero errors,
nine warnings. Test payload file hashes and resolved project.assets.json retained. These are test builds, distinct
from the self-contained development packages; test results do not qualify their wrappers or final signed bytes.

User-local .NET SDK 10.0.401 / runtime 10.0.12 downloaded from Microsoft's
[official release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) and checked
against SHA-512 `51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b`.
Private test PATH only; initial clean package checks preceded SDK installation. Python venv: pyftpdlib 2.2.0,
pyOpenSSL 25.3.0, cryptography 46.0.7 and transitive versions in retained pip freeze. The first tool setup failed
because curl was absent; urllib-based retry verified the download. No FileCat defect inferred.

Actual GNOME Wayland session, XWayland display/authentication and desktop D-Bus. WebKitGTK 4.1
2.52.6-0ubuntu0.24.04.1, libsecret 0.21.4-1build3, Samba 4.19.5 Ubuntu revision 4ubuntu9.7. Require flags enabled:
FILECAT_REQUIRE_SECRET_STORE/WEBKIT=1, FILECAT_TEST_SAMBA/GVFS=1 and FILECAT_TEST_BLOCK_DEVICE bound to the recorded
loop device. Missing mandatory service would fail, rather than count a skipped result.

Samba is a read-only owned share on 127.0.0.1:445, verified with native smbclient byte comparison before tests. Its
initial guest access was denied by the private home directory; force user=benny in the test share resolved access
without changing profile permissions. Initial config retained. Raw FAT16 loop backed only by the immutable fixture;
fresh 64 MiB FAT32/exFAT image loops record exact backing paths and mount sources before any format/test. Root is used
only for fixture setup and the exact UnixFatRecordTests filter. Independent resulting files are each 5,000 bytes,
SHA-256 `c59d3c0480cc2d71d8f646e735e92da65450311eec46e81a5db8c7e6e8a92054`. Raw FAT16 hash remains unchanged;
identity-checked loops detached and the owned Samba server stopped after tests. Images retained.

## Results: total / passed / failed / skipped

| Run | Counts | TRX SHA-256 |
|---|---|---|
| Core full, corrected native setup | 741 / 702 / 0 / 39 | `61d47b47e46b7282cef1b227f2b6149b98ecc53a48cc18ca306d197a02ab795d` |
| Remote full | 116 / 94 / 0 / 22 | `7a2edf08132dc8f1d5b6a865f6483f7cb876b91d601c9d0debaf2ee6a986436a` |
| App full, native XWayland/WebKit | 230 / 203 / 0 / 27 | `080c035ab1b6b535005acbe797f0a1c8f53f6f5e115361acfe2eb724f6c14ff5` |
| SecretStoreTests, unlocked isolated native keyring | 4 / 4 / 0 / 0 | `1c2129d4fb713811f5636fdcd227c0b159d4a42bd2d96cd65125d867da9b88cc` |
| LinuxPageEngineTests, actual desktop display | 2 / 2 / 0 / 0 | `e241706eb0324dd2b9837f24401933d330d7e8b288f0b43f15c51a800c337088` |
| UnixNetworkTests, Samba and GVfs branches required | 5 / 5 / 0 / 0 | `855ae7f3e7974627736626377a01b9b429f46d397c8169b9916376c5f819dfdc` |
| UnixFatRecordTests, native image mounts | 1 / 1 / 0 / 0 | `676bde0af79e9f1a783ea37d92df891212f0f6515cb51f0da1646f8b3ff91709` |
| InspectorCrossCheckTests after binutils installed | 8 / 5 / 0 / 3 | `6dc63fa520168ec579d2a47e09db27abf24cada06f2536b98d87995aae5d88ee` |
| Windows-formatted recovery image corpus on Linux | 3 / 3 / 0 / 0 | `d20eed5a054d2b8a162588ea505e2b390a368bf5c557e2bf3cdda16f6a2a828d` |
| GitStatusTests after git installed | 8 / 7 / 0 / 1 | Retained in supplemental archive |

The existing Windows-formatted corpus hashes match the retained host inputs: exfat.vhd
`621c79fd338fe168874bee50b26a90fedbe4596cde1c44386778f8d09b326c82`, fat32.vhd
`c64e5b619de8a77fe669c4320ad313434d29d4febf3216cfda22456de0a847d9`, ntfs.vhd
`acd62b106a82baa9790bd8e662c3c8afae6e82013c20ef643a46aa03292081e0`.

## Failed setup runs, skips and limits

First full Core attempt **aborted** after four-minute inactivity while the native secret-store test waited; TRX outcome
Failed, SHA-256 `f9ae774decae93329a79ebe601918f705239e415c1d70df0864492004584acec`. Its 664 completed tests do not
make the suite successful. Blame named the secret-store test; no dump was generated. Additional bounded attempts
retained. Bare keyring --unlock invocations spawned competing daemons; the desktop keyring stayed locked or exposed
no collection object. Stopped the test-created daemons and service/socket, then started one real GNOME Keyring on the
same desktop bus with a new private data/control root. Locked=false and its bus owner verified; four native CRUD tests
and the full Core rerun pass. This proves libsecret/Secret Service with controlled test storage, not the normal login
keyring's autologin/unlock UX or human comprehension of its prompt.

A targeted WebKit attempt omitted XAUTHORITY: two GTK initialization failures, TRX
`5ef4cd3e00dc24d1d563105efbef98464b0490ecfde2e0fcb1a3191e4788cae9`. Correct XWayland authentication yields 2/2.
Both attempts retained; no product modification based on setup failures.

Complete skipped names/reasons in native-trx-summary.json. Many are Windows/macOS-only; others require the external
lab, network capture, performance corpus or live destructive/media/write-trace setup. They remain unexecuted by these
runs. Root FAT, Windows-formatted recovery, readelf and Git targeted runs replace their corresponding prerequisite
skips; do not subtract or merge counts into invented totals. The key-server negative was attempted after gnupg setup
but still correctly skipped because FILECAT_V24_SHARE/capture was not supplied; it is not a pass. Historical V08/V09/V24
records remain separate, and mandatory candidate reruns still apply. Human Orca/input/IME/polkit attestation is open.

Raw host root: artifacts/release-evidence/linux-os-matrix-20261002/24.04/. Private package/native archive SHA-256
`acb0cbae68d2a03efa9e965942a2ecb91d282d288b5cea89b444a724f5e89cc2`; includes failed/successful fixtures, TRXs,
logs, package inputs, scripts, state and test payload hashes. SDK installation and Python environment excluded; exact
SDK metadata retained. Socket objects skipped by tar (diagnostic log retained); regular evidence files preserved.
Separate resolved-assets archive `9971883afb205fd74cd749c1e11cf0268c5a145f346beffb2be5ba3f648dc0df`; native supplement
`91559331e35ba0d19988947f30aa6b04f8b9a310f266e336e828b997407dff33`; JSON TRX summary
`f4b3b527d6a70191bd9d91ce9abe40da928333488e5130ff20cee8683c88b74a`. Guest/host archive hashes match before OS overwrite.
