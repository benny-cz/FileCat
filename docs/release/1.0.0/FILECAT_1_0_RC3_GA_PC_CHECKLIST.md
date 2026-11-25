# FileCat 1.0.0 rc.3: GA PC checklist (REL-WIN-GA)

Prepared 2026-11-25. Under [DEC-14](evidence/E-DEC-2026-11-24-release-scope.md) you provide a physical PC with a released (GA) Windows 11. On it you install rc.3's x64 Setup, start FileCat, use it and remove it, with Smart App Control enforcing while FileCat's programs load. This closes checklist d1 in the [rc.2 checklist](FILECAT_1_0_RC2_FQ_CHECKLIST.md): REL-WIN-GA (ENV-03) and the GA-tier Smart App Control part of REL-SIGN-WINDOWS. It takes about 45 minutes. **[Admin]** marks a step that needs administrator approval.

**Rules:** as in the [consent walkthrough](FILECAT_1_0_RC3_CONSENT_WALKTHROUGH.md). Test rc.3 only; stop at the first Fail; never put your name in the chat; **Unverified** marks text that neither rc.3's source nor a record gives. Source notes cite source files at `rc3/integration-3` `94b74365`, and records at `main` `7a15ac8b` unless marked "rc3".

**You need:** your GitHub sign-in in a browser on this PC (the draft is private), a PDF, a folder with a few JPEG or PNG pictures, a small folder of your own to pack, and optionally a USB stick with a FAT or exFAT volume (G8).

## Read first: Smart App Control (SAC)

- **Where:** Windows Security → App & browser control → Smart App Control settings. It shows On, Evaluation or Off.
- **What each state means for this check:**
  - **On** enforces: a program runs only if Microsoft's service knows it or it has a valid signature from a trusted authority. Only On counts for d1.
  - **Evaluation:** Windows is still deciding whether to turn SAC on, and blocks nothing meanwhile. Install and use would pass without proving anything about enforcement.
  - **Off:** SAC does not run. Same as Evaluation for this check.
- **Turning it on: Microsoft's documents disagree, and this checklist could not confirm what your build does.**
  - The preview update of 26 March 2026 (KB5079391, builds 26200.8116 and 26100.8116) says SAC can now be switched on or off in that settings page without a clean install, and that this is rolling out gradually. Microsoft's SAC FAQ also says that recent updates allow it.
  - The Windows Security help page still says that SAC can be turned on only on a new installation, or after resetting or reinstalling Windows.
  - The help page adds that once SAC leaves Evaluation, it cannot return there without a reset or reinstall. The newer notes do not say otherwise.
  - So: if the page lets you choose On, choose it. If it does not, stop and tell the session. Resetting your PC is your decision, not this checklist's.
- **Side effect:** while On, SAC blocks other unsigned programs on this PC too. Per KB5079391, you can switch it off again afterwards.
- **From PowerShell:** `VerifiedAndReputablePolicyState` read 1 with SAC On and 0 with it Off in the release's earlier checks. Its value in Evaluation is **Unverified**.

<details><summary>Source</summary>Microsoft Learn, Smart App Control overview (learn.microsoft.com/windows/apps/develop/smart-app-control/overview, updated 2026-10-06); Microsoft Support, App &amp; browser control in the Windows Security app; Microsoft Support, Smart App Control FAQ (topic 285ea03d-fa88-4d56-882e-6698afdb7003); Microsoft Support, March 26, 2026 KB5079391 preview release notes; evidence/E-SIGN-DRYRUN-20261120.md:122; FILECAT_1_0_RELEASE_ACTIVITY_LOG.md:5386</details>

## What a block would look like

- Windows shows a popup titled "Smart App Control has blocked part of this app". If it disappears, Windows Security → Virus & threat protection → Protection history keeps an entry for Smart App Control or a blocked app (Microsoft Support). The popup's body text is **Unverified**.
- The Code Integrity log (Microsoft-Windows-CodeIntegrity/Operational) records events 3077, 3033, 3089 and 3118 naming the blocked file. A start from a script fails with "An Application Control policy has blocked this file".
- In FileCat, an operation that meets the policy says "A system policy (for example Smart App Control or an administrator rule) blocks this file." A blocked administrator window gives "Could not open FileCat as administrator" with the reason. What FileCat says when its picture, archive or Shell helper is blocked is **Unverified**.

<details><summary>Source</summary>Microsoft Support, Smart App Control has blocked part of this app (topic 0729fff1-48bf-4b25-aa97-632fe55ccca2); evidence/E-SIGN-DRYRUN-20261120.md:139-147; src/FileCat.Core/Jobs/Executors.cs:439; src/FileCat.App/ViewModels/MainViewModel.Elevation.cs:55-58</details>

## Steps

### G1 · Note the PC
- **Do:** Run `winver` for the edition and version. Then, in PowerShell:

```powershell
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' | Select-Object EditionID, DisplayVersion, CurrentBuild, UBR
$env:PROCESSOR_ARCHITECTURE
Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' ConsentPromptBehaviorAdmin
Get-ItemPropertyValue 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' VerifiedAndReputablePolicyState
Test-Path 'C:\Program Files\FileCat', "$env:APPDATA\FileCat", "$env:LOCALAPPDATA\FileCat"
```
- **Expect:** `AMD64`. `5`, Windows' default UAC setting. The SAC value as in Read first. Three times `False`: no earlier FileCat. If FileCat was here before, uninstall it, rename its two profile folders, and say so. On an ARM64 PC, stop: REL-WIN-GA needs x64.
- **Record:** the edition, DisplayVersion, CurrentBuild.UBR and the SAC state. The session checks that the build is a serviced GA build.

<details><summary>Source</summary>FILECAT_1_0_RELEASE_BLOCKERS.md:27, 59 (DEC-02, ENV-03: a GA Windows x64 PC); rc3 evidence/E-I644-administrator-window-environment-profiler.md:12 (5 is the default); eng/installer/FileCat.iss:76-79 (profile folders)</details>

### G2 · Smart App Control On
- **Do:** Open Smart App Control settings. If it shows Evaluation or Off, decide as in Read first, and choose On if it is offered. Then rerun the `VerifiedAndReputablePolicyState` line.
- **Expect:** On, and the value `1`. Whether Windows asks for administrator approval here is **Unverified**.
- **Record:** the state before and after, and a screenshot. **Closes:** d1's precondition.

<details><summary>Source</summary>evidence/E-SIGN-DRYRUN-20261120.md:122</details>

### G3 · Download
- **Do:** In the browser, signed in to GitHub, open the repository's Releases and the draft "FileCat v1.0.0-rc.3 (signed release candidate)". Download `FileCat-1.0.0-win-x64-setup.exe` and `FileCat-1.0.0-windows-assets.json`. Do not unblock the Setup in its Properties.
- **Expect:** No record covers a browser warning on this download (**Unverified**). If one appears, write down its wording, then keep the file.
- **Record:** Pass/Fail and any warning. **Closes:** d1.

<details><summary>Source</summary>.github/workflows/ci.yml:1384-1393 (the draft's name and assets); evidence/E-FQ-RC2-20261124.md:148 (SmartScreen on a browser download not covered)</details>

### G4 · Check the bytes and the signature
- **Do:** In PowerShell, in the folder the browser saved to:

```powershell
$m = Get-Content .\FileCat-1.0.0-windows-assets.json -Raw | ConvertFrom-Json
$m.CandidateRef; $m.Version; $m.SourceCommit
($m.Files | Where-Object Name -eq 'FileCat-1.0.0-win-x64-setup.exe').SHA256
(Get-FileHash .\FileCat-1.0.0-win-x64-setup.exe -Algorithm SHA256).Hash.ToLower()
(Get-FileHash .\FileCat-1.0.0-windows-assets.json -Algorithm SHA256).Hash.ToLower()
$s = Get-AuthenticodeSignature .\FileCat-1.0.0-win-x64-setup.exe
$s.Status; $s.SignerCertificate.Subject; $s.TimeStamperCertificate.Subject
Get-Content .\FileCat-1.0.0-win-x64-setup.exe -Stream Zone.Identifier
```
- **Expect:** `refs/tags/v1.0.0-rc.3` and `1.0.0`. The next two hashes are equal. The manifest's own hash equals the Windows manifest hash that the session gives you from the tagged run. `Valid`; the signer's subject names you (do not paste it); a timestamp authority's subject. `ZoneId=3`, the download mark.
- **Record:** Pass/Fail, the Setup hash's first 16 characters and SourceCommit's first 7. **Closes:** d1; REL-SIGN-WINDOWS on GA hardware.

<details><summary>Source</summary>eng/Select-ReleaseAssets.ps1:35-45, 58-83 (the manifest: names, sizes, SHA-256, candidate reference); ci.yml:1352-1355 (selected after signing), 1394-1403 (GitHub's server digests checked); evidence/E-RC2-TAG-20261124.md:52-55 (manifest hashes recorded per tag); FILECAT_1_0_RELEASE_ACTIVITY_LOG.md:5384 (timestamped Setup); FILECAT_1_0_RELEASE_BLOCKERS.md:37 (DEC-13); evidence/E-FQ-RC2-20261124.md:210 (ZoneId=3)</details>

### G5 · Install [Admin]
- **Do:** Double-click the Setup in the download folder. If SmartScreen appears (no record covers it: **Unverified**), write down its wording and the publisher it shows, then let Setup run if that publisher is you. Approve Windows' prompt. Keep the defaults: accept the license, `C:\Program Files\FileCat`, "Create a desktop shortcut" unchecked, Install, then Finish with "Start FileCat" checked.
- **Expect:** Windows' prompt shows you as publisher. Its program name should be "FileCat Setup", which is Inno Setup's default description; FileCat.iss sets no other (**Unverified**: not yet observed). The wizard's pages are Inno Setup's standard ones. FileCat starts.
- **Record:** Pass/Fail, a screenshot of the prompt, and SmartScreen's wording if any. **Closes:** d1 (clean install; Setup's engine loads under SAC).

<details><summary>Source</summary>eng/installer/FileCat.iss:21-32, 44, 60-61, 66, 69-74; Inno Setup help, VersionInfoDescription (default: the AppName followed by Setup); evidence/E-SIGN-DRYRUN-20261120.md:128 (SAC let Inno Setup's extracted engine run)</details>

### G6 · First start
- **Do:** Read the title. Help → About FileCat. Tools → Settings… → Privacy.
- **Expect:** The title ends "[you] (administrator, not elevated)", or "(standard user)": Setup starts FileCat with your ordinary rights. About reads `Version 1.0.0 (abcdef1)`, with SourceCommit's first 7 characters, on "Windows 11 (build …, x64)". Privacy shows "Automatically check for updates" on: a new profile asks GitHub once at its first start (I638). If a dialog offers a newer release, choose Not now and note it.
- **Record:** Pass/Fail. **Closes:** d1 (launch).

<details><summary>Source</summary>Inno Setup help, [Run] section (postinstall entries run with the original user's rights); src/FileCat.Core/Platform/ProcessAccount.cs:26-44; src/FileCat.App/Views/AboutDialog.cs:119-147; src/FileCat.App/Views/SettingsDialog.cs:347-348, 375; rc3 evidence/E-DEC-2026-11-24-contract-amendments.md:24-34 (I638)</details>

### G7 · Use FileCat so that its programs load
SAC must be On. Work in the ordinary window, in this order. Each line says which of FileCat's programs it loads.
1. **Icons** (the Shell helper, `FileCat.ShellHost.exe`): open a folder with `.txt`, `.jpg` and `.exe` files. Expect the icons Explorer shows.
2. **Pictures** (the picture worker, `FileCat.exe --picture-worker`): in your pictures folder, View → Show or hide pictures. Thumbnails appear; F3 on one shows it.
3. **PDF** (the picture worker with PDFium): F3 on your PDF shows "Page 1 of N" and the page.
4. **Web page** (WebView2): Ctrl+L, `C:\Program Files\FileCat`, F3 on `THIRD-PARTY-NOTICES.md`. The page is drawn.
5. **Archive** (the archive worker): with the cursor on your small folder, File → Archives → Pack into archive… (Alt+F5), choose 7z, Pack. Enter on the new `.7z` lists it; F3 on a file inside shows it.
6. **[Admin] The helper** (`FileCat.PrivilegedHost.exe`): Ctrl+L, `C:\Program Files`, F7, `FileCat-sac`, Create. In the error click Review denied items as administrator…, approve Windows' prompt, read "FileCat — administrator operation" and click Run as administrator. The folder appears.
7. **[Admin] The administrator window** (`FileCat.AdminLauncher.exe`): Tools → Open FileCat as administrator…, Open as administrator, approve. A window whose title ends "(elevated)" opens. In it, delete `C:\Program Files\FileCat-sac` (F8), then close it.
- **Expect:** every line works, with no SAC popup. Steps 6 and 7 show the texts that steps W3 to W5 and W2 of the consent walkthrough quote, with your account's name.
- **Record:** Pass/Fail per line, and any SAC popup. **Closes:** d1 (use, with the candidate's programs loaded under SAC).

<details><summary>Source</summary>eng/publish.ps1:113-125, 157, 189-191 (helper, Shell helper, notices, launcher in the payload); evidence/E-FQ-RC2-20261124.md:245 (icons come from the Shell helper), 217 (PDF pages), 243 (pictures); src/FileCat.App/Services/PictureWorker.cs:10-20; src/FileCat.App/Services/PictureDecoder.cs:243-248; src/FileCat.App/Controls/FileListControl.Pictures.cs:236 (thumbnails); src/FileCat.App/Views/ViewerWindow.cs:648-649, 722-723 (pages drawn, pictures and PDF pages decoded); src/FileCat.App/Controls/PageView.cs:497-500 (WebView2); src/FileCat.App/Views/ArchiveCreationDialog.cs:13-18, 29-35; src/FileCat.Platform.Windows/Shell/ArchiveAppContainer.cs:125-141; src/FileCat.Core/Commands/CommandRegistry.cs:325, 338, 376, 446, 486; src/FileCat.App/ViewModels/MainMenuModel.cs:17-19, 47, 53; evidence/E-SIGN-DRYRUN-20261120.md:152 (only startup was exercised before)</details>

### G8 · Optional [Admin]: a USB stick through the helper
This adds Windows' physical device identity and the installed recovery route (d7, d9). Close every other FileCat window first.
- **Do:** Insert the stick. Tools → Recover deleted files…: note the stick's entry "Disk N: [model]" and its size. Choose the stick's drive, click Scan, approve Windows' prompt, read the helper window, and click Run as administrator. Compare the model and size with Disk Management **[Admin]**.
- **Expect:** FileCat's question "Recover deleted files" says that Windows asks for approval and that the helper only reads the drive; its button is Scan. The helper window's heading starts "Read" and ends "to find deleted files". Its step reads `1. Read drive E: (\\?\Volume{…}) to find deleted files. The helper can only read it: nothing on it, or anywhere else, is written. It stops when FileCat ends the session.` (with your drive letter). The footer says "FileCat reads what it finds itself; this helper only hands it the drive's bytes, and has no way to write." The scan opens in a new tab. If FileCat refuses because other FileCat processes may write, wait a minute and ask again.
- **Record:** Pass/Fail, and whether model and size agree. **Closes:** d7 (device identity), d9.

<details><summary>Source</summary>src/FileCat.App/ViewModels/MainViewModel.Recovery.cs:68-89, 133-160, 357-394, 518-521; src/FileCat.Platform.Windows/Recovery/DeviceAccess.cs:605, 629; src/FileCat.Platform.Windows/Elevation/ElevationPlanCodec.cs:263, 270-277; src/FileCat.PrivilegedHost/Program.cs:120-122</details>

### G9 · Smart App Control's record [Admin]
- **Do:** Windows Security → Virus & threat protection → Protection history. Then, in an administrator PowerShell, with the hours set to reach back before G3:

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-CodeIntegrity/Operational'; StartTime = (Get-Date).AddHours(-3) } -ErrorAction SilentlyContinue |
  Where-Object Message -match 'FileCat' | Format-List TimeCreated, Id, Message
```
- **Expect:** No Protection history entry for FileCat, and no event naming a FileCat file. The signed pilot logged none either.
- **Record:** Pass/Fail and the number of events. **Closes:** d1 (SAC enforcing, nothing blocked).

<details><summary>Source</summary>Microsoft Support, Smart App Control has blocked part of this app (Protection history); evidence/E-SIGN-DRYRUN-20261120.md:135, 147</details>

### G10 · Remove [Admin]
- **Do:** Close every FileCat window. Settings → Apps → Installed apps, the entry "FileCat version 1.0.0", Uninstall; approve.
- **Expect:** Windows' prompt shows you as publisher: the uninstaller is signed by the Setup's signer. The uninstaller's own questions are Inno Setup's (**Unverified** wording).
- **Record:** Pass/Fail. **Closes:** d1 (removal).

<details><summary>Source</summary>evidence/E-FQ-RC2-20261124.md:135, 142-143</details>

### G11 · What must be gone, and what stays
- **Do:** In PowerShell:

```powershell
Test-Path 'C:\Program Files\FileCat'
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' | Where-Object DisplayName -like 'FileCat*' | Select-Object DisplayName
Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs", "$env:PUBLIC\Desktop" -Filter 'FileCat*.lnk' -Recurse -ErrorAction SilentlyContinue
Get-Process FileCat* -ErrorAction SilentlyContinue
Test-Path "$env:APPDATA\FileCat", "$env:LOCALAPPDATA\FileCat", 'HKLM:\SOFTWARE\FileCat', 'HKLM:\SOFTWARE\FileCat.AdministratorTransfers', 'C:\Program Files\FileCat-sac'
Get-ChildItem "$env:LOCALAPPDATA\Packages" -Filter '*filecat*' -ErrorAction SilentlyContinue
```
- **Must be gone:** the program folder, the uninstall entry, the Start menu shortcut and every FileCat process. The first four commands print `False` and nothing else.
- **Stays by design:** your settings, history and journals in `%APPDATA%\FileCat` and `%LOCALAPPDATA%\FileCat`. Delete them yourself if you want.
- **May stay, from the source, not yet observed:** `HKLM\SOFTWARE\FileCat\PrivilegedHost\UsedPlans`, because G7's helper ran and the uninstaller has no rule for it; `HKLM\SOFTWARE\FileCat.AdministratorTransfers`, only if you saved remembered administrator preferences; and an AppContainer profile named `FileCat.ArchiveWriter.v1` from G7's archive step, whose folder is not in the records. `C:\Program Files\FileCat-sac` is gone if you deleted it in G7.
- **Record:** the output. The session decides whether what stays is acceptable. **Closes:** d1 (removal).

<details><summary>Source</summary>eng/installer/FileCat.iss:69-71, 76-79; evidence/E-FQ-RC2-20261124.md:137, 143; src/FileCat.Platform.Windows/Elevation/ElevationBroker.cs:21; src/FileCat.Platform.Windows/Elevation/ElevationBroker.Nonce.cs:12-18; src/FileCat.Platform.Windows/AdministratorTransferRegistry.cs:16; src/FileCat.Platform.Windows/Shell/ArchiveAppContainer.cs:417; Microsoft Learn, CreateAppContainerProfile (a per-user profile with folders and registry storage)</details>

## Report back

Paste this table into the chat, filled in, with the build line from G1. Add the wording of every dialog marked Unverified.

| Step | Pass/Fail | Note |
|---|---|---|
| G1 PC noted | | |
| G2 SAC On | | |
| G3 Download | | |
| G4 Bytes and signature | | |
| G5 Install | | |
| G6 First start | | |
| G7 Use: icons, pictures, PDF, page, archive, helper, administrator window | | |
| G8 Optional USB stick | | |
| G9 SAC record | | |
| G10 Remove | | |
| G11 What is gone, what stays | | |

## Not covered here

- ARM64 (d2), which DEC-02 does not require.
- Proof that nothing was written to the USB stick in G8: the session's, by tool.
