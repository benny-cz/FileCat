# E-V21-U1 — the cable pulled while FileCat copies to and from phones

V21 asks for disconnecting a phone mid-transfer and reconnecting: no false success, no partial file passed off as
whole, no hang, a way to finish. These need the owner's hands at the moment a transfer runs. On 2026-10-01 the owner
was at the phones ("iphone connected and unlocked, i am around, say when you want me to unplug the cable") and pulled
the cable seven times: twice on the iPhone, five times on the Motorola.

- **iPhone:** read only. The owner's leave: "Up to 50 photos", copied into a scratch folder on this PC, never opened,
  compared by size and hash only, deleted right after; nothing on the phone changes. No photo's name was printed (the
  test replaces every name in a message by `<name>`); the scratch folders and a list of the photos' sizes and hashes
  kept between two runs were deleted afterwards.
- **Motorola edge 60 pro:** inside `FileCat-test` only (the standing rule), with three files of 256 MiB of random bytes
  made here; `FileCat-test` was removed at the end of every run.

The test: `MtpUnplugTests` (Windows platform tests), two device tests behind switches: `FILECAT_MTP_UNPLUG=1` copies a
phone's photos off it (A: each photo read to the end of what the phone sends and copied by FileCat twice; B: copies run
until the phone goes; C: FileCat's question answered Retry once it is back, then whole copies again);
`FILECAT_MTP_UNPLUG_WRITE=1` copies the files made here onto the phone until it goes, then off it until it goes again.
A watcher polls Windows' device list four times a second for when the phone left and came back.

## The iPhone

**A (no pulling):** 50 JPEG photos from 22 folders, 231,388,283 bytes listed. The phone sent exactly the listed size for
all 50, and both FileCat copies were byte for byte what it sent (14.5 s each, about 16 MB/s). That answers E-V21-I1's
open question: the JPEGs this iPhone converts as it sends them come off at the size it lists. FileCat closing and
reopening its connection, without the cable pulled, changed nothing: the same bytes for all 50.

| | First pull | Second pull |
|---|---|---|
| The phone left Windows' list / FileCat asked | 58.5 s / 59.3 s | 62.5 s / 64.5 s |
| Where the cut fell | 0 of 2,398,826 bytes of a photo | 4,194,304 of 6,026,466 bytes |
| FileCat said | "The item no longer exists or its folder was removed." (**wrong: I78**) | the same |
| In the destination at that moment | no copy published; the cut photo's staged copy | 2 whole copies; the cut photo's staged copy |
| Back in the list / its storage open | 81.8 s / 81.8 s | 85.3 s / 85.5 s |
| Retry | resumed at 0 bytes; completed | resumed at 4,194,304 bytes; completed |
| Whole copies of all 50 afterwards | 43 as sent before the pull, 7 not (which ones was not recorded) | 43 as sent before, 7 not (#7–#13); two whole copies agree 50 of 50 |

**The seven photos:** after a physical reconnect the phone sends seven photos with other bytes at the same sizes, and
keeps sending them so; FileCat's copies are exactly what it sends now (read again directly, each one). Reopening the
connection without pulling the cable does not do this. The photo cut at 4 MiB and resumed after the reconnect came out
equal to the version sent before the pull, so its bytes after 4 MiB were the same in both versions and the difference
lay in its first 4 MiB, most likely the metadata a conversion writes at the start. FileCat's check before resuming
compared only the 64 KiB before the break; it could not have told, and kept an old start with a new rest (**I79**).
Here that happened to give one whole version; where the two versions differ on both sides of the break it would not.
Where exactly the bytes differ was not measured (the test now records it in 64 KiB pieces; it has not run on the
iPhone since).

## The Motorola (inside `FileCat-test`)

| Pull | FileCat's build | What happened |
|---|---|---|
| 1, copying onto the phone | first fix (`Explain` checks Windows' list) | left the list at 15.1 s; FileCat asked at 16.1 s: "Could not write the file: the device was disconnected. Connect it again and unlock it, then try again." Before Retry the phone held only the file finished before the pull, nothing of the one being written. After Retry: completed; all three files read back from the phone byte for byte; nothing else in the folder |
| 2, copying off it | the same | left 235.5 s, asked 236.4 s: "The item no longer exists or its folder was removed." at 0 of 268,435,456 bytes (**I78 again**); Retry: resumed at 0, completed, all three whole |
| 3, copying off it | + listings that end in an error or come back empty from a phone that is gone say so | the same wrong message at 58,720,256 bytes; resumed there, all three whole |
| 4, copying off it | + a trace of each step | the read failed with 0x8007001F while Windows no longer listed the phone: said "disconnected". The next attempt, a second later, failed with no trace at all and was said to be "no longer" there: **the cause** (below). Resumed at 109,051,904, all three whole |
| 5, copying off it | + the cause fixed | read 0x8007001F, then opening the device 0x80070002, both while not listed: "Could not open the device: the device was disconnected. Connect it again and unlock it, then try again. 8,388,608 of 268,435,456 bytes were copied." Back at 170.4 s; Retry resumed at 8,388,608 bytes after the new check; all three whole. **The test passed** |

**The cause of I78:** .NET does not raise a `COMException` for every failed COM call. "Not found" (0x80070002) arrives
as `FileNotFoundException`, "path not found" (0x80070003) as `DirectoryNotFoundException`, and an unplugged phone
answers "not found" to opening it and to much else. FileCat's device code caught `COMException` only, so these went
straight to the copy, which told the user the file no longer existed. Its listing also took an error from the phone's
enumerator for the end of the folder, and dropped items it could not describe: a phone unplugged while a folder was
listed gave a shorter, or empty, folder with no error.

Also seen in pull 5's trace: back in Windows' list, the Motorola first answered with no storage, then for half a minute
was not in the list at all, then opened with its storage (consistent with the phone coming back charging only and the
owner choosing File transfer). FileCat's device code told the answering phone without storage from a gone one.

## What changed in FileCat (`7ee8e92`; I78, I79)

1. **Device failures** (`WpdSession`): every device call's failure is handled in one place, which now takes the
   not-found exceptions .NET raises in place of `COMException` too. A failure from a device that Windows no longer
   lists, or that does not answer one request for its own description, is "the device was disconnected. Connect it
   again and unlock it, then try again.", whatever the code; the session is then opened anew. Refusals, a busy device
   and unsupported requests are what they say; a connected device's "not found" is still "not found".
2. **Folder listings:** an error from the device's enumerator is an error; items that cannot be described, or an empty
   answer, from a device that is gone are "disconnected", never a shorter folder.
3. **Resuming** (every source, not only phones): the part already copied is kept only when the file's first 64 KiB
   read the same as well as the 64 KiB before the break; otherwise the copy starts again from the beginning, and the
   job says so. On a phone this reads nothing extra: a device reads forward only, so resuming read the part already
   copied anyway, and the start is read first.

## How it was checked

| Check | Result |
|---|---|
| `MtpDisconnectTests` (5; no device: Windows' list and the device's answer are given) | .NET's own exceptions for 0x80070002 and 0x80070003 count as device failures; "not found" from a device not listed, or listed and not answering, says disconnected and drops the session; from a connected device it is "not found"; refusals, busy, unsupported unchanged. With the device-list check taken out, the case of a device Windows no longer lists fails. The handlers themselves need a device: pulls 2–4 (before) and 5 (after) are their check |
| `ResumeTransferTests` (8) | a new case: one byte at the start differs, same size and time: copied again from the start. It fails with the start's check taken out |
| The MTP device tests on the Motorola after each change (`FILECAT_MTP_TEST`, `FILECAT_MTP_READTEST`) | 19 total, 0 failed, 1 skipped (the thousand-file benchmark, behind its own switch); empty folders still list as empty: the phone listed and answering |
| `MtpUnplugTests` on the Motorola, pull 5 | passed: as above |

## Not covered

- **Locking** the phone mid-transfer instead of unplugging it.
- **The iPhone after the final fix.** The message is the same code for every phone and was checked on the Motorola;
  the iPhone's re-sent photos are the unit test's case (a different start: copied again from the start), not rerun on
  the iPhone.
- **A copy onto the phone after the final fix.** Its message was right in pull 1; the fix only widens what is caught.
- **SFTP uploads** keep their own check (`SftpJobs.ResumePoint`: the 64 KiB before the break), looked at afterwards: an
  upload resumes only while the local file's size and modification time are unchanged, and any write to a local file
  changes its time, which a phone regenerating what it sends does not. Nothing to change there.
