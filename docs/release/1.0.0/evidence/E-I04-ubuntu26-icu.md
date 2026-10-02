# E-I04 — Ubuntu 26.04 Debian dependency failure

Preliminary V19 execution on 2026-10-02. No frozen candidate or final qualification.

## Reproduction and remedy

Fresh Ubuntu 26.04.1 LTS, kernel 7.0.0-38-generic, GNOME 50.1, actual Wayland session on the identity-bound VMware
guest in [E-ENV-07](E-ENV-07-ubuntu-matrix.md). Clean powered-off snapshot `filecat-clean-ubuntu2604-20261002` retained
before FileCat, SDK or test dependencies. No `dotnet` command or installed FileCat during the attempt.

Unmodified development package `filecat_0.1.0~dev.526_amd64.deb`, source
`ecf5349eb1c81035e44b20c911b3a25955ed9915`, manual CI
[36999624175](https://github.com/benny-cz/FileCat/actions/runs/36999624175), SHA-256
`60e15d29faaba9fbb18ba1773c2e63026d895c843a9c0a14c07b7ffff8ceb4c7`.
Its declared ICU choices stop at libicu76. APT update succeeds; libicu78 78.2-2ubuntu1 is installed and available,
but none of the declared ICU alternatives is installable. `apt-get install` exits 100 and does not install FileCat.
This is a reproduced High clean-install failure for the Ubuntu 26.04 Debian claim.

`eng/package-linux.sh` adds libicu78 to the existing alternatives. Working producer SHA-256
`d158b2b95deaf70e769e18a2e99506c0204e05d580171035ea5fa783818fea66`.
[Microsoft's self-contained .NET dependency guidance](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-decision)
identifies libicu78 for Ubuntu 26.04; [Ubuntu's package record](https://packages.ubuntu.com/en/resolute/amd64/libicu78)
matches the guest's actual version. Rebuilt 26.04 installation/runtime checks pass below; compatibility check on
24.04 remains pending. I04 also retains its support-contract and other-platform gaps; this change cannot close the issue.

## Rebuilt package verification

Fix source `cc97a8d39eef5a3b20d58b7158efc7bdf1e6d462`, manual CI
[37015434145](https://github.com/benny-cz/FileCat/actions/runs/37015434145), development version 0.1.0-dev.531.
All four test lanes and Linux/macOS package jobs pass; Windows tag-only packaging skips. No tag or release publication.
Rebuilt Debian SHA-256 `d1b7f1298d090650f716e56425668ea6140e70c5d82235a4cf31a16311e8faf5`, verified on host and guest.

APT installs it normally without an SDK/developer PATH. ICU78 78.2-2ubuntu1, libssl3t64 3.5.5-1ubuntu3.7,
libsecret 0.21.7-2build1 and WebKitGTK 4.1 2.52.6-0ubuntu0.26.04.1 satisfy the package dependencies/recommendations.
CLI prints FileCat 0.1.0-dev.531. Installed desktop entry launches `/opt/filecat/FileCat`; `/proc` maps identify the
actual libicu78 libraries, and the visible Unicode source/target panels reopen. Settings/workspace/history hashes
remain unchanged by installation. Remove, reinstall, purge and reinstall preserve those files and a GUID-bound
unrelated `/opt/filecat` sentinel; owned executable/link/desktop entry disappear on removal. Detailed lifecycle and
other package checks in [E-V19-P2](E-V19-P2-linux-matrix.md). These are preliminary development-artifact results.

| Verification evidence | SHA-256 |
|---|---|
| deb-fixed-install26-host.log | `a330ad56240319e3dd2c1e83f392eefd2f479027593d71536585610da9b44cfc` |
| deb531-desktop-native-r2.txt | `022ae90840e83b65bbb8c5f0e4cd417696f5267419f54084547057336073ae73` |
| deb531-desktop-gui.png | `d5ca7182ff5500e06dce978506024a99cbff45cd89b72062b5ab0e4e102292b5` |
| deb-lifecycle26-host.log | `50d5a57183f6b8d4a8450e87e954fc6cce49e63ec94a2149e710d20fdbb67b1a` |

## Retained raw inputs

Owned guest root `/home/benny/FileCat-v19-26-e7fac2cb613344ebb26a0a2b75777a25`; host directory
`artifacts/release-evidence/linux-os-matrix-20261002/26.04/`. Original packages and failed install log retained.
A host transfer wrapper stopped after the first successful upload because it checked an unset exit-status variable;
that file's hash was rechecked and only absent files were uploaded on retry. No product test ran in that setup failure.

| Evidence | SHA-256 |
|---|---|
| deb-first-install26-host.log | `2c87d823231f555a62b975a291a5d5e7f859ce6498274bf3533f432ccab0ae10` |
| installed-os-r3.txt | `3ea4770fc0fb707c9ce38e5f061523f3e73af41a9ec9f92de75841c01cfe9e36` |
| Clean snapshot record | `69484d9c23f50df0a378717cc4be3cf6ae991e6dc303869b35eefd236ef5ff9e` |
| Private installer archive | `8f20d561edf2ecd8d0f56dbbce318ad8a95bdb6eb62b68d9e0c7d588048ccb5e` |

Installer archive contains private setup data and remains ignored. SSH host key obtained through authenticated VMware
guest operations before pinning. The private SSH helper initially used an obsolete Bouncy Castle DLL; correcting it to
SSH.NET 2026.0.0's declared 2.7.0 dependency restored access. This is a test-helper setup fault, not a FileCat SSH defect.
