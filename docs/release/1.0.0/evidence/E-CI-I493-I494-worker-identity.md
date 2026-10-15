# E-CI-I493-I494 — hosted worker-identity qualification at current source

2026-10-15. Exact `main` source `8fe93efe84a53d6b677ffc4aa805105cfbe4521f`, [GitHub Actions run 38699373291](https://github.com/benny-cz/FileCat/actions/runs/38699373291): five required jobs succeed, four conditional jobs skip. This record extracts the I493/I494 rows from the already sealed complete run. It adds hosted evidence to the [native Windows elevated-session control](E-I493-worker-administrator-authority.md), [Ubuntu root control](E-I494-unix-worker-root-identity.md) and [Mac root and actual-worker control](E-I494-mac-root-worker-identity.md). It is neither an installed-app nor a release-candidate qualification.

The two official Windows artifact ZIPs contain thirteen passing Platform rows each:

| Lane and retained ZIP SHA-256 | TRX member SHA-256 | Exact relevant results |
|---|---|---|
| `test-results-windows.zip` — `05a1f476c60785dfbe48b031dfe9e29656efd8ce051e25fb28da6032982bc081` | `8e3b3413df8b8538355a4c0d46c0058281cc85e6c6917d774183438fd0fe834a` (`tests/FileCat.Platform.Windows.Tests/TestResults/results.trx`) | Four `RestrictedProcessLowTokenTests` passed, eight `RestrictedProcessFallbackTests` passed, one `SandboxedWorkerEnvironmentTests` passed; zero failed/skipped in these classes. |
| `test-results-windows-arm64.zip` — `7944fca944fd736239e19c7c74cbaa1cd05a32e71df145105ceabbfdeb3a44a4` | `9d4651c3680c2ed0bda87cd49b727a3631f226a4b6395634c75c0a3750a8db59` (`tests/FileCat.Platform.Windows.Tests/TestResults/platform-results.trx`) | The same four/eight/one results passed; zero failed/skipped in these classes. |

The four low-token tests call `RequireElevatedAdministrator` and would report `NotExecuted` unless the process is elevated. Their **Passed** outcomes therefore include the actual elevated-token guard, privileged-group/privilege assertions, Windows `AccessCheck` of an administrators-only file and a started windowless worker that reads an ordinary-user file but is refused the administrators-only file. The eight fallback rows cover refusal of an elevated administrator token lowered to medium integrity, three other lowered-integrity cases, unavailable/denied token handles and the actual parent-token policy. The environment row checks the picture worker does not inherit the parent's variables. These hosted controls complement the signed-in/elevated VM session results; they do not prove every domain-joined or Administrator Protection token shape.

The `UnixWorkerIdentityTests` Core rows retain their platform-specific outcomes in the same run:

| Lane | Core TRX SHA-256 | Ordinary Unix identity case | Windows token case |
|---|---|---|---|
| Windows x64 | `cce6839ea2389ce0c7050501dfee929f5f40302a3d4b765a3ef7ccada73cb335` | Explicit `NotExecuted` | Passed |
| Windows ARM64 | `0ff6784eb855c31022f10ec7e2491877f038e88abfc6ac644ea496d8f972d7e9` | Explicit `NotExecuted` | Passed |
| Ubuntu 24.04 | `1d03663b7f866b847efb57bc52ebe65fc789e8a56b2902a387fccf7d07cfa9b8` | Passed | Explicit `NotExecuted` |
| macOS 26 | `d10fe782f2f3e915604e2637c185b9ab1411397da7286951765667223359f173` | Passed | Explicit `NotExecuted` |

The hosted Unix rows exercise an ordinary user, **not root**. Root behavior is measured by the separate owned Ubuntu and Mac native controls above. Likewise, the Mac actual-worker ZIP selected its top-level publish files and omitted one Windows-only subdirectory DLL, as disclosed in its own record.

All four platform lanes are part of `V:\FileCat\artifacts\release-evidence\ci-i108-picture-corrected-20261015-v1`, which retains **27 GitHub server-digested ZIPs, 2,778 members and fourteen TRX** with **33,552 passed, 1,242 explicitly not executed, zero failed**. Its independently verified self-excluding `file-manifest.json` SHA-256 is `cf093f14558d41b88a07297e130db64799daf879f1b4bc76bb5642a6e4db5325`; `audit.json` SHA-256 is `919304df246c93e8fa395e2a30ae902f470000e19343084b4884490feb119349`. The focused rows above were parsed directly from those archived TRX members; artifact and member SHA-256 values allow independent re-extraction. The original I493 parent failure and separate native root/VM evidence remain at their own source identities. No candidate, contract freeze or stable GO follows.
