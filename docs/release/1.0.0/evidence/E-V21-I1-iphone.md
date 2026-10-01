# E-V21-I1 — Windows MTP/WPD on a real iPhone: what it allows, shown as such (V21, I72)

The owner connected an iPhone on 2026-10-01 ("iphone connected"), with the rule: "do not delete any current data on
the iPhone, you can delete only yours". Plan: V21 asks for a read-oriented device of the iPhone class, with its
read-only capability shown accurately. Nothing of the owner's was read, copied, changed or deleted; no file name of
theirs is recorded here.

## The device

| | |
|---|---|
| Phone | Apple iPhone (`USB\VID_05AC&PID_12A8&MI_00`), firmware version 27.0.1 as it reports it, protocol "MTP: 1.00" |
| Storage | "Internal Storage", DCF file system, 128 GB, 50.9 GB free, 80 items at its root |
| Driver | Microsoft's MTP class driver (`wpdmtp.inf`, `WUDFWpdMtp` 10.0.26100.9492), the same as the Android phone's (E-V21-M1) |
| Host | Windows 11 Pro 26220, FileCat Debug test builds of `66eaa28` and then `2e93339` |

## What the iPhone says it allows

Read through Windows Portable Devices, read-only (a temporary probe, not kept in the repository):

- **The storage says read-write.** `WPD_STORAGE_ACCESS_CAPABILITY` is 0 (read-write), not 1 or 2 (read-only, or
  read-only with deleting). The storage's own property cannot be what tells FileCat that an iPhone is read-only.
- **The driver's command list says it.** Of the object-management commands it lists only deleting
  (`WPD_COMMAND_OBJECT_MANAGEMENT_DELETE_OBJECTS`): none to create an object with or without data. Of the property
  commands it lists getting only (no `..._PROPERTIES_SET`, so no renaming). Of the resource commands it lists open,
  read and close (no write).
- **Positive control:** the WPD view of a USB drive on the same computer, read the same way, lists creating with and
  without data, writing, committing, deleting and setting properties. The command numbers FileCat reads are those.

## What FileCat did with it before (`66eaa28`): I72

FileCat offered creating folders, renaming and copying onto any device's storage, whatever the device: F7, F2 (rename)
and F5 towards the iPhone were all offered. What a user then got was established with one attempt to create a folder
named `FileCat-test` at the storage root (the owner's rule allows FileCat its own data there, and it would have been
removed at once): the driver refused with **"Could not create the folder: The request is not supported.
(0x80070032) (0x80070032)"**. The root listed the same 80 items, with the same object IDs, before and after.

## What FileCat does now (`2e93339`)

A device's session reads the commands its driver lists when it is opened, and every storage's access when the
storages are listed. Inside a storage FileCat offers reading, and creating, renaming and deleting as far as both allow;
what is not offered is dimmed with its reason, and the device jobs refuse with the same reason before sending anything.

`MtpTests.What_FileCat_offers_in_a_storage_is_what_the_device_allows_there` (`FILECAT_MTP_READTEST=1`,
`FILECAT_MTP_DEVICE=iPhone`), on the iPhone, from the device's list of storages and with the storage opened directly
(a tab restored after a restart), alike:

| | |
|---|---|
| The driver lists | create folders no, create files no, rename no, delete yes |
| The storage says | read-write |
| FileCat offers | listing, reading, deleting |
| F7 | "The device does not let a computer create folders on it; it offers its files to copy off and to delete, as iPhones do." |
| Copying onto it | "The device does not let a computer add files to it; …" |
| Renaming | "The device does not let a computer rename its files; …" |

Passed, nothing written. The general explanations stay where the device does allow something: an iPhone has no
Recycle Bin, so deleting there is permanent after confirmation, as in Explorer and the Photos app.

**Without a device** (`MtpCapabilityTests`, five tests, every run): an iPhone's answers; a device that lists
everything under each storage access; each missing command taking away only its own change; the explanations inside
a storage, at the device level and for a device not opened yet (everything offered, the device decides); and jobs —
copy, move, new folder, rename onto the iPhone's answers and a delete on a read-only card — each refused with its
reason before reaching the device, the moved file left where it was. With the upload's guard taken out, the copy
case fails: the job went on to the device (here a device that does not exist) instead of refusing.

Windows platform suite: 137 total, 0 failed, 25 skipped (the device, VM and live-drive scenarios behind their
switches). Commit CI: run 36894133967 on `2e93339`, all four lanes passed (Windows, Windows ARM64, Ubuntu, macOS).

## Also changed with it

- A COM error message that already ends with its code ("The request is not supported. (0x80070032)") no longer gets
  the code a second time, and "not supported" is told plainly: "the device does not support it".
- The advice for a device that refuses access or shows no storage named only an Android phone's USB options; it now
  also names the iPhone's question to trust the computer.

## Reading off the iPhone (the owner's leave for one photo)

FileCat's copy reads a device file until its reader gives no more, and its reader stops at the size the device listed:
an iPhone that hands over more bytes than it lists would leave a shorter copy without an error. With the owner's leave
("you are allowed to do it"), one photo was read into memory, never saved or shown (a temporary probe; only sizes,
counts and a hash prefix printed): the newest month folder's only picture, a PNG. Listed 496,690 bytes; its stream
gave 496,690; FileCat's reader gave the same 496,690 bytes, byte for byte.

That does not answer the question, since a PNG is never converted. The listings of all 80 folders (names only) hold
263 `.JPG`, 33 `.MOV`, 16 `.PNG`, 2 `.AAE`, one `.MP4` and one `.GIF`, and no `.HEIC`: the phone hands its photos over
as JPEG (transferring with "Automatic", which converts HEIC as it goes, or shooting JPEG). Whether such a JPEG's bytes
are the size it lists is what one JPEG read would show; that needs the owner's leave for one more photo.

## Not done here

- **Deleting:** offered, as the device allows it, and not tried on anything of the owner's.
- **Locked and untrusted states, and unplugging part way:** these need the owner at the phone.
