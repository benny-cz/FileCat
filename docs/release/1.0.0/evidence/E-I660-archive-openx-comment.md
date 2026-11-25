# I660 — A security comment in the macOS archive writer blames the wrong refusal for leaving out O_NOFOLLOW_ANY

**Found:** 2026-11-24 by the fix for I646's macOS census (PR #32, `bada5da1d`), in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; corrected on branch `fix/i660-archive-openx-comment` for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate).

## Observation

On macOS, `UnixArchiveNative.Create` creates an archive's private staging file with `openx_np`, using `SafeOpen | O_RDWR | O_CREAT | O_EXCL`, where `SafeOpen` is `O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK`. Its comment (`UnixArchiveNative.cs:77`) says that `openx_np` "rejects O_NOFOLLOW_ANY with EINVAL".

While fixing I646, the author found the real rule: XNU's `vn_open_auth` refuses `O_NOFOLLOW` together with `O_NOFOLLOW_ANY` (EINVAL), whatever the call. Either flag alone is accepted. So the refusal once seen here came from that pair, not from `openx_np`.

## Why it matters

The code is safe as it stands:
- `O_NOFOLLOW` covers the final name, and `O_EXCL` refuses any existing entry there, links included.
- The folders above are pinned before the create and their relative identity is reviewed after it, which the comment calls mandatory.

The fault is that a security comment gives a false reason. A later reader could trust `openx_np` less than it deserves, or add `O_NOFOLLOW_ANY` and meet the same EINVAL.

Adding `O_NOFOLLOW_ANY` without `O_NOFOLLOW` is not a drop-in hardening either. FileCat builds this path itself, and under the temporary folder it reaches the file through the `/var` link, which `O_NOFOLLOW_ANY` refuses.

## Remedy

Correct the comment: say what protects the final name and the folders above it, why `O_NOFOLLOW_ANY` is not added, and where the EINVAL came from. The flags and the behaviour stay as they are.

## Fix

Branch `fix/i660-archive-openx-comment`: the comment at `UnixArchiveNative.cs:77` now says that `O_NOFOLLOW` (in `SafeOpen`) protects the final component, that ancestor pinning and the post-create relative identity review protect the folders above it and stay mandatory, and why `O_NOFOLLOW_ANY` is not added: XNU refuses it together with `O_NOFOLLOW` (EINVAL), which is the refusal once taken for `openx_np`'s, and without `O_NOFOLLOW` it would refuse the temporary folder's names, which reach the file through the `/var` link. Comment only: the compiled code is unchanged, so no test is added.
