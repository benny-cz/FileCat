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

## Not done (possible next steps)

- **Syncing and error states** (Explorer's arrows and red cross) are not in the attributes; they would need the Cloud
  Files API's placeholder state (`CfGetPlaceholderStateFromFindData`) or the Shell's property for it.
- **"Always keep on this device" and "Free up space"** as FileCat commands: today they are in the Shell's context menu,
  which FileCat shows; a command of its own would set `PINNED` or `UNPINNED` as Explorer does.
- **Google Drive for desktop** serves its files as a drive of its own rather than through the Cloud Files API, so its
  states are not covered by this.
- **Linux and macOS:** none of this applies there (no such attributes); a provider's folder is an ordinary folder.
