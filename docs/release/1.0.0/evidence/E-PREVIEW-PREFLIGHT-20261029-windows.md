# E-PREVIEW-PREFLIGHT-20261029 — Windows ZIP and SBOM preflight

**Preliminary package evidence, not a release asset or candidate qualification.** On 2026-10-29, the clean `be40902eeea5eac6c1f3eb2e417e0cbe42dc265d` checkout (product head `da5e5734`) published version `1.0.0-preview.1` for `win-x64` and `win-arm64` into an isolated directory:

`V:\FileCat\artifacts\release-evidence\preview-preflight-be40902e-20261029-v1`

Both `eng/publish.ps1 -Version 1.0.0-preview.1 -Runtime <RID> -ArtifactDirectory <directory>` invocations exited zero. The raw logs, ten ZIP/SBOM files, `preflight.json`, and the Windows VM receipt are listed by `SHA256SUMS` (14 files; manifest SHA-256 `b68d1f236f6df404de58c23804bad866ac8898316ae320e4c9b1f2a7c6931cc3`). An independent readback matched every listed file hash. The generated SPDX 2.3 namespaces bind both runtimes and all three payload kinds to the full source commit and version.

Each archive's files were streamed and SHA-256 compared with its own SPDX file inventory, with no missing, extra, or duplicate entry: x64 and ARM64 portable each 323/323; x64 and ARM64 framework-dependent each 135/135. Both portable ZIPs contain `FileCat.portable`, `LICENSE`, and the dependency notice index; both framework-dependent ZIPs contain the license and index without the portable marker. Neither ZIP kind contains `FileCat.PrivilegedHost.*`, as required by DEC-04.

The exact x64 portable ZIP (`156f7782db085537a561efe24b77d8349cf8936eb62011114f854d9403d2978e`) was copied through VMware Tools into the disposable Windows 11 Pro Insider Preview x64 guest (build 26300). The guest rehashed it before extraction; `FileCat.exe --version` exited zero and printed `FileCat 1.0.0-preview.1`. The guest receipt is `win-x64-portable-vm-smoke.json`. The copied ZIP, extracted folder, and guest receipt were removed from the VM after its host copy was preserved. No host UI was operated.

This preflight did **not** build Windows installers or Linux/macOS packages, exercise a full GUI operation, establish a GA Windows hardware result, or qualify an installed/final candidate. The hosted tag package run and its exact produced bytes remain separate. FCSEC-056's RAR filter-work finding remains open at this source; the owner permits the tag as a preview packaging precursor, while final 1.0.0 must include its correction. Stable publication remains NO-GO.
