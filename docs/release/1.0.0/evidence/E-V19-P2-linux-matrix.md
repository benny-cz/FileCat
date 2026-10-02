# E-V19-P2 — development packages on fresh Ubuntu 24.04 / 26.04

Preliminary V19 execution, 2026-10-02; no frozen candidate/final qualification. OS provisioning in
[E-ENV-07](E-ENV-07-ubuntu-matrix.md). Only actually completed cases count below.

## Inputs

Manual CI [36994087185](https://github.com/benny-cz/FileCat/actions/runs/36994087185), source
`d14199b8b2bc1f6170ceab2b91c3212e80909d0c`, 0.1.0-dev.522, linux-x64, self-contained .NET 10.0.12. All four test lanes
and Linux/macOS packaging passed; Windows tag-only package skipped. No publication. Hashes reverified on the guest.

| Artifact | SHA-256 |
|---|---|
| filecat_0.1.0~dev.522_amd64.deb | `2ae2a3b1954860dcdc3da42d92e4891270e9f7614cf3c21f24416299de725d0f` |
| FileCat-0.1.0-dev.522-linux-x64.tar.gz | `39b66f51b08647f4e4a20ec5ff76054d478660dd2ca03c87f21ab644b5172ee4` |
| FileCat-0.1.0-dev.522-x86_64.AppImage | `60d120256aca9c1e7192e0c573c1b25d2e194a3bd193961a7d498cfbc0e1fdc2` |

## Ubuntu 24.04 completed cases

- Fresh full Desktop 24.04.5 LTS, kernel 7.0.0-38-generic; GNOME Shell 46.0, actual Wayland login / XWayland 23.2.6.
  VM/disk identities match E-ENV-07. No dotnet command/SDK before or during package cases.
- Powered-off baseline snapshot filecat-clean-ubuntu2404-20261002 before FileCat/testing dependencies. Installer logs
  and fresh package inventory retained privately (contain the disposable account's hash).
- Debian dependencies resolve with libicu74 74.2-1ubuntu3.1, libssl3t64 3.0.13-0ubuntu3.16, WebKitGTK 4.1
  2.52.6-0ubuntu0.24.04.1 and libsecret 0.21.4-1build3. --version prints 0.1.0-dev.522. ldd finds only the optional
  .NET tracing provider's liblttng-ust.so.0 missing; normal app startup succeeds.
- Native gio launch of the installed desktop entry displays FileCat, its account title and a Unicode file. Later
  xdotool/wmctrl installed for XWayland input/close, after clean install/start.
- Ordinary GUI F5 copy of 40-byte žluťoučký.txt into the separately observed target panel completes; cmp succeeds and
  both hashes are `0cd110f27a8d5838cb198b950738bed55c1f26a96384c56369e0dd7f7d5e3c20`.
- Tar executable works from an ampersand/space path, but its desktop entry fails native validation/launch: **I98**,
  reproduced and helper-remediated in [E-I98](E-I98-linux-desktop-entry.md). Rebuilt-package validation pending.

Guest root `/home/benny/FileCat-v19-83b12fc1f8604ea2a2405069f4d783ad` not reused. Host evidence
`artifacts/release-evidence/linux-os-matrix-20261002/24.04/`.

| Evidence | SHA-256 |
|---|---|
| deb-first-install.log | `dafdd8e30101967b5e3226ad396d18686ee937fd48fe3393cb391eebf416201c` |
| deb-desktop-launch-r2.png | `614bdb338cd1ae7011393c8ad0f4cafc90b10cc22de9e4b694ef4369e6b7df29` |
| Initial native-session.txt | `221ede9ba13d88031d6331f7afc27a22d64e8a2dda35abdb2b9a814de1742c78` |
| Clean snapshot listing | `9c3f695be94ec65bdc1e130dad1c92cd3dad5929464e0623c619cc1994d8f035` |
| Private installer archive | `3098bbef0dc764130a1b103b6002692f71f4e3ef6f3ea9daaa17747bc40b0c6d` |

## Setup and remaining cases

Installer completed/powered off as configured; proposed restart never performed. Temporary VM console authenticated
and bound only to host 127.0.0.1:5901. Windows VM gracefully shut down after completed cases at owner request. Ubuntu will
be shut down when no longer needed.

GNOME locked after five minutes. Automatic approval review initially rejected idle/lock changes and unlock for lack of
specific security authorization. Owner then explicitly approved them for this disposable Ubuntu VM. idle-delay=0,
lock-enabled=false and unlock applied; LockedHint subsequently no. Recorded test setup, not a FileCat security claim.
Windows computer-use helper could not initialize; no Windows UI actions used.

Remove/purge/reinstall and state retention, normal AppImage launch, rebuilt tar GUI launch, remaining native services
and every 26.04 case are in progress. No human reader/input attestation, polkit safety evidence, support decision,
final signature or stable GO supplied by these automated preliminary checks.
