# I645 — An elevated FileCat sweeps the ordinary account's temporary folders and follows links there

**Found:** 2026-11-24 by the B12 review of the modes added since B12 (I641, [PR #28](https://github.com/benny-cz/FileCat/pull/28), finding F1), in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md). `src/` at `main` `f4e42468b` is identical to the tag. **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Observation

At every start, `src/FileCat.App/Services/AppServices.cs:153-158` runs three sweeps of FileCat's temporary folders in the profile:
- `DragStaging.Sweep`;
- `ClipboardVirtualStaging.Sweep`;
- `AppPaths.SweepTemporaryLeftovers`.

None checks the token. So an administrator window, which is the same account with the elevated token and the same profile, runs them as administrator. Elsewhere FileCat already refuses user-profile state at high integrity; for example, it writes no crash log and exports no logs there.

The sweeps follow links:
- `src/FileCat.Core/Operations/DragStaging.cs:109-121` and `:149-157` follow a junction under `temp\drag`, and set every file in its target to `Normal`.
- `src/FileCat.Core/State/AppPaths.cs:264-282` and `src/FileCat.App/Services/ClipboardVirtualStaging.cs:164-179` delete by path through `temp`.

## The review's probe

The review built `FileCat.Core` from the tag. It called these methods from an elevated session, on fixtures it owned in that session's temporary folder, and removed them afterwards. FileCat itself was not run.
- **Junction under `temp\drag`.** Through a planted, backdated junction, a read-only, hidden and system file had its attributes reset to `Normal`.
- **`temp` itself a junction.** The leftover sweep deleted a file in the junction's target.

## Why it matters

A program running as the ordinary account, at medium integrity, can plant such a junction in the account's FileCat temporary folder. The next time the user opens FileCat as administrator, files in folders that only administrators can change have their attributes reset, or are deleted.

**Not run.** The usual opportunistic-lock and object-manager redirection, which would let these deletes reach a file the attacker chooses, was not tried. It is the class of FCSEC-011 and FCSEC-025.

## Remedy (proposed) and its cost

Skip these sweeps in an elevated FileCat, and in a FileCat running as root. Also make them never follow a reparse point or symbolic link, elevated or not. This changes product source, so it reaches users only in a candidate after rc.2.
