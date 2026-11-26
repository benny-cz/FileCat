# FileCat 1.0.0 rc.8: the owner's consent walkthrough

Prepared 2026-11-25, updated for rc.5 the same day, and for rc.6, rc.7 and rc.8 on 2026-11-26. rc.5 differs from rc.3 only by I672 to I679 (signing in to a share on Windows, a share whose server does not answer no longer stopping the window, a picture timeout's wording, two status races, and tests). rc.6 differs from rc.5 only by I680: Setup and the uninstaller close a running FileCat. None of these is on a consent path. rc.7 differs from rc.6 only by [I681](evidence/E-I681-linux-census-zombie.md), which your U3 on rc.6 found: on Linux, the process check behind U2 to U4 now passes over zombie processes. The steps and what they expect are unchanged; on rc.6, U3 did not get what it expects. rc.8 differs from rc.7 only by [I682](evidence/E-I682-no-display-abort.md): on Linux, FileCat started without a usable display says why and exits instead of crashing. No step starts FileCat that way. Under [DEC-14](evidence/E-DEC-2026-11-24-release-scope.md) you attest the UAC, polkit, authopen and TCC prompts and device identity on `v1.0.0-rc.8` yourself. This walkthrough covers the person's part of REL-CONSENT-ATTEST (PPL-03, checklist d7), REL-I17-BROKER (d8, c44), REL-I106-RECOVERY (d9, b35) and REL-SIGN-MAC (b2) in the [rc.2 checklist](FILECAT_1_0_RC2_FQ_CHECKLIST.md). There are three sections, one per machine, of 20 to 25 minutes each. Do them in any order.

**Where it stands:** on rc.6 you passed the Windows section, W1 to W7; Windows' process records confirm each step ([record](evidence/E-RC6-TAG-20261126.md)). In the Ubuntu section, U1 and U2 passed and U3 failed (I681). **On rc.8, do the Ubuntu section in full (U1 to U5) and the Mac section.** The Windows code is the same in rc.8, and the session repeated W2 and W5 by tool on rc.7's and rc.8's own Setups. Carrying W1 to W7 over from rc.6 is your acceptance at GO; you may repeat the section on rc.8 instead.

**Rules**
- Test rc.8 only. Help → About FileCat must read `Version 1.0.0 (abcdef1)`, where `abcdef1` stands for the first 7 characters of rc.8's source commit. The session gives you the commit.
- Stop at the first Fail and tell the session. Do not work around it.
- Windows' prompts show your name as the publisher (DEC-13). Keep your screenshots. In the chat write "publisher: my name", never the name itself.
- Quoted texts come from rc.8's source or the release records. Where the text is Windows', Ubuntu's or macOS's own and no record has it, the step says **Unverified**: write down what you see.
- Each step has a folded Source note. Source files are cited at the `v1.0.0-rc.8` tag (`814a42f2`). Apart from the installer (as in rc.6), the Linux process check's `SingleInstance.cs` and `SingleInstance.ProcessImages.cs` (rc.7) and `Program.cs` (rc.8), none of them cited by a step, its source files are rc.5's (`19002592`).
  - **Re-checked for rc.7 and for rc.8 on 2026-11-26, by script:** each cited range of a code or build file at each tag was compared with the range the note first cited at `rc3/integration-3` (`94b74365`), where these notes were written; U5's note, added later, was checked by hand.
  - **Unchanged, with three exceptions.** The consent page's coverage sentence now says "this 1 step" (I669), as W4 quotes it. The update setting's note was reworded, which G6 does not quote. And rc.6's compile of the x64 installer in CI (I680) moved `ci.yml`'s signing and draft steps down by 19 lines, which the rc.6 update missed: M1 and the [GA PC checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)'s G3 and G4 now cite the moved lines.
  - Records are cited at `main` `7a15ac8b`, or at `rc3/integration-3` where marked "rc3".

## 1. Windows 11 25H2: the Hyper-V VM FileCat-FQ-W11 (about 25 minutes)

**The session prepares:** rc.8 installed with its Setup, UAC at Windows' default, and the VM running with the administrator test account `fcqa` signed in. The records call the VM's computer FCFQ-W11. **You need:** VMConnect in a basic session, and `fcqa`'s password. It exists only in a DPAPI-protected file for your account; ask the session for its path. A VM has no physical disk to identify, so Windows' device identity is step G8 of the [GA PC checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md).

### W1 · Administrator window: Windows' prompt refused
- **Do:** Start FileCat from the Start menu and check About. Tools → Open FileCat as administrator…. In FileCat's question click **Open as administrator**. In Windows' prompt click **No**.
- **Expect:** The main window's title ends "fcqa (administrator, not elevated)". FileCat's question is titled "Open FileCat as administrator" and says "Windows will ask for approval to open a separate FileCat window with full administrator rights. Your current window stays open." Its buttons are Open as administrator and Cancel; Cancel is the default, so Enter cancels. Windows' prompt names the program "FileCat administrator window", with you as verified publisher. After No there is no second window, and FileCat says "Administrator approval was canceled."
- **Record:** Pass/Fail and a screenshot of Windows' prompt. **Closes:** d7 (REL-CONSENT-ATTEST); I644's prompt check.

<details><summary>Source</summary>src/FileCat.App/ViewModels/MainMenuModel.cs:53; src/FileCat.Core/Commands/CommandRegistry.cs:486; src/FileCat.App/Views/AboutDialog.cs:119-126; src/FileCat.Core/Platform/ProcessAccount.cs:26-44; src/FileCat.App/ViewModels/MainViewModel.Elevation.cs:26-31, 51-53; src/FileCat.App/Views/OverlayDialogService.cs:519-520; eng/Build-PrivilegedBootstrap.ps1:38-39; rc3 evidence/E-I644-administrator-window-environment-profiler.md:78, 130</details>

### W2 · Administrator window: approved
- **Do:** Repeat W1, but open the prompt's details (Windows' own link) before you click **Yes**. In the new window: Ctrl+L, `C:\Program Files\FileCat`, Enter. Put the cursor on `THIRD-PARTY-NOTICES.md` and press F3, then close the viewer. Keep this window open for W5.
- **Expect:** The details show `C:\Program Files\FileCat\FileCat.AdminLauncher.exe` and `--new-instance`. The new window's title ends "fcqa (elevated)", and it says "FileCat is running as administrator. Operations have full rights, drag and drop from other programs is blocked, and the normal profile is read-only." There, Tools → Open FileCat as administrator… is unavailable: "This FileCat window is already running as administrator." F3 shows the file's text instead of a drawn page, with the reason "Web pages are not drawn while FileCat runs as administrator or root: …".
- **Record:** Pass/Fail, a screenshot of the details, and whether the new window came to the front by itself (not yet observed, E-I644). **Closes:** d7; I644's prompt check; I645's elevated-viewer check (the session looks at `cache\webview`).

<details><summary>Source</summary>src/FileCat.App/Services/AdministratorWindow.cs:25-31; src/FileCat.App/App.axaml.cs:108-109, 157-161; src/FileCat.App/ViewModels/MainViewModel.Commands.cs:39-40; src/FileCat.App/Views/ViewerWindow.cs:202, 658-659; src/FileCat.App/Controls/PageView.cs:13, 521-523; eng/publish.ps1:157; rc3 E-I644 lines 130, 133; rc3 evidence/E-I645-elevated-temp-sweeps-follow-links.md:126</details>

### W3 · Administrator helper: Windows' prompt refused
- **Do:** In the ordinary window: Ctrl+L, `C:\Program Files`, Enter. F7, name `FileCat-consent`, **Create**. In the error click **Review denied items as administrator…**. In Windows' prompt click **No**. Then open Operations (Ctrl+J).
- **Expect:** The error is titled "Could not create the folder" and says "Access is denied. If the destination is a protected folder, Windows Controlled Folder Access may be blocking FileCat.", the path, and "FileCat will continue the ordinary operation and offer one review of the access-denied items when it finishes." Its buttons are Retry, Review denied items as administrator…, Skip and Cancel operation. FileCat then says "Windows will ask for approval, then the FileCat helper will show the remaining items for review." Windows' prompt names "FileCat administrator helper: runs one approved plan, then exits", with you as publisher. After No, Operations shows the job "Retry folder creation as administrator" with "Windows did not get administrator approval, so nothing was changed." No folder exists.
- **Record:** Pass/Fail and a screenshot of the prompt. **Closes:** d7, d8.

<details><summary>Source</summary>CommandRegistry.cs:325, 376, 506; src/FileCat.App/ViewModels/MainViewModel.Operations.cs:520-532, 678-686; src/FileCat.Core/Jobs/Executors.cs:328, 432; src/FileCat.App/Views/OperationDialogs.cs:679-695; MainViewModel.Elevation.cs:141-186; src/FileCat.App/Services/AdministratorFileRetry.cs:110-116; eng/Build-PrivilegedBootstrap.ps1:33; src/FileCat.Platform.Windows/Elevation/ElevationBroker.cs:132-136; src/FileCat.Platform.Windows/Elevation/ElevatedJobExecutor.cs:19-27; src/FileCat.Core/Jobs/JobManager.cs:867-870</details>

### W4 · Administrator helper: consent window read, then Cancel
- **Do:** In Operations, on the job `Create folder "FileCat-consent"`, click **Retry as administrator…**, then **Yes** in Windows' prompt. Read FileCat's helper window and click **Cancel**.
- **Expect:** The window is titled "FileCat — administrator operation", with the heading "Retry folder creation as administrator" and the text "Requested by [computer]\fcqa from FileCat at [time]. This approval covers only this 1 step: 1 new folder; the helper exits when it finishes." (I669 corrected this sentence's singular in rc.3.) The steps are open (the toggle reads "Hide the steps") and list `1. Create folder "FileCat-consent" in C:\Program Files`. The footer says "Completed steps are kept if a later step fails. Links are never followed." The buttons are Run as administrator and Cancel; Cancel is the default. After Cancel, the job says "The plan was declined in the administrator window; nothing was changed." No folder exists.
- **Record:** Pass/Fail and a screenshot of the whole window. **Closes:** d8 (REL-I17-BROKER, I310, I324), d7.

<details><summary>Source</summary>src/FileCat.Core/Jobs/JobManager.cs:794-795 (the job's title); src/FileCat.App/Views/OperationsView.axaml:57, 241; src/FileCat.App/ViewModels/OperationCenterViewModel.cs:166-168; MainViewModel.Elevation.cs:87-94, 141-145; src/FileCat.PrivilegedHost/Program.cs:116-128, 173-177; src/FileCat.PrivilegedHost/ConsentDialog.cs:104-130; src/FileCat.Platform.Windows/Elevation/ElevationConsent.cs:21-33, 60-62, 65-87; src/FileCat.Platform.Windows/Elevation/ElevationPlanCodec.cs:261, 309-313; ElevatedJobExecutor.cs:56-62</details>

### W5 · Administrator helper: approved, exact effect
- **Do:** As W4, but click **Run as administrator**. Reread the panel with Ctrl+R. Then delete `C:\Program Files\FileCat-consent` in the administrator window from W2 (F8) and close that window.
- **Expect:** The same helper window, with a new time. The job completes, `C:\Program Files\FileCat-consent` exists, and nothing else in `C:\Program Files` changed.
- **Record:** Pass/Fail. **Closes:** d8, d7.

<details><summary>Source</summary>src/FileCat.PrivilegedHost/Program.cs:132-143, 163-171; ElevatedJobExecutor.cs:64-104; ElevationPlanCodec.cs:261; CommandRegistry.cs:378 (Reread, Ctrl+R)</details>

### W6 · A standard account (c44): create it and sign in
You create this account yourself, with one command, so its password never passes through the session.
- **Do:** As `fcqa`, right-click Start → Terminal (Admin) and click **Yes**. Run `net user fcstd * /add` and type a password twice. Run `net user fcstd`: its groups must include Users and not Administrators. Sign out, sign in as `fcstd`, and start FileCat. You may later remove the account with `net user fcstd /delete` **[Admin]**.
- **Expect:** FileCat's title ends "fcstd (standard user)". **Unverified:** the menu name, net user's prompts and output, and Windows' first-sign-in screens are Windows' own.
- **Record:** Pass/Fail. **Closes:** c44 (its setup).

<details><summary>Source</summary>ProcessAccount.cs:36-44; FILECAT_1_0_RC2_FQ_CHECKLIST.md:74 (c44 needs a standard account)</details>

### W7 · Standard account: the helper through Windows' credential prompt (c44)
- **Do:** As `fcstd`, repeat W3 with the name `FileCat-c44`, but in Windows' prompt enter `fcqa`'s user name and password. Read the helper window, then click **Run as administrator**. Optional, while the helper window is open: Task Manager → Details, with the user-name and elevation columns added. `FileCat.PrivilegedHost.exe` should run as `fcqa`, elevated, and `FileCat.exe` as `fcstd`, not elevated (**Unverified**: Windows' column names).
- **Expect:** Windows asks for an administrator's credentials (**Unverified** wording) and names "FileCat administrator helper: runs one approved plan, then exits", with you as publisher. The helper window is as in W4, but says "Requested by [computer]\fcstd …" and lists `1. Create folder "FileCat-c44" in C:\Program Files`. After Run, `C:\Program Files\FileCat-c44` exists. A Fail looks like a window "Nothing was run" with a reason, such as "The FileCat window that asked for this operation is no longer running." or "The requesting user could not be identified." Copy that reason.
- **Record:** Pass/Fail and screenshots of both windows. **Closes:** c44 (I04, REL-I17-BROKER), d8, d7. The helper's loader, pipe and lifetime, the rest of d8, stay with the session.

<details><summary>Source</summary>rc3 docs/adr/ADR-14-privileged-broker.md:9 (the administrator may be another account); ElevationBroker.cs:79-93; src/FileCat.PrivilegedHost/Program.cs:28-29, 105-113; ConsentDialog.cs:181-206</details>

## 2. Ubuntu 24.04.5: the VMware VM (about 20 minutes)

Before FileCat reads a drive here, polkit asks for your password for two things. First `pkexec` runs FileCat's process check as root, because your account cannot see which other processes are FileCat. Then UDisks2 opens the drive read-only. The candidate .deb installs no polkit policy of FileCat's own, so the dialogs' wording is polkit's and UDisks2's: **Unverified**, write it down. Answer a process-check dialog within 90 seconds; after that FileCat treats it as unanswered.

**The session prepares:** rc.8's .deb installed. **You need:** your account's password, and a USB stick with a FAT or exFAT volume that you do not mind being read, connected to the VM through VMware's removable-devices menu. Without a stick, the session can add a small virtual disk instead; that tests polkit but not physical identity. Close every other FileCat window.

<details><summary>Source</summary>src/FileCat.App/RecoveryProcessCensus.cs:9-27, 78-109 (the 90 s limit at 19, 102); src/FileCat.Recovery/Unix/UnixDevices.cs:168-196; eng/package-linux.sh:61-73, 205-209 (FileCat's own polkit policy ships only in local qualification builds); rc3 evidence/E-I658-census-hidden-processes.md:11; rc3 evidence/E-I646-linux-root-census-opens-replaceable-path.md:68 (on this VM the ordinary check cannot read 48 processes)</details>

### U1 · The stick's identity
- **Do:** In a terminal run `lsblk -o NAME,VENDOR,MODEL,SIZE,TRAN,MOUNTPOINTS`. In FileCat check About, then open Tools → Recover deleted files….
- **Expect:** The list shows the stick's volume by its mount point, with a detail such as "vfat · [size] USB · sdb1", and the whole stick as "Disk sdb: [vendor model]", with "Whole disk, [size] USB · mounted at … · also finds deleted partitions". Vendor, model, size and USB agree with lsblk.
- **Record:** Pass/Fail and the lsblk line. **Closes:** d7 (device identity), d9.

<details><summary>Source</summary>src/FileCat.App/ViewModels/MainViewModel.Recovery.cs:91-127; UnixDevices.cs:400-421; CommandRegistry.cs:484</details>

### U2 · Process check refused
- **Do:** Choose "Disk sdb: …" and press Enter. When Ubuntu asks for your password, click **Cancel**.
- **Expect:** The password dialog comes before FileCat's own question. If it shows a command, it is `/opt/filecat/FileCat --filecat-recovery-process-census` with FileCat's process number and 32 hex characters. After Cancel, FileCat shows "Recover deleted files": "FileCat does not scan disk sdb ([model]) yet: other FileCat processes may still write to this disk. Close other FileCat windows, wait for their helpers to finish, then ask for the scan again. If none are open, check that process information is accessible." Nothing is read.
- **Record:** Pass/Fail and the dialog's wording. **Closes:** d7, d9.

<details><summary>Source</summary>MainViewModel.Recovery.cs:518-521, 545-551, 654, 667-672; RecoveryProcessCensus.cs:16, 80-99, 54-62; evidence/E-FQ-RC2-20261124.md:47 (/opt/filecat/FileCat)</details>

### U3 · Drive read refused
- **Do:** Tools → Recover deleted files…, the same disk, Enter. Approve the password dialog. In FileCat's question click **Authorize and scan**, and approve again if asked. When the next password dialog appears, click **Cancel**.
- **Expect:** FileCat's question, "Recover deleted files", begins "Scan disk sdb ([model]) for deleted files? The scan lists this source's partitions, …" and includes "Linux may ask you to approve read-only access to this device." and that the disk is mounted. Its key points are "Source: disk sdb ([model])", "Linux may ask you to approve read-only access to this device." and "Recover files to another disk. Mounted programs may still write to this one." A second process-check dialog may follow, since FileCat checks again after your answer. The scan opens in a new tab; after Cancel it shows "The request to read the drive was dismissed." and "Ctrl+R retries · Backspace goes up". Nothing is read.
- **Record:** Pass/Fail, how many dialogs you saw, and their wording. **Closes:** d7, d9.

<details><summary>Source</summary>MainViewModel.Recovery.cs:559-574, 662-695; UnixDevices.cs:98-109, 186-201; FileListControl.cs:1431-1434; src/FileCat.Core/Listing/ListingModel.cs:960-964, 1001-1012; evidence/E-V09-T2-linux-write-trace.md:20 (a polkit refusal showed in the scan tab)</details>

### U4 · Drive read approved
- **Do:** As U3, but approve every dialog. Then close the scan tab and disconnect the stick in VMware.
- **Expect:** The scan tab lists the stick's partitions, and inside them any deleted files.
- **Record:** Pass/Fail. **Closes:** d7, d9.

<details><summary>Source</summary>UnixDevices.cs:186-196; MainViewModel.Recovery.cs:689-694; evidence/E-V09-T2-linux-write-trace.md:19</details>

### U5 · One real Tab in Settings (I618)
Added on 2026-11-26: I618's last native check needs a key pressed by a person, because the VM's injected keys do not count either way.
- **Do:** In FileCat, Tools → Settings… (Ctrl+Comma), the Tools tab. Under User commands (F9) click **Add command**, click into its **Program** field, and press Tab on your keyboard once. Then Cancel, and Discard draft if asked.
- **Expect:** The keyboard moves to **Browse for command program…**: its focus ring shows on that button.
- **Record:** Pass/Fail. **Closes:** I618's Ubuntu keyboard route; c28.

<details><summary>Source</summary>src/FileCat.App/Views/UserCommandsEditor.cs:49 (the Program field), 88 (Browse for command program…); src/FileCat.Core/Commands/CommandRegistry.cs:510 (Settings…, Ctrl+Comma); evidence/E-I618-settings-layout-and-draft-dismissal.md:60</details>

## 3. macOS 27.2.1: the M1 Mac (about 25 minutes)

The Mac build is ad-hoc signed and not notarized (DEC-03), so macOS blocks it until you allow it in System Settings. Only that route counts for REL-SIGN-MAC: removing the quarantine attribute or turning Gatekeeper off never counts. FileCat's Info.plist declares no privacy purpose strings, so every macOS dialog is macOS's own wording: **Unverified**, write it down.

**You need:** your GitHub sign-in in the Mac's browser (the draft is private), your administrator password, and a disposable USB disk with a FAT or exFAT volume, not yet plugged in. Quit any running FileCat. If FileCat ran on this Mac before, first run `tccutil reset All io.github.benny-cz.filecat` in Terminal so that macOS asks again (**Unverified**: Apple's tool, not in the records).

<details><summary>Source</summary>FILECAT_1_0_RELEASE_EXECUTION_REPORT.md:93 (REL-SIGN-MAC); FILECAT_1_0_RELEASE_BLOCKERS.md:28 (DEC-03); eng/package-macos.sh:105-124, 127-136; evidence/E-FQ-RC2-20261124.md:63-64</details>

### M1 · Download (b2)
- **Do:** In the browser open the draft "FileCat v1.0.0-rc.8 (signed release candidate)" and download `FileCat-1.0.0-osx-arm64.zip` and `FileCat-1.0.0-macos-assets.json`. In Terminal, in Downloads, run `xattr -p com.apple.quarantine FileCat-1.0.0-osx-arm64.zip` and `shasum -a 256 FileCat-1.0.0-osx-arm64.zip`. If the browser unpacked the ZIP by itself, run xattr on `FileCat.app` instead and say so.
- **Expect:** xattr prints a value. The SHA-256 equals the ZIP's `SHA256` in the JSON file's `Files` list, and its `CandidateRef` is `refs/tags/v1.0.0-rc.8`.
- **Record:** Pass/Fail and the hash's first 16 characters. **Closes:** b2 (REL-SIGN-MAC).

<details><summary>Source</summary>.github/workflows/ci.yml:1403-1412 (draft name); eng/Select-ReleaseAssets.ps1:56, 58-83; FILECAT_1_0_RC2_FQ_CHECKLIST.md:112 (b2)</details>

### M2 · Gatekeeper blocks the first open (b2)
- **Do:** Unpack the ZIP in Finder if needed, drag `FileCat.app` to Applications, and double-click it there.
- **Expect:** macOS refuses to open it (**Unverified** wording).
- **Record:** Pass/Fail and a screenshot. **Closes:** b2.

<details><summary>Source</summary>eng/package-macos.sh:127-128 (ad-hoc signature); evidence/E-FQ-RC2-20261124.md:64 (spctl rejects rc.2's app)</details>

### M3 · Open Anyway (b2)
- **Do:** System Settings → Privacy & Security, scroll down, click **Open Anyway**, and authenticate if asked. When the warning comes back, click Open. Then check About.
- **Expect:** FileCat opens, and About shows rc.8's version.
- **Record:** Pass/Fail and each dialog's wording (**Unverified**). **Closes:** b2.

<details><summary>Source</summary>Apple Support, Safely open apps on your Mac (support.apple.com/102445, published 2026-05-27); AboutDialog.cs:119-126</details>

### M4 · Files and folders: Allow (b2)
- **Do:** In FileCat: Navigate → Go to home folder, then open Documents.
- **Expect:** macOS asks whether FileCat may access the Documents folder (**Unverified** wording). After **Allow**, its contents are listed.
- **Record:** Pass/Fail and the wording. **Closes:** b2, d7.

<details><summary>Source</summary>src/FileCat.App/ViewModels/MainMenuModel.cs:32; CommandRegistry.cs:392; eng/package-macos.sh:105-124 (no purpose strings)</details>

### M5 · Files and folders: Don't Allow (b2)
- **Do:** Open Desktop and click **Don't Allow**. Then look at System Settings → Privacy & Security → Files & Folders → FileCat.
- **Expect:** The panel lists nothing and shows the system's reason (**Unverified** wording) above "Ctrl+R retries · Backspace goes up". System Settings shows Documents allowed and Desktop not (**Unverified**: macOS's layout).
- **Record:** Pass/Fail. **Closes:** b2, d7.

<details><summary>Source</summary>ListingModel.cs:1001-1006 (the system's reason is kept); FileListControl.cs:1431-1434</details>

### M6 · The USB disk's identity (b35)
- **Do:** Plug in the USB disk. In Terminal run `diskutil list external`, then `diskutil info diskN` for it. In FileCat open its volume under `/Volumes`; if macOS asks about a removable volume, click **Allow** and note it. Then Tools → Recover deleted files….
- **Expect:** The list shows "Disk rdiskN: [media name]" with "Whole disk, [size] USB · mounted at /Volumes/… · also finds deleted partitions", and the volume by its mount point. The media name, size and protocol agree with diskutil.
- **Record:** Pass/Fail and diskutil's media name and protocol lines. **Closes:** b35, d7 (device identity), d9.

<details><summary>Source</summary>UnixDevices.cs:799-823 (diskutil's MediaName and BusProtocol); MainViewModel.Recovery.cs:91-119; FILECAT_1_0_RC2_FQ_CHECKLIST.md:145 (b35)</details>

### M7 · authopen: Cancel (b35)
- **Do:** Choose "Disk rdiskN: …", Enter, **Authorize and scan**. In macOS's authorization dialog click **Cancel**.
- **Expect:** FileCat's question includes "macOS may ask you to approve read-only access to this device." and offers Authorize and scan. The scan tab then shows "Reading /dev/rdiskN was not approved." Nothing is read.
- **Record:** Pass/Fail and the dialog's wording (**Unverified**). **Closes:** b35, d7, d9.

<details><summary>Source</summary>MainViewModel.Recovery.cs:662-683; UnixDevices.cs:226-267; evidence/E-V09-M6-macos-authopen-preparation.md:76</details>

### M8 · authopen: the disk changes while you decide (b35)
- **Do:** Ask again as in M7. While macOS's dialog is open, eject the disk in Finder and wait until it disappears. Then approve the dialog. Plug the disk back in.
- **Expect:** Nothing is read, and the scan tab says "The selected device changed or was removed while opening. Select it again."
- **Record:** Pass/Fail. **Closes:** b35, d9.

<details><summary>Source</summary>UnixDevices.cs:49-79; src/FileCat.Recovery/RecoveryProvider.cs:671-676 (a disk that took the old one's place is refused too); evidence/E-V09-M10-macos-guard-device-removal.md:1-6</details>

### M9 · authopen: approved (b35)
- **Do:** Tools → Recover deleted files…, the disk again, **Authorize and scan**, and approve.
- **Expect:** The scan tab lists the disk's partitions, and inside them any deleted files. If no dialog appears this time, note that.
- **Record:** Pass/Fail. **Closes:** b35, d7, d9.

<details><summary>Source</summary>UnixDevices.cs:98-109, 226-266; MainViewModel.Recovery.cs:689-694</details>

## Report back

Paste this table into the chat, filled in. Add the wording of every dialog marked Unverified, and say which screenshots you kept.

| Step | Pass/Fail | Note |
|---|---|---|
| W1 Administrator window, refused | | |
| W2 Administrator window, approved | | |
| W3 Helper, Windows' prompt refused | | |
| W4 Helper, consent window cancelled | | |
| W5 Helper, approved | | |
| W6 Standard account created | | |
| W7 Standard account through credentials (c44) | | |
| U1 Stick identity | | |
| U2 Process check refused | | |
| U3 Drive read refused | | |
| U4 Drive read approved | | |
| U5 Settings: a real Tab reaches Browse | | |
| M1 Download, quarantine, hash | | |
| M2 Gatekeeper blocks | | |
| M3 Open Anyway | | |
| M4 Documents allowed | | |
| M5 Desktop refused | | |
| M6 USB disk identity | | |
| M7 authopen cancelled | | |
| M8 Disk changed during the dialog | | |
| M9 authopen approved | | |

## Not covered here

- The helper's loader, pipe and lifetime (the rest of d8), and proof that nothing was written to the media (I495, d9): the session's, by tool.
- Windows' physical device identity: step G8 of the [GA PC checklist](FILECAT_1_0_RC3_GA_PC_CHECKLIST.md).
- b1's placement round trip on the Mac and a1's .deb install and removal: the session's.
