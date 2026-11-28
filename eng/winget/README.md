# WinGet distribution

The **WinGet manifests** workflow prepares `benny-cz.FileCat` for the community repository.
Run it manually with an exact stable tag such as `v1.0.0`; pull requests touching these
files test that baseline, and published non-prerelease releases test their own tag.
The release must be immutable and include `FileCat-VERSION-win-x64-setup.exe` with a
GitHub SHA-256 digest. Preview and ARM64 packages are not included.

Choose an explicit installation method; there is no automatic fallback:

- **DirectInstaller** (default for PRs, releases and manual runs) invokes the downloaded,
  hash- and signature-verified official installer directly with standard Inno silent
  switches. It independently verifies the installer while WinGet validates the manifests.
- **WinGetLocal** explicitly runs `winget install --manifest` and preserves its diagnostic
  evidence. A failure in this mode fails the run and never switches to DirectInstaller.

The isolated `windows-2025` runner checks the download's size, digest and trusted
Authenticode signature, generates three schema-1.28.0 manifests, runs `winget validate`,
installs with the selected method, records the actual Apps & Features metadata, and checks the installed
`FileCat.exe` and uninstaller against the installer's signing certificate. It then
regenerates the manifest with the observed display name, validates it and uninstalls,
requiring both the registry entry and installation directory to disappear. It does not
launch FileCat or use speculative GUI command-line options. Existing installations
are refused. This is an installer test on **Windows Server 2025**, not Windows 11 GUI
qualification or a test of upgrades from earlier versions.

If the runner's WinGet is missing or too old, the workflow installs the official stable
App Installer bundle and its x64 dependencies directly for the runner user. Their
digests are checked against the same GitHub release; the release and hashes are recorded.

Successful runs upload `winget-manifests`; copy its version folder into
`manifests/b/benny-cz/FileCat/VERSION/` in `microsoft/winget-pkgs`. The separate
`winget-verification` artifact contains command logs, installation logs and
`verification.json`, including failures and the explicit `InstallMethod`.
In DirectInstaller mode, `WinGetLocalInstall` is `NotRun`; a successful run establishes
manifest validation and the direct installer round trip, without claiming a successful
WinGet local-manifest installation. Neither artifact contains the installer.
Submission, Microsoft's CLA if requested, and upstream review remain separate steps.
No publication token is required and no upstream pull request is created automatically.

Both installation methods have a 180-second limit; WinGetLocal enables verbose logs. Before stopping
a timed-out process tree, the test captures relevant processes (arguments, CPU, session and
window titles), ARP and directory state, and the download's `Zone.Identifier`. It also keeps
WinGet's default diagnostic logs. No download-origin mark is changed or removed. Cleanup
uses the signed uninstaller if installation started, with a 60-second wait limit; a timeout
remains a failure even if cleanup succeeds.

### Known headless local-install limitation

[Run 46089308004](https://github.com/benny-cz/FileCat/actions/runs/46089308004) verified
the release hash and signature and passed `winget validate` with WinGet 1.29.380.
Its local-manifest install timed out before Inno started: the diagnostic snapshot recorded
a WinGet window titled `Window Dialog`, a newly launched `CHXSmartScreen.exe`, and
`ZoneId=3` on WinGet's downloaded copy; no FileCat installation directory or ARP entry
was created. This is consistent with the unattended local-manifest warning described in
[microsoft/winget-cli#6362](https://github.com/microsoft/winget-cli/issues/6362).
The two test methods preserve this distinction without changing MOTW, SmartScreen,
trust settings or downgrading WinGet.

When submitting a manifest supported by a successful DirectInstaller run, describe the
exact validation and direct silent install/uninstall evidence, link the local-install
limitation, and leave the upstream **tested with `winget install --manifest`** checkbox
unchecked. The upstream repository's installation validation and review remain required.

GitHub does not start another workflow for a release event created using a repository's
`GITHUB_TOKEN`. If the release pipeline uses that token, run this workflow manually for
the exact published tag. The release pipeline is unchanged.

For metadata-only work, `New-FileCatManifest.ps1` can generate files on any platform from
an explicit version and known SHA-256; these files are not installation-verified.
`Test-FileCatManifest.ps1` refuses to install outside a GitHub Actions Windows runner.

References: [first contribution](https://github.com/microsoft/winget-pkgs/blob/master/doc/FirstContribution.md),
[manifest schema](https://github.com/microsoft/winget-pkgs/blob/master/doc/manifest/schema/1.28.0/README.md),
[WinGet installation](https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/troubleshoot-apps-start-failure-use-windows-package-manager),
[workflow triggering](https://docs.github.com/en/actions/how-tos/writing-workflows/choosing-when-your-workflow-runs/triggering-a-workflow).
