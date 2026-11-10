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

The unregistered `UnixArchiveFile.ExchangeRetained` primitive was added at
`716457db` and strengthened at `eaf621f1`. It accepts a creator-owned sealed
and observed rebuild stage plus a separate held original in one pinned
directory. It rereviews both exact observations and single-link status,
requires a private stage and creator-owned namespace without group/other write, records each
new expected name immediately after a successful native swap, then rereviews
both held objects, names, bytes, origin and outer metadata. Only ctime is
excluded from the post-swap equality because rename changes it. A failed
postcheck retains both objects and reports `NeedsReview`; it never unlinks or
reverses a name. The seal bit is set only by `SealForVerification`, so a stage
whose writes were merely stopped cannot exchange. This primitive does not
itself supply a durable journal intent, output metadata policy, recovery or
cleanup. A higher-level publication transaction must prove those separately.

The first frozen Mac batch passed 248/250 storage cases with two expected
platform skips, including all three new exchange cases (evidence SHA-256
`991b502871b3e26970f0bda0469e30157b33d2b48d873adef5c9dfc5c7fea944`).
The strengthened source passed 249/251 cases with the same two skips; all
four exchange cases passed, including unsealed-stage refusal (evidence SHA-256
`83ff32f1f4df7c4aea6a8d44cf7382f671c19c90bfc9f192f620e28ee3d42999`).
The host independently counted raw TRX rows and checked the frozen packet and
overlay hashes; the Mac ended without FileCat/test processes. Ubuntu and
Windows matching tests remain queued, and no cross-platform qualification is
claimed from the Mac result alone.

The Windows native update uses a different transactional-data recipe. This
Unix primitive does not enable an update profile or relax the separate recovery,
metadata, crash, confinement and all-platform acceptance requirements.
