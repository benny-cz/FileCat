# E-I684 — While the system waited for the user to approve reading a drive, FileCat said the drive was not responding

**Found:** 2026-11-26, in the owner's consent walkthrough on `v1.0.0-rc.8`, Mac step M8 ([rc.8](E-RC8-TAG-20261126.md)). **Severity:** Low. A misleading notice: it blames the drive for a wait that is the user's own decision. It clears when the user answers, and nothing is read or lost. **Disposition:** Must fix for 1.0.0 under the owner's rule of 2026-11-24 that every known issue is fixed, unless the owner decides otherwise.

## What happened

In M8 the owner asked for a scan of the USB disk, clicked Authorize and scan, and, while macOS's authorization dialog for `authopen` was open, ejected the disk in Finder, as the step asks. The dialog stayed open from 18:09:19 until the owner approved it at 18:14:01 (authd: "Succeeded authorizing right 'sys.openfile.readonly./dev/rdisk4'").

A screen capture at 18:11, taken at the owner's request, showed:
- **The dialog,** still open.
- **The scan tab,** at "Loading… (Esc stops)".
- **FileCat's notice:** "/dev/rdisk4 is not responding. Other drives keep working; its operations continue when it responds again."

After the approval, FileCat reported what M8 expects: "The selected device changed or was removed while opening. Select it again." Crops of the notice and of the scan tab are kept with the walkthrough's notes. The full capture is not kept, because it shows the owner's name in macOS's dialog.

## Why

- **Where the open runs:** a drive scan opens the drive inside the recovery listing, on that drive's lane of FileCat's I/O scheduler (`DeviceIoScheduler`). On macOS the open is `UnixDeviceSource.Open`, which, when the user may not read the drive, waits for `authopen`. `authopen` in turn waits for the administrator's answer in macOS's dialog.
- **The scheduler's rule:** `DeviceIoScheduler` marks a device "not responding" when one of its calls runs longer than its hang threshold, 8 s (`HangThreshold`, plan §6.3), and FileCat then shows the notice above. The rule is meant for SMB, removable media and cloud recall that stop answering.
- **The mismatch:** the scheduler cannot tell a drive that does not answer from a call that waits for a person. **Any answer that takes longer than 8 s shows the notice**, with or without an eject: on macOS to `authopen`, and by the same path on Linux to polkit's UDisks2 dialog or on Windows to the consent for the brokered drive read. FileCat later says "/dev/rdisk4 responds again." when the answer comes.
- **Seen only on macOS:** in M8. In the walkthrough's other steps the owner answered within seconds. Linux and Windows open drives the same way, which is the inference above; it was not observed there.

## What a fix needs

The wait for the user's answer must not count toward the hang threshold, while a drive that stops answering still does. For example, the authorization wait can be marked on the scheduler so that its clock pauses for it and restarts at the answer. That touches the shared scheduler and the three platforms' authorized drive opens. The walkthrough's consent steps run on that path: Linux U2 to U4, Mac M7 to M9, and the GA PC's optional G8.
