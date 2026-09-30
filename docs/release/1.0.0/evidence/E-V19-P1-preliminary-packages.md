# E-V19-P1 — preliminary package checks: Linux packages on Ubuntu 22.04, the macOS app on an M1 Mac

Preliminary runtime evidence of V19 behavior for development packages. Not final qualification: no candidate exists, and
neither machine is a final target (Ubuntu 22.04 is below the plan's 24.04/26.04 rows; the Mac is the owner's personal
machine, not a clean install).

## Inputs

Packages from the manual CI run 36759624490 on `45efc09` (version `0.1.0-dev.281`), jobs "Package Linux" and "Package
macOS", downloaded with `gh run download` to `artifacts/release-evidence/ci-36759624490/`:

| Package | SHA-256 |
|---|---|
| `filecat_0.1.0~dev.281_amd64.deb` | `ef34a308b29cc64aabed09d7f4d265495dcc5bc6e619df9ce7041ec2f6fad587` |
| `FileCat-0.1.0-dev.281-linux-x64.tar.gz` | `aad595b2b5cc6fbcb365b20b278a5c19fda848dbffc35e33cc99b7166fd4b900` |
| `FileCat-0.1.0-dev.281-x86_64.AppImage` | `b9e5c3a884502bc85ae9990d4a792409d18b86aca3414f638a798cc9f7784151` |
| `FileCat-0.1.0-dev.281-osx-arm64.zip` | `b852116e55b534c6c9ec41fb089f069e15cfc669ba71bdac2db752a07668e336` |

## Linux (lent Ubuntu 22.04.5 VM, kernel 6.8.0-138; log `ubu-v19-log.txt` `235d7ad01098fc5c0d34a963b2ddf2aa21d7f91b050196ee18306429fc3bf55f`)

- **deb:** `Depends: libc6, libgcc-s1, libstdc++6, libssl3 | libssl1.1, libicu76 | libicu74 | libicu72 | libicu71 |
  libicu70 | libicu67, libfontconfig1, libx11-6, libice6, libsm6`; `Recommends: xdg-utils, libsecret-1-0,
  libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37`. `apt-get install ./….deb` succeeded (installs `/opt/filecat` and the
  packaged link `/usr/bin/filecat`); `filecat --version` printed `FileCat 0.1.0-dev.281`. `ldd` over the native libraries
  found one missing dependency, `liblttng-ust.so.0` of `libcoreclrtraceptprovider.so` (the .NET runtime's optional LTTng
  tracing provider, which the runtime loads only when that tracing is enabled). `apt-get remove` removed `/opt/filecat`
  and the link; reinstall and `purge` left nothing behind.
  - A first reading suggested `/usr/bin/filecat` survived `remove`. Rechecked in fresh processes
    (`ubu-deb-remove.txt` `db53f040fda7286be35d538a6ccc109cf148d45564561c5cd748580bc3956757`): the link belongs to
    the package (`dpkg -S`) and is gone after `remove`; the first script's `command -v` had answered from bash's command
    hash. Not a defect.
  - **I04 detail:** the ICU alternatives end at `libicu76`; Ubuntu 26.04's archive supplies `libicu78` (plan §4.1), so
    the `.deb` would not install there as built. Not checked on 26.04 yet (ENV-04).
- **tar.gz:** extracted into a path with spaces (`~/fc tar test`), `--version` worked; its desktop-entry helper wrote
  `~/.local/share/applications/filecat.desktop` with the quoted path; `desktop-file-validate` passed with one hint
  (more than one main category in `Categories=System;FileTools;FileManager;Utility;`).
- **AppImage:** normal launch through FUSE (`fusermount` and `fusermount3` present) and `--appimage-extract-and-run`
  both printed the version; runtime `type2-runtime` commit `8f39b89`.

## macOS (MacBook Pro M1, macOS 26.6.2 build 25G83, Gatekeeper assessments enabled; log `mac-app-check.txt` `78940b5d162c668d46e089926cd1aeaf1d66be18936f04c3b11b0bf530e9e959`)

- **Signature:** `Signature=adhoc`, `TeamIdentifier=not set`, `flags=0x2(adhoc)`; `codesign --verify --deep --strict`
  passes (the seal is intact).
- **Gatekeeper:** `spctl --assess --type execute` **rejects** the app, both extracted plainly and extracted from a ZIP
  carrying a quarantine attribute (as a browser download would). A user opening the downloaded app by double-click gets
  Gatekeeper's block; this is the expected outcome of an unnotarized ad-hoc build and is DEC-03's subject (notarize, or
  label a non-notarized preview with instructions).
- **Architectures:** the executable and the .NET runtime libraries are thin `arm64`; `libAvaloniaNative.dylib`,
  `libHarfBuzzSharp.dylib` and `libSkiaSharp.dylib` are universal (`x86_64 arm64`). For I03/I18 the arm64 package
  therefore ships x86_64 code as well (inventory and size, not a malfunction), as the x64 Windows payload ships foreign
  WebView2 loaders (E-I15-V1).
- **Info.plist:** `CFBundleIdentifier io.github.benny-cz.filecat`, version `0.1.0`, `LSMinimumSystemVersion 13.0`
  (I04: must match the Platform Support Decision's macOS minimum, DEC-02).
- `--version` of the unquarantined app printed `FileCat 0.1.0-dev.281`.

## Not covered here

Interactive first launch, file associations and "Open with", desktop integration after upgrade, uninstall of the
Windows installer with the new script (E-I15-V1 covers it), and every final-target OS row.
