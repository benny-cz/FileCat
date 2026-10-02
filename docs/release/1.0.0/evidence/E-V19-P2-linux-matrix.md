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

Rebuilt development packages: manual CI [36999624175](https://github.com/benny-cz/FileCat/actions/runs/36999624175),
source `ecf5349eb1c81035e44b20c911b3a25955ed9915`, 0.1.0-dev.526. All four test lanes and Linux/macOS package jobs
pass. Windows tag-only package skipped; no release publication. Inputs retained separately from dev.522.

| Rebuilt artifact | SHA-256 |
|---|---|
| filecat_0.1.0~dev.526_amd64.deb | `60e15d29faaba9fbb18ba1773c2e63026d895c843a9c0a14c07b7ffff8ceb4c7` |
| FileCat-0.1.0-dev.526-linux-x64.tar.gz | `28a13f482e84ef35f5d52fa1935e29a10bd5cc66b0fa8c1778e5fa235c6c3c23` |
| FileCat-0.1.0-dev.526-x86_64.AppImage | `368606912e87f89a13da811b1aead43093334a0941b848d2030953d25b5530a4` |

I04 rebuild: manual CI [37015434145](https://github.com/benny-cz/FileCat/actions/runs/37015434145), source
`cc97a8d39eef5a3b20d58b7158efc7bdf1e6d462`, 0.1.0-dev.531. All four test lanes and Linux/macOS packaging pass;
Windows tag-only package skips, no tag/release publication. Host/guest hashes agree.

| ICU-remediated artifact | SHA-256 |
|---|---|
| filecat_0.1.0~dev.531_amd64.deb | `d1b7f1298d090650f716e56425668ea6140e70c5d82235a4cf31a16311e8faf5` |
| FileCat-0.1.0-dev.531-linux-x64.tar.gz | `d686db4baf4c4aec849f089a0020836a5bf861746a8aafb46727eefee704803f` |
| FileCat-0.1.0-dev.531-x86_64.AppImage | `98d494e00d07c687bd540382d28fe114bedb54f638d60259e40462b1d35defec` |

I99–I103 rebuild: manual CI [37036698071](https://github.com/benny-cz/FileCat/actions/runs/37036698071), source
`fa3a02ad4a0d9b5323f5316504efa842c120d099`, 0.1.0-dev.539. All four test lanes and Linux/macOS packaging pass;
Windows tag-only packaging and release attachment steps skip. No tag, release or publication. Linux artifact
11241366611, archive digest `sha256:8a5af9b579f888f345b17cf25109ea741791c4e7e779d2ea3a4766a0dd744edb`.
Downloaded bytes hashed on host; fresh 24.04 native execution passes below. Fresh 26.04 repeat still pending.

| Rebuilt artifact | Bytes | SHA-256 |
|---|---:|---|
| filecat_0.1.0~dev.539_amd64.deb | 56,035,608 | `993f522958497da3c3a8c4da463bfc90e4bcf9044ff85a781e5775f832481b67` |
| FileCat-0.1.0-dev.539-linux-x64.tar.gz | 68,611,061 | `7e89a5f80157360b0073d47c9e6d42e2b0b5ef8014b35b1bf713235e0f467f6b` |
| FileCat-0.1.0-dev.539-x86_64.AppImage | 63,810,040 | `33af2f5579b426302ccf323357be2287a9415634825c0a0dbb3a73361ad6d99c` |

## Ubuntu 24.04 completed cases

- Fresh full Desktop 24.04.5 LTS, kernel 7.0.0-38-generic; GNOME Shell 46.0, actual Wayland login / XWayland 23.2.6.
  VM/disk identities match E-ENV-07. No dotnet command/SDK during initial dev.522 install/GUI/lifecycle/AppImage cases.
  A private SDK was added later for native suites; package launches still use the system PATH with no dotnet command.
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
- Debian remove → reinstall → purge → reinstall succeeds. Package executable, symlink and desktop entry disappear
  on removal; settings/workspace/history hashes remain identical at every phase. A GUID-bound unrelated file placed
  in /opt/filecat survives both removal and purge, independently checked by SHA-256. No autoremove or user-data cleanup.
- Normal AppImage --version and GUI launch succeed with APPIMAGE_EXTRACT_AND_RUN unset and no SDK on the developer
  PATH. /proc/7452/exe resolves to /tmp/.mount_FileCaEfCDhE/usr/lib/filecat/FileCat; mountinfo proves a read-only FUSE
  AppImage mount. Saved Unicode source/target panels reopen. Graceful window close terminates FileCat and unmounts it.
  The first observation command ended on a case-sensitive pgrep mismatch (FileCat versus filecat); native process,
  executable and mount evidence supplies the result, not that command's exit status.
- Rebuilt dev.526 tar's unchanged packaged helper hash equals E-I98. It passes desktop-file-validate and native gio
  launch from `rebuilt tar with & 100% žluťoučký`; /proc/10904/exe/cmdline identify that exact payload and source argument.
  Actual GUI shows the Unicode file. Normal close terminates it. A Categories hint is informational, not a validation failure.
- Debian update dev.522 → dev.526 succeeds; all three saved-state files and the unrelated install sentinel retain hashes.
- Rebuilt Debian GUI opens with deliberately corrupt settings and backup in a new --data root. Preserved corrupt bytes
  hash `2f3892553b3c4c1ae0c715f96b568f4a71a6ec4b74d77b154f9c9a8ea640d8a5` equals original; after normal close,
  settings.json parses as JSON. Default state remains separate.
- Rebuilt tar and normal AppImage likewise preserve corrupt settings bytes, open usable defaults, save valid JSON and
  restart with the source tab retained, in separate owned --data roots. Actual window PID/executable/argv recorded.
  Native service/full suite results and complete skip inventory are in [E-X02](E-X02-fresh-linux-native.md).

Guest root `/home/benny/FileCat-v19-83b12fc1f8604ea2a2405069f4d783ad` not reused. Host evidence
`artifacts/release-evidence/linux-os-matrix-20261002/24.04/`.

| Evidence | SHA-256 |
|---|---|
| deb-first-install.log | `dafdd8e30101967b5e3226ad396d18686ee937fd48fe3393cb391eebf416201c` |
| deb-desktop-launch-r2.png | `614bdb338cd1ae7011393c8ad0f4cafc90b10cc22de9e4b694ef4369e6b7df29` |
| Initial native-session.txt | `221ede9ba13d88031d6331f7afc27a22d64e8a2dda35abdb2b9a814de1742c78` |
| Clean snapshot listing | `9c3f695be94ec65bdc1e130dad1c92cd3dad5929464e0623c619cc1994d8f035` |
| Private installer archive | `3098bbef0dc764130a1b103b6002692f71f4e3ef6f3ea9daaa17747bc40b0c6d` |
| GUI copy byte oracle | `9f0fae800c3baee6355375dd8e84ec15899c5d410b21389aedda57e7c73e9541` |
| Debian lifecycle log | `c66ad2c0b3b368ce49ca346438f42b85b316a7d1327422367d2b866e33dc2bd9` |
| Normal AppImage CLI log | `590148e8d53bb7c9313ebe68e877b02b0ea49e878b302af6e7aaec6964181e31` |
| AppImage executable/mount evidence | `639b88af241bfe4deea948d3dc74ed8c601d65176c057e5fe67aa8f580d1ef71` |
| Normal AppImage GUI screenshot | `8d68b8c2ba8ad47beb39eaf67d6e42b693454a221e15153488cab4efb01be5f2` |
| Rebuilt tar native executable/entry record | `a844378df209a798b00821cb49e47b7ea34dc2daa9a4a2a97deb6afabe9ad6d2` |
| Rebuilt tar GUI screenshot | `c2e8a053d84c0878e16da2a6c5f4e48403bc7d8a9dfaed54ce104df74151d798` |

## Dev.539 on restored clean Ubuntu 24.04

Restored `filecat-clean-ubuntu2404-20261002` only after E-X02's final 26.04 archive verification. Trusted retained SSH
key matches; UUID/disk identity, Ubuntu 24.04.5, GNOME 46 Wayland and absence of FileCat/SDK verified before setup.
Preflight `f914d5f40cdc79309a7a3b1f25b1165e7013b030e2c9605856ff440440d8e27b`. Idle/locking changes reapply the
owner's standing disposable-VM permission. Exact three package hashes above match on guest. No SDK installed.

- Debian install resolves ICU74 (74.2-1ubuntu3.1), CLI reports dev.539; actual GUI maps prove ICU74 loaded.
- All three formats pass real GUI launch in a 203-byte UTF-8 TMPDIR, corrupt settings/backup preservation,
  forwarding from a different session and Unicode TMPDIR, saved forwarded location, graceful close and restart.
  No second window appears; client exits zero, owner retains its PID. Closed socket absent and persistent profile
  lock independently acquirable; stale endpoint metadata is retained by design. AppImage runs via a read-only FUSE
  mount with extraction environment unset; its exact mount disappears on close. Per-format explicit state roots remain separate.
- Installed Debian desktop entry opens the Unicode fixture. Actual F5/Return copy into the observed owned target
  passes independent cmp: 45 bytes, source/target SHA-256 `6e9791c8779a87b1b44772d102249350b721ae1e0bbdcd9177394c84560e1b4b`.
  Before/dialog/after Linux console screenshots retained; executable/argv/maps and normal close recorded.
- Packaged tar helper SHA-256 `cdfa97bb3846c6a4305ea4deca32248779af8a2d79bd922a253f68120abbafc9` equals E-I98.
  Actual generated desktop entry launches the packaged executable from the ampersand/percent/Unicode directory.
  Independent GLib argv/icon oracle passes 12/12 unusual paths. Desktop validator emits its existing multiple-main-category hint.
- Debian remove → reinstall → purge → reinstall passes. Executable, symlink and desktop entry removed; settings,
  workspace/history and GUID-bound unrelated install-directory sentinel retain identical bytes throughout.

Private raw root `artifacts/release-evidence/linux-os-matrix-20261002/dev539-24/`. Package result JSON
`3b593ac17a3419980a551b73a5007213680f14a88e65bd10fbe633476bcc1229`; GLib result
`8a72eceb261c09a5dc1330e58f4fa3cf2be2c50424667e490889d5a69dc692e2`; lifecycle log
`80f4fe18c85bd1da315fad42d836af8d231554e8b78dc8dec4400ff73d8e6860`; desktop transition log
`70bb044fc730d3743d6b2591f560b8299f772541edc1fb56d4c251ed61af1cc8`. Harness source
`d818a8798f8b8b477d2b1d6fc0d1951f2b53f5a6179d5e6d68e260116a4ac9ba` retained.
Full raw archive `6d8c1f2c3aa65658b2bafaca45b4b86bc685b435904e5ab7b8c79ae6cf348775`, 324,638,274 bytes, 824 members;
guest/host hashes agree. Independent stream verifier checks all three artifact inputs, source/target bytes, 3/3 package
results, 12/12 launcher cases and retained workspace before restoring the other clean baseline.

Setup failures retained: root `--help` attempt (unsupported switch starts GUI) lacked a display and aborted after the
successful package install/version output. First oracle used wrong lock directory and expected endpoint metadata
deletion; corrected oracle checks local/instance.lock and actual socket removal, with original failure/input retained.
An activation command quoted awk incorrectly after desktop launch; corrected PID-bound observation supplies its result.
No product modification for these setup/oracle failures.

## Ubuntu 26.04 completed cases

- Fresh full Desktop 26.04.1 LTS, kernel 7.0.0-38-generic, GNOME 50.1, actual Wayland/XWayland session and exact
  VM/disk identities verified. Clean powered-off baseline snapshot retained before FileCat/test dependencies.
  All package cases below ran with no `dotnet` command/SDK in the environment. Private SDK added only afterward.
- Unmodified dev.526 Debian install exits 100 because none of its ICU alternatives is available (E-I04).
  Rebuilt dev.531 resolves dependencies, starts via CLI/installed desktop entry and actually loads ICU78.
- Dev.526 tar desktop entry launches the exact packaged executable from `tar with & 100% žluťoučký`; real GUI
  displays the fixture. Native F5 copy to the observed owned target succeeds: 44 bytes, independent cmp and matching
  SHA-256 `1e815bf502330f78ba1ed8ba334748d2c5a0f1e4d56397ec6f2bd1cfdf95986b`.
- Normal dev.526 AppImage CLI and GUI run with extraction environment unset; native executable/mountinfo proves
  read-only FUSE mount. Source/target state reopens; normal close terminates it and unmounts the exact mount.
- Dev.531 Debian remove → reinstall → purge → reinstall passes: executable, symlink and desktop entry removed;
  user settings/workspace/history and GUID-bound unrelated install-directory file survive every phase unchanged.
- All three dev.531 formats open with intentionally corrupt settings/backup in separate owned --data roots,
  preserve original bytes (`8c0f9e2afc98ee8480abd616f4c98178ff0441937c65201f332b1fb2d7cc5214`), save valid JSON after
  normal window close and retain the source tab across restart. PID/executable/argv and actual window recorded;
  AppImage's real FUSE mount separately identified. Default state remains separate.
- Rebuilt dev.531 packaged desktop helper SHA equals E-I98. Independent native GLib argv/icon oracle passes 12/12
  unusual paths, including quotes, percent fields, newline and shell metacharacters. Result JSON SHA-256
  `8f35dabfa89573b14f31b30abd65ba4948a648ec8accd5cc8b278c0a17eb25f4`.

Guest root `/home/benny/FileCat-v19-26-e7fac2cb613344ebb26a0a2b75777a25`; host raw directory
`artifacts/release-evidence/linux-os-matrix-20261002/26.04/`. Lifecycle logs and package inputs retained separately
from 24.04. Package-state host log SHA-256 `009826b30cd9a273a5b58db562a63b41cdfd979efdc994290fe521a6ff73a275`;
GUI copy oracle `cfc3edcfb8e20ca3210d8dfd4cce24756e2eb5f37c3145f6df697232d312b28c`.

Setup failures retained: initial GUI launcher kept its SSH output stream open, causing the wrapper timeout even after
native executable/maps output completed; later launches detach all standard handles. A Debian observation used the
wrong process-name case (`FileCat` versus `/usr/bin/filecat`); corrected exact executable observation supplies its result.
GNOME 50 XWayland uses an input-emulation portal. The observed temporary interaction session was approved through the
authenticated Linux VM console under owner-authorized disposable VM testing. Queued modifier state was cleared through
that console before copying. Initial screenshots show no copy, and do not count as success. No new network desktop
service was enabled; VMware console remains authenticated and bound to 127.0.0.1.

## Setup and remaining cases

Installer completed/powered off as configured; proposed restart never performed. Temporary VM console authenticated
and bound only to host 127.0.0.1:5901. Windows VM gracefully shut down after completed cases at owner request. Ubuntu will
be shut down when no longer needed.

GNOME locked after five minutes. Automatic approval review initially rejected idle/lock changes and unlock for lack of
specific security authorization. Owner then explicitly approved them for this disposable Ubuntu VM. idle-delay=0,
lock-enabled=false and unlock applied; LockedHint subsequently no. Recorded test setup, not a FileCat security claim.
Windows computer-use helper could not initialize; no Windows UI actions used.

26.04 native suites are retained with explicit skips (E-X02). Dev.539 fresh 24.04 compatibility passes above;
fresh 26.04 repeat and final-candidate full lifecycle/transition checks remain pending.
No human reader/input attestation, polkit safety evidence, support decision,
final signature or stable GO supplied by these automated preliminary checks.
