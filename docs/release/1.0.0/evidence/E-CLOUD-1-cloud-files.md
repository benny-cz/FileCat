# E-CLOUD-1 — OneDrive, Dropbox, iCloud Drive and other cloud providers

Asked by the owner (2026-10-01): icons and support for OneDrive, Dropbox and the like were missing; Salamander has
them. This is what was missing, what FileCat does now, and how it was checked.

## What was missing

- **The providers' marks never appeared.** FileCat asks its restricted Shell helper for the Shell's overlay icons — the
  marks providers draw through their own Shell code — but only inside Git repositories (`FileListControl`: no Git
  status known, no overlay asked for). In a OneDrive or Dropbox folder nothing was ever asked for.
- **Their folders were not among the places.** The drive bar and the location menu (Alt+F1, Alt+F2) list drives, This
  PC, network, phones, working sets, the Registry, Home, Desktop, Documents and Downloads — no cloud folder.
- What already held: a cloud placeholder is never downloaded for an icon, a thumbnail or a preview (the attributes
  that mark it are checked first, I16), and the OneDrive folder's own icon came through as a known folder's.

## What FileCat does now (`b2d96ea`, and the places with this record)

Every provider built on Windows' Cloud Files API is covered alike — OneDrive, Dropbox, iCloud Drive, Box, Nextcloud and
the rest — because the state Explorer shows comes from file attributes a listing already has. Nothing is opened, so
nothing is downloaded, and no provider's Shell code runs in FileCat:

| Attributes | State | Mark (upper left of the icon, the one corner the other marks leave free) |
|---|---|---|
| `RECALL_ON_DATA_ACCESS`, `RECALL_ON_OPEN` or `OFFLINE`, or `UNPINNED` | only in the cloud, or asked to be ("Free up space") | a cloud outline |
| `PINNED` | kept on this device always | a filled tick |
| none of these, in a sync root | on this computer, kept until space is needed | a ringed tick |
| `PINNED` and `UNPINNED` together | the API's "excluded": the provider does not sync it | none |

The sync roots come from the registry (`HKLM\…\Explorer\SyncRootManager\{id}\UserSyncRoots\{SID}`) with the names the
providers give them, read once. They now also appear among the places, after Downloads, with the icon their folder
carries (OneDrive's known-folder icon; Dropbox's and iCloud Drive's from their folders' `desktop.ini`).

## How it was checked

- **Against the owner's real folders**, by attributes alone (a listing, nothing opened): OneDrive 4,000 entries looked
  at (3,482 files only in the cloud, 13 pinned, the rest on this computer), Dropbox 55, iCloud Drive 40. This is where
  the excluded state was found: Dropbox gives its own `.dropbox`, `.dropbox.cache` and `desktop.ini` both pin
  attributes (`0x00180022`, `0x00180032`), and a first version drew them a cloud. They now get no mark.
- **Drawn**, with the app's own window renderer, on the owner's Dropbox and iCloud Drive folders in both themes, and on
  a throwaway folder in temp with a file set `attrib +P` and one `attrib +U` for the two states the owner's folders do
  not hold on their own: every state reads as the table says, and an ordinary file outside a sync root has no mark.
  The pictures showed the owner's file names and were deleted after looking.
- **The places**: the drive bar shows Dropbox, iCloud Drive and OneDrive with their providers' icons.
- **Tests:** `CloudFilesTests` (every row of the table, the three attribute values seen in Dropbox, and roots matched
  by whole path segments), `CloudSyncRootTests` (on the owner's computer: Dropbox, iCloud Drive, OneDrive - Personal,
  each a real folder). App suite 202, Platform suite 131, 0 failed.

## Validation: looking at a cloud folder downloads nothing (asked by the owner)

FileCat already kept away from cloud files' content wherever it reads on its own: the listing flags a placeholder
(`EntryFlags.Offline`), quick view shows a message for one instead of its content ("press F3 to open it
explicitly"), the metadata service reads nothing from one, content search passes over it, and the checksum files
beside files are not read while they are only in the cloud. `CloudBrowsingTests.Looking_at_a_cloud_folder_downloads_nothing`
(gated on `FILECAT_CLOUD_FOLDER`) checks all of it at once on a real provider's folder, with the Windows platform as
the app registers it and the real icon source and its Shell helper:

- the folder listed; for six seconds, as drawn rows ask, every file's icon, four metadata fields (dimensions,
  duration, title, the checksum check) and the checksum check of the row;
- a content search over the folder and its subfolders, and the folder's size counted;
- before and after, the attributes of every file underneath.

On the owner's OneDrive, a folder chosen so that a fault would cost little (five online-only files, 1.1 MB): 42 files
underneath, **all 42 only in the cloud before and all 42 still only in the cloud after**; the five at the top marked as
placeholders in the listing; the search found nothing (it reads no cloud file's content); the size counted
7,012,811 bytes in 42 files from the listing alone.

## Writing in a provider's folder (`edd950a`; I76)

The owner's leave (2026-10-01): "create some testing directory, and then delete it, but only this one".
`CloudWriteTests.Files_in_a_cloud_folder_are_copied_renamed_moved_deleted_and_downloaded_like_any_others` (gated on
`FILECAT_CLOUD_WRITE_ROOT`; run on the owner's OneDrive) makes a folder `FileCat-test-<random>` there with FileCat's
own job, works only inside it with FileCat's jobs, and removes it; from the Recycle Bin it removes only the entries
whose original place was inside it (read from the bin's own `$I` records); it checks that the provider folder's
top-level entries are the same before and after.

| Step | On OneDrive |
|---|---|
| Copy in a text file, 4 MiB of random bytes and a small tree | the same bytes |
| Rename, move inside the folder | the same bytes, the old names gone |
| "Free up space" (the `UNPINNED` attribute) | FileCat shows it bound for the cloud at once; OneDrive made it a placeholder 8–11 s later (`RECALL_ON_DATA_ACCESS`), and FileCat's listing marks it |
| Copy that file out while it is only in the cloud | it downloads as it is read: the same 4 MiB in 0.8–1.0 s, the file then on this device |
| Delete a file permanently; send one to the Recycle Bin | gone; the recycled one found in the bin (and removed from it afterwards) |
| Delete the folders OneDrive keeps in sync, then the test folder | **first run: refused** (I76); after the fix: deleted |

**I76 (Medium, fixed `edd950a`):** OneDrive marks the folders it syncs read-only (here `0x31`, and `0x431` once they
are placeholders), and so does the Shell every customized folder. That mark protects nothing — Explorer deletes such
folders — but `RemoveDirectory` refuses a folder while it is set, and FileCat's permanent delete stopped at each one
with "Access is denied. If the destination is a protected folder, Windows Controlled Folder Access may be blocking
FileCat", which was not the cause. Removing a folder on Windows now clears the mark when it is the reason for a
refusal and puts it back when the folder still cannot be removed (`ReadOnlyFolderTests`, three tests, all failing
without the change). The first run's test folder, left behind by that refusal, was then removed by FileCat's fixed
delete, and its one recycled file purged from the bin; nothing else in OneDrive or in the bin was touched.

OneDrive keeps what was deleted in its own online recycle bin for a while, as it does with anything deleted in its
folder; that is the provider's, not FileCat's.

**Not done here:** Dropbox and iCloud Drive (one test folder was allowed; OneDrive was chosen), and "Always keep on this
device" or "Free up space" as FileCat commands of their own.

## Not done (possible next steps)

- **Syncing and error states** (Explorer's arrows and red cross) are not in the attributes; they would need the Cloud
  Files API's placeholder state (`CfGetPlaceholderStateFromFindData`) or the Shell's property for it.
- **"Always keep on this device" and "Free up space"** as FileCat commands: today they are in the Shell's context menu,
  which FileCat shows; a command of its own would set `PINNED` or `UNPINNED` as Explorer does.
- **Google Drive for desktop** serves its files as a drive of its own rather than through the Cloud Files API, so its
  states are not covered by this.
- **Linux and macOS:** none of this applies there (no such attributes); a provider's folder is an ordinary folder.
