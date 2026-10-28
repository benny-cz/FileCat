# E-I552 — Linux picture-worker filesystem and network boundary

## Identity and finding

- Baseline: `5e4033770bcb040943fde70ff5fbf01d26ce4489`.
- Accepted implementation: `559a7572398089ab15ebc835990fef59ab615bfc`.
- Native x64 evidence: `V:\FileCat\artifacts\release-evidence\i552-linux-picture-sandbox-20261028-v1`; 37-entry manifest SHA-256 `abda52d309752f6e851dcd141a844d2d5b30e1a3511d0c8d3691c7c564ca04a6`.
- Native ARM64 evidence: `V:\FileCat\artifacts\release-evidence\i552-linux-arm64-picture-domain-20261028-v1`; 20-entry manifest SHA-256 `46c24aa5de78d36b0ecf48f04902710c9a43bf0cb0cb6653ae7279ea0cd82749`.
- Native environments: Ubuntu 24.04.5 x64, kernel 7.0.0-38-generic, .NET 10.0.14; and temporary Ubuntu 26.04.1 ARM64, kernel 7.0.0-34, .NET SDK 10.0.101/runtime 10.0.1, Landlock ABI 8 and fontconfig 2.17.1.

The previous Linux picture worker dropped retained root identity and blocked direct process creation, but it entered the worker runtime with the ordinary user's filesystem and network authority. This was an uncovered mandatory I08 boundary, not a regression of the already accepted I549 process-creation case.

## Correction

FileCat now enters a trusted `--picture-sandbox` launcher before hostile picture bytes are consumed. The launcher:

- applies a Landlock read allowlist for the exact application/runtime and required system/font roots;
- grants no filesystem writes and refuses launch when Landlock cannot be established;
- installs an architecture-specific seccomp network/socket and `io_uring` denial for x64 and ARM64; and
- replaces itself with the normal worker using `execv`, so every worker-runtime thread inherits the pre-exec boundary.

The worker then applies the existing thread-synchronized process-creation filter. Framework-dependent, self-contained and extracted-runtime roots are resolved explicitly; attacker-controlled loader environment is not copied into the worker.

## Accepted results

- Native Ubuntu focused containment: **12 passed / 0 failed / 0 skipped**. The live pre-input worker has no-new-privileges plus both filters on every observed runtime thread.
- The native probe cannot read an unlisted owned secret, cannot create an owned file and cannot create a socket, while it can read the admitted application assembly; the secret remains byte-identical and no output file exists.
- Native Ubuntu affected picture slice: **99 passed / 0 failed / 3 explicit non-Linux skips**.
- A self-contained linux-x64 publish invokes the real pre-exec route on the tracked FileCat PNG and emits a valid 16,419-byte `FCPX` result, SHA-256 `f10f3874fe841bb95c5956c346066856d743e2bd68f502f4edd18c7aca359fbf`, with empty stderr.
- Native ARM64 focused containment: **12 passed / 0 failed / 0 skipped**; affected picture slice: **99 passed / 0 failed / 3 explicit skips**. The self-contained linux-arm64 worker emits a valid `FCPX` result, SHA-256 `d00395e82191887bca06d84f10c5af1089af386e850d21e5a496e6bedd7ab086`, for input SHA-256 `6025669ab8d5d4a33ab5853e5c6bf7882d954f8f683074cc79e40a2f5db8d5aa`.
- Windows structural coverage: **7 passed / 5 explicit Linux-native skips**; complete Release solution build: zero warnings/errors.
- Complete Windows Core regression: **4,453 passed / 152 explicit skips / 0 failed**.
- Complete Windows Remote regression: **2,479 passed / 168 explicit skips / 0 failed**; complete Windows App regression: **2,563 passed / 69 explicit skips / 0 failed**.

The initial observer sampled the trusted launcher/worker handoff after only one filter, and a later framework-dependent run supplied the runtime directory rather than the dotnet root. Both raw failures are retained; the final observer waits for the inherited network filter plus the worker process filter, and final runtime-root selection passes.

## Disposition

`I552-LINUX-PICTURE-DOMAIN` is **Accepted** for preliminary native Linux x64 and ARM64 picture-worker filesystem/network containment. The temporary ARM64 Lima VM, image cache, Lima installation, validation awake process and Mac stages were removed after evidence collection. `REL-I08-CONTAINMENT` remains Open for whole-process memory, other parser/Shell routes and installed-candidate qualification. Hosted and frozen-candidate qualification remain separate.
