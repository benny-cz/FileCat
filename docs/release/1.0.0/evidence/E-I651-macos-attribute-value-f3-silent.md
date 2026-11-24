# I651 — macOS: an attribute row offers "F3 to view", but F3 does nothing, so a Finder tag's name never shows

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (b25, I615). The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/b25_finder_tag.txt`). **Status:** open. The owner decides between making the value readable and making the interface say it is not.

## Observation

**Package and machine.** `FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed…`, on the physical M1 Mac, macOS 27.2.1.

**Fixture.** A 31-byte file with a "Blue" Finder tag: a 51-byte `com.apple.metadata:_kMDItemUserTags` attribute. It was copied with F5; the copy's bytes and tag attribute bytes matched the source.

**What the copy's File → Inspect → Extended attributes and resource fork showed:**
- **The row:** `com.apple.metadata:_kMDItemUserTags` — "Extended attribute, 51 bytes, unavailable, Not read — F3 to view".
- **Enter on the row:** "… can be viewed with F3; opening it with an application needs extraction first (F5)."
- **F3:** opened no viewer and showed no message.
- **File → View:** enabled, but opened nothing.
- **"Finder tag: Blue":** never shown.

**On Windows, for comparison.** The same F3 on an alternate data stream's row opens its viewer: batch 30, installed rc.2 in the qualification VM.

## Cause, from the source

`47f7b0630` ("Give macOS file xattrs a qualified refusal", 2026-11-20) makes the macOS value and action adapter refuse a regular file. `src/FileCat.Core/HiddenData/UnixHiddenData.Inventory.cs` throws `NotSupportedException`: "A held-object attribute value/action adapter is not qualified for this macOS object."

The row's "unavailable" reflects that refusal. Its "Not read — F3 to view", the Enter text and the enabled View command still offer the read. F3 then fails without saying why.

I615's acceptance requires the tag's name ("Finder tag: Blue") on the copied file. With the value refused, it cannot pass.

## Why it matters

The interface promises an action that silently does nothing, and a remediated issue (I615) can no longer meet its acceptance.

## Remedy (proposed) and its cost

Either:
- **Make the value readable.** Qualify a value read for a regular file's attribute on macOS, for example reading it through the file's own descriptor with the same bounds. Then F3 works and I615's decoded name shows.
- **Make the interface honest.** Keep the refusal, but have the row, Enter and F3 say that the value cannot be read on macOS, and record I615 as limited by it.

This changes product source, so it reaches users only in a candidate after rc.2.

## Fix

**Choice: the interface says the value cannot be read (b).** The refusal stays, and the row, Enter, F3 and View now say that the value cannot be read on macOS. Qualifying a read (a) was not chosen, because it cannot be made as safe as the reason for the refusal. A value is read only from an object FileCat holds open:
- **Folders.** On macOS, FileCat holds a folder with `O_DIRECTORY | O_EVTONLY | O_NOFOLLOW`. `O_DIRECTORY` makes the open fail before anything other than a folder is acquired.
- **Files: no equivalent open.** The macOS SDK on the test Mac has no `O_PATH` and no flag that restricts an open to regular files. Its flags are `O_NONBLOCK`, `O_NOFOLLOW`, `O_EVTONLY`, `O_SYMLINK`, `O_NOFOLLOW_ANY`, `O_RESOLVE_BENEATH`, `O_UNIQUE`, `O_EXEC` and the rest. Holding a file therefore means an untyped `open(2)` of its name.
- **The open's own effects.** A FIFO or device put at that name between FileCat's check and the open would be opened, and that open acts on its own. The held-folder notes of 20 November recorded an event-only open of a FIFO acting as a reader. A device's driver open runs. FileCat sees the wrong type only after the open has happened.
- **Paths do not bind.** The path calls that list names and sizes (`getxattr` with `XATTR_NOFOLLOW`, which the SDK also offers with `XATTR_NOFOLLOW_ANY`) open nothing, but they hold nothing either. A value they returned could belong to whatever object the name resolved to during the call.

`47f7b0630` refused exactly this, and the owner's inventory notes say so ("rather than opening an arbitrary replacement device with O_RDONLY/O_EVTONLY"). A safe read for a file needs either a typed open from Darwin or a route that reads without opening and still binds the value to the listed object. Neither is qualified here.

**Change.**
- **The reason.** `HiddenInventory` carries why its values cannot be read (`ValueRefusal`, with a short `ValueRefusalSummary` for rows). The macOS path view, used for files, links and special files, says:
  > FileCat reads, copies and deletes attribute values on macOS only for folders. It acts on an item it holds open, and only for a folder can macOS refuse to open whatever is put in the item's place, such as a pipe or a device. Names and sizes are listed.
- **Everywhere it is said.** The same text replaces "A held-object attribute value/action adapter is not qualified for this macOS object." in the refusal, and ends the list's notice.
- **On each row.** Each row reads "Not readable on macOS: FileCat reads attribute values of folders only" instead of "Not read — F3 to view".
- **F3, Alt+F3, File → View and F5.** On such a list these commands are unavailable, with that reason, so the menu and key bar show them dimmed and invoking one says why.
- **Enter.** Enter says: "*name*" cannot be viewed or extracted here, followed by the reason.
- **Errors F3 still reaches.** F3's viewer path now reports a provider's `NotSupportedException` instead of dropping it, so F3 never ends without a word.
- **Incomplete folder lists.** A held folder whose attribute list is incomplete says that the list must be complete first.

Folders keep their full value route. Windows and Linux lists are unchanged.

**I615 is limited by this.** A Finder tag on a folder is read through the held folder and shows "Finder tag: Blue". A Finder tag on a file is listed with its name and its 51-byte size, but its value is not read, so "Finder tag: Blue" cannot show for a file on macOS. I615's acceptance, a tag on a copied file showing its name, cannot pass on macOS while this refusal stands. The copied tag's bytes are still copied and verified (b25's byte check).

**Tests.**
- `HiddenDataTests.Mac_Finder_tag_is_named_for_a_folder_and_a_files_value_refusal_is_explained` (Core, macOS).
  - On a folder, the 51-byte `Blue\n4` tag reads and decodes as "Finder tag: Blue".
  - On a file, the tag is listed with its size. The inventory, its notice, the row's summary and the read refusal all carry the reason, and the provider refuses the value.
- `HiddenDataUiTests.Rows_whose_values_cannot_be_read_say_why_on_the_row_Enter_F3_View_and_F5` (App, every platform, with an adapter that lists a tag but cannot read it).
  - The row shows the adapter's summary.
  - View, Alt+F3 and Copy are unavailable with the reason, and invoking each says it.
  - Enter says the value cannot be viewed or extracted, with the reason. No viewer, dialog or job appears, and no value is requested.
- `HiddenDataUiTests.A_files_streams_and_attributes_open_as_a_list_are_copied_out_and_deleted` and `HiddenDataTests.A_files_own_streams_and_attributes_are_listed_read_and_deleted` now expect the macOS row text, the reason on View, Alt+F3 and F5, and F3's message.

**Results.** On the physical M1 Mac (macOS 27.2.1, .NET SDK 10.0.101, Release):
- **Before:** the rc.2 product source (`ec126ea94`) with these tests, plus declarations of the new members used by nothing, failed all four. The rows read "Not read — F3 to view", and the refusal read "A held-object attribute value/action adapter is not qualified…".
- **After:** all four passed. The focused Core run (sparse copy and supplementary data) had 40 passed, 19 skipped and 0 failed. The focused App run had 35 passed, 13 skipped and 0 failed.

On Windows x64 at medium integrity, the focused App run had 35 passed, 13 skipped and 0 failed. Hosted CI on the pull request is still to run.

**Not verified here.** The Mac GUI harness could not run a build of this source, because the console was locked (see I650). b25 was not rerun.

**What the next installed candidate must check.** On the Mac, open File → Inspect → Extended attributes and resource fork for a file with a Finder tag (b25):
- The row says "Not readable on macOS: …".
- Enter, F3, File → View and F5 each say why, and none opens a viewer or starts a copy.
- The same tag on a folder shows "Finder tag: Blue" and opens with F3.

b25's current expectation that a file shows "Finder tag: Blue" fails by design and needs this narrower acceptance, or I615 recorded as limited on macOS.
