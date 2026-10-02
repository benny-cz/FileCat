# E-I100 — Unix launches in separate sessions run independently

Discovered 2026-10-02 during E-I99's actual GUI check, Ubuntu 26.04.1. Same account, profile, data root, TMPDIR and
hash-bound I99 working FileCat.dll. No candidate.

Expected A-07/V23 B12 behavior: a second launch opens its folder in the existing window and exits. Launching from a
new SSH session instead returns timeout exit 124 at ten seconds, with the original window still showing source.
strace proves the child creates /tmp/.dotnet/shm/session17460/FileCat-4E0D79942132A7ED while the original server holds
session16723's mutex. The child proceeds through GUI startup, unlinks the first instance's live socket and binds the
same endpoint. The bounded child is terminated and the original GUI is gracefully closed; all private state retained.
The same-session control forwards successfully (E-I99).

This matches Microsoft's documented distinction: Unix Local synchronization objects are limited to a shell session.
See [NamedWaitHandleOptions, .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.threading.namedwaithandleoptions?view=net-10.0).
The observed .NET 10.0.12 mutex storage under /tmp also warrants checking V09's write-location accounting separately
from the configured TMPDIR. Neither cross-TMPDIR behavior nor a recovery scan is claimed tested by this trace.

Severity Medium (one profile's state/instance contract); must fix. Linux reproduced; macOS has the same source path
but native reproduction pending. Windows uses a login-session namespace and is not established affected. Status Open:
root cause established, remediation and cross-process regression still pending. I99's passing in-process checks do
not close this issue. Prior Unix single-instance evidence does not cover distinct-session launches.

Raw host records in artifacts/release-evidence/linux-os-matrix-20261002/26.04/:

| Record | SHA-256 |
|---|---|
| i99-gui-forward-native.txt | `d2666354dc032e26a8f2ab22d935fc5268b8d76cf60b561c9a3b9d92b2855a95` |
| i99-gui-client-strace-r2.txt | `0d82e25ce1b2d92c5ed64ea328dfb6e45315706af4bbea34c829a025b8a934e2` |
