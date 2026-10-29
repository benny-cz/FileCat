# E-PREVIEW2-PREFLIGHT — corrected-source Mac test and Windows package preflight

Recorded 2026-10-29. The source is pushed commit
`9a2d45ac66b2990e8378ca2104e777708f35af7f`. This is a **local
preflight**, not an official tagged package set, draft release, candidate or
publication. Raw evidence is under
`V:\FileCat\artifacts\release-evidence\preview2-preflight-9a2d45ac-20261029`.
Its 13-file `SHA256SUMS.txt` has SHA-256
`de6cb649a330721768477f58ef4c6f2996c4fc0c5c67cdf31a89362bbee68722`.

## CI failure and bounded correction

[Run 40954472606](https://github.com/benny-cz/FileCat/actions/runs/40954472606)
at `f32fef2f` failed one macOS Core test after 4,887 passes and 138 skips:
`HtmlPageTests.Local_markdown_links_open_only_contained_markdown_files_in_another_viewer`.
The failed `Assert.Equal` compared a temp path beginning `/var/folders/` with
the same file's canonical path beginning `/private/var/folders/`. The product
resolver intentionally returns the canonical path. The raw job log is retained
as `ci-40954472606-macos-failure.log`; this failure is not counted as a product
regression or a passing CI run.

Commit `9a2d45ac` makes the test check the expected `docs/more/next file.md`
suffix and the target's exact file contents. Its traversal, wrong-type,
external-URL, missing-file and symlink refusal assertions remain. The focused
Windows Core test passed 1/1. A fresh isolated Mac clone of the exact pushed
commit passed the previously failing test 1/1, 0 skipped, 0 failed. The Mac
has SDK 10.0.403 rather than the repository's pinned 10.0.101, so this
supplemental run adjusted only that disposable clone's `global.json` and
SharpCompress lock file to resolve its installed SDK. The native TRX, log,
exit status and source/SDK status are retained under `mac-test/`; the clone
was removed. The exact locked hosted run at `9a2d45ac` remains required for
GATE-CI-FINAL.

## Exact-source Windows package preflight

`eng/publish.ps1 -Version 1.0.0-preview.2 -Runtime win-x64` completed with
exit 0 from clean tracked source `9a2d45ac`. NuGet's vulnerability service
was temporarily unavailable (`NU1900`), but compilation, package writing and
three exact SPDX documents completed. Both extracted ZIPs returned
`FileCat 1.0.0-preview.2` and exit 0 from `FileCat.exe --version`. The portable
ZIP carries `FileCat.portable`; the framework-dependent ZIP does not. Neither
ZIP carries the privileged host, as intended for ZIP distributions.

| Local output | Bytes | SHA-256 |
|---|---:|---|
| `FileCat-1.0.0-preview.2-win-x64-fdd.zip` | 65,447,058 | `7155afa35894f48e57223c800cba95826d55dec03be19f21d3071a26bc9f67b3` |
| `FileCat-1.0.0-preview.2-win-x64-portable.zip` | 102,327,824 | `3bf831fb731f496272a8f08cc738a701a6f3480cc4c9a523cfa28cbdb6e89d76` |
| `sbom-1.0.0-preview.2-win-x64-fdd.spdx.json` | 144,309 | `25855ae56445a23507270e828da2fedb839d6fd03b42a1e4ab6c5c2bddb7bbf0` |
| `sbom-1.0.0-preview.2-win-x64-portable.spdx.json` | 268,562 | `8675e0c3ebfe358328c9ea010a728ccac72e6ebeff91cb72a3bbd9c2b6ecee4b` |
| `sbom-1.0.0-preview.2-win-x64-setup-payload.spdx.json` | 273,193 | `8206acb7af29ddcc44140e11183cb82f59077af6ff37d9268a4d8a222ca525d3` |

The SBOM namespace and package `sourceInfo` identify the full `9a2d45ac`
commit. The ZIPs have 178 and 366 members respectively. Raw publish and
version-smoke logs plus final ZIP/SBOM bytes are retained. Intermediate
publish trees and the superseded `c4d8e886` preflight outputs were removed
after retaining its CI failure log.

## Remaining stopping conditions

GATE-CI-FINAL stays Open until every required hosted lane passes at the
latest product source with skips reconciled. I562/REL-PIPELINE stays Failed
until a separately owner-approved new immutable tag completes protected draft
preflight, private upload and post-upload digest verification. This Windows
local preflight does not cover the installer, ARM64, Linux, macOS or downloaded
asset behavior. The earlier official `preview.1` packages retain their own
native evidence, but predate AC-59 and I563. Stable publication remains NO-GO.
