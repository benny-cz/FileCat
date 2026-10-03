# E-I110 — require complete direct/helper comparison reads

V09/I09 diagnostic audit identifies a validation gap in
`LiveDriveRecoveryTests.An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it`.
The previous assertion compares two returned counts and two zero-initialized full buffers. Equal short reads,
including two zero-byte reads, could satisfy both assertions. G6's native case success therefore does not independently
establish complete requested reads; its source-write qualification was already failed and remains failed.

The comparison now requires **each returned count to equal the requested length**, then compares bytes. Output
records each offset, requested/direct/pipe count and returned-data SHA-256. Deleted-entry counts are logged separately,
and the protocol must report its expected clean session ending. No production read rights, reader or broker changes.

Private evidence: `artifacts/release-evidence/v09-live-comparison-strict-20261003`.
Build/test source is e48251c plus retained `comparison.patch`; exact modified source SHA-256
`12044821a3b40ed85ccb14c04c114227057f4579f96d09b6300284f2aa12763e` and test DLL SHA-256
`fb00c44c3ec225ee195cc25ad2a17141eb8600d8ff60381757e1f790c0a9a789`.
Off-source native DeviceRead/LiveUsbGuard controls pass **23**, with **two explicit skips**: the manual topology
case and the quarantined physical USB comparison. The strengthened physical body is **not executed or qualified**.

An additional elevated existing volume-read test fails on host C: with Win32 error 50, request not supported.
Bounded runner PID 54356/native PID 61116 retains exit one and full failed TRX. A direct native read-only probe
(PID 6196) independently opens exact C:/E: volume GUIDs with GENERIC_READ, aligned memory and 512/4,096-byte reads,
both cached and unbuffered. All four C: reads return error 50/zero bytes; all four E: reads return the complete
requested bytes. Both volumes report NTFS. This establishes a host-native C: limitation, without attributing its
cause to FileCat, .NET, a particular filter or the unrelated USB source change. No speculative production fallback
or broadened access rights are introduced.

The successor bounded runner sets only its own TEMP/TMP to an owned E: fixture directory. The existing elevated
production-reader test then passes **one, zero skips**, covering boot bytes, unaligned small reads, a request larger
than the helper chunk and end-of-device behavior. Controller PID 59432/native PID 16868 exit cleanly; all USB/topology
opt-in variables are cleared. This does not qualify C:, the USB, the installed broker or a candidate.

An earlier direct launch PID 59932 leaves initial VSTest output without exit/TRX; it is retained as incomplete,
cause unknown. The subsequent runner records native stdout/stderr/process/exit separately with a sixty-second bound.
Independent verification checks 32 retained files, native probe results, TRX outcomes, exact source/DLL/runner hashes.
A separate final check confirms all recorded workers absent. Host last-access updating is enabled; that setting is
an observation, **not source-write attribution**.

| Private evidence | SHA-256 |
|---|---|
| Independent 32-file inventory/control outcomes | `f94052b67abe0ed6d15a99c9d1f5af793a25cfe20fe5b6ad3937f410cf47c19d` |
| Off-source TRX, 23 pass/two skips | `7616e89be7da687861c833a8ddfc305ceaebb46f8a5057a9e33b713501e645b6` |
| Elevated E: volume TRX, one pass | `488e08e46c152028ba703227bc5a1deff35f100c77a6e2f7535c49450af0f345` |
| Retained elevated C: failure TRX | `b883a7770548c05c3e7074da0dcf94b53508bd3b63efa1879cff9b19c5d72b2a` |
| Eight native C:/E: controls | `04bb78416f3a708984542abcc39a391a15efff6e9098e6b26215d99b93df6d26` |

Exact correction `c92d31a80849ffb61e92b002a24818c5cf047a28` CI
[37143264712](https://github.com/benny-cz/FileCat/actions/runs/37143264712) passes all four test lanes; three packaging
jobs skip. Retained run JSON SHA-256 `bfcdab49f70c1f971331529b5e1f26687e084e02a9a8ec207656af726c4dde15`, full-log
SHA-256 `6df0c3060c6d90ea9a23e608e65bd1cbcbbbabc5f05e96a01fe1ddcc28ff3df5`.
CI does not execute the gated strict physical body or qualify a candidate.

I110's assertion correction is implemented; strict physical execution remains pending resolution of G6's source
change. I09/I106 remain open, no candidate exists and recommendation remains NO-GO.
