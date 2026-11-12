# Package route tests

12 November 2026, for the [route decision](RELEASE_ROUTE_DECISION.md) (ledger row 12; plan §16.1). Each package is built
from a clean checkout of one exact commit by the repository's own packaging script. It is then installed or unpacked as a
user would do it, and the real-worker lanes run against that package's own executable: the creation and Add dialogs for
every native profile, and the ZIP lane (create, F5, F7, F2, F8, F4 with commit, and a ZIP the strict catalog refuses).
With `FILECAT_ARCHIVE_PACKAGE_ROUTE=1` the tests grant the worker nothing themselves. No route is listed by this; these are
dry runs of the procedure the release candidate repeats after the independent review.

## What the packaging survey changed first

- **Windows, installed copy.** Under Program Files, the installer's folder inherits read access for restricted
  application packages, which the worker's LPAC AppContainer needs. Nothing else is needed.
- **Windows, portable copy.** A user's own folder grants AppContainers nothing, and only tests had granted the worker
  access. Now, at the first worker start, a portable copy (with its marker) in a folder the current user owns grants
  FileCat's worker identity read access to that folder, and logs it (`68c287bf`). Any other folder the worker cannot read
  makes writing unavailable, with that reason.
- **Windows, framework-dependent build.** It has no runtime of its own for a confined worker, so writing is unavailable
  and the reason says so (`7bc4944a`).
- **Linux AppImage.** The AppImage mounts its image on squashfs over FUSE, which is not a qualified place for the confined
  worker (the bundle must be on ext4). For this release, writing from the AppImage is unavailable. The reason says to
  install the .deb or to extract the AppImage into an ext4 folder (`e843439e`). On Linux and macOS, availability now
  reports such bundle problems up front, as on Windows, instead of failing at the worker start.

## macOS app (passed)

Commit `a06260ab`, `eng/package-macos.sh` on the Mac (macOS 27.0.1, M1), from a clean clone of that commit.

| Fact | Result |
|---|---|
| Package | `FileCat-1.0.0-dev-osx-arm64.zip` `74f44e20…daf9`; SBOM `0f1310fb…a425` |
| Bundle signature | `codesign --verify --deep --strict` passes (ad hoc) |
| Launcher after packaging | Identifier `io.github.benny-cz.filecat.archive-worker`, embedded Info.plist (5 entries), App Sandbox entitlement present; `FileCatArchiveWorker` `c34f4243…2317`, the same bytes as the published build |
| Main executable | No App Sandbox entitlement |
| Route lane (`pkgroute-mac`), app unpacked with `ditto` into a user folder | 12 of 12: 10 creation and Add rows, the ZIP lane and the refused ZIP |

## Linux (.deb and AppImage)

**First run, `a06260ab`: failed, and found a packaging defect.** `eng/package-linux.sh` built the tarball, the .deb and the
AppImage (with the VM's existing appimagetool, whose checksum matches the pin; nothing was downloaded). The .deb installed
cleanly into `/opt/filecat` (root-owned, ext4). Every route test then failed: all formats were unavailable from the
installed copy. The payload kept the build user's umask-002 modes, so 192 files and 106 folders were group-writable, and
the confined worker rightly refuses a bundle anyone but its owner can change. Test publishes had passed only because the
test runner removes group and other write. Both packaging scripts now normalize modes (755 folders and executables, 644
files), and the Linux script refuses a payload that is still writable by others (`092f5d9a`). The AppImage check failed
too, but only on wording: a storage check refused the FUSE mount before the AppImage's own reason could be given. The
AppImage reason now comes first.

**Rerun, `092f5d9a`:** pending.

## Windows (installed layout and portable)

Pending. The installed layout is the setup payload copied into Program Files as the installer copies it. Compiling the
real installer needs Inno Setup, which is not on this machine (its pinned download needs the owner's approval).
