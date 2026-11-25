# Release servicing

FileCat has no updater (plan §19.3, ADR-15), so servicing is an explicit maintainer process.

## Release artifacts

The Windows, Linux, and macOS packaging scripts produce the following. CI builds them on tags. SignPath Foundation declined FileCat on 2026-10-31, and the maintainer chose SSL.com's eSigner instead (DEC-12, [code signing policy](DOWNLOAD.md#code-signing-policy)). For a release candidate, a CI job in the protected `signing` environment signs every program and library in the Windows payloads that its vendor did not sign, FileCat's own included, and both Setups and their uninstallers; it runs only after the maintainer approves it. The macOS app is ad-hoc signed and not notarized, and the Linux packages are unsigned. In 1.0.0, Windows x64 is the stable tier; the Windows ARM64, Linux and macOS packages are labelled previews (DEC-02).

| Artifact | Contents | Receives .NET security fixes |
|---|---|---|
| `FileCat-<ver>-win-<arch>-setup.exe` | Per-machine installer (Inno Setup) of the self-contained ReadyToRun build | Only through a new FileCat release |
| `FileCat-<ver>-win-<arch>-portable.zip` | The same build without the administrator helper, plus a `FileCat.portable` marker; state lives in `Data/` next to the exe | Only through a new FileCat release |
| `FileCat-<ver>-win-<arch>-fdd.zip` | Framework-dependent build without the administrator helper (the helper's per-plan retry is part of the installed FileCat) | Yes, through the installed .NET 10 Runtime (`Microsoft.NETCore.App`) |
| `FileCat-<ver>-linux-x64.tar.gz`, `.deb`, `.AppImage` | Self-contained Linux payloads in three package formats | Only through a new FileCat release |
| `FileCat-<ver>-linux-x64-appimage-runtime-source.tar.gz` | The AppImage runtime's corresponding source (type2-runtime, libfuse, squashfuse) with a script that rebuilds the shipped runtime and relinks it against a changed libfuse (LGPL-2.1, EXT-04) | – |
| `FileCat-<ver>-osx-arm64.zip` | Ad-hoc-signed, non-notarized Apple silicon app bundle (preview) | Only through a new FileCat release |
| `sbom-<ver>-<rid>-<payload>.spdx.json` | SPDX 2.3 inventory of the exact unpacked payload files, reviewed NuGet dependencies, runtime pack, and source commit | – |

There is one SBOM for each distinct payload: setup, portable, and FDD on Windows; tar,
Debian, and AppImage on Linux; and the app bundle on macOS. The platform asset manifest
separately pins each outer archive or installer byte stream. An SBOM is composition
evidence, not a signature, notarization receipt, legal determination, or candidate approval.

**Administrator windows.** Every Windows package can reopen FileCat as administrator after Windows' approval
(Tools → Open FileCat as administrator, and the administrator reviews built on it). Each package carries
`FileCat.AdminLauncher.exe` beside `FileCat.exe`: every administrator window starts through it, and it removes the
.NET runtime's environment settings first (I644); without it FileCat opens no administrator window. Only the installer
includes the administrator helper, which retries one displayed plan. A portable or framework-dependent copy opened as
administrator runs the program files in its own folder, which the ordinary account can usually change. Users of such a
copy should keep it in a folder only administrators can change, such as one under Program Files, or use the installer
([README](../README.md#work-with-visible-outcomes), [security policy](../SECURITY.md#administrator-windows-from-portable-copies)).

## Security releases

- After each .NET Patch Tuesday that fixes something FileCat's runtime or libraries use, publish a rebuilt
  self-contained release within **14 days**.
- Fixes for FileCat's own vulnerabilities ([SECURITY.md](../SECURITY.md)) ship as soon as the fix is verified.
- Release notes name the fixed issue classes and say whether the framework-dependent build is affected.

## Checklist

1. `dotnet test FileCat.slnx` is green on Windows. The portable-test lanes on Linux and macOS are green.
2. Run `FileCat.exe --benchmark 1000000 --benchmark-panels 4` and record its results beside [TV-01's](validation/TV-01.md). Performance is measured, not held to frozen numeric targets (DEC-14).
3. Tag the release candidate `v<version>-rc.N` on the release source. CI sets the version from the tag, not from `VersionPrefix` in `Directory.Build.props`, which only names local builds: a `v1.0.0-rc.N` tag builds packages that call themselves FileCat 1.0.0 (I634). The workflow maps only `v1.0.0-rc.N` tags so far; a later version needs that mapping extended first.
4. CI builds the artifacts; once the maintainer approves the `signing` run, it signs the Windows ones and drafts the candidate privately. Verify the signatures on a clean machine with Smart App Control enforcing (TV-13).
5. After the GO, add `v<version>` at the candidate's source; a stable tag builds and signs nothing. Publish the qualified candidate's packages unchanged as the GitHub release, with its notes ([readiness plan §13.1](design/FILECAT_1_0_RELEASE_READINESS_AND_VALIDATION_PLAN.md#131-after-explicit-go)). The startup update check is on for new profiles unless disabled in Privacy; stable builds read `releases/latest` and preview builds consider published prereleases.

## How users upgrade

- **Installer:** run the new setup. It replaces the binaries; settings, workspaces, and journals are kept
  (they live in the user profile and are versioned).
- **Portable:** replace everything except `Data/`.
- **Newer state, older binaries:** an older build opens state written by a newer one read-only and says so.
