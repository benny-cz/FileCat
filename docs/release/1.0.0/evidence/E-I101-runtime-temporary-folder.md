# E-I101 — Recovery omits the Unix runtime temporary folder

Reproduced 2026-10-02 on fresh Ubuntu 26.04.1 with .NET 10.0.12, after I100. No candidate.
CheckDiskSafety includes application state and an IPC fallback, but omits the configured temporary folder when
the socket fits there. Runtime endpoints use that folder. A running usual instance can also have a different
temporary folder from this process, and its socket fallback alone does not identify that location.

The new regression maps only the runtime temporary folder to the target disk and expects refusal. Baseline
fails 1/1: Refusal is null. This is a guard reproduction with controlled disk mapping; no unsafe physical-device
scan or deleted-data overwrite was performed. Potential Critical, must fix, V09/I09. Status Open at discovery.

Private guest records under the owned e7fac2cb613344ebb26a0a2b75777a25 stage:

| Record | SHA-256 |
|---|---|
| i101-before-test.cs, LF-normalized regression input | `660e2a1a98d0facefbf2d73be41224b330683e3d81d504f2850f86369dbdd678` |
| i101-before-r1/before.trx | `53d2aea5d04c378788787a0b69d02ec0e21d83340a94d8f45e6f50911e31861b` |
| Baseline working FileCat.dll | `63298a51c38a74a33394e6342bbc604992f0b87742a01654f75fb14cdcc4e983` |

Source is the retained I100 native R2 working tree plus the recorded regression. Baseline build/run logs retained.
Full runtime write tracing, rebuilt packages, macOS and exact candidate validation remain separate requirements.

## Remedy and affected validation

The Unix scan guard always checks Path.GetTempPath(), and the owner publishes bounded JSON containing both its
actual socket and runtime temporary folder. A data-root instance checks those actual locations for the running
usual instance, even if it uses another TMPDIR and a socket fallback. Missing, malformed, relative or oversized
metadata conservatively refuses recovery. Metadata remains owner-only and atomically replaced; no Unix mutex returns.
The bounded reader consumes at most 32769 bytes, accounting for a file changed while it is read.

Native R2 source is the retained baseline with these LF-normalized inputs:

| Input | SHA-256 |
|---|---|
| SingleInstance.cs | `d2bc66fc68ee336ac98ae7dcd3fe40a551064922e85b046b3f0951493b2ea513` |
| MainViewModel.Recovery.cs | `67390a2080a0d7e71e61948b318b8111b7dce5f6cfd5ad23d8677580c5fa1e63` |
| RecoverySafetyTests.cs | `00986c44801552a56a24ee043c8e2d00ee3f873c6ae6444c40729deb50d7781e` |
| Boundary harness | `6bac5b3e7a5ae9f36d8a45cbf3c52f334bb229e5c587284c87ab6d32f995824e` |
| Native FileCat.dll | `c9670cc926430f2e52f4f3be16dc8ec063acaf7cf7cb6667195c6d6dd16c435b` |
| Native test DLL | `37ce4617e82e45d20c4281d508ce2757342333c30df02386930ae7f675498b9d` |
| Native production instance smoke DLL | `3477f2b6c5db7046754c6ec7bd736d0aff9b57945b3f1acc313c48b9db4580a3` |

| Result | Outcome | SHA-256 |
|---|---|---|
| i101-native-guards-r2/guards.trx | 5 pass, 1 expected fallback skip | `89c6c5d3a45a8cae26f2351dc80a0589d5cda74194b4b9fbf9f03adb7f21e34d` |
| i101-boundary-r2/results.json | 17 pass, 1 expected skip across three fresh processes | `a150d9dff6ca1cf285aec1e5d18216b8dcc1120f36e5f339ec7c34a0130410b6` |
| i101-processes-r2/results.json | 6/6 production protocol cases; all servers/forwarders exit 0 | `25f4676f3b12b6861070722554fff3be76917f00b0e87a73cc7feb88ef8ae3e1` |
| native-app26-i101-r2/App.trx | Full affected App suite: 206 pass, 27 explicit skips, 233 total | `e541fdc37085a38dc49e2c8c1ed64748f4569ab3f10548cf571d856e6d9d8757` |

Actual GNOME Wayland/XWayland session used for the full suite, with both WebKit cases required and passing.
Windows targeted controls: 3 pass, 3 Unix-specific skips. Each guard checks a known source disk, an unknown mapping
and a different-disk control; the usual-instance case independently maps its socket and temporary folders.
R1 compilation failed on an inferred IReadOnlyList type; corrected to IEnumerable. One process-harness invocation
used a missing source-tree script path; it executed no product case and is retained separately from the corrected
successful run. Original failing regression/binaries and all logs/fixtures retained. Host copies of the five primary
native records were verified against the guest hashes above.

Status Remediated and natively verified, not Closed. CI, rebuilt Ubuntu 24.04/26.04 packages, full write tracing and
exact candidate evidence remain pending. No stable release or signing/publication action occurred.
