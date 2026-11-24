# I662 — Linux File-system details no longer show a bind mount's source root (a regression of I612)

**Found:** 2026-11-24 by the Ubuntu GUI harness on rc.2's tarball ([`v1.0.0-rc.2`](E-RC2-TAG-20261124.md), `7269ec61`), row a4, and re-checked in three fresh FileCats (17:26, 18:50 and 20:06). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

**Steps:** open `/run/snapd`, select `ns` (findmnt gives its FSROOT as `/snapd/ns`), then File-system details (Ctrl+Alt+Enter), Copy all, and Read full record.

**Expected:** [I612](E-I612-linux-bind-mount-source-root.md)'s check: "Source root /snapd/ns" in the Item section and "Root within source /snapd/ns" in a Volume section, matching findmnt.

**Actual:** the sections are Item, Storage, Sharing, Access, Extra data and Location, and the native statx metadata. There is no Volume section, and no Mounted at, Source root or Root within source line: only "File system tmpfs".

**Cause.** Since `4364d1b37` (2026-11-17, "Add filesystem details and compression execution checkpoint"), `MainViewModel.FileRecord.cs` builds the report with `FileDetailsReader`. Its Linux reader takes only the file-system type from mountinfo. The I612 code in `UnixFileRecords.Read` (lines 82 and 260) has no caller left. Those files are the same at `7269ec61` and in today's tree.

## Why it matters

I612's remediation is undone in rc.2 and on main: a bind mount's source root is again absent from the report that is meant to show it. That is I612's own fault, advanced origin information missing from a read-only report, back again.

## Remedy (proposed)

Carry mountinfo's root field into the Linux reader of `FileDetailsReader`: "Source root" at the selected mount root, and "Root within source" in a Volume section, as I612's code did. Check it against findmnt for a bind mount and for `/`. Then remove `UnixFileRecords.Read`'s unused copy of that code, or route through it.

## Finite check

I612's check, on the next candidate: an owned bind mount (or `/run/snapd/ns` on Ubuntu) shows Source root and Root within source equal to findmnt's FSROOT, and a plain mount of `/` shows `/`.

## Fix

Branch `fix/i662-i664-ubuntu-harness-findings`, from `rc3/integration-3`.

**What changed** (`src/FileCat.Core/Records/`)
- `UnixFileRecords.LinuxDetails`, the Linux reader behind `FileDetailsReader`, keeps the mountinfo line it already matches by statx's mount ID and device (`LinuxMount`: device, root, mount point, type, source and options), its paths decoded from mountinfo's octal escapes.
- **Source root** in the Item section, in the summary and in the full record (`FileDetailsSnapshot.SourceRoot`): for the root of a mount whose root is not `/`. The item is a mount's root when statx's `STATX_ATTR_MOUNT_ROOT` says so (Linux 5.8 and later, so a path through a symbolic link counts too), else when its path is the mount point, as I612 decided it.
- A **Volume** section in the full record, for every item on a known mount: Mounted at, File system, Source, **Root within source**, Options and Observation. A mount of a whole file system shows `/`, so the check for `/` reads a value rather than an absence.
- The legacy `UnixFileRecords.Read`, which has no product caller, routes through the same parser, rule and Volume fields: I612's code exists once.
- Mount paths are shown with control, format and line-separator characters written as `\u` and their code (`RecordText.Visible`), so a path with a newline cannot break the report into lines of its own. Ordinary paths read as findmnt shows them.

**Tests**
- `LinuxMountDetailsTests` (Core, every OS, 18 tests). Fixture mountinfo lines: the VM's snapd line (`519 31 0:27 /snapd/ns /run/snapd/ns …`), `/` with an optional field, a namespace file's root (`mnt:[4026532859]`), escaped names (`\040`, `\011`, `\012`, `\134`) and a bidirectional override, malformed lines, backslashes that start no escape, and the mount-root rule. The report's text is read as the harness reads it.
- `UnixFileDetailsNativeTests.Linux_details_show_a_mounts_root_within_its_source_as_findmnt_does` (Core, Linux). The real mount table against `findmnt -T`: `/run/snapd/ns`, a file in it, a snapd namespace file mounted there, `/` and a temporary folder, in the summary, the full record and the legacy record.

**Results.** The VM is the Ubuntu 24.04.5 VM (kernel 7.0); runs kept in `~/FileCatValidation/aw-claude/i662-i664/` there and `%LOCALAPPDATA%\Temp\fc-i662-i664\` on the Windows workstation.
- **Before**, on the VM: the base `2e5bb419` with the native test added fails. `/run/snapd/ns` has no Source root: expected `["/snapd/ns"]`, actual `[]` (`core-native-before.trx`, SHA-256 `eb2b038faea104cc25709ff45636ba75dcc00a9c0628897d248933db8006bd4d`). The fixture tests call the new parser, so they cannot run on the base; the mutations show what they catch.
- **After**, on the VM, from the branch's final source tree: the native test passes.
  - `/run/snapd/ns`: Source root and Root within source `/snapd/ns`.
  - `snap.firmware-updater.fstab` in it: Root within source `/snapd/ns`, no Source root.
  - `firmware-updater.mnt`, a namespace file mounted there: both `mnt:[4026532582]`, findmnt's FSROOT.
  - `/` and a folder in `/tmp`: Root within source `/`, no Source root.
- **Mutations**, each failing new tests (Windows, the fixture tests):
  - no Source root in the Item section: 3 tests;
  - a Source root for `/` too: the `/` test;
  - escapes not decoded: 3 tests;
  - statx's mount-root attribute ignored: the rule's test;
  - control characters not written out: the escaped-path test, at its Source root line, which the tab and the newline broke;
  - no Volume section: 5 tests.
- **Regression**: 15 Core classes of details and records (`LinuxMountDetailsTests`, `UnixFileDetailsNativeTests`, `UnixFileRecordTests`, `FileDetailsTests`, `FileDetailsFieldsTests` and others).
  - On the VM, the final tree: 259 tests, 0 failed, 37 skipped for fixtures not prepared there, macOS or Windows (`core-final.trx`, `8b72ce6698f773679a238195a3f8451ff344a6cf4d96908c08b117c7b73a0886`).
  - On Windows: 259 tests, 0 failed, 50 skipped (`core-details-windows.trx`, `b1875aeffacf24e7c003e1a50aef30238d75dcc3beb8d2258b3ba5e508acf117`).
- **The Ubuntu GUI harness**, row a4, with a linux-x64 self-contained build of the branch's final tree, packed as the release tarball is (`FileCat-1.0.0-linux-x64.tar.gz`, SHA-256 `19ad8de287fc1bb3c19995a2d084e5d2dc15e1412dd166396745c4b3e458ce6b`; results in `harness-final/` on the VM):
  - PARTIAL: ok 3, FAIL 0, BLOCKED 1 (`a4.txt`, `4ffbecbdeaf156aa1f4a0c93d5a2b940bbe394c42bdcf95b6518078711ae0179`). rc.2 failed it.
  - `ns` in `/run/snapd`: "Source root  /snapd/ns" and "Root within source  /snapd/ns", matching findmnt's FSROOT.
  - A folder on the `/` mount: no Source root line, and "Root within source  /".
  - The owned bind mount is BLOCKED: `sudo -n` asks for a password on the VM.
  - An earlier build of the branch, before I663's last change, gave the same result.

**Not verified here.** An owned bind mount, which needs root (the VM's `sudo` asks for a password); a Btrfs subvolume; a kernel before 5.8, whose mount-point rule only the fixtures check; an installed candidate.
