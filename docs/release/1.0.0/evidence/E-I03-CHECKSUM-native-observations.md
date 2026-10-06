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
diagnostic is retained. The first compiler install fails on mirror transport;
a bounded invocation with IPv4 forced succeeds without persistent network changes.

The same unchanged C sources, lookup shim, original packages and changed copies
then run on Ubuntu 26.04.1 x64, UID 1000, GCC 15.2.0-16ubuntu1, at O0/O2. All
12 ordinary MD5 controls and 55 child exits pass. Each original runs three times
per build: **all twelve original checksum calculations match its embedded field**.
All 24 changed-copy calculations disagree with their changed file's embedded
field, as intended, and all 36 Linux calculations match the replay. Each case's
value is identical across O0/O2 and all three processes. The three payload-byte
changes change the calculated value; the field-only change preserves calculation
while making comparison fail.

This establishes finite independent checksum matches for those two original
Linux artifacts using the declared source algorithm. The retained 72 Mac
disagreements still reject a portable/general buffer-lifetime guarantee. It does
not prove the downloaded producer's compiler/source reproducibility, full byte
coverage, whole-file authenticity or candidate qualification.

The original compiler setup's named dpkg fields are blank: inline guest transport
expanded their dollar-brace references before Python ran. Its full named original
baseline is unavailable. The empty package-change assertions in v6/v7 are invalid
and explicitly superseded. A saved-file reader verifies the complete original APT
transaction and current named dpkg inventory: thirteen new compiler packages,
no upgrade/removal in that transaction; `gcc` and existing `libc6-dev` gain manual
marks. This correction does not rerun checksum calculations.

Scoped restoration first refuses the simulation's unfamiliar `Purg` records,
before mutation. The corrected parser still requires exactly those thirteen
packages and no installation/other removal. All thirteen are purged, the original
manual package set is restored, and an actual named before/after restoration
comparison verifies no other installed version changes. Full original pre-install
named inventory remains unavailable; APT metadata, downloaded inputs and evidence
are retained. No network/power policy, product source or payload changes. The
final combined reader seals 108 AppImage observations, 36 plain controls and
164 successful child commands with the original failures and corrected setup
limitations.

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
| native-linux-v5/compiler-setup-failure.zip | 578d5525704a6eaa11f6414656549c0f6a47a688ab834ac87b334b256fe9c1e7 |
| native-linux-v6/independent-native-linux-checksum-v6.json | 8e5a7a4081ff8a7979ed893dab7af8d044e00a3b4340ce19a8cb920f9c349aa5 |
| independent-combined-checksum-observer-v7.json | 3d934499eed1a50136c2b27bfadf4441e9c435637969ce656efe8e3ccb2966b7 |
| setup-observer-v9/independent-host-setup-observer-v10.json | c779813010349250019f3507ba0371b369f67e2a6e97fd78d3fc34716ab292fc |
| compiler-restoration-v13/independent-compiler-restoration-v13.json | 318534912a9fad2dbdc1a78a2fb0c68ebc6b492f73ccd06201ce9f725fd7f8fd |
| independent-final-checksum-observer-v14.json | ccf4684459099c9293e278f838a88d4fcaccc47baf464e47d63b4ebb1b765038 |

No physical source, release tag, publication or candidate is used. Counts remain
140/162 preliminary Remediated, one Closed and 21 remaining issue remediations.
All final-candidate campaigns and explicit human stable GO remain gated.
