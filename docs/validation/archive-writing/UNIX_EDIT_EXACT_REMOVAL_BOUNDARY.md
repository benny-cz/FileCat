# Unix edit cleanup boundary

9 November 2026. Native Unix edit creation, capture, publication and read-only recovery have
component evidence. Native Discard and capture cleanup still refuse removal. This is intentional
until a deletion operation can prove that the object removed is the exact reviewed object.

The current lease pins parent directories and files and rechecks their identities. That prevents
ancestor path redirection and detects many replacements, but it does not make a later name-based
unlink conditional on the reviewed file identity. Linux
[`unlinkat(2)`](https://man7.org/linux/man-pages/man2/unlinkat.2.html) removes the entry named
relative to a held directory descriptor; its supported flags do not include an exact-file identity
condition or `AT_EMPTY_PATH`. The
[macOS `unlinkat(2)` manual](https://man.freebsd.org/cgi/man.cgi?manpath=macOS+26.3&query=unlinkat&sektion=2)
likewise specifies removal by directory descriptor plus path. An uncooperative process with the
same user identity can replace the final name after FileCat's last review and before that call.
The private 0700 directory and an advisory lock do not exclude another process with the same user
identity. A rename into quarantine is also name-based at its first step; it can retain a mismatch
for review, but cannot by itself prove that the original reviewed object was the one moved.

Therefore `UnixEditDirectoryLease.DeleteSnapshot` and `RemoveEmpty` must continue to retain their
artifacts, and the UI must report incomplete cleanup honestly. A future Unix deletion route needs a
platform/filesystem-specific conditional operation or an independently enforced namespace owner
that excludes same-user replacements during the entire review and removal. Any route must be
qualified on Linux and macOS with attacker replacement, hard-link, symlink, interrupted cleanup,
same-byte new inode, and process-kill cases before it is registered. No pathname-only fallback may
be added merely to make Discard appear complete.
