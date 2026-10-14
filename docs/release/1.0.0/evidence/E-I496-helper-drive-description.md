# E-I496 — a recovery session accepted an impossible drive description from its helper

I17/I106/V06/V09, 2026-10-15. **A trust-boundary gap, corrected with eight controls.** No candidate, publication or human GO follows.

## The gap at exact parent `b67caad8`'s predecessor

A recovery session asks the administrator helper one `Info` question and takes the answer as the drive's size and sector size. `PipeDeviceSource`'s constructor read both out of the reply and stored them without a check, although that answer is the one thing in the session nothing else can corroborate, and `Length` becomes the bounds of the scan, of `WindowSource`, and of the progress arithmetic.

A fake helper that answers one `Info` request proves seven descriptions were all accepted:

| Description | Before | Fixed |
|---|---|---|
| length `0` | accepted | refused |
| length `-1` | accepted | refused |
| length `long.MaxValue` | accepted | refused |
| sector size `0` | accepted | refused |
| sector size `-4096` | accepted | refused |
| sector size `3` (not a power of two) | accepted | refused |
| sector size `1 MiB` | accepted | refused |
| length 1 MiB, sector size 4,096 (ordinary) | accepted | accepted |

## The correction

The description is validated where it arrives: a positive length no larger than one pebibyte, and a sector size that is a power of two between 512 bytes and 64 KiB. Anything else ends the session with a message naming the drive and the value, instead of carrying it into a scan. `BrokeredDeviceSource` inherits that constructor, so the installed-helper path is covered by the same check.

The read path already bounded its answers — a count below zero or above the request is refused, exactly `count` bytes are read, a nonzero status throws, and zero ends the read — and `RawReadProtocol`'s other replies already checked their counts. This closes the one reply that did not.

## Scope

Eight controls run the real protocol over a real named pipe against a fake helper; seven of them failed before the fix. The full Windows Platform suite passes **452 tests with 36 explicit skips** on the development host, and the same compiled assembly passes on the disposable Windows 11 VM in both sessions — **22 of 22 with four elevated-only skips** in the signed-in medium session, and **22 of 22 with no skips** elevated in that session — alongside the served-length, worker-token and page controls. This is a validation gap, not a demonstrated privilege escalation: the helper is FileCat's own elevated process, its pipe is named per session and its plan is validated separately ([I17](../FILECAT_1_0_RELEASE_ISSUES.md#i17)). It does not qualify the installed helper end to end, other replies' semantics, or a candidate.
