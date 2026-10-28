# E-I545 — enforced and observed Linux WebKit page sandbox

**Preliminary I08/V10 containment child accepted at `c0e9cb5a13b174fdaae7da9b1b2091d4282de5ca`; exact pushed-source CI, wider containment and candidate qualification remain open.**

## Finding and correction

The Linux page engine created an ephemeral WebKitGTK context, disabled scripts and active features, served only FileCat's private scheme and sent all other requests to a refusing proxy. It did not query or enable WebKit's native process sandbox. V10 explicitly requires the native sandbox state to be recorded; a child process and network refusal alone are not containment evidence.

The correction resolves `webkit_web_context_get_sandbox_enabled` and `webkit_web_context_set_sandbox_enabled` from the loaded WebKitGTK library. Before constructing a web view or loading any page, it enables the sandbox when necessary, reads the state back and refuses the page engine unless the result is enabled. A required native test asserts the accepted state before loading hostile HTML. Unsupported WebKit builds now fail closed with a specific explanation instead of silently using an unverified process boundary.

## Exact validation

Raw evidence is retained at `V:\FileCat\artifacts\release-evidence\i545-linux-webkit-sandbox-20261028-v1`. The exact two-file patch over clean base `bc9759d1cb8def2cdcb1b0d2fdb284d58271cf64` has SHA-256 `7baa6e09542808768f42c948c06b0d113d7281cc86355be5b9cd570374568223`; that overlay is byte-identical to commit `c0e9cb5a`.

- Windows x64 compiles the native bindings and runs the affected class **2 passed / 3 explicit Linux skips / 0 failed**. TRX SHA-256: `b9a045417e4d36649cd4e0ff5391a6e67632148f1745a9dfc2d258e7a5307ccf`.
- The complete Release solution build at committed source exits zero with **0 warnings / 0 errors**. File-logger SHA-256: `e1d20bd65deabc669d03fc4a51c6d0c21aa5fb72cf1be3baf01b7a94afcfb15d`.
- Ubuntu 26.04.1 uses .NET SDK 10.0.101, WebKitGTK `2.52.6-0ubuntu0.26.04.1` and bubblewrap `0.11.1-1ubuntu0.3`. All five `LinuxPageEngineTests` run unskipped and pass in the signed-in Xwayland session. TRX SHA-256: `9dc96536fe65eff78e96afdbbd22a940e4cc81eefe83a76dc216231aebbeab70`.
- A second bounded five-case run passes and records the live process chain. TRX SHA-256: `efdf669abaf7d74cfc71c703ce7e66f96f9cf77be18fc9dfb30080457b861af6`; process observation SHA-256: `79d0c1bf67ae12937cc165002f3a56714e81f0ae89789399b0c5e9d9e5d785c1`.

The OS observation sees `/usr/bin/bwrap` start the actual `WebKitWebProcess`. The inner bubblewrap and WebKit processes report ordinary UID/GID 1000, `NoNewPrivs: 1`, seccomp mode 2 with one filter, a two-level `NSpid`, a one-ID user mapping, and user/PID/mount/network namespace identities distinct from the test host. The separate WebKit network process remains outside that page-process namespace; FileCat's existing refusing proxy and request-policy controls continue to cover network requests.

## Retained setup failures and boundary

The first guest attempt exited 127 before build because the VMware command environment did not include the guest's pinned SDK and Xvfb is not installed; its empty SDK output and setup-location record are retained and are not product results. A first OS-observation attempt passed the product test **1/1** but captured no short-lived WebKit PID; it remains a failed observation setup. The five-case repeat supplies the required process evidence.

This accepts only the current Linux WebKit page-process sandbox boundary. It does not establish a filesystem/network sandbox for the Unix picture worker, contain detached Mac picture descendants, qualify Windows Shell/context handlers, freeze a candidate or authorize publication. Those scopes remain in REL-I08-CONTAINMENT, REL-FQ-CANDIDATE and REL-GO-PUBLISH.
