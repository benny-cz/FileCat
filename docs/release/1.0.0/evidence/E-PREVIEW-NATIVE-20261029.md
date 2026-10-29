# E-PREVIEW-NATIVE-20261029 — official old-tag package starts

Recorded 2026-10-29. **Preliminary package-format evidence only.** Source is
the immutable, owner-approved `v1.0.0-preview.1` tag at
`be40902eeea5eac6c1f3eb2e417e0cbe42dc265d`. Its [hosted run
40936654935](https://github.com/benny-cz/FileCat/actions/runs/40936654935)
passed every producer and package job, then failed protected draft preflight
before upload ([I562](E-I562-preview-draft-empty-release-inventory.md)). No
release exists. This old tag predates AC-59, I563 and the latest README; it
cannot qualify a later candidate.

The 24 extracted inputs came from the three official GitHub artifact ZIPs
whose server digests, manifest identities and byte-for-byte extraction are
verified in I562. No rebuilt package was substituted. The current native
receipt root is
`V:\FileCat\artifacts\release-evidence\preview-package-native-20261029-v1`.
Its 28-file `audit-manifest.json` independently rehashes; manifest SHA-256
`9457700b7d322a8fc0de8118200ff56b8e001726f115ead97ec4fc0cc2ab71c6`.
The original official packages remain in I562's private root, rather than
duplicated here.

| Environment and official file | Native result | Exact package SHA-256 |
|---|---|---|
| Windows 11 Insider build 26300 x64 VM, portable ZIP | Manifest hash matches; marker present, helper absent; `FileCat.exe --version` exits 0 with `FileCat 1.0.0-preview.1`. | `9ba0ad2f9c03e7ba4397848caa02ccae565bdd7f947302f0de70b2dbfa5de8f6` |
| Same VM, framework-dependent ZIP | Manifest hash matches; marker and helper absent; `FileCat.exe --version` exits 0 with the same version. | `14e6108bc583fffed85ba87b7fdfbf493be7d322997841b4197d0f56087165b0` |
| Same VM, x64 setup EXE | Manifest hash matches; silent per-machine install exits 0 into a new owned directory; installed helper is present; installed `--version` exits 0; silent uninstall exits 0 and the app and directory are gone. VMware's guest process had a High token. | `91251556bebcb8c524058817e7433f2a083e2e9faf4ac27fb4f659216370ea3f` |
| Ubuntu 24.04.5 x64 VM, tarball | Manifest hash matches; extracted `FileCat --version` exits 0 with the expected version. | `e75521088e0166ba7c60878d658a7512dd4dbd421d750b5f23413d953509281b` |
| Same VM, Debian package | Manifest hash matches; `dpkg-deb -x` payload `FileCat --version` exits 0. This is **not** a `dpkg -i` install. | `84b5eff111e158227de904bd23e7edac643d196fc8cabaf011a6b93f8cc9d87c` |
| Same VM, AppImage | Manifest hash matches; extract-and-run exits 0 with expected version. A separate normal FUSE launch writes expected version, empty stderr and its own `exit=0` log, but `vmrun` reports exit 2 for that shell invocation. FUSE mode is **not accepted** on this ambiguous wrapper result. | `25ec476b7a1ae41afb2285f507f61e4d546d7e619c321a1bcd5e8c73df1e5693` |
| macOS 27.0.1 ARM64, `.app` ZIP | Guest hash matches; `codesign --verify --deep --strict` passes; binary is Mach-O arm64 and `--version` exits 0 with the expected version. This transfer did not exercise download quarantine or Gatekeeper. | `737ef82b42cf8ace081f405ac9e0efdadcfb9a2e3f3ff15d5cf2d9453b53aa8d` |

The official Linux corresponding-source bundle is 4,704,832 bytes, SHA-256
`be7816ce0e129b0417758f942378bed89350e04fb1700c7bd5caa1005d72e587`,
the exact reviewed [EXT-04](E-EXT04-appimage-source-bundle.md) pin. It is in
the eight-member hosted Linux package set. This establishes that the first
hosted tag package job produced and selected those bytes; the protected draft
failed before it could attach them. It is not a legal determination.

## Receipts and limits

- Windows native ZIP log SHA-256:
  `3767bf3e6135a26d1adab52e15cb9de02bd026f1133da15a7fc116b22d1b33ea`;
  result JSON `0c0c73db65ce853f30e0aa2c6c0d3b08399c5e0b6794b3ebf7246a896b89242f`.
  Installer result JSON `94e61fec3fd6a6295e86da3c1f5165f331597ddd26e3534779ec355a7037dcc6`,
  installer transcript `d0a25bb5dd73164adda32ec21b9ae173771bde44ef40e9f0695c1d996367afdb`,
  setup log `dc209858a97d2f3401f942ba34d49ef61bce6ce09256dcdb27f0b6917ac411dd`,
  uninstall log `56e969e5d9d4f95b1c3b5ade01f1eeac5a7c97643c7eafd051ae4c327c333394`.
- Ubuntu native log SHA-256:
  `398c3270137d789bd24a7dd08ccce1c546772265abc63e62edf965db5d6b14c5`.
  The separate normal-FUSE wrapper log is
  `86f3ea2b1328826600d76beb1a2d0251b5f84e0754dc524250c669886353530f`;
  stdout is `7c0af08d7994ab1c2d84d93f1d8788733ce0a873c61b866e7bc9b540ecc63f1d`,
  stderr empty.
- Mac native log SHA-256:
  `d946c7df0b9f32c74c33e4898cb19f35768c76eedab3ba3caa5ddad07f63dab9`.

The owned Mac `~/FileCatReleaseValidation/preview1-macos`, Ubuntu
`/home/benny/FileCatPreview1`, Windows guest Desktop `FileCatPreview1` and
Windows `C:\Program Files\FileCatPreview1` test directories were removed
after the logs were copied. The Windows uninstall had already removed the
installed app; the later directory query found no directory. Guest cleanup
scripts/receipts were also removed after transfer. The Mac, Ubuntu and Windows
cleanup receipt SHA-256 values are respectively
`4e040eb49df0a18a7d4275858effba8fa21dd1598a681622a5d4d2066c511296`,
`eb1d575423dde2c2c7aa0efcd757fe5d14b45ff6adbfe5e9a5777894bd24725e`
and `57118bbce22d6a4842894f0287ac1408099eac246f0f7905a501a5b4ca3c5f6b`.

Two early Windows VMware command forms and the first Ubuntu `mkdir -p`
argument form failed before an app/package launched. The Windows Registry
inventory query also had invalid syntax; it is not proof that no earlier
registration existed. The independent pre-install check did find no
`C:\Program Files\FileCat` directory. These setup outcomes are retained
separately; the corrected guest scripts and their hashes are in the audit
root. No host UI was used.

This does not cover a GA Windows x64 machine, Windows ARM64 installer/ZIP on
physical hardware, a clean `dpkg -i`, a clean normal-FUSE result, downloaded
Mac Gatekeeper/TCC, native GUI interactions, signing, or the final-candidate
identity. Those remain with V19/V20, REL-SIGN-MAC, REL-FQ-CORE and the
named external gates. The next pipeline step is a fresh corrected tag after
current-source CI and package preflight, with the owner's exact-tag approval.
