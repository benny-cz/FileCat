# E-I15 — Installer uninstall into a folder that already holds user files

Issue I15 (plan DPI P15, V19-UNINSTALL). Classification: **preliminary** runtime evidence (development payload, Insider
VM); not final qualification of any release artifact.

## E-I15-S1 — static

- `eng/installer/FileCat.iss` at `4f6b062`: `[UninstallDelete] Type: filesandordirs; Name: "{app}"`.
- Inno Setup documentation, [UninstallDelete] section (read 2026-09-30): `filesandordirs` "matches directory names also,
  and any directories matching the name are deleted including all files and subdirectories in them"; it also warns
  against using a wildcard to delete all files in `{app}`.
- `AppPaths.Resolve` (`src/FileCat.Core/State/AppPaths.cs`): an installed build keeps state in `%APPDATA%\FileCat` and
  `%LOCALAPPDATA%\FileCat`; only a `FileCat.portable` marker moves state beside the executable.

## E-I15-V1 — runtime, both installer scripts, one payload

- **Environment:** VMware Workstation VM "Jamf / Windows 10 x64" (owner's VM, snapshot `filecat-before` taken before the
  run; to be reverted). Guest: Windows 11 Pro **Insider Preview** 10.0.26300.8068 (DisplayVersion 25H2), x64, local
  administrator session, UAC on with "elevate without prompting", Smart App Control off, Defender real-time on.
  Driven from the host with `vmrun` guest operations (no network used).
- **Inputs (SHA-256):**
  - Inno Setup 6.7.1 installer `4d11e8050b6185e0d49bd9e8cc661a7a59f44959a621d31d11033124c4e8a7b0` — release asset of
    jrsoftware/issrc `is-6_7_1` (10,619,024 bytes), Authenticode valid, signer "Pyrsys B.V." (Sectigo Public Code
    Signing CA R36). The same version the A01 CI image used.
  - Baseline script = `4f6b062:eng/installer/FileCat.iss` `4ac44658e110c08f20ae8b2ac4839c9c765b57f3ee4d7662d84f24de2e183f28`.
  - Fixed script = `5b061cc:eng/installer/FileCat.iss` `9137276529e5214db29d0511e6a98ffc483af1edb38ed073774c6f8192033787`.
  - Payload: `eng/publish.ps1 -Version 0.1.0-i15check -Runtime win-x64` from source `f87ad32` on the host (255 files,
    264,650,971 bytes), zipped as `ead6d65a49d518e07131bfeed61ca0b4fece61b2f93ae72807df0aaa3b516f24`.
  - Test script `4fb0708376dcb8dd7939ebce17d05a5500c39ba4d18db7bc67a463b9af5860cb`.
- **Built in the guest:** `ISCC /DAppVersion=0.1.0` of each script: base setup
  `CCEC7125484E39EA833E60FCD431F135709B78D3FECE74477C0672BC91892D11`, fixed setup
  `349CECE66A63B42F61DC77DACB2E9B6B4E6078AEC6C8B77728C040D957A6A95E`.
- **Procedure:** `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR=<folder>` install, then the folder's
  `unins000.exe /VERYSILENT …`, waiting until `unins000.exe` is gone. User files: `mytool.cmd` and `notes\todo.txt`
  before installing, `later.txt` added after installing.

| Scenario | Installer | Folder | After uninstall |
|---|---|---|---|
| A | baseline | `C:\Tools`, holding the user's files | **Folder gone. `mytool.cmd`, `notes\todo.txt` and `later.txt` deleted.** Uninstaller log: `Deleting directory: C:\Tools\notes` … `Deleting directory: C:\Tools` |
| B | fixed | `C:\Tools2`, same setup | Folder kept with exactly `later.txt`, `mytool.cmd`, `notes\todo.txt`; no FileCat file remains ("Removed all? Yes") |
| C | fixed | default `C:\Program Files\FileCat` (264 entries installed) | FileCat started from there and was still running after 25 s; it added **0** entries to its installation folder; after uninstall the folder is gone; `%APPDATA%\FileCat` and `%LOCALAPPDATA%\FileCat` kept (by design) |

All installer and uninstaller exit codes were 0.

- **Raw outputs (retained locally, not committed; SHA-256):** `log.txt` `f5234fc6bb98df25a2de49a435dc61efc22f1ee2f6c62d96f07e03e866c08571`,
  `uninstall-A.log` `529dcdf1e1efa527d03d25fe21eac1475ebefd346f2b72a9993788860c9cb012`,
  `uninstall-B.log` `45aac52473c3ca4f67130bc8eba03b6021f9f16c28e1df09f96b7937b16cfaed`,
  `uninstall-C.log` `a6f3c644614f5b77ebda2b6f2c1b8991d1a4ae1352e882f66ba831666d0a27a5`,
  `A-base-existing-folder.json` `e6226b53c7f1dd535c90bcedf18eafd3d7b43b38870a72cc3c7f59af5ca6f57d`,
  `B-fixed-existing-folder.json` `322867ee322f567a0fdd72a00e6d3c2c5a14c47d294f4615b828d03092344529`,
  `C-fixed-default-folder.json` `d75c957cbf195bf0031dff9bbcca304ebca398074474c30a301fd22d32045152`.
- **Result:** defect reproduced with the baseline script; fixed script keeps the user's files and still removes its own
  folder when nothing else is in it.
- **Limitations:** Insider guest build; development payload, not a candidate; silent mode only (the interactive
  folder page and its "folder exists" warning were not exercised); upgrade/reinstall and x64→ARM64 crossgrade not yet
  run (V19). Final V19 lifecycle evidence must come from the final signed setup on a GA Windows 11 machine.
- **Side observation (for I03/V19/V20):** the win-x64 payload includes `runtimes\win-x86\native\WebView2Loader.dll`
  and `runtimes\win-arm64\native\WebView2Loader.dll`, foreign-architecture native files that the plan requires to be
  justified or safely removed.
