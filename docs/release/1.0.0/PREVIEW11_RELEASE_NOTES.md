# FileCat 1.0.0-preview.11

An unsigned preview for Windows, Ubuntu and macOS. This is a prerelease for evaluation; stable 1.0.0 remains in validation.

Preview.10 was not published. These changes are compared with the last published version, preview.9.

## What changed since preview.9

- **Create and change more archive formats.** Pack (Alt+F5) can create ZIP, 7z, RAR5, TAR, TAR.GZ, TAR.BZ2 and TAR.ZST archives, and compress one file to GZ, BZ2 or ZST. On the qualified routes below, F5/F6 add files, F7 creates a folder, F2 renames, F8 deletes and F4 edits a member. FileCat rebuilds the whole archive beside the original, checks the result in a separate worker and then publishes it. Interrupted operations retain unfinished changes for review.
- **Keep archive metadata or explain why writing is unavailable.** The new ZIP route copies unchanged members without recompressing them and preserves their supported timestamps, Unix owners and comments. An archive containing metadata or features FileCat cannot reproduce stays read-only with a reason. Examples include unknown ZIP extra fields, self-extractors, unsupported name encodings, links, sparse files, and encryption outside the supported profile. A gzip file containing an original name or time can also be read-only.
- **Browse supported password-protected archives.** Password-backed 7z and RAR browsing retains its read session through nested navigation, viewers and queued extraction. Writing has stricter, format-specific requirements; being able to browse an archive does not mean it can be rewritten.
- **Review interrupted edits.** On Windows x64/NTFS, Linux x64/ext4 and macOS ARM64/APFS, FileCat keeps F4 working copies in private native edit storage. File → Edit sessions lists incomplete copies after an interruption and explains retained files or a refusal to discard a copy still in use. The same storage serves edits of server files. A copy FileCat cannot safely distinguish from your work is kept.
- **Create a reusable file list.** Commands → Create file list exports names, paths, sizes and timestamps to the viewer, clipboard or a UTF-8 text file. Save custom formats and choose selected items, the current folder or subfolders. See the [file-list guide](https://github.com/benny-cz/FileCat/blob/7eb4766f9398e095673b5cb1f92849dd07a4edde/docs/CREATE_FILE_LIST.md).
- **Use Windows folder menus and file icons.** Right-click empty folder space for its native Windows menu; Shift+right-click includes extended commands. Supported Shell New actions select the created item for inline rename. Association icons and readable theme-specific colors identify NTFS-compressed and EFS-encrypted names; the Attr column still shows C and E.
- **Keep column layouts steady.** Right-click a column header to open that panel's column settings. Applying unrelated settings preserves list position and dragged widths; filling columns retain their proportions across saves and restarts. Alt+0–9 keeps the special views' own columns. Menu navigation keys now reach the focused menu before panel shortcuts.
- **See pending decisions more clearly.** Review decision is highlighted when a job needs input. Select/Unselect by mask includes folders by default, and the file-list chooser closes on an outside click while leaving the dialog open.

## Archive writing: package and location limits

The new archive routes cover **Windows x64 on NTFS, Linux x64 on ext4 and macOS ARM64 on APFS**. They are evaluated for the actual target when a job starts. Nested archives and network locations remain read-only for these routes. The new archive-writing route has not been qualified for Windows ARM64 and stays disabled there. ZIP locations outside the new routes retain the legacy ZIP behavior where it is available.

| Package | New archive worker |
|---|---|
| Windows x64 installer | Restricted AppContainer worker; supported NTFS locations only. |
| Windows x64 self-contained portable ZIP | On first use, grants the worker read access to the portable program files in a folder you own. This does not grant access to its Data folder. |
| Windows framework-dependent ZIPs | Disabled: the confined worker is not granted access to the shared .NET runtime. Use the x64 installer or self-contained portable ZIP. |
| Ubuntu x64 `.deb` and portable payload on ext4 | Confined worker using cgroup, seccomp and Landlock, subject to the host's required facilities. The worker bundle must also be on ext4; the mounted AppImage uses SquashFS/FUSE. Install the `.deb` or extract the AppImage onto ext4. |
| macOS ARM64 application | App Sandbox helper. Memory is sampled and the worker is terminated above budget; a fast allocation can exceed that budget between samples. This is not a hard memory ceiling. |

Windows refuses the new route in cloud-synced and linked folders; updating an existing archive also requires NTFS transactions. Unix targets must satisfy the ownership, permissions and metadata checks. An existing archive with read-only attributes or additional hard links is refused. The new worker is disabled while FileCat runs as administrator or root. The app gives the location's specific reason. See the [capability matrix](https://github.com/benny-cz/FileCat/blob/7eb4766f9398e095673b5cb1f92849dd07a4edde/docs/CAPABILITIES.md) and [route decision](https://github.com/benny-cz/FileCat/blob/7eb4766f9398e095673b5cb1f92849dd07a4edde/docs/validation/archive-writing/RELEASE_ROUTE_DECISION.md) for details.

Changing an archive needs space for another complete copy and has no undo. The profile limits are 100,000 members, 8 GiB per member, 64 GiB logical content and an 80 GiB archive.

The archive implementation has platform, package, recovery and fuzz evidence at the commits recorded in its [implementation status](https://github.com/benny-cz/FileCat/blob/7eb4766f9398e095673b5cb1f92849dd07a4edde/docs/validation/archive-writing/IMPLEMENTATION_STATUS.md). Those records do not qualify this preview as an installed stable candidate. Trusted release signing, signed-candidate boundary checks and final installed acceptance remain open.

## Before working on important data

- Keep a separate backup. Recover to another physical disk. FileCat requests read-only recovery access, but a mounted operating system can still change its own filesystem metadata. Ext4 and APFS undelete are not supported.
- Remote and device locations differ in Trash/Recycle Bin support and replacement guarantees. Review the displayed delete mode and result.
- Treat unfamiliar files as untrusted. Page preview disables scripts and network requests; process separation does not completely contain every parser and helper. See the [safety guidance](https://github.com/benny-cz/FileCat/blob/7eb4766f9398e095673b5cb1f92849dd07a4edde/README.md#work-with-visible-outcomes).

## Upgrading and downgrading

Finish or review active edits and interrupted operations before downgrading. New edit sessions use a newer record schema and may live in `edit-sessions-native`; preview.9 and older versions retain their files but may not list them. This version lists both native and legacy stores. Existing ZIP edit sessions keep the route they were created with. Downgrading does not convert archives or interrupted-operation records automatically.

## Choose and verify a download

Windows 11 x64 and ARM64 have setup, self-contained portable and framework-dependent ZIP packages. Setup filenames omit `preview.11`; the app reports the preview version. Framework-dependent ZIPs need the matching .NET runtime. Windows ARM64 is tested on a hosted runner; physical ARM64 hardware is not a qualified stable tier.

Ubuntu 24.04/26.04 x64 with GNOME has `.deb`, AppImage and portable `.tar.gz` packages. Other distributions and desktops are outside the preview's claimed support. macOS 26+ on Apple silicon has an ad-hoc-signed, non-notarized app ZIP: extract with `ditto -x -k`, then use **Open Anyway** in System Settings → Privacy & Security when needed.

These Windows packages have **no trusted release signature**. Match each download's byte size and SHA-256 to its platform's `*-assets.json` manifest and the [exact hosted inventory](https://github.com/benny-cz/FileCat/blob/main/docs/release/1.0.0/evidence/E-PREVIEW11-TAG-20261116.md#exact-hosted-inventory). The release also includes SBOMs and the Linux AppImage runtime source bundle.

Platform manifest SHA-256 values:

- windows: `4ba448492049f4e426d2365b83609e50865771925d82437dd078eaaee4012105`
- linux: `d83c1b775a9535d6d1537f76b6fc302542d7d3b34f834c2aa47cc37d4dafe192`
- macos: `28505b0af25ab7f7a1972f3eb03a9c5f0868e8cdab38fdc5681ea0aba4039930`

The product source is `7eb4766f9398e095673b5cb1f92849dd07a4edde`. All five source checks passed on main and on the tag. The tag's 20 primary test receipts contain 60,090 passes, 5,743 skips and no failures; one Ubuntu backing-disk probe passed on main but skipped on the tagged runner because its test folder had no identifiable backing disk. Other results match. Package and inventory checks are recorded in the release evidence. Stable 1.0.0 remains **NO-GO**.
