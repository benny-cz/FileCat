# E-I102 — Recovery misses an independent FileCat

Reproduced 2026-10-02 on fresh Ubuntu 26.04.1 after I101's hash-bound working remedy. No candidate.
An actual FileCat window started with --new-instance and a unique usual profile is visible (PID 27195), while a
separate process calling the production UsualInstanceRunning API for that profile reports false. The window
gracefully closes and its saved workspace contains the opened fixture folder, proving usual-state writes.
Program skips TryForward for this option; StartServer then has no instance owner to advertise.

Potential Critical for V09/I09: a data-root recovery instance can miss a FileCat writing to the source disk.
This evidence exercises instance detection and state writes, not an unsafe physical-device recovery scan.
Status Open at discovery. A separate regression also proves a usual owner on another profile is omitted:
the target disk contains that owner's local state, but CheckDiskSafety returns null. Baseline fails 1/1.

| Private guest record in owned stage | SHA-256 |
|---|---|
| i102-before-r1/oracle.json | `0b17e86afa3866bf9d94d3a0163d66cffd95f95b34addb21fb261088938b2f6b` |
| Saved usual-profile workspace | `6bbe0365d28b794409fdfd7997d1c17c2f3f6aaa43689e203268e6c8e8a4f9cb` |
| Working FileCat.dll | `c9670cc926430f2e52f4f3be16dc8ec063acaf7cf7cb6667195c6d6dd16c435b` |
| i102-before-test.cs, LF-normalized other-profile regression | `146bc0b6eb78897c0f21e883a3ea2d1b62a9f1cbe1272d8222de1eef8ecf2a23` |
| i102-profile-before-r1/before.trx | `b567e9aa4f1022bf853f312940b6ab7ce0443aea39a57797e64e05c4d44bbf85` |
| Other-profile baseline FileCat.dll | `5e83e640e6c026b929b52e79c4149451962070aba3f24aa475077fa7227bafdc` |

Profile i102-new-c68dbc3ecec241d6a80b is solely this fixture. Window/probe/state-file records and logs retained.
Required remedy: advertise independent lifetimes inside their tracked state folders, detect busy owners without
creating/writing files, conservatively guard unknown locations, and retain normal forwarding and shutdown behavior.

## Remedy and revalidation

TryForward now honors NewInstance itself, so Program always calls it. Independent launches and launches whose
owner did not answer register a uniquely named lifetime lock and separate bounded metadata in LocalDirectory/instances.
The directory is explicitly part of AppPaths.WriteFolders (including a link/mount there). No registry is written
outside the selected data/state root. Native flock success is required on Unix; Windows uses an exclusive file handle.
No independent listener replaces the normal owner's endpoint. Normal release removes only its unique pair; a crash's
unlocked stale pair is ignored. Busy owners with missing/invalid metadata conservatively refuse a scan.

Recovery checks active usual profiles, including profiles other than its own, without creating or writing those
folders. Actual independent runtime locations are guarded alongside the usual state and normal-owner IPC locations.
The legacy pipe probe runs only when its socket exists, avoiding a half-second wait for every absent profile.

Source is the retained I101 working tree plus the baseline regression and these normalized R4 inputs:

| Input | SHA-256 |
|---|---|
| SingleInstance.cs | `b13dc77d443ec8cc36bbbe59350ef3f8728e38a7816306c964f28364b5640e67` |
| Program.cs | `6e2389c56ba88e4c2bad6403af0bfe2326a37bb4b7086a2d911523c2046efa60` |
| AppPaths.cs | `fd2b94b8209c5aa809bbcdafc5177ad2bf55119fe730e0b82f37ad55f00ce741` |
| MainViewModel.Recovery.cs | `d11f5169b8a2c9b26273d88214a9e2c370c60180f4599b29416ff26bbd9aee81` |
| RecoverySafetyTests.cs | `ccab72368c051b784d8486285e1906f6632e99221c4604b2487cbaedc8aba2fb` |
| Boundary harness | `153b77fb0f34be5853b63c15496c77b142f292d09ec9f5390b6a0b91a07656fe` |
| Process harness R5 (adds forwarding control beside an independent window) | `5c0836575ed418b36556580b5a635421aa4a39fa5be56e0f23f4b4dfdad4da90` |
| Native FileCat.dll | `60c8ca0c7d9dc84579e94f318d327abcb5e29e41a8c3c1f2d2401ea6e972a310` |
| Native FileCat.Core.dll | `5874f564e52184342ffef1789cc29987893abc0a78dcf04e2f479007a945a190` |
| Native test DLL | `edf0cfcddf88f357e4b58810260089eb54b4d2a57e6d3606521e3e8ba8c44fee` |
| Native production instance smoke DLL | `89a63e6910c2d72795cf488a9d9121989753057d8fce56a61828460e03899a12` |

| Record | Outcome | SHA-256 |
|---|---|---|
| i102-boundary-r4/results.json | 23 pass, 1 expected fallback skip; three fresh TMPDIR processes | `4506dded1510c7c3145902fdd2988bee585aa6a8f18b791540aecb26ab588238` |
| i102-processes-r5/results.json | 8/8; separate sessions/TMPDIRs, two independent owners, normal owner alongside one | `962fc51cab8df6158b4b0c500e47316763bddd51fa41cb6a6bd20ba340b64743` |
| i102-paths-r4/paths.trx | 13 pass, 1 Windows-name skip | `32cf4da12f8e34ba65a3a0a6a4eb9a38ce2bea410b59caaa37ec575665a67c69` |
| native-app26-i102-r4/App.trx | Full affected suite: 208 pass, 27 explicit skips, 235 total; both required WebKit cases pass | `0d10b89c73ad851a0eafeb8ad1cec8df20358774101602c97a7e714e8ed997c7` |
| i102-gui-after-r4/oracle.json | Actual GNOME session, PID31327: running true, after close false, pair cleaned, workspace saved | `0df3c20115ea101c373a1fe2094913583e2615e33c0d9a2deeb7ef4789897a19` |
| Windows guards-r4.trx | 5 pass, 3 Unix skips | `4cf49ea17f2beafcab157620e07ea2ea3e91f0a2889068123687abc76f2ec650` |
| Windows paths-r4.trx | 13 pass, 1 Unix-permissions skip | `d1fb914dc0b747decb35ae5018fab9707ce1d0ad7d46126050dc75afe0e37c67` |

The controlled crash is SIGKILL (-9), solely to the spawned owned test process. The remaining-owner probe stays true
after the first owner closes and false after the last exits/crashes. Profile file sizes/mtimes remain unchanged by
probes. Forwarding reaches the normal owner while the independent window stays separate. Live missing/invalid
metadata and stale incomplete metadata have opposite controls: refused while busy, ignored when unlocked.

R1 exposed that a Unix FileStream reader conflicts with the held lifetime lock; metadata therefore uses a separate
inode. Windows R1 rejected setting UnixCreateMode even to null; the property is now set only on Unix. Its other-profile
test initially omitted actual Windows window state creation; setup corrected. R3 passes retained; R4 adds the missing
live-metadata control and final source. All intermediate inputs/binaries, failed results and fixture logs retained.
Seven primary native records copied to the host and hash-verified. Saved GUI workspace after hash
`97ff7bb212478d7604493f86acfcec89963351b086bf555bb89bbd6d50fe8b7d`.

Status Remediated and natively verified, not Closed. CI/macOS, rebuilt packages, runtime/device write tracing,
cross-installation/portable-instance discovery and exact candidate qualification remain requirements, with no wider
all-process discovery claim. No stable release, signing or publication occurred.
