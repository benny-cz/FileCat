# Unix archive update exchange feasibility

10 November 2026. This is a native syscall feasibility probe, not a FileCat
publication implementation or qualification. The task-owned script
[`eng/validation/archive-writing/probe_unix_atomic_exchange.py`](../../../eng/validation/archive-writing/probe_unix_atomic_exchange.py)
has SHA-256 `930c0e4bc04127b511b71a148acae32e25d7840958436bc30aa15f360f234acb`.
It uses directory-relative native calls with flag `2`, temporary files inside
the task-owned guest root, checks device/inode and bytes after both exchange
directions, checks missing-target failure, and removes its own fixtures.

| Guest | Native call | Result |
| --- | --- | --- |
| macOS arm64, Darwin 27.0.0 | `renameatx_np(..., RENAME_SWAP)` | Existing original/stage exchanged atomically; identity/bytes matched the opposite name; swapping back restored them; missing name returned `ENOENT`. |
| Ubuntu x64, Linux 7.0.0-42-generic | `renameat2(..., RENAME_EXCHANGE)` | The same existing-file, reversal and `ENOENT` checks passed. |

The Mac's installed SDK defines `RENAME_SWAP` as `0x00000002`; its installed
`renameatx_np(2)` manual says the source and target are atomically swapped on
filesystems that support the operation. Both live guest calls succeeded on
their task volumes. A separate symlink fixture proved that the named symlink
itself is exchanged and the symlink target is untouched. This behavior is
useful for avoiding a missing-name window but does **not** condition the swap
on either name still identifying the reviewed inode. A same-user namespace
race can substitute either name after the final precheck. A production update
must preserve both held objects and journal intent before attempting exchange,
then reconcile both names, exact identities, bytes and metadata without
canceling after a possible effect. It must neither unlink nor reverse an
uncertain swap by pathname. A failed postcheck is `NeedsReview`, with all
artifacts retained. This probe does not establish that those rules suffice for
the plan's adversarial namespace/publication gate; that gate remains open.

The Windows native update uses a different transactional-data recipe. This
Unix result does not enable an update profile or relax the separate recovery,
metadata, crash, confinement and all-platform acceptance requirements.
