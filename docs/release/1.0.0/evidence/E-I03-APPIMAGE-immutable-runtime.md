# E-I03-APPIMAGE-PIN — immutable runtime identity and real package

**Status:** preliminary acceptance of `I03-APPIMAGE-RUNTIME-PIN` at `a508a2707b7e2ed06b0ae2b600b2dcb7e291a4d5`; not final-candidate qualification or release GO.

## Reviewed input and acquisition

The mutable `continuous` URL changed after FileCat's earlier reviewed runtime. The current x86_64 runtime is 944,632 bytes with SHA-256 `617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46`. Official GitHub metadata reports the same digest for release ID `417724209`, asset ID `657374641`, created 2026-10-19T20:37:12Z. FileCat now downloads through the asset-ID API endpoint and checks the digest; replacing the moving-tag asset creates another ID, while deletion, an API error/JSON body or changed bytes fails the hash gate.

The release points to source commit `fedfad51acc2860b2916a1eef32ba4a500ff31ef`. GitHub's comparison with the previously reviewed `8f39b89e2ac31e1640b3d3f7e9a5108e6ce805fa` contains one commit and one changed script: it adds `-ffile-prefix-map` while building libfuse and the already declared dependencies so separate builds do not embed a random temporary path. It does not remove the previously reviewed 0700 extraction behavior or change runtime logic. The commit is unsigned; no source-commit signature is claimed.

The runtime's detached signature asset ID `657374763` has SHA-256 `4c38bbfd3c8fe7b43cd37483544e5aa682b5932674d89e53fcccd2d3fc3a8f12`. It verifies under fingerprint `570C77ACEA40C0F1B758902CBF96CCA56490F695`. The 685-byte key is byte-identical to Git blob `5bdb7e4c0f145934aabaeaccb23b9df23e87f6a5` at the pinned source commit (SHA-256 `db8b615eb5bbf5e8418d52906b4a492960926370e2c5041ad6acaccda56ef0c6`). GPG reports a valid signature and undefined trust; this is source/release self-consistency, not an independently certified owner identity. The notice index records that boundary.

## Real Ubuntu 26.04.1 package evidence

The final working-tree package run exits zero and produces all six required files:

| Output | Bytes | SHA-256 |
|---|---:|---|
| `FileCat-1.0.0-rc.1-linux-x64.tar.gz` | 71,039,079 | `855d3459b998611ae57bb5a4b874e340a29ab211a2dcfb95a6cd71947848907d` |
| `filecat_1.0.0~rc.1_amd64.deb` | 57,667,270 | `7e6aa81af0cca2163acbc06ee64c9dd7c484b4eb40b85ad29e6b4de9c1c71bb0` |
| `FileCat-1.0.0-rc.1-x86_64.AppImage` | 65,796,600 | `298ee8f1e78503f2aedefa8fe13a585a525d8020759473c89a088ccfa625c9f3` |
| tar SPDX 2.3 | 244,105 | `a5a2d90cdae304a8de67a9ff8d41d41bafdfb64d5704ee6356ce496b06816581` |
| deb SPDX 2.3 | 253,540 | `60e29c773afc937ad0310a6458f5ac91e0ba924e6f233f6c67db663fb8118a5a` |
| AppImage SPDX 2.3 | 259,967 | `13d06c372e303d9fa19fac810e9b393faff8b3289b74a7b8e6c6e5dc447fddec` |

The SBOMs contain respectively 301, 310 and 318 exact file records plus forty package records. Only the AppImage SBOM includes `./.appimage/runtime`, and its SHA-256 is the reviewed runtime hash. The final AppImage prefix differs from the original runtime in exactly the contiguous sixteen bytes at 932,096–932,111, the runtime's `.digest_md5` section; every other prefix byte agrees. This preserves the previously measured appimagetool transformation boundary for the new input.

Normal FUSE launch and `APPIMAGE_EXTRACT_AND_RUN=1` each exit zero and print exactly `FileCat 1.0.0-rc.1`. No exact-name FileCat process or `.mount_FileCa` mount remains. Extraction finds seven runtime-notice files byte-identical to the reviewed snapshot; the embedded index names asset ID 657374641, the exact hash/source revision, committed key blob/fingerprint and detached-signature identity. The first cleanup query was over-broad and matched SDK compiler command paths containing the checkout name; it is retained separately and not counted. The corrected exact process/mount check is the accepted result.

The production notice/refusal suite passes 63/63 on Windows (seven allowed, 56 refused). A fresh Ubuntu control flips one runtime byte: the production notice tool exits 1 with `AppImage runtime input differs from the reviewed notice snapshot.` and creates no output. A first combined verifier used a relative AppImage path after changing directory and exited 127 before extraction; no product failure is inferred, and the corrected absolute-path verifier supplies the results above.

## Retained evidence and limits

Raw records are under `E:\FileCat\artifacts\release-evidence\i03-appimage-runtime-20261028-v1`. Selected record SHA-256 values:

| Record | SHA-256 |
|---|---|
| asset-ID download identity | `814fb7000f93bf9dfff64e8cc56d4f9afa7748de4bfb3057e327b8b29dac80d3` |
| GPG verification | `99e724d4d519ba9844eeb606ffae843fbf50caedcb411d021b1e40a6e0450fb8` |
| committed/release key comparison | `a5af4ee9ede4fefd2c2652688603e0ef068fe0f16112ed87563028e3e7f92d07` |
| final package log | `9183ed53abddbc7b6568538afe9d287a510d7b6f757938564ad1466bec0c3845` |
| final output identities | `4d661f3aeb8700ae58a698f44f0704d60478b2783de4bf57952aecabb9b2f912` |
| runtime-prefix transformation | `e7125d7b56e9fe402aebbca5aa3486ae2dc1eee86fcd1f9c653608a1c6154e4d` |
| three-SBOM summary | `a176de157ae5659b89ceee0c81ca8f288c168bc544b500e7c1e99cde1f48c7a3` |
| embedded runtime index | `ab3d02cd02c118ec60966505070f310d1384800a6292634d8b857fb5790a3d81` |
| changed-runtime refusal | `d40a916b6fbab99c537c3daea92292add6e3963f3d62509ca566f7427949f3d3` |
| final 63-control notice record | `b7cbf125b85a8867500c37109f6904ec0e72dad97702d20444620ef8a9a532f7` |

This closes only the immutable runtime selection and real preliminary package/SBOM/launch/refusal stopping condition. The Ubuntu run uses a six-file reviewed overlay on a `c7ed9540` checkout; exact pushed-source hosted packaging remains `GATE-CI-FINAL`. It is not a frozen candidate. Complete static-library/source-obligation and legal eligibility, cross-platform composition, signing, installed lifecycle, candidate qualification, REP custody and stable GO remain open.
