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

The subsequent [cross-directory probe](../../../eng/validation/archive-writing/probe_unix_crossdir_exchange.py)
(SHA-256 `36822272f916c766dd20bbbf09d3358fcd515e78df4b9e8e136797d5aced2b9e`)
used separate pinned directory descriptors on the same task volume. Its fresh
stage was in a `0700` private directory and its original was in a separate
directory; both files had ordinary `0644` mode. The exact
`renameat2(RENAME_EXCHANGE)` and `renameatx_np(RENAME_SWAP)` calls succeeded on
Ubuntu ext4 and Mac APFS respectively. Each public name then held the stage's
identity and bytes while the private stage name held the original's identity
and bytes. Swapping back restored both; an absent public name returned `ENOENT`.
The probe synced **both** directory descriptors after each successful exchange
and removed only its own fixtures. This is native feasibility evidence for a
private staging parent; it is not a production transaction or a durability
claim across power loss. The local Mac `renameatx_np(2)` manual and the
[Linux rename manual](https://man7.org/linux/man-pages/man2/rename.2.html)
describe the native exchange operations. The conclusion that a private parent
can keep a mode-adjusted stage unreadable before publication is an inference
from the tested directory permissions and the held parent design; it still
requires adversarial live validation and a complete security policy.

**Next design candidate:** reserve and pin a private sibling directory on the
same volume, prepare the stage there, apply/review the admitted original outer
security metadata before exchange, and journal *both* parent identities and
exact leaf names. After the atomic cross-directory exchange, reconcile the
public stage and privately retained original using their held handles, then
sync both parents and append the observed result. Recovery must inspect both
names and identities without automatic path cleanup or reverse exchange. The
current one-parent `ArchiveTransactionFacts` and unregistered same-directory
`ExchangeRetained` primitive do not encode this candidate. The public target
name remains vulnerable to an equivalent-user substitution between review and
the native call, because neither syscall is conditional on its expected inode.
That adversarial publication gate remains open.

The existing `UnixArchiveFile` also binds each held child to one immutable
parent and uses that parent's lock for all observation and disposal. A
cross-directory swap changes both children's parent namespace. Simply swapping
their parent references after the syscall would let concurrent callers enter
the wrong lock. The implementation therefore needs a pair-owned lease or an
equivalent stable synchronization/ownership design before using this syscall
from product code. The Python probe does not exercise that C# lease lifecycle.

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
overlay hashes; the Mac ended without FileCat/test processes. The strengthened
source also rebuilt on the Windows VM with zero errors or warnings and passed
20/212 storage cases under a real standard account, with 192 expected Unix-only
skips and no failures. All four exchange cases were present and skipped by their
platform guards. The independently checked transport SHA-256 is
`6e52fa18ca5e92b5de3063120c1661d34b5383921de3d2bc396a2316915c44a9`;
its raw TRX rows, packet/source hashes, loaded non-admin profile and clean child
exit were verified. The first Windows runner rejected its otherwise clean 20/212
result because its inventory threshold incorrectly required 50 Windows passes;
that harness failure remains retained and is not counted as a pass. The VM ended
without FileCat/test processes, and a new complete regression batch on the same
source is running. Ubuntu matching tests remain queued. No cross-platform
qualification is claimed from the Mac and Windows results alone.

The Windows native update uses a different transactional-data recipe. This
Unix primitive does not enable an update profile or relax the separate recovery,
metadata, crash, confinement and all-platform acceptance requirements.

The frozen `34d98b51` metadata-guard source initially produced four Ubuntu
exchange failures because the test fixture's temporary parent was writable by
group/other. The primitive correctly rejected that parent before the intended
case assertion. Commit `6222dc55` confines each fixture parent to mode `0700`.
A fresh Ubuntu native batch then passed 271/274 selected Core cases and 199/233
archive App cases, with expected skips and no failures. All six exchange cases
and all 39 RAR5/7z method/password update-stage cases passed. The evidence
transport SHA-256 is
`10817d187830549a5d385ac28706ee15180ff0fa023e68caffd2f42af402179b`.
This still does not prove a durable two-parent update transaction.
