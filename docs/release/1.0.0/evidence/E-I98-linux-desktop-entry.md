# E-I98 — Linux tar desktop entry corrupts paths

Preliminary V19 remediation, 2026-10-02. Medium severity; must fix for the Linux tar desktop-launch claim. Candidate
closure and rebuilt-package validation remain pending.

## Reproduction

Fresh Ubuntu 24.04.5 VMware guest, GNOME 46 Wayland / XWayland 23.2.6, no SDK. Input development tarball
FileCat-0.1.0-dev.522-linux-x64.tar.gz, SHA-256
`39b66f51b08647f4e4a20ec5ff76054d478660dd2ca03c87f21ab644b5172ee4`, manual CI 36994087185 at
`d14199b8b2bc1f6170ceab2b91c3212e80909d0c`. Evidence in
`artifacts/release-evidence/linux-os-matrix-20261002/24.04/` and the owned guest root in E-V19-P2.

Extracted into `tar with & spaces`: the executable prints its version, but the shipped install-desktop-entry.sh inserts
the directory into a sed replacement without escaping it. Ampersand expands to the matched original line. Exec acquires
`Exec=filecat %F` inside its pathname; Icon acquires `Icon=filecat`. desktop-file-validate rejects duplicate %F; GLib
cannot construct the application entry; gio launch exits 1. Reproduction log SHA-256
`9c835153085f4879c49928930ca71ac338bc9bfbfb1045bdcee94a343890ef9d`.

Other path characters lack Desktop Entry string/argument escaping too. Native control on the unchanged shipped helper
passes **4/12** cases. Before-results JSON SHA-256
`ec9b9415a308ce9195e3bf11498d6d798fcd7daba6c5905aaaebc8d966c89266`; original helper SHA-256
`a018fe21ca20bf547292fac84cf2da5678cc62be8b7e18fa7fe0883aaa49197f`. No unintended external execution claimed.

## Remedy and validation

The package copies eng/install-linux-desktop-entry.sh. It writes values as data, escapes Desktop Entry string and quoted
arguments, preserves directory newlines, and leaves %F as one separate argument, following the
[Desktop Entry specification](https://specifications.freedesktop.org/desktop-entry/latest/exec-variables.html).

Exec invokes /bin/sh with the helper pathname as an argument. --launch execs FileCat with the original file arguments.
This also handles literal percent paths: the first escaped direct-executable attempt passed desktop-file-validate but
GLib returned NULL. GLib's [2.80 source](https://github.com/GNOME/glib/blob/2.80.0/gio/gdesktopappinfo.c) checks the first
parsed executable before launch-time field expansion. A fixed interpreter path avoids checking the unexpanded %% name;
no shell command string or eval handles the path. Equals is valid in the helper argument too.

eng/validation/validate-linux-desktop-entry.py uses desktop-file-validate and real GLib DesktopAppInfo/launch. A stub
captures native spawned argv independently; the parsed icon must equal its original path. Selected-file arguments contain
spaces, ampersand, percent, quotes and Unicode. The fixed helper passes **12/12**: plain/spaces, ampersand, single/double
quotes, percent field-like text, dollar/backtick, backslash, pipe/semicolon/hash/parentheses, UTF-8, tab/newline, trailing
newline, and equals. Fixtures and failed attempts retained; no user menu overwritten. Linux CI now runs this check and
archives fixtures on failure too.

| Verified working input/result | SHA-256 |
|---|---|
| Fixed helper | `cdfa97bb3846c6a4305ea4deca32248779af8a2d79bd922a253f68120abbafc9` |
| Native harness | `aac79e91c3b85953ab920b3130cd1dc4599dcb05252a6d21794881d77d342789` |
| Fixed results JSON | `0799b853d5da3dc3d73ce43ece29cc52e4cc89d258e7c3570c2623dccc5e1695` |

Native helper checks do not qualify a rebuilt FileCat artifact or replace 26.04/final-candidate cases. Source identity is
the commit containing this remedy; exact working helper/harness hashes above identify the pre-commit test inputs.
