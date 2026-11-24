# FileCat 1.0.0 — final-qualification checklist for `v1.0.0-rc.2`

Working list, started 2026-11-24. The checks below are what the release records say remains for an installed candidate, on rc.2's exact bytes (private draft 456678823, [rc.2 record](evidence/E-RC2-TAG-20261124.md)). Progress is recorded in [E-FQ-RC2-20261124](evidence/E-FQ-RC2-20261124.md). An item is done only when its stop condition holds on the candidate and the evidence record says so.

**How the list was made:** a read-only survey of the 45 Open or Blocked ledger rows, every issue row from I563 to I634, the older rows that say an installed check remains, and each linked record's stop rule. Where a record names no platform, the item sits where its own source run happened ("any OS").

**Machines:**
- **Windows x64:** the disposable Hyper-V VM FCFQ-W11 on the development PC, a clean Windows 11 Pro 25H2 (GA) install, because the VMware Windows VM is unavailable. Nothing runs on the owner's desktop.
- **Ubuntu:** the 24.04.5 VM.
- **macOS:** the M1 Mac.

Status: **done** (with the record's section), **partial**, **open**, or **blocked** (with what it waits for).

## Command line, all platforms

| # | IDs | Check | Status |
|---|---|---|---|
| L1 | REL-FQ-CANDIDATE | The 24 draft assets match GitHub's digests and the three producer manifests | done (E-FQ-RC2 "The candidate's bytes") |
| L2 | REL-SIGN-WINDOWS, I03 | Signatures of both Setups and all four ZIPs' PE files with revocation; ZIP payloads against their SBOMs; versions | done (E-FQ-RC2 "Windows: development PC") |
| L3 | REL-FQ-CANDIDATE, I03 | Linux packages: manifest pins, .deb control, `--version` of the .deb's binary, the AppImage and the tarball, payloads against SBOMs, desktop entry, libraries | done (E-FQ-RC2 "Linux") |
| L4 | REL-SIGN-MAC, I03 | macOS ZIP: pin, `codesign --verify --deep --strict`, Gatekeeper's assessment, bundle versions, `--version`, payload against SBOM, libraries | done (E-FQ-RC2 "macOS") |
| L5 | GATE-CI-FINAL (e1) | Reconcile rc.2's results with the last published baseline, preview.11's tag run, test by test | done (E-FQ-RC2 "Skip reconciliation", "The two high-integrity tests" and "Permission lanes"); the Mac permission lane needs an ownership-honoring volume |
| L6 | I629, I630, I632, I633, I611 (e2) | The named test classes ran, not skipped, in rc.2's run on the platforms their rules name | done (E-FQ-RC2 "Named test classes") |
| L7 | REL-I03-PROVENANCE (e4), I611 (e5) | Tie the dependency inventory to the 24 assets; PDFium binaries against the kept upstream release | open |
| L8 | I629 step 5 (e6), REL-V22-DOCS (e7) | Desk reviews: the Shell-helper start modes; README, support, update and reporting text against the frozen contract | open |

## Windows x64 (Hyper-V VM FCFQ-W11), installed with Setup unless noted

| # | IDs | Check and stop condition | Needs | Status |
|---|---|---|---|---|
| c1 | REL-SIGN-WINDOWS, REL-FQ-CANDIDATE, plan 12.3 | Install the x64 Setup, installed `--version` 1.0.0, every installed program file signed (the uninstaller included), the administrator helper only in the installed form (DEC-04), uninstall removes the folder, reinstall works; the portable and framework-dependent ZIPs' `--version`, the latter with and without the .NET runtime | command line | done (E-FQ-RC2 "Windows: install lifecycle"); SmartScreen on a browser download not covered by any record |
| c2 | I634, I606 | About shows 1.0.0 | GUI | done (E-FQ-RC2 "Windows: installed-UI checks") |
| c3 | I566 | Paste the five top-level items of the owner's `FileCat_README_Update.zip` from Explorer: 7 files with matching bytes and times, each with `ZoneId=3`, and 4 folders; the ZIP unchanged, no staging left | GUI; the owner's ZIP | open |
| c4 | I567, I564 | Markdown Page: Ctrl+F, type without clicking and match; Escape back to the Page; Page Up/Down scroll, also after the viewer window has focus | GUI | open |
| c5 | I568 | Find feedback in Page and in Text or Hex: neutral, green on a hit while typing (250 ms), red for none; wrap both ways; the wrap notice survives a status update | GUI | open |
| c6 | I569 | In Viewer, Compare and Hex Editor, Close Find and Escape close Find; focus returns to the content; the query is kept on reopening | GUI | open |
| c7 | I570 | Operations drawer: fit with one and several short jobs, newest report followed, about 3 rows scrolling to the oldest, a chosen older row kept, removal, a short window with Copy report, Save report and the last issue reachable, Stop reachable with a recovery item, a manual height kept until Fit height | GUI | open |
| c8 | I571 | Operations history: newest at the top, opening chooses the newest, a late update doesn't move a chosen row | GUI | open |
| c9 | I572 | A denied folder's `..` row reaches the parent without another retry, by double-click and Enter; Back works; a denied place with no parent keeps its retry; a network share's login unchanged | GUI; a share with credentials | open |
| c10 | I563, I573, REL-FQ-CORE | Markdown links in `docs/validation/FILECAT_EXPLORATORY_UX_HARDENING_RESULTS.md`: same-folder opens a separate viewer, the original Page stays; parent-folder shows a review with Cancel selected; Escape and Cancel open nothing; Open makes one viewer; missing, non-Markdown, absolute, `scheme:` and outside-symlink links refused | GUI | open |
| c11 | I577 | Refresh keeps the reading position in Page, Text, Hex, Info and Picture; a shortened file lands at a reachable place; a failed reload keeps the old view | GUI | open |
| c12 | I579 | An unfinished command survives Up then Down exactly; an empty line stays empty; nothing runs | GUI | done |
| c13 | I580 | Text and Page with Find open: after a refresh to matching and non-matching content, count, highlight and border describe the new content without a keystroke; a failed reload keeps view and query | GUI | open |
| c14 | I589 | Windows' own Properties sheet still opens | GUI | done |
| c15 | I590 | A hidden/system file beside an ordinary one: with hidden items off, one row plus a truthful hidden count; on, both rows and no count | GUI | done |
| c16 | I593 | An ordinary local file: no clipboard overwrite and no false "UNC" success; a mapped drive or share: the server/share path and count copied | GUI; a share | open |
| c17 | I601 | Alt+Enter and right-click → Properties on a file and a folder: exact path, clean return; several marked items give the one-item message | GUI | done, with [I635](evidence/E-I635-alt-enter-ignores-marks.md) for Alt+Enter |
| c18 | I602 | Send to → Compressed (zipped) folder on a file and a nested folder: ZIPs open, not empty, paths and hashes match; a taken name gets ` (2)`; no empty ZIP reported as success | GUI | open |
| c19 | I603 | "Share in Explorer…": Explorer selects the exact file, Share works, cancelling sends nothing, return to FileCat | GUI | open |
| c20 | I604 | Windows Copy and Cut on a marked file: the clipboard holds the exact path; paste or move to a scratch folder; a changed selection refused | GUI | open |
| c21 | I605 | A wide Apply command is readable before Run, Cancel runs nothing; a read-only command shows the exact path, exit code and short output | GUI | open |
| c22 | I607 | F1's "Change shortcut…" and the footer's "Choose a command…" reach both steps; change one command's key | GUI | open |
| c23 | I608 | Git badges in a case-sensitive NTFS repository with `PROOF.txt` and `proof.txt`, and after a case-only rename in an ordinary folder | GUI; Git for Windows | open |
| c24 | I609 | Click to select, two separate clicks rename inline, Escape, Enter, double-click opens, no rename from empty space, another row or a drag | GUI | open |
| c25 | I610 | Drag over FTPS and SFTP both ways, one Registry value, a small throwaway drive (confirmation; Shift-drag refused); review, bytes, Cancel, disconnects | GUI; FTPS and SFTP servers; a subst drive | open |
| c26 | I611 | PDF: two-page, malformed and encrypted files; F3 pages, fit/zoom, Info; F4 original bytes; clear no-render message; refresh after fewer pages | GUI; PDF files | open |
| c27 | I616 | Sparse copy on NTFS: length and SHA-256, allocation under ¼, Completed only for the exact target; a failed extent read leaves no target | GUI | open |
| c28 | I618 | Settings at normal and narrow sizes: Tools card, tab tops, search, results, shortcut chooser, pre-selected names, empty-name error, outside click, Escape and close with and without changes, Cancel and OK | GUI; real keyboard | open |
| c29 | I619 | Panel role badges legible | GUI | partial: legible at normal width; narrow widths and larger text open |
| c30 | I620 | Minimum window and larger text: About (all themes), Advanced Find, New connection, Apply command, Pack, Create link without an outer horizontal scrollbar, missing button, clipped warning or detached label | GUI | open |
| c31 | I621 | Ignored Folders: Enter on blank text keeps the editor, Enter on text adds one entry, Add/Remove enablement, Cancel keeps the saved list | GUI | open |
| c32 | I622 | Analyze folder again opens the chooser with the last metadata field; Cancel keeps the order; another field sorts | GUI | open |
| c33 | I623 | Pictures: thumbnails, select/mark/drag/menu, sort/filter, narrow scrolling; F3 Space/Backspace in one window to both ends; archive or remote folder; damaged picture; over 64 MiB uses the preview fallback | GUI; pictures | open |
| c34 | I624 | Many pictures: redraw, unchanged-folder events and Reread don't reload thumbnails; one changed picture updates its tile; the 512-entry/96-MiB cache; OneDrive online-only placeholders stay labelled and never download in the background | GUI; OneDrive online-only files | blocked (OneDrive account in the VM) |
| c35 | I627 | Create file list: one recursive rule, three destinations, saved rule reuse, Cancel writes nothing, unreadable child gives no partial output, links not entered, compact dialog fits | GUI | open |
| c36 | I629 step 2 | Elevated `--native-context-menu --background X Y FOLDER` and `--probe-new NewFolder FOLDER` exit 1 with no READY and create nothing; ordinary `--probe --background FOLDER` exits 0; `--probe-new` NewFolder and `.txt` print READY, INVOKE and `FILECAT_CONTEXT_CREATED`, exit 19; a removed folder gives exit 1 and one error line | command line; elevated and ordinary prompts | open |
| c37 | I629 step 3, I556, REL-I16 | Windows' folder menu on empty space with "FileCat actions…"; Shift adds extended commands; New → Folder or Text Document starts a rename that a move or key within 5 s cancels; Paste moves an item cut in Explorer; Properties; elevated FileCat starts no helper | GUI | open |
| c38 | I629 step 4 | Default-app icons for `.txt` and `.jpg` at every size; `.lnk`/`.url` never reach the helper; the icon helper is the restricted low-integrity one; no `System32\FileCat type probe` left | GUI; token viewer | open |
| c39 | I630 | Name colors for compressed, encrypted and hidden-compressed items in all 7 themes, active and inactive panels and the picture grid; Attr `C`/`E`; High Contrast cyan and magenta; contrast at least 4.5:1 | GUI; EFS (Pro) | open |
| c40 | I632 | From `cmd.exe`, a tool and an Apply command naming a 128-byte `.exe` give an immediate start error, no 16-bit box, window responsive | GUI | open |
| c41 | I633 | Three or more interrupted operations: with the keyboard only, the row with the keyboard is the one whose details and actions show | GUI | open |
| c42 | I107 | Main menus open, stay open and accept clicks | GUI | done |
| c43 | I512, I514, I515, I517 | Free space beside the file mask, capital attributes, drive Properties in front, manual scroll kept across refresh | GUI | partial: free space, capital attributes and both drive Properties routes pass; I517's scroll open |
| c44 | I04, REL-I17-BROKER | A standard account's administrator retry through UAC to the helper's consent page: requester, full text, connection and token, exact effect | GUI; UAC; a standard account | open (the person's attestation is d7) |

## Ubuntu 24.04.5 VM, installed `.deb` unless noted

| # | IDs | Check and stop condition | Needs | Status |
|---|---|---|---|---|
| a1 | REL-FQ-CANDIDATE, I03, I548, plan 12.3 | Install the `.deb` on a clean system: package version, `--version`, `filecat (unconfined)` AppArmor profile; removal clears the profile; reinstall; the AppImage through FUSE; the launcher | `sudo` | partial: command-line checks done without installing (L3); install needs `sudo` |
| a2 | I634, I606 | About shows 1.0.0 | GUI | open |
| a3 | I611 | PDF checks as c26 | GUI | open |
| a4 | I612 | A bind mount of an owned folder: Source root and Root within source match `findmnt`; hash unchanged; unmount cleans up; a `/` mount shows no invented subfolder | GUI; `sudo` | open |
| a5 | I613 | SSH host-key prompt left open over 20 s, then approved: the first listing appears without Refresh; cancel or another key fails closed | GUI; sshd | open |
| a6 | I614 | ext4 ACL copy: bytes, access and default ACLs, `nobody` still denied, inherited ACL the source lacks removed | GUI; `sudo` | open |
| a7 | I616 | Sparse copy as c27 | GUI | open |
| a8 | I617 | Invalid-UTF-8 and literal U+FFFD names: no ambiguous row, clear refusal, a recursive Copy refused with nothing changed, the valid name usable | GUI | open |
| a9 | I618 | Settings → Tools: a real Tab from Program reaches Browse | GUI; real keyboard | open |
| a10 | I589 | Properties on an owned file from the panel and from a context result: path, mode, owner, group; no change | GUI | open |
| a11 | I590 | Hidden dotfile count as c15 | GUI | open |
| a12 | I593 | No UNC command in the Mark menu or command search; Copy full paths works | GUI | open |
| a13 | I610 | Remote-to-local drag over SFTP and FTPS | GUI; servers | open |
| a14 | I619 | Panel role badges legible | GUI | open |
| a15 | I620 | Minimum-size dialogs as c30 | GUI | open |
| a16 | I621 | Ignored Folders as c31 | GUI | open |
| a17 | I622 | Analyze folder chooser as c32 | GUI | open |
| a18 | I623 | Pictures as c33 | GUI | open |
| a19 | I627 | Create file list as c35 | GUI | open |
| a20 | I630 | Ordinary files show no attribute name colors | GUI | open |
| a21 | I574 | Apply command on two marked files, one with a space or punctuation: preview, report, exit 0, output; a failing command's exit and reason; bytes unchanged | GUI | open |
| a22 | I575 | A stale Find result after the file is replaced: visible changed-result reason, no job; a new search copies exactly; a queued transfer whose source changes is refused before the destination exists | GUI | open |
| a23 | I576 | Right-click and Shift+F10 on `..`: only the three parent commands, exact parent, original tab unchanged | GUI | open |
| a24 | I578 | Ctrl+Enter and Ctrl+Shift+Enter insert one quoted name or path at the caret; a selected placeholder replaced once; nothing runs | GUI | open |
| a25 | I581 | Help → Check for updates gives a truthful stable-channel result; nothing downloads; automatic checks off means no connection; a preview build reports a newer preview or itself, never a draft | GUI; internet | open |
| a26 | I628 | Create file list's choosers close on an outside click and keep the dialog and draft | GUI | open |

## M1 Mac, `FileCat-1.0.0-osx-arm64.zip`

| # | IDs | Check and stop condition | Needs | Status |
|---|---|---|---|---|
| b1 | REL-FQ-CANDIDATE, plan 12.3 | Extract, `codesign --verify --deep --strict`, `--version`; place, remove, reinstall | command line | partial: extraction, signature and version done (L4); placement round trip open |
| b2 | REL-SIGN-MAC (DEC-03) | Download the ZIP in a browser (quarantined): Gatekeeper blocks it; Privacy & Security → Open Anyway; TCC grant and deny recorded | GUI; the owner's GitHub sign-in for the private draft; admin and TCC prompts | blocked (owner) |
| b3 | I634, I606 | About shows 1.0.0 | GUI | open |
| b4 | I582 | A decomposed-accent name found by typing the composed accent with real input; reversed once; an ASCII control | GUI | open |
| b5 | I583 | Command-search rows named by their titles in the accessibility tree; one runs and focus returns | GUI; accessibility inspection | open |
| b6 | I584 | Delete review, job and Undo say Trash; Undo restores the exact hash | GUI | open |
| b7 | I585 | Find in a ZIP with Named, Containing and Inside archives: the archive-member note before and after "No matches"; clearing Containing finds members | GUI | open |
| b8 | I586 | Flat View of two same-named files: both rows and the complete banner without Reread | GUI | open |
| b9 | I587 | Recursive content comparison: each row's accessible name is path, status and detail | GUI; accessibility inspection | open |
| b10 | I588 | Sync review rows' accessible names; excluding a step updates row and plan; Cancel changes nothing | GUI; accessibility inspection | open |
| b11 | I589 | Properties as a10 | GUI | open |
| b12 | I590 | Hidden-item count as c15, with a dot or Finder-hidden file | GUI | open |
| b13 | I591 | A sixth custom column profile survives a restart with its name and shortcut | GUI | open |
| b14 | I592 | Info on a three-slice universal Mach-O: three labels including `arm64e.x1` | GUI; the E-I592 file | open |
| b15 | I593 | UNC command absent as a12 | GUI | open |
| b16 | I594 | Changing only the modified time: the job's title, `stat`, unchanged bytes, creation time and mode | GUI | open |
| b17 | I595 | A JPEG with orientation and an empty second EXIF block: rotated dimensions, stored values in Info, no warning | GUI; a made JPEG | open |
| b18 | I596 | A broken link's Properties and View message | GUI | open |
| b19 | I597 | Edit new file… with a space in the name opens TextEdit without Xcode | GUI | open |
| b20 | I598 | Resource-fork and extended-attribute copy to APFS | GUI | open |
| b21 | I599 | ACL copy to APFS, write still denied | GUI | open |
| b22 | I600 | Folder ACL and attribute copy; same-name and cross-volume refusals | GUI; a second volume | open |
| b23 | I610 | Remote drag as a13 | GUI; servers | open |
| b24 | I611 | PDF as c26 | GUI | open |
| b25 | I615 | A copied Finder tag shows its name in Streams and attributes | GUI | open |
| b26 | I616 | Sparse copy: bytes and length; allocation only where the volume makes a sparse source | GUI | open |
| b27 | I619 | TARGET and Set as target centred, unclipped, same click area | GUI | open |
| b28 | I620 | Minimum-size dialogs as c30 | GUI | open |
| b29 | I621 | Ignored Folders as c31 | GUI | open |
| b30 | I622 | Analyze folder as c32 | GUI | open |
| b31 | I623 | Pictures as c33 | GUI | open |
| b32 | I625, I626 | "Review decision…" on a waiting conflict; Include folders checked by default in Select by mask | GUI | open |
| b33 | I627 | Create file list as c35 | GUI | open |
| b34 | I630 | No attribute name colors on ordinary files | GUI | open |
| b35 | REL-I106-RECOVERY | A second physical disk's scan; authopen approve and refuse; a device change | GUI; a USB disk; authopen (owner) | blocked (owner and media) |

## Blocked on people, hardware or an owner decision

| # | IDs | What remains | Blocked by |
|---|---|---|---|
| d1 | REL-WIN-GA, REL-SIGN-WINDOWS, I02, I04 | A physical GA Windows 11 PC: clean install, launch, use and removal, and Smart App Control enforcing with the candidate's components loaded | ENV-03 |
| d2 | REL-SIGN-WINDOWS, I611, I623, I634 | ARM64: install the ARM64 Setup, signatures, About, PDF, pictures, Smart App Control | ENV-02 (not mandatory under DEC-02); a Windows on Arm VM on the Mac could cover it |
| d3 | REL-PERFORMANCE (I07, I42) | Fixed workloads on the reference machine | ENV-08 and numeric targets |
| d4 | REL-I06-AGGREGATE | Whole-process memory accounting against fixed limits | Owner limits; reference machine |
| d5 | REL-HUMAN-UX | Unprompted tasks by Commander users, newcomers and platform users | PPL-01 |
| d6 | REL-HUMAN-AT, I583 | Experienced screen-reader users | PPL-02 |
| d7 | REL-CONSENT-ATTEST | A person attests UAC, polkit, authopen and TCC prompts and device identity | PPL-03 |
| d8 | REL-I17-BROKER, I310, I324 | Real consent under default UAC read by a person; remaining helper loading, connection and lifetime work | PPL-03; scope |
| d9 | REL-I106-RECOVERY, I495, I496, I519 | Other media and layouts, installed consent, the physical USB stick repeat, the installed helper end to end, a hostile GPT disk | Physical media; PPL-03; scope |
| d10 | REL-I08-CONTAINMENT, REL-I16-LAUNCH, I493, I546, I549, I550, I552 | Whole-process memory, remaining parser, Shell and helper-launch routes, installed containment | Owner scope decision |
| d11 | REL-FQ-CORE and older open-ended issues | 232 statuses from I300 to I556 that say only "candidate remains", with no fixed step; AC-11's real-phone Move | Owner scope decision; an MTP phone |
| d12 | REL-FQ-CANDIDATE | Every row Accepted, skips and failures reconciled, evidence sealed | Everything above |
| d13 | REL-REP-CUSTODY | Seal the evidence read-only under DEC-10 and check its manifest independently | After FQ |
| d14 | REL-PIPELINE | A negative and positive test of manifest-only promotion to stable | Owner-gated GitHub |
| d15 | REL-GO-PUBLISH | The owner's explicit GO, then the promotion checks | Owner |
