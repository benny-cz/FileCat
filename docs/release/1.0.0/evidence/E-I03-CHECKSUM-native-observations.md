# E-I03-CHECKSUM — finite AppImage checksum observations

2026-10-06. Preliminary §10.3/V20 follow-up on two unchanged development
AppImages. I03 remains Open. These observations do not qualify a candidate,
establish whole-file authenticity or reproduce the downloaded producer binary.

| Original CI run, attempt 1 | Product source | AppImage SHA-256 | Embedded MD5 |
|---|---|---|---|
| 37483079672 | 145f569a59dbad8def5a3b065eec232f4446eea8 | c3bfdd02c6e50a551a106473f15cca2362c3df213ad7ac5f7e8df4211d822e59 | 61164d3923d5e5533820fbdf22e4876b |
| 37497325774 | 850298359fc16cf28503cea42f3ba9c0b1599240 | ccf14fcb65ef41576da41085da094c31285dd8209965787fd31b5a9f7efcc3c0 | e7177da6067c89c6d13cbd0f7b4726b2 |

Original CI member lengths/hashes and all eight retained immutable upstream
source blobs verify before inspection. Independent ELF64 section parsing finds
`.digest_md5` at 932,096/16 bytes, `.sha256_sig` at 933,136/1,024 bytes and
`.sig_key` at 934,160/8,192 bytes in both packages. The earlier proof of the sole
16-byte transformation of the pinned runtime prefix remains valid
([E-I03-APPIMAGE](E-I03-APPIMAGE-runtime-inputs.md)).

The exact declared appimagetool source revision is
`8c8c91f762b412a19f4e8d2c4b35afb98f2d7c81`. Its `src/digest.c` hashes a complete
4,096-byte local buffer on each iteration, including bytes not written by the
current reads. It does not initialize those bytes. A literal chunk/seek replay
that explicitly reuses prior initialized storage reproduces both embedded
values. This is an observational model: C does not guarantee that storage
assumption for each fresh loop-local array. The first whole-file/zeroed-section
MD5 assumption remains retained as a failed observation.

The native probe compiles unchanged original `digest.c`, `md5.c` and `md5.h`.
Its explicit lookup shim supplies the independently parsed three section
offsets/lengths; it does not validate upstream ELF parsing. The only native
executables run are these owned probes, as ordinary Mac user UID 501. Neither
original AppImage nor FileCat is executed. Both original package hashes remain
unchanged.

Two installed Mac toolchains are selected explicitly, with matching linker/SDK:
Apple clang 21/CLT MacOSX27.0 and Apple clang 15/Xcode MacOSX14.0. Each builds at
O0 and O2. All 24 ordinary MD5 controls pass, including known empty/`abc` vectors,
binary data and an unaligned tail. Two originals and four owned one-byte-changed
copies run three times per build: 72 retained AppImage calculations. All 109
recorded child commands exit zero.

**None of those 72 calculations matches the replay or the embedded field.** Each
case is stable in its three separate processes, but results vary between builds.
For the unchanged second package, both O0 builds return
`d0301a24ee03bd0b3661bd46239a2654`; clang 21 O2 returns
`284a11b7cebd9355daa3cabb922f6aef`, and clang 15 O2 returns
`aeedabe3f0322ee169609c594e820b0a`. This finite observation rejects using the
matching replay as a portable independent checksum qualification.

Changes at offsets 4,160, 1,000,000 and 944,632 change each native build's result
relative to its own baseline. Changing only the embedded field at 932,096 does
not change the computed result. These four controls do not establish complete
byte coverage, signatures or authenticity. The original producer's exact
compiler/ABI/lifetime behavior and independent embedded-checksum semantics
remain unqualified. The full SHA-256 package manifests remain separately
verified; this does not identify a new FileCat runtime defect.

Original failures are preserved: v1's selected clang 15 cannot link against the
new CLT SDK; v2 stops at its first native/replay disagreement; v3 finishes all
calculations and input checks but its supervisor exits one while serializing
`os.uname`. V4 independently reads all original child logs, command arguments,
exits, vectors, case hashes and executable pins, retrieves the original failed
and completed logs, and seals the evidence without rerunning calculations.
No failure is relabelled as a successful supervisor execution. No persistent
Mac settings or selected Xcode path change.

The first Ubuntu transport verifies and extracts the same input ZIP, then stops
before compilation because neither GCC nor clang is installed. Its original
diagnostic is retained. Minimal compiler setup and a Linux producer-platform
repeat continue separately; no Linux checksum pass is claimed here.

Private `FileCatReleaseEvidence/appimage-checksum-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-replay-observation-v1.json | 41874f4704136f09b71cb26222d825aa2dd0699ff3d2d7f213ce4db4618086b7 |
| native-control-v1/toolchain-inspection-v1/stdout.json | 8742ff8568306110b5a0b5291d37499875dc8572b4d5f87c678e4b4c8238ab01 |
| native-control-v1/toolchain-inspection-v1/failed-v1-logs.zip | f179bffef7852560833f13b2f69af9a5d84fd39737acc8bfda834a0ae5b2ef33 |
| native-control-v2/native-stderr.txt | fc92470fe7899b6ae3cfeb8cd988f9c96b65256a1acbefd3581c59006007a5fd |
| native-control-v3/native-stderr.txt | cdee01bf5091f6170d666ce409be2c843f9df15ed0f350c3c03120ef22df2bcc |
| native-control-v4/independent-native-checksum-v4.json | dae0becc7e846d35897db513718f841909f0297f157a07f3c619ceff1dbdc802 |
| native-linux-v5/failed-v5-diagnostic.zip | 89f0c64b014aefc561ba88affd16b854cf834967958a22a41ec161a9d67b0d89 |

No physical source, release tag, publication or candidate is used. Counts remain
140/162 preliminary Remediated, one Closed and 21 remaining issue remediations.
All final-candidate campaigns and explicit human stable GO remain gated.
