# WinGet distribution

The **WinGet manifests** workflow prepares `benny-cz.FileCat` for the community repository.
Run it manually with an exact stable tag such as `v1.0.0`; pull requests touching these
files test that baseline, and published non-prerelease releases test their own tag.
The release must be immutable and include `FileCat-VERSION-win-x64-setup.exe` with a
GitHub SHA-256 digest. Preview and ARM64 packages are not included.

The isolated `windows-2025` runner checks the download's size, digest and trusted
Authenticode signature, generates three schema-1.28.0 manifests, runs `winget validate`,
installs silently, records the actual Apps & Features metadata, and checks the installed
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
`verification.json`, including failures. Neither artifact contains the installer.
Submission, Microsoft's CLA if requested, and upstream review remain separate steps.
No publication token is required and no upstream pull request is created automatically.

The WinGet install command has a 180-second limit and verbose diagnostics. Before stopping
a timed-out process tree, the test captures relevant processes (arguments, CPU, session and
window titles), ARP and directory state, and the download's `Zone.Identifier`. It also keeps
WinGet's default diagnostic logs. No download-origin mark is changed or removed. Cleanup
uses the signed uninstaller if installation started, with a 60-second wait limit; a timeout
remains a failure even if cleanup succeeds.

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
