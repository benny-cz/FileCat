# E-I689 — From an ordinary window on Windows, FileCat refused every disk scan, saying other FileCat processes might write

**Found:** 2026-11-27, by the owner's GA PC checklist on `v1.0.0-rc.9`, step G8 ([checklist](../FILECAT_1_0_RC3_GA_PC_CHECKLIST.md)). **Severity:** Medium. From FileCat's usual window on Windows, deleted files could not be recovered from any disk, and the message sent the person looking for FileCat windows that did not exist. FileCat started as administrator could scan. Nothing was read or written. **Disposition:** Must fix for 1.0.0, in rc.10 with I688 (the owner chose "Fix in rc.10" for I688 the same day; I689 follows the owner's rule that every known issue is fixed).

## What happened

In G8 the owner chose the USB stick in Tools → Recover deleted files… and clicked Scan in the ordinary window. FileCat answered at once, before Windows' prompt:

"FileCat does not scan disk 1 (UFD_2.0 Silicon-Power8G) yet: other FileCat processes may still write to this disk. Close other FileCat windows, wait for their helpers to finish, then ask for the scan again. If none are open, check that process information is accessible"

No other FileCat window was open. In FileCat started as administrator, the scan showed the stick's content at once.

## Why

Before a disk scan, FileCat takes a census of FileCat processes, since another one could write to the disk while it reads. Anything but a clear census refuses. Two things kept it from being clear in an ordinary window on Windows.

1. **What standard rights can see.** Without administrator rights, Windows does not let a program read the executable of many processes. On the release session's host 139 of 462 processes could not be identified that way (services, `csrss`, `lsass` and others). An unidentified process might be FileCat, so the census was never complete, and FileCat refused. As administrator only `vmmem` (a Hyper-V virtual machine's memory) could not be identified.
2. **FileCat's own worker.** A Windows FileCat without administrator rights starts a Cloud Files query worker, `FileCat.exe --storage-cloud-query`, within a second of opening, and keeps it for as long as it runs. The census counted it as another FileCat. So every scan from an ordinary window was refused, whatever was done before. An administrator window does not start it at once: in rc.9's W2 by tool, the ordinary window's worker started 1 s after the window, and the administrator window started only its Shell helper. That is why the administrator window could scan.

The first was found from the code and a census probe. The owner tested the first fix on the owner's own build and saw the same message, which led to the second.

## The fix

On `fix/i688-download-mark-nul`, for rc.10:
- **`23f39bbe`:** an ordinary window reads a drive only through the administrator helper anyway. So a census that standard rights cannot complete goes on to the helper. Before it asks anything, the helper takes the census again with full rights: the installed `FileCat.exe --filecat-recovery-process-census` beside it, as polkit's `pkexec` runs it on Linux. It refuses the read unless that census is clear. A FileCat the ordinary window can see still refuses at once. `vmmem` and `vmmemWSL` join the kernel processes the census passes over.
- **`13f203c7`:** FileCat's own workers that write nothing are part of it, not another FileCat. A FileCat process is passed over as such only if all of these hold:
  - its recorded parent is this FileCat, or the FileCat that asked the helper's census;
  - it started after that parent;
  - its arguments are exactly the Cloud Files query worker's or the picture worker's.

  Archive workers, the context-menu helper, any other FileCat, and the same worker started by another process still count, as does anything Windows does not say.

## Checks

- **The real worker, at standard rights, on the release session's host:**
  - Before `13f203c7`: once the Cloud Files query worker ran, the census said another FileCat was running, which is G8's refusal.
  - After it: that worker is the window's own. The same worker started by another process still counts, except in the census of the FileCat that started it, as the helper takes it.
- **The census tests at `13f203c7`, at standard rights:** 117 run, 0 failed, 29 skipped (other platforms).
- **The helper's census, as administrator:** `FileCat.exe --filecat-recovery-process-census` printed a clear census in 9 s on the release session's host.
- **G7-then-G8 by tool in the Windows VM, on installed bytes ([rc.9](E-RC9-TAG-20261126.md#i689-in-the-windows-vm-by-tool-2026-11-27)):**
  - **The setup:** in the test account's ordinary window, a copy first, then Recover deleted files on a test drive, T:.
  - **rc.9:** refused the scan with G8's text, before any prompt.
  - **The fixed build (`13f203c7`):** asked to scan T:. The window started the helper, elevated, which started the installed `FileCat.exe --filecat-recovery-process-census` for that window, and only then showed its own window. Approved, the scan listed T:'s two deleted files as recoverable.
  - **With a second FileCat window open:** both builds refused, as they must.
- **The owner's retest:** with an unsigned test Setup built from the branch at `4c43d63b` on the release session's host (SHA-256 `6f2d2da3a31252d28d7e33311f63cf06b31c66b0227343d936cccd54935c6ee7`), G7's pack and then G8's scan of the USB stick from the ordinary window passed ("Both passed", 2026-11-27).
- **Still to come:** rc.10's tagged run, and the owner's G8 on rc.10.
