# E-V19-P3 — dev.549 successor Linux packages

Preliminary native execution, 2026-10-03 local / 2026-10-02 UTC. No candidate or final qualification.
Exact source `ea4a2acad34bf0641fbc15bc64a64013e8600322`, manual CI
[37068909015](https://github.com/benny-cz/FileCat/actions/runs/37068909015), development version 0.1.0-dev.549.
All four test lanes and Linux/macOS development package jobs pass; no tag or release created.

| Linux input | Bytes | SHA-256 |
|---|---:|---|
| Debian package | 56,072,316 | `3d286ea7110dba808254feeb70bfae01654b0da6317587929f38e53df49c6686` |
| linux-x64 tar | 68,619,992 | `871e6f79048f8a9ccfa303c6dc19b754bbcc5a09df83c9babaec619a0d19f665` |
| x86_64 AppImage | 63,814,136 | `96925fa266a1d52daadebeb2e4309abdd7a6a429b6740e4c1fa42c02a0e45990` |

SDK-free Ubuntu 26.04.1, hostname filecat-ubuntu2604, actual GNOME Wayland/XWayland desktop; verified VMware
UUID `c13a4d56-88aa-1159-57b9-9cea95bb06e9`. Fresh marked fixture token `3e504c9d299e4dada8b78038ad2ae3bf8`.
The owned Debian installation is upgraded from dev.539. This is a successor check on the existing disposable
desktop, **not another clean-install baseline**. Prior clean-baseline package/lifecycle results remain E-V19-P2.

All three formats pass native window/PID/executable checks, version check, corrupt settings preservation,
startup with a 209-byte temporary path, forwarding from a separate session using a 209-byte Unicode temporary
path, workspace persistence, graceful exit and native restart. Lock becomes independently available and socket
is removed after close. Tar runs from a path with spaces, ampersand, percent and Unicode. AppImage uses a
read-only FUSE mount with extraction overrides cleared; mount is removed on close. No SDK is present.

This harness does not repeat desktop-entry argv, uninstall/reinstall, native copy or physical recovery tracing.
Ubuntu 24.04 successor execution has not occurred. Further recovery audit subsequently reproduces a renamed
apphost discovery gap (E-I106); any new build needs its own affected checks and artifact identity.

Private root: `artifacts/release-evidence/ci-37068909015`. Full owned fixture, packages, extracted payload, native
maps/window records, corrupt originals, state and logs retained. Guest/host archive hashes agree; every regular
archive member independently stream-verified for size and SHA-256.

| Evidence | SHA-256 |
|---|---|
| adapted dev549-package-checks.py | `33fb7e7d97cc3edc708c22c151811d71429a3ea921cdf6f9aa12d0c6bc0b2342` |
| native-26-results.json, 3/3 formats pass | `0de7439905c87dacb5b4c07e8482f8c6e07985ee16d393a7936ce8ac821e788c` |
| native-26-full.tar.gz, 255,962,858 bytes, 353 members | `26cc6852dd2ea9db27ae6ab53be55583514094c45f5e85e80f651cfa3d4e57aa` |
| native-26-full.tar.gz.verified.json | `7e8953b787ab04b7871683ab9bd3cbda60620b7670c7997b285cd4c41afa4eaf` |
