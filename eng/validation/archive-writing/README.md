# Archive worker qualification probes

These FileCat-owned probes exercise proposed native boundaries. They do not register an archive writer or qualify a
shipped package. Check for other testers before running; close and reap only this task's processes afterwards.

## Windows x64

Build `tests/FileCat.Platform.Windows.Tests` and `tests/FileCat.CoreCrashHarness` with the pinned SDK. Publish a dedicated
self-contained bundle (the lane adds a read/execute grant to this bundle, never to the global runtime or input trees):

```powershell
dotnet publish tests/FileCat.WorkerProbe/FileCat.WorkerProbe.csproj -c Release -r win-x64 --self-contained true -o .tmp/archive-worker-probe
eng/validation/archive-writing/run-windows-containment.ps1 -ProbeAssets <absolute-bundle-path> -VSTestConsole <absolute-sdk-path>/vstest.console.dll -EvidenceDirectory <absolute-owned-evidence-path>
```

Use the chosen installed `dotnet` host with the SDK's VSTest console. The harness lowers its own integrity before spawning
the actual test parent and records both levels plus runtime/PIDs/exit status. Integrity is measured rather than inferred
from the guest username or successful control login; ordinary-user token qualification remains separate. New evidence
prefixes are exclusive and unique. The nine archive
tests require the explicit opt-in; ordinary test runs skip them.

Asset provisioning also rejects a shared-runtime shape and linked asset trees before changing their ACLs.

The AppContainer requires the exact package identity, zero capabilities, no administrative groups/privileges, low
integrity, mandatory no-write-up policy and checked LPAC access behavior before resuming the suspended child. LPAC
verification uses supported `DuplicateTokenEx`/`AccessCheck` calls with exact-package positive and user-only/broad-package
negative controls. Win32 token query class 46 returned error 87 on the observed Windows 11 build and is not used as proof.
References: [AppContainer setup](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer),
[LPAC semantics](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-token_information_class),
[AccessCheck](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-accesscheck).

The probe covers private file reads/writes, five package-profile locations, registry creation, a live loopback listener,
child creation, a deliberately inheritable unrelated file handle, 64 MiB proxy copying and readback hashes, native commit
allocations, CPU termination, capped stderr, repeated disposal and an actual parent kill. The idle worker ignores stdin
EOF so parent-death success cannot be attributed to voluntary exit on a closed pipe. Native handle-query control succeeds
before the forbidden handle probe; Windows may terminate that deliberate negative probe with `STATUS_INVALID_HANDLE`.
LPAC refused Winsock startup with 10107 on the observed guest; this is recorded separately from socket/connect error 10013.

The real profile-write probe defeated a package-specific DACL deny. The adapter now protects the entire package profile
(including siblings of the API-returned `AC` directory) with medium no-write-up labels, explicitly replacing old child
labels. Unexpectedly large trees or reparse points refuse setup. See
[mandatory integrity](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control) and
[security-label propagation](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-setnamedsecurityinfow).
Installed asset ACLs, profile setup races, private registry/IPC namespaces, alternate network routes, real archive engines,
ARM64 and final package modes still require qualification. No production registration depends on these probes passing.

## macOS ARM64

The C probe is an ad-hoc signed App Sandbox experiment, not the release helper. It attempts its own executable read,
unrelated file read/write, a loopback connection, `fork` after `RLIMIT_NPROC=0`, and small/large anonymous mappings after an
attempted 1 GiB `RLIMIT_AS`. No large mapping is touched and both mappings are immediately unmapped. A successfully created
mapping above the intended allowance is a failed resource gate, even if disk/network denials work.

The observed Mac's matching compiler is `/Library/Developer/CommandLineTools/usr/bin/clang` (21.0.0) and its sysroot is
`/Library/Developer/CommandLineTools/SDKs/MacOSX27.0.sdk`. Use these explicitly rather than changing global `xcode-select`.
In an owned fixture, arrange `probe.app/Contents/MacOS/archive-probe`, copy `mac-probe-info.plist` as `Contents/Info.plist`,
compile the C source there, and sign with:

```sh
codesign --force --sign - --identifier io.filecat.archive-probe --entitlements mac-probe-entitlements.plist probe.app
```

Invoke the executable by its **absolute** path; App Sandbox changes the working directory. Supply absolute paths to an
owned existing sentinel and an absent output file outside the bundle/container. Verify the sentinel hash and absent
output after the probe. Preserve the JSON and signing/tool facts. The observed result admitted its own read and refused
unrelated read/write/network with EPERM and fork with EAGAIN. The absolute 1 GiB address-space limit returned EINVAL, while
both 64 MiB and 2 GiB mappings succeeded. This does not qualify a hard RSS/commit bound or prove that no supported design
exists. Signing, supported whole-process resource/lifetime controls, actual engine workloads and the release candidate
remain open gates. Do not substitute deprecated sandbox APIs or a managed-heap setting.
