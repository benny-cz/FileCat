# FileCat 1.0.0

FileCat 1.0.0 is the first stable release of FileCat, a keyboard-first, multi-panel file manager in the tradition of Altap Salamander, Total Commander and FAR Manager. It works with local disks, removable storage, archives and remote servers. Copy and move show their destination before they start and their result afterward; viewers, inspectors, checksums, signatures and deleted-file recovery are there when the work goes deeper.

**Windows 11 x64 is the stable tier.** Windows 11 ARM64, Ubuntu 24.04/26.04 x64 with GNOME, and macOS 26 or later on Apple silicon ship as **previews**. Their packages come from the same source and also call themselves 1.0.0; the release-blocking tier is Windows 11 x64 alone, and the [preview limits](#known-limitations) below apply to the others.

These notes compare 1.0.0 with the last published preview, [1.0.0-preview.11](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.11) (source `7eb4766f`). Tag `v1.0.0` points at `6d814134fa1d537c1e255f592da1ee2a3a332497`, the source of release candidate `v1.0.0-rc.6`. This release publishes that candidate's packages unchanged.

## Downloads

| Your system | Tier | Packages |
|---|---|---|
| Windows 11 x64 (build 22000 or later; versions Microsoft still services) | **Stable** | `FileCat-1.0.0-win-x64-setup.exe` (installer, recommended) · `FileCat-1.0.0-win-x64-portable.zip` · `FileCat-1.0.0-win-x64-fdd.zip` |
| Windows 11 ARM64 | Preview | `FileCat-1.0.0-win-arm64-setup.exe` · `FileCat-1.0.0-win-arm64-portable.zip` · `FileCat-1.0.0-win-arm64-fdd.zip` |
| Ubuntu 24.04 or 26.04 x64 with GNOME (X11, or XWayland under Wayland) | Preview | `filecat_1.0.0_amd64.deb` · `FileCat-1.0.0-x86_64.AppImage` · `FileCat-1.0.0-linux-x64.tar.gz` |
| macOS 26 or later on Apple silicon | Preview | `FileCat-1.0.0-osx-arm64.zip` (ad-hoc signed, not notarized) |

- **Windows installer:** installs for all users, under Program Files by default, and needs administrator approval. Only the installed build has the administrator helper. It retries one displayed plan after an access-denied failure and, on x64, serves **Inspect with authorization…** in the permissions inspector.
- **Windows versions:** the x64 installer also accepts Windows Server 2016 (build 14393) with Desktop Experience, which it does not check. It refuses Windows 10 and Windows Server 2019 and 2022; Windows Server 2025 passes its build check but has not been tried. The ARM64 installer needs Windows 11. The x64 package also runs on Windows 11 ARM64, emulated, and About says so.
- **Windows portable ZIP:** self-contained; keeps settings, history and other state in `Data\` beside the app. No administrator helper.
- **Windows framework-dependent ZIP:** needs the matching .NET 10 runtime and gets .NET fixes from that installed runtime. No administrator helper, and it cannot run the confined archive-writing worker.
- **Ubuntu:** the `.deb` installs to `/opt/filecat` with a `filecat` command and a menu entry. The tarball's `install-desktop-entry.sh` adds a menu entry for your account. The AppImage runs without installing. Other distributions and desktops are not claimed.

The release also holds one manifest per platform (`FileCat-1.0.0-windows-assets.json`, `FileCat-1.0.0-linux-assets.json`, `FileCat-1.0.0-macos-assets.json`), an SPDX SBOM for each package payload (`sbom-1.0.0-*.spdx.json`), and `FileCat-1.0.0-linux-x64-appimage-runtime-source.tar.gz`: the AppImage runtime's corresponding source, with a script that rebuilds the runtime and relinks it against a changed libfuse. That makes 24 files.

## Verifying downloads

- **Hashes.** Each platform manifest lists the name, size in bytes and SHA-256 of every other file for its platform, the source commit, and the candidate tag that built those bytes. GitHub shows the same SHA-256 beside each asset. Compute yours with `Get-FileHash -Algorithm SHA256 FILE` (PowerShell), `sha256sum FILE` (Linux) or `shasum -a 256 FILE` (macOS).
- **Manifest SHA-256:** windows `ad724b812f848d703d56d7aa9a5c9c426de2e4b27a24ffb9053e774e4a43489f`, linux `2f85788c646851b54d8d9fa725b95b60771ed61df352c607ce77cd7122b63d7f`, macos `36950022caef78722731a225a5445cf59f20845299d41e6bc50ceeebebce7736`. The manifests were written when the packages were built: their `CandidateRef` names `refs/tags/v1.0.0-rc.6`, and their `CandidateQualified` and `SigningVerified` fields always read `false`, because a manifest pins bytes and records no decision.
- **GitHub's attestation.** The release is immutable: its assets cannot be replaced after publication, and GitHub signs a release attestation over their digests. With the GitHub CLI, run `gh release verify v1.0.0 --repo benny-cz/FileCat`, and for a downloaded file `gh release verify-asset v1.0.0 FILE --repo benny-cz/FileCat`.
- **Windows signatures (new in 1.0.0; the previews were unsigned).** Both Setups, their uninstallers, and every program and library in the Windows packages carry an Authenticode signature. Files their vendors signed, such as Microsoft's, keep the vendor's signature. Every other one, FileCat's own included, is signed by the project's maintainer with an individual-validated SSL.com certificate and an RFC 3161 timestamp. The certificate's subject is the maintainer's name and city, and Windows' prompts show it as the publisher. Check a file with `Get-AuthenticodeSignature FILE` (status `Valid`) or in Properties → Digital Signatures. A ZIP file itself carries no signature; the files inside it do.
- **macOS.** `codesign --verify --deep --strict FileCat.app` checks that the bundle is intact. An ad-hoc signature names no publisher, so the hash and the attestation are what tie the app to this release.
- **Ubuntu.** The `.deb`, AppImage and tarball carry no signature of their own; check them by hash and attestation. Inside the runtime-source bundle, `sha256sum -c --strict SHA256SUMS` checks its sources.
- **SBOMs** list each payload's files, reviewed dependencies, runtime pack and source commit. They describe composition; they are not signatures.
- **Candidate record:** tagged run `45755406125` passed 19 test jobs (73,817 tests passed, 13,698 skipped, 0 failed). The stable tier's installed check ran on a physical PC with a released Windows 11 `[[edition, version and build; Smart App Control state]]`.

## Installing, upgrading from a preview, and removing

**Installing**
- **Windows installer:** run it and approve Windows' prompt. A desktop shortcut is optional.
- **Windows ZIPs:** extract and run; nothing is installed. To write archives, run a portable copy from a folder you own: on first use it gives the archive worker read access to its program files, never to `Data\`. For deleted-file recovery, keep FileCat and its state on another disk, or start it with `--data` pointing to another disk. If you also open administrator windows from a ZIP copy, read [Known limitations](#known-limitations) first; an installed FileCat in its default Program Files folder avoids that risk.
- **Ubuntu:** `sudo apt install ./filecat_1.0.0_amd64.deb`. The `.deb` recommends WebKitGTK (`libwebkit2gtk-4.1-0`) for the page view, `libsecret-1-0` for saved passwords, and `xdg-utils`. Where Ubuntu restricts unprivileged user namespaces, as 24.04 and later do, it also installs an AppArmor profile (`/etc/apparmor.d/filecat`) so that the page view's WebKit sandbox can run. To change archives with the AppImage, extract it onto an ext4 drive, or use the `.deb`.
- **macOS:** extract with `ditto -x -k FileCat-1.0.0-osx-arm64.zip DESTINATION`; a plain `unzip` lost the bundle's signing metadata in a preview check. macOS blocks the first start: choose **Open Anyway** in System Settings → Privacy & Security, then confirm.

**Upgrading from a preview**
- **Installer:** run the 1.0.0 Setup over the preview. It closes FileCat first if it is running. A window that cannot close at once, such as one still copying, is ended, and its interrupted operations come back at the next start. Setup replaces the program files and keeps your settings, workspaces and journals. Installed apps already listed a preview install as version 1.0.0; Help → About shows which one you have.
- **Portable ZIP:** replace everything except `Data\`. **Framework-dependent ZIP:** replace the folder. **`.deb`:** install the new package; a preview's package version, `1.0.0~preview.N`, sorts before `1.0.0`. **AppImage, tarball, macOS app:** replace them.
- A preview's update check, automatic or Help → Check for updates, finds 1.0.0 newer and offers it. From 1.0.0 on, the check asks GitHub only for the latest stable release.
- Before going back to an older build, finish or review open edits (File → Edit sessions) and interrupted operations. An older build opens state written by a newer one read-only and says so.

**Removing**
- **Windows installer:** Settings → Apps → Installed apps → FileCat → Uninstall. It first closes FileCat if it runs from that folder. If FileCat asks about running operations or unsaved changes, answer it there and choose **Check again**, or choose **End FileCat**. It then removes the files it installed, its shortcuts and its uninstall entry, and last the program folder once it is empty, so other files in a folder you chose stay. It keeps, on purpose:
  - your settings, history and journals in `%APPDATA%\FileCat` and `%LOCALAPPDATA%\FileCat`;
  - `HKLM\SOFTWARE\FileCat.AdministratorTransfers`, the administrator window's remembered settings, if you saved any;
  - `HKLM\SOFTWARE\FileCat\PrivilegedHost\UsedPlans`, the helper's record of plans it has run. A plan runs at most once; entries expire after two days and the record prunes itself. Removing it at uninstall would let a just-used plan run again after a reinstall;
  - the per-user AppContainer profile `FileCat.ArchiveWriter.v1`, made for the archive worker the first time it ran for you. An administrator's uninstall cannot reach every user's profile.

  Delete them yourself if you want them gone.
- **Windows ZIPs:** delete the folder. A portable copy keeps its state in `Data\` inside it; a framework-dependent copy keeps it in `%APPDATA%\FileCat` and `%LOCALAPPDATA%\FileCat`, as the installer does. The AppContainer profile can stay, as above.
- **Ubuntu:** `sudo apt remove filecat` removes the program and its AppArmor profile. For the AppImage or the tarball, delete the files; if you ran the tarball's `install-desktop-entry.sh`, also delete `~/.local/share/applications/filecat.desktop`. Your settings stay in `~/.config/FileCat` and `~/.local/share/FileCat`.
- **macOS:** move FileCat.app to the Trash. Your settings stay in your account.

## What's new since preview.11

**Packages**
- The Windows packages are signed: both Setups, their uninstallers, and the programs and libraries inside every Windows package (see [Verifying downloads](#verifying-downloads)). The macOS app stays ad-hoc signed, and the Linux packages stay unsigned.

**New tools**
- **Disk usage** (Tools → Disk usage…): scan the locations you choose and see what occupies them by folder and file, as a treemap, a sunburst or a ranked table. Save analyses and keep a history, compare a saved analysis with a later scan, export reports (Markdown, HTML, CSV, or a chart as PNG or SVG) or the whole inventory, and review copying, moving or deleting what you marked. In the installed Windows build, a Fast NTFS scan reads one whole local NTFS volume through the administrator helper after your approval.
- **Permissions and ownership** (File → Inspect): the item's owner, mode and access rules in plain language beside their exact values, with the changes each section allows, on Windows, Linux, macOS and SFTP or FTP servers. An administrator window, or FileCat running as root, inspects read-only. The installed Windows x64 build adds **Inspect with authorization…** for one file or a volume root, after Windows' approval and FileCat's own consent. On the tested Ubuntu OpenSSH 9.6p1 server, **Review SFTP permissions** changes a mode or a numeric group. The [capability matrix](https://github.com/benny-cz/FileCat/blob/v1.0.0/docs/CAPABILITIES.md) lists what each platform and location allows.
- **Owner column** on Windows x64 (Settings → Columns), beside Linux's Owner and Group.
- **File-system details** (File → Inspect → File-system details…, Ctrl+Alt+Enter), formerly File-system record: a summary first, then **Read full record** for the metadata, with supplementary values in their own tab. **Read file-system record** in the same window reads the item's full file-system record only when asked: its IDs, every time with timestamp checks, journal entries, links, object ID, reparse and cloud data, clusters, streams and attributes with what they say, and security. An administrator window (Windows) or FileCat running as root (Linux, macOS) adds the raw on-disk records. The record is copied or saved only while it describes one object, and on Windows only when it holds no values decrypted from EFS (I671). New optional columns: Allocated, Hard links and, on Windows, Reparse type. Column header tooltips explain what their column shows. On Linux, Created shows the birth time where the file system keeps one; on Windows, Link target also shows app execution aliases and WSL symbolic links.
- **NTFS compression and EFS** (Windows; File → Inspect): reviewed jobs that compress or decompress, or encrypt or decrypt with EFS, with exact per-item counts, on local fixed NTFS drives in a normal (not administrator) window; **Back up EFS key…** for selected encrypted files. EFS needs Windows 11 Pro, Enterprise or Education. Both run natively in the ARM64 build.
- **Administrator windows** (Windows): **Tools → Open FileCat as administrator…**, and administrator reviews offered by a denied folder, Create link, the viewer, checksums, Find, the Registry, portable recovery, Bulk Rename and Operations. Every package can open one; the per-plan helper stays in the installer. In Settings, **Copy appearance and workspace between sessions…** carries your look and workspace into an administrator window.

**Remote**
- Small files copied to and from SFTP servers take three round trips each instead of 10 to 13: 100 ms away, 1,000 small files go up in about 5 minutes instead of about 23. FTPS downloads skip one request per file (I42).
- After an FTPS data connection is refused, its final reply is read before the next command, so later commands no longer read the wrong reply (I631).

**Security and privacy fixes**
- An administrator window could load a .NET profiler that the ordinary account named in its environment. Every administrator window now starts through `FileCat.AdminLauncher.exe`, which removes the .NET runtime's environment settings first, and the framework-dependent build disables startup hooks too (I644; all Windows packages).
- An elevated FileCat swept the ordinary account's temporary folders and followed links there. The sweeps now run only in an ordinary FileCat and never follow a link (I645).
- Every FTPS data connection must now present the certificate the session accepted, or it is closed before any data crosses it.
- Linux: the root process census opened an executable path another account could replace (I646), and it took processes that `/proc` hides (`hidepid`) for absent (I658). A census that cannot see every process now answers unknown and asks for the root census, which needs an administrator's approval; inside a separate process namespace, such as a container, a deleted-file scan is refused.
- Linux: FileCat started as root with your home folder, as `sudo -E` does, read and wrote your saved state. On Linux and macOS, FileCat running as root now starts with default settings, saves nothing, keeps no log, and starts no program your profile chooses (I655).
- The diagnostics log now hashes the paths in error messages and crash reports too (I643).

**Fixes**
- Alt+Enter and Properties act on the marked items and ask you to select one when several are marked (I635). Analyze folder and column sorts keep Origin marks and alias or WSL link targets in order (I636).
- Compare gives the keyboard to its rows when it opens and when Find closes (I637). Choosing Compare after a panel was read again with its dialog open now compares what the panels show; before, it did nothing (I668). A finished comparison keeps its result, where a late progress report could put "Comparing… 100%" back (I673).
- Windows network shares: a share or server that needs a sign-in now says so ("… requires credentials … Press Enter to sign in"), and Enter opens Windows' sign-in prompt, then lists it; before, Enter went up a level, and a share opened directly showed only Windows' "The user name or password is incorrect." (I672). Going to a share whose server does not answer, or starting FileCat on one, no longer stops FileCat's window until the connection times out, about 20 s; the panel shows "Loading…" and can be left (I675).
- Windows: the folder's own menu (right-click empty space) offers the Shell's Paste, so a file cut in Explorer moves into the folder (I642). A Markdown page no longer stays blank when WebView2 never reports that it finished; Refresh works while a page loads, and after 10 s the status line says it has not finished (I648). A tool or Apply command whose program Windows cannot run, such as a 16-bit one, fails at once instead of waiting behind Windows' message box (I632).
- Show pictures on a file's streams and attributes no longer ends FileCat; those rows show as plain tiles (I649).
- macOS: typing a time in Change attributes and times no longer ends FileCat while an accessibility client is active (I650). Copies on APFS keep sparse files' holes (I652). An attribute value of a file now says why it cannot be read, where F3 did nothing (I651).
- Linux: File-system details show a bind mount's source root again (I662).
- Linux: one zombie process anywhere on the system, a program that has ended but whose parent never collected it, made deleted-file recovery refuse every mounted disk. The process check now passes over a process whose threads have all ended, as on macOS (I681).
- Git badges take Git's own comparison settings into account, such as Git for Windows' `core.autocrlf`, are right in a repository's subfolders, and keep names that are not ASCII (I647, I657, I659). A file that only a clean filter such as Git LFS's could call changed reads "Git state unknown".
- Screen readers hear readable names for keyboard reference rows, spin buttons, theme choices, Disk usage rows and Registry search results (I653), and every drawn picture tile (I664). VoiceOver no longer moves the keyboard on every layout change.
- The Places bar moves a place that no longer fits to its overflow menu (I661); panel numbers follow a theme change (I663); the interrupted-operation list acts on the row that has the keyboard (I633); a one-step consent page says "this 1 step" (I669); a PDF error no longer doubles its period (I654).

**Smaller changes**
- Right-click Back or Forward, or use its arrow, for the tab's history in that direction.
- Resizable dialogs reopen at the size you last gave them.
- Dialogs keep the active theme's colors, and turning on high contrast while FileCat runs takes over from any theme.
- A folder that keeps changing is read again at most every 2 s, quietly, and the status line says "changing often".
- The recovery list says why a folder that was not deleted appears in it.
- A picture that does not decode within its 30 s says whether its decoder was slow to start, was still being sent the picture, or did not answer (I677). In a report's recorded storage calls, **Next recorded call page** is off on the last page (I674).
- The x64 installer also installs on Windows Server 2016 (see Downloads).
- Setup and the uninstaller close a running FileCat before they replace or remove its files (I680).

## Known limitations

**Release scope**
- Performance is measured, not held to frozen numeric targets on a reference machine.
- No study with uncoached users or with experienced screen-reader users was made before 1.0.0. Accessibility rests on automated NVDA, Orca and VoiceOver runs and their recorded findings.

**Remote transfers (I42).** Jobs copy one file at a time. At a 100 ms round trip, each small file takes about 0.3 s over SFTP either way, about 0.6 s as an FTPS download and about 1 s as an FTPS upload; FTPS uploads did not get faster. These figures come from one OpenSSH 9.6p1 server and one FTPS test server with an added 100 ms delay.

**Windows 11 x64 and all Windows packages**
- An administrator window runs the program files of the copy that opened it. A portable or framework-dependent copy runs from its own folder, which your ordinary account, and any program running as you, can usually change. The same holds for an installed copy you put outside Program Files. If you open administrator windows from such a copy, keep it in a folder only administrators can change, such as one under Program Files, or use the installer's default folder (I640; accepted for 1.0.0).
- The consent that **Inspect with authorization…** shows appears on your normal desktop, where another program can draw over its text but cannot press it. Read the path and the change shown before you approve.
- Windows Server 2016 rests on one virtual-machine run of a pre-release build. Server Core and Server 2025 are untried, and About names Server 2016 "Windows 10 (build 14393, x64)".
- Under heavy load the page view can wait up to 90 s for WebView2 to start. The status line says so after 10 s; after 90 s the viewer says WebView2 did not start and shows the file's text (I648).

**Windows 11 ARM64 (preview)**
- Built, tested and started on hosted ARM64 runners in CI; not tested on physical ARM64 devices.
- The confined archive-writing worker is not available; ZIPs keep the older ZIP route where it is available. The Owner column and **Inspect with authorization…** are not offered. Shell extensions that exist only as x64 DLLs cannot load, as in Explorer on ARM64.

**Ubuntu (preview)**
- Only Ubuntu 24.04 and 26.04 x64 with GNOME is claimed. Not available: the Registry, phones and cameras (MTP), programs' own icons and Shell thumbnails, and administrator retry.
- The released packages do not carry the polkit mechanism behind **Inspect with authorization…**; only a separate qualification build of the `.deb` has it.
- Archive writing needs the `.deb` or a payload extracted onto ext4; a mounted AppImage cannot run the worker. The page view needs WebKitGTK.

**macOS (preview)**
- Ad-hoc signed and not notarized, so Gatekeeper asks for **Open Anyway** on the first start. Not available: the Registry, MTP, programs' own icons and Shell thumbnails, administrator retry, and the Owner and Group columns.
- Permissions are shown, not changed: released macOS builds make no mode, access-list, group or Finder-flag changes (Permissions and ownership says the macOS writer awaits release qualification). **Change attributes and times** still sets timestamps and Finder Hidden.
- Attribute values are read only for folders; for files, names and sizes are listed (I651). The archive worker's memory is sampled and the worker is stopped above its budget, which a fast allocation can exceed between samples; it is not a hard ceiling. Finder's Put Back does not know FileCat's deletions; FileCat's Undo restores them.

**Everywhere**
- Keep a separate backup. Recovery asks for read-only access, but a mounted file system can still change its own metadata during a scan; use a disk image or storage-level read-only protection when the source must stay byte-identical. Recovery is not guaranteed, and ext4 and APFS undelete are not supported.
- Changing an archive rebuilds it beside the original, needs free space for a second copy and has no undo. Limits: 100,000 members, 8 GiB per member, 64 GiB in all, an 80 GiB archive. An archive FileCat cannot write back exactly stays read-only and says why.
- The hex editor saves in place through a journal, but not atomically. Recycle Bin or Trash support and atomic replacement differ by location; FileCat reports a provider that cannot replace atomically.
- Pictures decode in a separate worker and the page view runs without scripts or network requests, but process separation does not make every parser a full sandbox.
- A checksum match shows integrity; only a trusted signature shows where a file came from.
- FileCat shows pictures and reads media metadata; it does not play audio or video. Its interface is in English.
- Disk usage needs an ordinary FileCat, not one running as administrator or root.

The [issue register](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/FILECAT_1_0_RELEASE_ISSUES.md) lists every known issue and its current status. `[[At GO: name any unresolved register row that users can meet beyond those above, or state that there is none.]]`

## Privacy and the update check

- FileCat needs no account and sends no usage analytics. Settings, workspaces, history, job records, caches and logs stay in your profile, or in a portable copy's `Data\` folder. FileCat connects to the network when you use remote or network locations, and for the update check.
- **The update check is on by default for new profiles.** Settings → Privacy → Automatically check for updates turns it off, and a saved off setting stays off. When FileCat starts and its last completed check is at least a day old, it asks GitHub's releases API for the latest release. The only FileCat information it sends is its version, in the User-Agent (`FileCat/1.0.0`). GitHub, and any proxy on the way, see your network address, the time and that version. An administrator window, or FileCat running as root, never checks automatically, because it does not use your saved settings and cannot know whether you turned the check off (I670).
- A check that fails offline or times out counts as done and stays silent. A check still running when you close FileCat does not, so it runs again at the next start.
- A newer release opens a dialog with **Not now**, **Skip this version** and **Open release page**. The check never downloads or installs anything. Help → Check for updates asks at any time, skipped versions included.
- **Tools → Export diagnostics** makes a local ZIP for you to check and share yourself. It holds no file contents or credentials. Unless diagnostic mode is on, the paths in its log and crash reports are replaced with hashes, but a message can still name a file, folder or server, such as a bare file name or a server's name. Its `settings.json` is plain text and names saved server hosts and user names, key-file, start and tool paths, and Find's ignored folders.
- Saved connection passwords go to the operating system's credential store where there is one, and otherwise last only for the session; they never go into the settings file. Page view loads only the page and the images, style sheets and fonts in its own folder, with scripts off.

See the [privacy policy](https://github.com/benny-cz/FileCat/blob/v1.0.0/docs/PRIVACY.md).

## Reporting problems and security issues

- **Security issues:** report them privately with **Report a vulnerability** on the repository's **Security** tab, not in a public issue. Say what an attacker controls, the impact, the FileCat version (Help → About) and package, your operating system and its version, and the smallest reproduction you can share. Expect an acknowledgement within 7 days and an assessment within 14; confirmed issues are fixed in a security release and disclosed once users can update. See the [security policy](https://github.com/benny-cz/FileCat/blob/v1.0.0/SECURITY.md).
- **Other problems and questions:** open a [GitHub issue](https://github.com/benny-cz/FileCat/issues) with the version, package and operating system. Check a diagnostics ZIP before you attach it, and leave out personal files.
- **Servicing:** FileCat has no updater; each fix ships as a new release. After a .NET security fix that affects FileCat, a rebuilt self-contained release follows within 14 days; the framework-dependent ZIP gets runtime fixes from the installed .NET runtime.
