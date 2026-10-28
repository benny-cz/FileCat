# Release servicing

FileCat has no updater (plan §19.3, ADR-15), so servicing is an explicit maintainer process.

## Release artifacts

The Windows, Linux, and macOS packaging scripts produce the following. CI builds them on tags and signs them through SignPath once the Foundation project exists.

| Artifact | Contents | Receives .NET security fixes |
|---|---|---|
| `FileCat-<ver>-win-<arch>-setup.exe` | Per-machine installer (Inno Setup) of the self-contained ReadyToRun build | Only through a new FileCat release |
| `FileCat-<ver>-win-<arch>-portable.zip` | The same build plus a `FileCat.portable` marker; state lives in `Data/` next to the exe | Only through a new FileCat release |
| `FileCat-<ver>-win-<arch>-fdd.zip` | Framework-dependent build without the administrator helper (administrator retry is part of the installed FileCat) | Yes, through the installed .NET 10 Desktop Runtime |
| `FileCat-<ver>-linux-x64.tar.gz`, `.deb`, `.AppImage` | Self-contained Linux payloads in three package formats | Only through a new FileCat release |
| `FileCat-<ver>-osx-arm64.zip` | Ad-hoc-signed, non-notarized Apple silicon app bundle (preview) | Only through a new FileCat release |
| `sbom-<ver>-<rid>-<payload>.spdx.json` | SPDX 2.3 inventory of the exact unpacked payload files, reviewed NuGet dependencies, runtime pack, and source commit | – |

There is one SBOM for each distinct payload: setup, portable, and FDD on Windows; tar,
Debian, and AppImage on Linux; and the app bundle on macOS. The platform asset manifest
separately pins each outer archive or installer byte stream. An SBOM is composition
evidence, not a signature, notarization receipt, legal determination, or candidate approval.

## Security releases

- After each .NET Patch Tuesday that fixes something FileCat's runtime or libraries use, publish a rebuilt
  self-contained release within **14 days**.
- Fixes for FileCat's own vulnerabilities ([SECURITY.md](../SECURITY.md)) ship as soon as the fix is verified.
- Release notes name the fixed issue classes and say whether the framework-dependent build is affected.

## Checklist

1. `dotnet test FileCat.slnx` is green on Windows. The portable-test lanes on Linux and macOS are green.
2. `FileCat.exe --benchmark 1000000 --benchmark-panels 4` stays within the recorded TV-01 budgets.
3. Version bumped (`VersionPrefix` in `Directory.Build.props`). Tag `v<version>`.
4. CI builds the artifacts and signs them. Verify the signatures on a clean machine with Smart App Control enforcing (TV-13).
5. Publish the GitHub release and its notes. The opt-in update check reads `releases/latest`.

## How users upgrade

- **Installer:** run the new setup. It replaces the binaries; settings, workspaces, and journals are kept
  (they live in the user profile and are versioned).
- **Portable:** replace everything except `Data/`.
- **Newer state, older binaries:** an older build opens state written by a newer one read-only and says so.
