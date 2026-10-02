# E-I99 — Unix instance sockets under long temporary paths

Preliminary native execution on 2026-10-02, Ubuntu 26.04.1 GNOME Wayland/XWayland. No release candidate.

## Failure and remedy

The full App suite at `cc97a8d39eef5a3b20d58b7158efc7bdf1e6d462` fails two RecoverySafetyTests with
ArgumentOutOfRangeException: a valid 73-byte TMPDIR plus .NET's CoreFxPipe_ prefix and FileCat's instance name
exceeds Linux's Unix socket address limit. TRX `160e8757b884d918fdd37eb5ff063106829e2fb45c711220cba643245af186b7`
records 230 total / 202 passed / 2 failed / 26 skipped. Requirements: A-07, V23 B12 and V09's recovery write guard.
Severity Medium; must fix for the Unix launch contract.

SingleInstance retains the established relative endpoint where it fits. Otherwise it uses an absolute name without
.NET's prefix; paths too long even then use /tmp/filecat-<uid>, created owner-only. The UTF-8 byte count includes room
for the terminating NUL and the different Linux/macOS limits. Existing linked, foreign-owned or permissive fallback
directories are rejected without changing or removing them. The recovery write guard includes that extra directory,
including when its disk cannot be identified. Windows' endpoint behavior is unchanged.

## Hash-bound working inputs and execution

Native test build starts from exact `cc97a8d` source archive
`6b1675ec8c15770de3b0fc6f6a762e5606b120e6228bb6616157e831ea526dfb`, with only these LF-normalized source replacements:

| File | SHA-256 |
|---|---|
| src/FileCat.App/SingleInstance.cs | `2ad90bb01fd36ecc4cbb2e9c85afe475bf8927c5f75c37e9c3134e4de272490b` |
| src/FileCat.App/ViewModels/MainViewModel.Recovery.cs | `3003e30d56ba5c4ce8797d458e4bb0bf4071e931df341a94673474fcfef41502` |
| tests/FileCat.App.Tests/RecoverySafetyTests.cs | `6da5d954a0204cfea4d7baff780f260940a561e4b9a1c9fedfb6b505377b4801` |

Private SDK 10.0.401 / runtime 10.0.12; build zero errors, nine warnings. Native FileCat.dll SHA-256
`db5f8e0236ce972991ab055db7dda5885190efd09383349b9824980f4ecdf566`; test DLL
`96179cee8b9c4b79988d1e5c17bfa06ff90586403db1873366343424a1f500d6`. This working build is distinct from CI packages.
An attempted patch upload did not apply because of line endings; original source remained intact. Exact normalized
inputs were then copied to a separate owned source tree. Both attempts are retained.

The new eng/validation/validate-unix-instance.py runs fresh direct xUnit processes under boundary, long ASCII and
Unicode TMPDIRs (73, 197 and 160 bytes including NUL). Four existing/new instance and recovery-safety tests per case:
11 passes and one expected boundary skip; all three scenarios pass. The skipped test requires the extra fallback
directory, which the boundary case does not need. Oracle SHA-256
`b722c4c12e3265913c23ad540f3a64c9b54e381797d4d6b889d3cb6617effb59`; harness
`456946c355e9625ee3ec8bfe1255539d2151ed04216ec2ecd3197b697392befd`. CI runs the harness on Linux and macOS and
retains logs, XML and fixtures. Windows targeted recovery tests: four total, three passed, one platform skip.

Affected full native App rerun: **231 total / 204 passed / 0 failed / 27 skipped**, TRX
`7afee55795f8f8183d24b8b5ca55c9294b4bbd7c71f70eb72df8fac4203542a2`. Actual WebKitGTK branches required and pass;
complete skip inventory retained. Unchanged Core/Remote native results remain separate (E-X02).

## Real GUI forwarding and limits

An actual GUI starts under a 198-byte TMPDIR, listens in the benny-owned 0700 fallback directory, then accepts the
second launch's target folder in the same Unix session. Forwarder exits zero; one window retains the server PID;
source and target tabs appear in the framebuffer and persist in parsed workspace JSON after graceful close.
Forwarding record `1d7ce19995c5de9cc8ff26cdf40df1ff66a43cd0c3aa4f4b3099ae5d58715636`, state oracle
`b1f0ed8d7e64fa5c380a2c7597cc654f764aa014846db3edccc9c54a9cc35a70`, screenshot
`868e2de5bccc78be37a17eb14fd824b65abeec07e3a3ebea53a93d728eb0191c`. The socket itself has mode 0775 under the
0700 parent; do not describe it as 0600. CurrentUserOnly pipe enforcement remains enabled.

The preceding launch from another SSH Unix session times out rather than forwarding; tracing proves distinct
session-local mutexes and replacement of the original socket. This is independently tracked as **I100**, not a
passing I99 case (E-I100). Failed launches and traces are retained.

Raw host root: artifacts/release-evidence/linux-os-matrix-20261002/26.04/. Earlier package/native source evidence for
instance startup and recovery write locations is invalidated by this remedy. CI/macOS execution, rebuilt package
checks on both fresh Linux baselines, re-audit and final-candidate qualification remain pending; issue is not Closed.
