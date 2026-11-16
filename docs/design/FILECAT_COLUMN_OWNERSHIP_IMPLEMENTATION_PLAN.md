# FileCat: Owner column and useful panel columns — implementation plan

**Status:** plan only. No production code, behavior, settings, ownership or permissions were changed to write it.
**Written:** 2026-11-15 and 2026-11-16. **Baseline:** `main` at `1131471595025d13767ae4078d1ad0edc82ccc7d`
(2026-11-16 09:13:27 +0100). Drafted on branch `codex/archive-writing` at `04bbd2b52f1d7cb306cc529445c7e718d7a38f88`
(2026-11-15 23:20:47 +0100, working tree clean when inspected), which has since merged into `main`. Every cited line
reads the same on both (§2.1).
**Request:** an ACL owner column for Windows panels, the macOS/Ubuntu analogs, and other useful columns on all
platforms, understandable and useful to users.

How to read the evidence marks used throughout:

| Mark | Meaning |
|---|---|
| **[S]** | Read in the source at the baseline; path and line given. |
| **[P]** | Observed in a disposable read-only probe on one machine (§2.4). Not a test result. |
| **[D]** | Stated by vendor documentation; link beside the claim. Says nothing about what FileCat implements. |
| **[G]** | Not verified. An implementation-time gate in §8.3 must settle it. |

Names marked *(new)* are proposals: keep the responsibility, fit the name to the code around it.

## Review status (2026-11-16): read first

The draft is complete. An independent UX and scope review is reconciled below as amendments. They are **binding and
override the sections they name** until they are merged into the text. The platform-correctness and the
architecture/performance reviews were still running when work stopped; their findings are **not** in this document.

| # | Overrides | Amendment |
|---|---|---|
| A1 | §4 row 2, OWN-25, GW4 | A dropped connection does not turn shown values into `error`. Rows already read keep their value with the note (M6); rows not yet read show `error`; one retry per interval. |
| A2 | §3.2, §3.6, §6.6, §6.9, O3 | Inspect, for a metadata column that was not painted: apply `Get`'s three cheap tests first (does the field apply to the item; placeholder; expensive field on a network location, `MetadataService.cs:106-109`), then `Peek`. A field that does not apply adds no line. So that the focused row has values when the column is scrolled out of view, painting also asks for the focused row's metadata columns that are horizontally out of view: one row, a deliberate bounded exception. A value still not read says how to get it: `(not read yet: Right arrow scrolls the columns into view, or View → Analyze folder reads the whole folder)`. |
| A3 | §3.3, §6.9 | For an `Available` value the detail is the field's detail followed by `MetadataValue.Detail` when set (M6's note). An `Absent` value with a reason has no tooltip (the cell is empty) but gets an Inspect line: `Owner: (none) — No longer there.` |
| A4 | §6.10 | Count cloud placeholders apart from other `Unsupported` rows (the pass has the entry's flag). Result grammar, fixed order: "Sorted by <Title>: all N items have a value." or "n of m items have a value."; then, each only when not zero: "x with no access, y cloud placeholders not read, z not available here, f could not be read, g no longer there; they are listed last."; then the name clause, the bound clause, the hint. While waiting for names the status reads "Analyzing Owner: looking up names… (Esc sorts now)"; Esc there skips the wait, sorts, and the result says names were still pending. Reword "reads every item" (`MainViewModel.Workspaces.cs:318`) to "reads the whole folder" and "to compute every value" (`TabViewModel.cs:1156`) to "to read the whole folder". |
| A5 | §7, §8 | OWN-12 belongs to O2 and O4. PF1's baseline is taken before O1; PF1 and PF2 after O4; PF3 and PF4 in O5. GL1 and GM1 belong to O2. OWN-06's tooltip and Inspect half belongs to O3. New rows: OWN-28, name lifetimes, re-asking and the 4,096-entry and 262,144-character bounds (fake clock, `PrincipalNamesTests.cs`); OWN-29, the add-box description, the 160-px default measured headless, the short SID form. OWN-15 also covers "Reread asks names again"; OWN-19 also covers "the Sort button names the field". GA1 is **Not run**: one check on Windows with NVDA that the Inspect box's Owner lines are read (the accepted AC-S3-061 evidence is Ubuntu with Orca only). |
| A6 | §3.5, §6.9, §8.5 | Tooltip trigger. The accepted rule stays: a cell showing a plain value in full has no tooltip (FCUX-061). A metadata cell gets a tip when it is clipped (as today, now with the detail), or when what it shows needs explaining: a state text (`no access`, `—`, `error`), a value kept after a failed re-read, or an ID shown because no name is known. A resolved name in full, a permissions string in full and `…` stay quiet; the Permissions detail is for Inspect only. OWN-22 asserts both directions. §8.5: O3 changes when a tip appears in exactly this way and no other. |
| A7 | §3.5, §6.10 | The hint reads "Add the column in Settings → Columns (or right-click the column header) to see the values." The Settings command also gets the keywords "columns" and "owner" (`CommandRegistry.cs:555`). |
| A8 | §3.3, §6.4 | The type text is "Windows reports a deleted account", never FileCat's own claim. A `Resolved` answer is kept however long it took; the 3-second distrust applies to "not found" only; errors are `Unavailable`. |
| A9 | §3.5, §6.9 | One description per field, used by both the Analyze chooser and the add box (`AnalysisDescription` moves to one shared place; the note is hidden for the six fixed columns). Owner on Windows: "The account each item belongs to (a user, group, or service; not who created or last changed it)". Owner on Linux and macOS: "The user account each item belongs to (not who created or last changed it)". Permissions: "The mode bits: what owner, group, and others may read, write, and run. ACLs, where present, are not shown". Other descriptions are unchanged. |
| A10 | §3.2, §3.5 | An account SID of a computer or domain is shown in the cell as `S-1-5-21-…-<last number>`: an end-clipped full SID would make every account of one domain look alike. The full SID is in the detail, the tooltip and Inspect; sorting uses the full ID; every other ID is shown in full. That `BUILTIN\Administrators` fits in 160 px is [G]; OWN-29 measures it. |
| A11 | §4 row 10, §1.3, §6.11 | MTP panels use the ordinary profiles (`Columns.cs:246`) and belong with row 6: the column is empty there. In panels without file-system paths the cell is empty, as for every metadata column today, and Inspect says why. §1.3's third bullet and §6.11's "the cell says why" apply to file-system panels only. |
| A12 | §3.3, §5.2, §5.3 | `UnixPermissions.Octal` returns `755` and `644` (four digits only with a special bit); the examples written `0755` and `0644` are wrong. |
| A13 | §5.2, §7 | Rank 6 (Links) is "next increment, inside C2". O3: a shown tip is retired when a value or name arrives (`FileListControl.cs:253-258`); the Inspect box is a snapshot. O7 also writes the release note of §6.11 and puts §9.2's "documented" items into `docs/CAPABILITIES.md`. |

## Contents

1. [Executive recommendation](#1-executive-recommendation)
2. [Baseline and evidence](#2-baseline-and-evidence)
3. [Meaning and UX contract](#3-meaning-and-ux-contract)
4. [Platform and provider capability matrix](#4-platform-and-provider-capability-matrix)
5. [Ranked column decisions](#5-ranked-column-decisions)
6. [Implementation design](#6-implementation-design)
7. [Ordered work packages](#7-ordered-work-packages)
8. [Validation and evidence checklist](#8-validation-and-evidence-checklist)
9. [Risks, assumptions and exclusions](#9-risks-assumptions-and-exclusions)
10. [Definition of done and launcher](#10-definition-of-done-and-launcher)

---

## 1. Executive recommendation

### 1.1 First delivery: Owner, where it is real

Deliver one optional **Owner** column for file-system panels on Windows (local drives, UNC paths and mapped SMB
drives), sharing the existing column identity `meta:owner` and header "Owner" with Linux and macOS, plus the
correctness work that makes the column honest on all three systems:

- The value is the **entry's own owner** (a link's, not its target's), read without following the final link.
- The value keeps the **canonical ID** (SID, UID, GID). Account names are looked up separately, off the file's I/O
  queue, with bounded workers, a bounded cache and a retry policy. The raw ID shows at once and is never lost.
- Distinct, explained outcomes: read, name not known here, name lookup unavailable, being read, no access,
  not supported here, gone, failed. No outcome invents an owner.
- The full identity and the reason for an unavailable value are reachable with the pointer (tooltip) and the keyboard
  (Inspect focused row, Ctrl+Shift+I), from cached data only.
- Sorting by Owner is deterministic and never calls a provider or a name service. Analyze folder includes folders,
  keeps every value it computed for the sort, and says what it actually achieved.

Seven work packages (O1–O7, §7) deliver this. Each is reviewable alone and leaves the product working.

### 1.2 What is deliberately not in the first delivery

No other new column. The evaluation in §5 ranks three follow-ups that are worth building next, each with its own
acceptance, and explains why the rest are deferred or rejected:

| Rank | Follow-up | Why next, why not now |
|---|---|---|
| C1 | **Mode (octal)** column on Linux and macOS | Cheap (`UnixPermissions.Octal` exists). The first delivery already shows the octal form in the Permissions cell's detail, so the column is a convenience, not a gap. |
| C2 | **Allocated size** (with **Links** from the same native read) on all three systems | High value for sparse, compressed and cloned files. Each platform measures something different; it needs its own native verification. |
| C3 | **Packed size and ratio** in archive panels | The values are already in the listing tags; it needs a small "listing-data column" route because archive members have no file-system path. |
| N1 | Resolve share-local owners through the file server | Makes NAS and workgroup shares friendlier; adds server-directed lookups that deserve their own review. |
| P1 | Owner and mode from SFTP listings | Provider-native, numeric only; needs the same listing-data route as C3. |

### 1.3 Why users benefit

- A Windows administrator can answer "whose is this?" in the panel: profile shares, leftover installer files, files
  written by a service, items owned by `TrustedInstaller`, an unresolved SID left by a removed account.
- The same column means the same thing on Linux and macOS, and now says the UID or GID behind a name there too.
- A value FileCat cannot read says why, instead of an empty cell that reads as "no owner".
- Sorting a large folder by Owner gives an order that is what the banner says it is.

### 1.4 Product defaults chosen

These settle the ambiguities the request left open. None blocks implementation; D1, D7 and D12 are the ones a product
owner may want to flip, and each flip is small (noted in §6 and §9.4).

| ID | Decision |
|---|---|
| D1 | Windows Owner is **optional**: offered in Settings → Columns and in Analyze folder; in no built-in profile. |
| D2 | One column on all systems: header "Owner", settings key `meta:owner`, field ID `owner`. |
| D3 | The displayed entry's own owner. The final path component is never followed; parent folders are traversed normally. |
| D4 | The stored value is a typed principal (namespace + canonical ID). Names are never stored in it. |
| D5 | Names come from this computer's account lookup only. No lookup is directed at a file server in the first delivery. |
| D6 | One new metadata state, **Denied**; a producer may return a ready `MetadataValue` to report a reason. |
| D7 | Cloud placeholders are not read, in the automatic and the explicit path, for every metadata field. |
| D8 | Accessible route: the Sort menu and Inspect focused row. The spoken row summary does not gain Owner. |
| D9 | Sort order: names A–Z, then IDs without a name (numerically), unknown last in both directions (§6.6). |
| D10 | Analyze folder keeps the values it computed (bounded) and reports counts, not "every value". |
| D11 | Freshness: re-read while painted after 5 s (30 s on network locations), at once on window or tab activation and on Reread; never for unpainted rows. |
| D12 | No new built-in profile, no renamed column, no change to any default profile. |

---

## 2. Baseline and evidence

### 2.1 Source fingerprint and its relation to the reviewed ZIP

- The baseline is `main` at `11314715`. The plan was drafted against the working tree of `codex/archive-writing` at
  `04bbd2b5`, as directed by the owner during planning. That branch was then 494 commits ahead of its merge base,
  `main` at `9cc9f2ac883db59621f2b5f22d65c73fea283c5a` (2026-11-14), and it merged into `main` on 2026-11-16. From
  `04bbd2b5` to the baseline ten files changed. The only one cited here is `MainWindow.axaml.cs`: two lines added at
  833 and one changed at 1027 (menu keys), all below the one range this plan cites there (`:209-216`).
- The ZIP named in the planning prompt (`FileCat(8).zip`, SHA-256
  `61b3026aea934d5d189a24f3c91df4a1e81eba98919e8c7603b0e300429e460e`) is byte-identical to `E:\FileCat\FileCat.zip`,
  built on 2026-11-15 23:14 from the tracked files of the `main` checkout, before the merge. It is **not** this
  baseline.
- Relevant differences between the two, checked file by file for every source cited below:
  - **Identical on both** (line endings aside): `MetadataService.cs`, `Columns.cs`, `ColumnProfilesEditor.cs`,
    `PanelView.axaml.cs`, `FileListControl.cs`, `FileListAutomation.cs`, `MainViewModel.Workspaces.cs`,
    `TabViewModel.Verification.cs`, `EntrySorting.cs`, `ExternalViewBuilder.cs`, `EntryStore.cs`, `UnixPermissions.cs`,
    `UnixFiles.cs`, `UnixFileRecords.cs`, `LocalFileSystemProvider.cs`, `ChangeMonitor.cs`, `PathUtil.cs`,
    `IPlatform.cs`, `WindowsPlatform.cs`, `WindowsFileRecords.cs`, `ElevationBroker.FileTrust.cs`, all of
    `FileCat.Remote/Sftp` and `Ftp` cited here, `ResultSetProvider.cs`, ADR-02/10/12, the UX ledger and workflow, and
    every test file the prompt names.
  - **Different, metadata code unchanged:** `TabViewModel.cs` (navigation only; the metadata section sits 73 lines
    lower), `AppServices.cs` (archive back ends added to composition), `MainViewModel.Commands.cs`,
    `MainViewModel.Operations.cs`, `MainWindow.axaml.cs`, `Entries.cs` (`ItemRef` gained two archive properties),
    `DeviceIoScheduler.cs` (adds `RunOwned` and `StoppingToken`; defaults unchanged),
    `WindowsFileOperations.Native.cs`.
  - **Different and relevant to the matrix:** `ZipProvider.cs`, `ArchiveProvider.cs`, `ArchiveFormats.cs` and the new
    `src/FileCat.Archives/Writing` engines. On the baseline archives are written through native jobs, and the write
    engines parse and preserve member owner fields (§4, row 8). Listings still carry none.
- All line numbers below are the baseline's; Appendix A maps the ones that differ to `main` at `9cc9f2ac`, before the
  merge. Re-check them before editing: other sessions commit to `main`.

### 2.2 Verified existing behavior

| # | Behavior | Anchor [S] |
|---|---|---|
| E1 | A metadata field is a `MetadataField` record: ID, title, cost, name test, producer `Func<string, CancellationToken, object?>`, formatter, optional sort key, `Folders`, `RefreshAfter`. | `src/FileCat.Core/Metadata/MetadataService.cs:43-53` |
| E2 | States are `NotRequested, Pending, Available, Absent, Unsupported, Failed`; a value is `MetadataValue(State, Value, Detail)`. | `MetadataService.cs:20-36` |
| E3 | `Owner`, `Group`, `Permissions` exist for Linux and macOS: `Cheap`, `Folders: true`, `RefreshAfter` 5 s. Owner's producer is `UnixPermissions.Stat(p) is { } s ? UnixPermissions.UserName(s.Uid) : null`. | `MetadataService.cs:393-410` |
| E4 | `BuiltInFields.All` is `[Version, Dimensions, LinkTarget, Zone, Hidden, Verified]` on Windows and `[Permissions, Owner, Group, Dimensions, LinkTarget, Zone, Hidden, Verified]` elsewhere. `MetadataService` registers exactly that list. | `MetadataService.cs:484-485`, `:82-86` |
| E5 | The column chooser is the static `ColumnProfileSet.Choices`, built from `BuiltInFields.All` as `meta:<id>` with a default width of 110. A field registered only at run time (`MetadataService.Register`) would not be selectable. | `src/FileCat.App/Controls/Columns.cs:259-269` |
| E6 | Unix Details has Permissions; Unix Full has Permissions, Owner (90), Group (90). Windows profiles have neither. There is no Security profile. | `Columns.cs:64-100`, `:102-137` |
| E7 | Profiles persist as keys (`KeyOf`), at most ten, always with Name; `Create` returns null for an unknown key and `Parse` drops it. | `Columns.cs:228`, `:336-384` |
| E8 | Result sets, Registry, recovery, hidden data, journal and Recycle Bin have fixed layouts; ZIP (`zip`) and other archives (`arc`) use the ordinary profiles. | `Columns.cs:246-257` |
| E9 | A metadata cell needs a file-system path: `GetMetadataText` returns empty when `item.FileSystemPath` is null. The same test guards the sort key and Analyze. | `src/FileCat.App/ViewModels/TabViewModel.cs:1127`, `:1166`, `:1215`; `src/FileCat.Core/Resources/Entries.cs:189` |
| E10 | Demand comes from painting: `RenderRow` calls `GetMetadataText` for metadata columns that are in the profile and horizontally in view; the service drops queued work for rows no longer visible. | `src/FileCat.App/Controls/FileListControl.cs:1296`, `:1331-1342`, `:1230`; `MetadataService.cs:185` |
| E11 | Production runs on the per-device scheduler at `Background` priority: two workers a device, up to six when one hangs past 8 s. Requests beyond 256 outstanding a device are dropped and asked again. | `MetadataService.cs:165-181`; `src/FileCat.Core/Threading/DeviceIoScheduler.cs:47-52` |
| E12 | The cache holds 50,000 values across all fields and paths, first in first out, keyed by field, modified time, size and path. | `MetadataService.cs:62`, `:97`, `:249-256` |
| E13 | Stale publication is refused: `Forget`, `ForgetFolder` and `Invalidate` mark running productions not current. | `MetadataService.cs:75-80`, `:241-246`, `:326-358` |
| E14 | Reread invalidates all metadata, and so does a finished Attributes job. | `src/FileCat.App/ViewModels/MainViewModel.Commands.cs:237`; `MainViewModel.LocationContext.cs:122`; `MainViewModel.Operations.cs:704` |
| E15 | Sorting by a metadata field calls the key provider twice per comparison; unknown keys sort last in both directions; ties fall to name, then store index. | `src/FileCat.Core/Listing/EntrySorting.cs:55-74` |
| E16 | Every `Load` and every applied `Refresh` uses a new `EntryStore`; the store instance is the listing identity tabs compare. | `src/FileCat.Core/Listing/ListingModel.cs:395`, `:444`; `TabViewModel.cs:1130` |
| E17 | Hover shows a tip only for clipped cells (and the Verified column's own tip); Ctrl+Shift+I reports every column of the focused row from rendered text. Neither starts metadata work. | `FileListControl.cs:1812-1845`, `:1867-1900`; `src/FileCat.App/Views/PanelView.axaml.cs:1395-1411` |
| E18 | The spoken row summary (`DescribeItem`) has kind, size, modified, marks, Git, warnings and cached verification. No other metadata column enters it. | `FileListControl.cs:2217-2238` |
| E19 | The Sort button's menu lists every sortable column of the tab, metadata columns included. The header menu offers "Set up columns…". | `PanelView.axaml.cs:1035-1075`, `:1141-1173` |
| E20 | Windows already reads owner SIDs for the file-system record inspector, through a handle opened with `READ_CONTROL` and `FILE_FLAG_OPEN_REPARSE_POINT`, and translates them with `SecurityIdentifier.Translate`. | `src/FileCat.Platform.Windows/WindowsFileRecords.cs:657-715`, `:902-921` |
| E21 | Unix reads the entry's own mode, UID and GID (`statx` with `AT_SYMLINK_NOFOLLOW` on Linux, `lstat` on macOS) and resolves names through a 256-entry, 32,768-character cache with numeric fallback. | `src/FileCat.Core/FileSystem/UnixPermissions.cs:109-140`, `:164-246` |
| E22 | A platform-backed field already exists: `BuiltInFields.HiddenDataSource` is set from `Platform.HiddenData` at composition. | `MetadataService.cs:461-471`; `src/FileCat.App/Services/AppServices.cs:44`; `src/FileCat.Core/Platform/IPlatform.cs:19` |

### 2.3 Concrete gaps this plan closes

| # | Gap | Anchor [S] | Closed by |
|---|---|---|---|
| G1 | Windows has no Owner field, so no chooser entry, no column, no sort. | `MetadataService.cs:485` | O4 |
| G2 | Analyze folder filters Owner out on Windows by ID. Registering the field alone would leave it hidden there. | `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs:310` | O4 |
| G3 | Analyze skips every `IsContainer` entry although Owner, Group, Permissions, Link target and Streams declare `Folders: true`. | `TabViewModel.cs:1215` | O5 |
| G4 | Analyze's completion text claims "every value computed" whatever was skipped, failed or evicted. | `TabViewModel.cs:1231` | O5 |
| G5 | The 50,000-value cache can evict an analysis's earlier values before the sort that needs them. Source-derived, not measured. | `MetadataService.cs:62`, `:252-256` | O5 |
| G6 | The sort key provider calls `Get`, which schedules work on a miss or a due refresh (dropped later by its `() => false` demand test) and takes the publication lock, per comparison. | `TabViewModel.cs:1167`; `MetadataService.cs:111-119`, `:148-164` | O5 |
| G7 | Sort keys are read live per comparison; a value arriving mid-sort changes a key under the sort. | `EntrySorting.cs:61-62` | O5 |
| G8 | `Compute` reuses any cached `Available` or `Absent` value without consulting `RefreshAfter`, and stores without recording when, so a value Analyze produced is never refreshed by later painting. | `MetadataService.cs:134`, `:139`, `:114`, `:156-159` | O1 |
| G9 | `Compute` has no cloud-placeholder gate; `Get` has one. An explicit analysis of Dimensions or Version reads placeholder content. | `MetadataService.cs:107`, `:130-146` | O1 |
| G10 | A producer can only say "value" or "null"; null becomes `Absent`. Denied, unsupported and gone all collapse into an empty cell on Unix. | `MetadataService.cs:207-222`; `UnixPermissions.cs:109-121` | O1, O2 |
| G11 | `MetadataValue.Detail` (the reason) is stored and never shown anywhere. | no consumer in `src` | O3 |
| G12 | Unix Owner and Group store only the display string: the UID is lost once a name resolves, and unresolved IDs sort as text ("10" before "9"). | `MetadataService.cs:402-404` | O2 |
| G13 | Unix name lookups run inside the producer on the device queue and can block it; the name cache never expires, so a numeric fallback cached during an outage stays. | `UnixPermissions.cs:167-170`, `:197-198` | O2 |
| G14 | `LinuxStat` reads UID and GID from the buffer without checking that the kernel returned them. | `UnixPermissions.cs:127-130` | O2 |
| G15 | With a metadata sort on a column the profile does not show, the Sort button describes it as "Metadata". | `TabViewModel.cs:142-147` | O5 |
| G16 | The Analyze description of Owner says "The user each item belongs to", too narrow for Windows. | `MainViewModel.Workspaces.cs:333` | O4 |
| G17 | Documentation disagrees with the source (§2.5). | — | O7 |

### 2.4 Probe observations

One disposable PowerShell probe (P/Invoke through `Add-Type`), run 2026-11-15 on Windows 11 Pro build 26320, x64, NTFS,
in an elevated shell, in a scratch folder that was removed afterwards. It read; it changed nothing but its own folder.

| # | Observation [P] |
|---|---|
| P1 | A junction to `C:\Windows`, opened with `READ_CONTROL` and `FILE_FLAG_OPEN_REPARSE_POINT`, returned the junction's own owner. Without the flag it returned the target's (`S-1-5-80-956008885-…`, TrustedInstaller). |
| P2 | `GetNamedSecurityInfoW` on the same junction also returned the junction's own owner on this build. Its documentation is silent on links, so the plan does not rely on it. |
| P3 | A handle opened with access 0 fails `GetSecurityInfo(OWNER)` with error 5. `READ_CONTROL` works for files and, with `FILE_FLAG_BACKUP_SEMANTICS`, for folders. |
| P4 | `C:\System Volume Information` fails to open with error 5 even elevated; a missing file with error 2; `C:\pagefile.sys` with error 32 (sharing violation). "In use" is a real outcome beside "denied". |
| P5 | `LookupAccountSidW(NULL, …)`: TrustedInstaller → domain `NT SERVICE`, use 5; `S-1-5-32-544` → `BUILTIN\Administrators`, use 4; `S-1-5-18` → `NT AUTHORITY\SYSTEM`, use 5; `S-1-1-0` → name `Everyone` with an **empty domain**; the current user → machine name and use 1. An unknown account-domain SID and `S-1-22-1-1000` both failed with 1332 in about 0.1 ms on this workgroup machine. |
| P6 | 2,000 owner reads (open, query, close) over files in `C:\Windows\System32` took 227 ms cold and 91 ms warm: about 114 and 45 microseconds an item, three distinct owners. |
| P7 | No FAT or exFAT volume was mounted. What Windows reports there is unverified (gate GW3). |

### 2.5 Documentation that disagrees with the source

| Document | Says | Source says [S] |
|---|---|---|
| `docs/adr/ADR-10-metadata-scheduling.md:9` | "at most four outstanding per device" | The constant 4 is only multiplied by 64 into a 256-request backlog guard; concurrency is the scheduler's two workers (six with replacements). `MetadataService.cs:63`, `:166` |
| `ADR-10:16-17` | Built-in fields are version, dimensions, link target and origin | Also permissions, owner, group, hidden and verified. `MetadataService.cs:393-481` |
| `docs/CAPABILITIES.md:46` | Permissions, Owner and Group are "in the default Details and Full profiles" | Details has Permissions only. `Columns.cs:66-72` |

Neither queue number is treated as a performance guarantee in this plan.

---

## 3. Meaning and UX contract

### 3.1 What Owner means

**Owner** is the principal that the file system records as the owner of the listed file or folder itself.

It is not the person who created the item, not its author, not who last changed it, not the user who is signed in or
the account FileCat runs as, and not the list of accounts that may open it. It is not an effective-access calculation.

| System | What is read | What is shown |
|---|---|---|
| Windows | The owner SID in the entry's security descriptor (ownership is a part of the descriptor, separate from its DACL and SACL). The owner can be a user, a group, a built-in identity or a service. | The qualified account name when this computer has one (`CONTOSO\alice`, `WORKSTATION\alice`, `BUILTIN\Administrators`, `NT AUTHORITY\SYSTEM`, `NT SERVICE\TrustedInstaller`), otherwise the SID. A name whose domain part is empty shows alone (`Everyone`) [P5]. |
| Linux, macOS | The owning UID (Owner) and GID (Group) of the entry, as the mounted file system reports them. | The account name from this system's name service, otherwise the number. UID 0 is `root` with ID 0, a real value. |

**Permissions** on Linux and macOS stay what they are: the nine mode bits and the set-ID and sticky bits. They are not
an ACL, not effective access, and say nothing about mount options or platform policy. An ACL can allow or deny more;
when an ACL carries named entries, the group bits show its mask, not the owning group's rights
([acl(5)](https://man7.org/linux/man-pages/man5/acl.5.html) [D]). The Permissions detail says so (§3.3).

### 3.2 States

One table for cell text, the detail behind it, and behavior. "Detail" is what the tooltip and Inspect focused row show.

| Situation | State | Cell | Detail (examples) | Retry |
|---|---|---|---|---|
| Owner read, name known | `Available` | `CONTOSO\alice` | `User account · S-1-5-21-3623811015-3361044348-30300820-1104` | Value re-read per §6.7; name kept 10 minutes |
| Owner read, no name here | `Available` | `S-1-5-21-…-1013` (the full SID; the cell clips) | `This computer has no name for this ID. It may belong to another computer or domain, or to an account that no longer exists.` | Name asked again after 2 minutes |
| Owner read, lookup failed or slow | `Available` | the ID | `The name could not be looked up just now (the domain or name service did not answer). FileCat asks again shortly.` | Name asked again after 30 seconds |
| Owner read, lookup queued | `Available` | the ID | `Name lookup in progress.` | — |
| Ownership being read | `Pending` | `…` (muted, as today) | `Being read.` | — |
| Reading the owner was refused | `Denied` *(new)* | `no access` | `Windows did not allow reading this item's security information. The item has an owner; FileCat may not see it.` or `The file is open exclusively by the system or another program.` | With the value, per §6.7 |
| No owner concept here | `Unsupported` | `—` | `exFAT volumes keep no owner.` / `Not read for cloud placeholders (reading could download the file).` | With the value |
| Item no longer there | `Absent` | empty | `No longer there.` | With the value; the row leaves at the next listing refresh |
| Transient failure | `Failed`, or the last value kept | `error`, or the last value | `The network path was not found. FileCat tries again while the row is shown.` When a value was read before: `Could not be read again just now (…); showing the last value read.` | With the value |
| Column not painted yet | `NotRequested` | empty | Inspect says `(not read yet: the column is not in view)` | None until painted |
| Not applicable (parent row, drive, share) | — | empty | — | — |
| Location without file-system paths | — | empty | Inspect says `(not available for items in this location)` | — |

Rules behind the table:

- A valid ID whose name cannot be found is ownership data, not a failure. The text never asserts that an account was
  deleted: `LookupAccountSidW` returns the same error for a missing account and for a lookup that timed out
  ([LookupAccountSidW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountsidw) [D]).
- `Denied` never implies that the item has no owner.
- Nothing is requested for a row, tooltip, accessibility query or sort when the column is not painted.
- No state uses a warning color. Another account owning a file is not a fault.

### 3.3 Detail text

The detail is cached data formatted for reading. It is built from the cached value and the name cache, never from I/O.

| Field | Detail |
|---|---|
| Owner, Windows, resolved | `<type> · <SID>`. Types from `SID_NAME_USE`: `User account`, `Group`, `Local group` (alias), `Built-in identity` (well-known group), `Computer account`, `Deleted account` (only when Windows itself reports that type), otherwise no type ([SID_NAME_USE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-sid_name_use) [D]). |
| Owner or Group, Unix, resolved | `User · UID 1001` / `Group · GID 20` |
| Owner or Group, Unix, unresolved | `This system has no user name for UID 1001.` / `… no group name for GID 20.` |
| Permissions | `Octal 0755 · mode bits only; an ACL or the mount can allow or deny more.` (`UnixPermissions.Octal`, `UnixPermissions.cs:42`) |
| Any field, not `Available` | The reason from `MetadataValue.Detail`, as a sentence. |

Tooltip: `Owner: CONTOSO\alice` on the first line, the detail on the next. Inspect focused row:

```
Focused item: budget.xlsx
Size: 48,2 kB
Modified: 12.11.2026 09:14
Owner: CONTOSO\alice
    User account · S-1-5-21-3623811015-3361044348-30300820-1104
```

and for an unavailable value:

```
Owner: no access
    Windows did not allow reading this item's security information. The item has an owner; FileCat may not see it.
```

All of it passes through `Formatters.SafeName` (`src/FileCat.App/Services/Formatters.cs:205`), as cell text already
does (`FileListControl.cs:1335`): account names come from directories and remote systems and are untrusted text.

### 3.4 Links and special entries

| Entry | Behavior |
|---|---|
| Symbolic link, junction, mount point | The link's own owner. Windows: the handle is opened with `FILE_FLAG_OPEN_REPARSE_POINT`, documented to return "a handle to the symbolic link" rather than the target ([CreateFileW, Symbolic link behavior](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) [D]; junction observed [P1]; file symbolic links and ARM64 [G] GW1). Unix: unchanged `AT_SYMLINK_NOFOLLOW` / `lstat` ([statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) [D]). |
| Broken link | Still the link's own owner: the link object exists without its target [G] GW2. |
| Folder | Its own owner. Nothing inside it is read. |
| Parent row `..` | No value. `Applies` admits only `File` and, for folder fields, `Directory` (`MetadataService.cs:126-127`). |
| Drives, servers, shares, Registry keys | No value: these are other providers' rows without file-system paths [S] E9. |
| Archive file in a folder listing (`EntryFlags.Container`) | It is a file: its own owner. The flag marks a file that can be entered, not a directory. |
| Cloud placeholder (`EntryFlags.Offline`) | `—` with the reason. Not opened (§6.8). |

Not following the final component is also a safety property: a junction or symbolic link planted in a listed folder
cannot redirect the read to a server of someone else's choosing and cause an outbound sign-in attempt.

File systems without ownership:

- **Windows, FAT and exFAT.** These volumes do not preserve ACLs ("the NTFS file system preserves and enforces ACLs,
  and the FAT file system does not",
  [GetVolumeInformationByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew) [D]).
  Windows is expected to answer with a placeholder owner, `Everyone` (`S-1-1-0`) [G] GW3. FileCat shows `—` with
  "…volumes keep no owner", decided by the volume's `FILE_PERSISTENT_ACLS` flag, never by guessing from the name.
- **Linux and macOS, FAT, exFAT and some network mounts.** The kernel reports the owner the mount was given. FileCat
  shows what the mounted file system reports, as `ls -l` does, and the documentation says so. If the kernel does not
  return a UID or GID at all, the cell is `—` (G14).

### 3.5 Defaults, width and discovery

- **Defaults.** No built-in profile changes (D1, D12). A custom profile is never rewritten. Profile shortcut numbers
  do not move.
- **Width.** The chooser's default for Owner on Windows is 160 px: `BUILTIN\Administrators` and `NT AUTHORITY\SYSTEM`
  fit at the default font; longer names clip and get the tooltip. Linux and macOS keep 110 in the chooser and 90 in
  the built-in Full profile. The Name column keeps its fill share; horizontal scrolling, border dragging, large fonts
  and high DPI behave as they do for every metadata column.
- **Discovery.** The established flow only: header menu → Set up columns… → Settings → Columns → Add. The add box
  gains one line of description for the selected column (§6.9). Analyze folder lists Owner on Windows. Command search
  finds Analyze folder by "owner" and "ownership".
- **Explanatory copy touched** (nothing else is reworded):

| Place | Now | Becomes |
|---|---|---|
| Analyze folder, Owner (`MainViewModel.Workspaces.cs:333`) | The user each item belongs to | The account each item belongs to (a user, group, or service; not who created or last changed it) |
| Analyze folder command (`src/FileCat.Core/Commands/CommandRegistry.cs:502`) | …(versions, picture sizes, link targets, where files came from)… | …(versions, picture sizes, link targets, owners, where files came from)… |
| Add-column note, Owner *(new)* | — | The account the file system records as the item's owner. Not who created or last edited it, and not who may open it. |
| Add-column note, Group *(new)* | — | The group the file system records for the item. |
| Add-column note, Permissions *(new)* | — | Mode bits for owner, group and others. ACLs, where present, are not shown. |
| Add-column note, Streams / Attributes *(new)* | — | Windows: Alternate data streams the item carries, counted. Linux, macOS: Extended attributes the item carries, counted (not the R/H/S flags of Attr). |

### 3.6 Accessibility

- **Sort.** The Sort button's menu already lists Owner when the profile has it [S] E19. A metadata sort on a column
  the profile does not show is described by the field's title instead of "Metadata" (G15).
- **Reading a value.** Inspect focused row (Ctrl+Shift+I, or the context menu) is the keyboard and screen-reader route.
  It opens the existing read-only "Full values of focused item" box. It gains the detail lines of §3.3 and, for a
  metadata column that was not painted (scrolled out horizontally at a large font, say), the cached value through
  `MetadataService.Peek`, which never starts work.
- **What does not change.** The spoken row summary does not gain Owner. A value that arrives, and a name that resolves
  a moment later, would otherwise change the focused row's spoken value twice and repeat it. No accessibility node is
  added per row. No lookup happens for an off-screen peer.

---

## 4. Platform and provider capability matrix

Four things are separate and stay separate: the host system, the provider that lists the item, what the file system
can report, and how one read turned out. A Windows FileCat showing an SFTP folder has no Windows security descriptor
for those items and must not show one.

| # | Context | Today | First delivery | Where the data comes from | Limits and fallback | Deferred |
|---|---|---|---|---|---|---|
| 1 | **Local Windows** files and folders (NTFS, ReFS; x64 and ARM64) | No Owner | Owner, files and folders | `CreateFileW` with `READ_CONTROL`, all sharing, `FILE_FLAG_BACKUP_SEMANTICS` and `FILE_FLAG_OPEN_REPARSE_POINT`; `GetSecurityInfo(SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION)`; names from `LookupAccountSidW(NULL, …)` | FAT and exFAT: `—`. Refused or in use: `no access`. No name: the SID. ARM64 runs the same code [G] GW1 | — |
| 2 | **Windows UNC paths and mapped SMB drives** | No Owner | The same column through the same file-system provider. Reads queue per server: the device key of a UNC path or a mapped drive is its server (`src/FileCat.Platform.Windows/WindowsFileSystemProvider.cs:13-23`) | The same calls over the session Windows already has to that server. No new sign-in and no prompt: these calls cannot show one. Names by this computer's lookup: domain accounts and built-in identities resolve; a server's own local accounts and Samba's `S-1-22-…` IDs stay IDs [P5] | An open, a query and a close per row, each a round trip [G] GW4. Shown values are re-read every 30 s, not 5. A dropped connection gives `error` with the reason and a retry at that pace | N1: ask the server for names the local lookup does not know |
| 3 | **Local macOS** | Owner, Group, Permissions | Unchanged columns; values gain the ID, the detail, numeric order for unnamed IDs, honest failure states | `lstat` (`lstat$INODE64` on x64); `getpwuid_r`, `getgrgid_r` | Apple silicon is the supported architecture (ADR-12). [G] GM1 | — |
| 4 | **Ubuntu / Linux** | Owner, Group, Permissions | As macOS, plus the returned-mask check of G14 | `statx` with `AT_SYMLINK_NOFOLLOW`; `getpwuid_r`, `getgrgid_r` through the system's name service, which can block (LDAP, SSSD), so lookups leave the device queue | glibc before 2.28 has no `statx`: `—` with the reason, as the code already allows for (`UnixPermissions.cs:116-119`). [G] GL1 | — |
| 5 | **Mounted network and removable file systems** | As 3 and 4; on Windows as 1 | What the mounted file system reports, stated as such | Unix: the mount's UID and GID numbers, named by this system's name service, exactly as `ls -l` does. Windows: a removable NTFS drive from another computer carries that computer's SIDs, which stay IDs | Unix: the numbers mean what the server or the mount options say, not necessarily this system's accounts. All local mounts share the device key `/` (`src/FileCat.Core/FileSystem/PathUtil.cs:159-165`), so a stalled mount delays other local folders' metadata until the scheduler adds workers. Existing behavior, unchanged | Per-mount device keys on Unix |
| 6 | **SFTP** | No metadata columns: items have no file-system path. The listing contract has no owner, group or mode (`src/FileCat.Remote/Sftp/Channel.cs:8-28`, `:31`); the SSH.NET adapter reads name, type, size and time (`SshNet.cs:379-391`) | None. Cells stay empty; Inspect says the value is not available in this location | — | — | P1: numeric UID, GID and mode from the attributes the listing already receives. Scoped to the server. Never named through this computer's accounts, never a shell command per row |
| 7 | **FTP, FTPS** | As 6: the same provider and contract (`src/FileCat.Remote/Ftp/FluentFtp.cs:149`, `:549-561`) | None | — | Ownership exists only as server-formatted text in raw listings. Unknown is acceptable | At most the server's own text, with P1 |
| 8 | **ZIP and other archives** | No metadata columns: members have no path (`src/FileCat.Core/Archives/ZipProvider.cs:370`; `src/FileCat.Archives/ArchiveProvider.cs:223`). Listing tags carry sizes, encryption, kind and link target, no owner (`ZipProvider.cs:12-18`; `ArchiveProvider.cs:10-26`; `src/FileCat.Archives/ArchiveFormats.cs:31`, `:377-418`) | None | — | Never the archive file's owner, never a temporary extracted file's | A member-owner column showing what the archive records. The baseline's write engines already parse and preserve it for rewrites: tar mode, UID, GID and names (`src/FileCat.Archives/Writing/StrictTarCatalog.cs:11`, `:85`, `:99-102`), the ZIP UID/GID extra field (`StrictZipCatalog.cs:334`), the RAR5 owner record (`StrictRar5Framing.cs:9`). Recorded numbers and the names stored in the archive only; a recorded UID is never looked up locally. Members added on Windows are written with neutral 0/0 and no names by design (`TarRewriteEngine.cs:111`) |
| 9 | **Search results, flat and mixed result sets, working sets** | Fixed layout without metadata columns (`Columns.cs:139-147`). Rows may be files on disk (`src/FileCat.Core/Search/ResultSetProvider.cs:269-272`), archive members or other providers' items | Unchanged | — | — | Owner there needs a layout decision and per-item device queues in Analyze, which today runs on the listing's device (`TabViewModel.cs:1205`) |
| 10 | **Registry, MTP, recovery, Recycle Bin, change journal, hidden data** | Dedicated layouts with their own meaning | Unchanged. File ownership is not pushed into them | — | Registry key owners stay in the existing key-ACL inspection | — |
| 11 | **This PC, Network lists** | Not applicable | Not applicable | — | — | — |

---

## 5. Ranked column decisions

Priorities are ordinal, reasons are stated, nothing is scored. The first delivery contains ranks 1 and 2 only.

### 5.1 Already there: not to be proposed as new

Name, Ext, Size, Modified, Created, Attr, Version (Windows), Dimensions, Link target, Origin, Streams (Windows) or
Attributes (extended-attribute count, Linux and macOS), Verified; and the Folder, Kind and Details columns of the
dedicated layouts. Link target with the Links profile already covers "where does this link point".

### 5.2 Ranking

| Rank | Candidate | Task it serves, example value | Data and cost | Decision |
|---|---|---|---|---|
| 1 | **Windows Owner** | "Whose is this?" on a profile share, a service's output folder, after an account was removed. `CONTOSO\alice` | One metadata read a visible row; names per distinct owner. §6 | **First delivery** |
| 2 | **Linux and macOS Owner, Group, Permissions** | Existing | Existing reads | **Retain.** First delivery fixes G10, G12, G13, G14 and adds the detail. No new column |
| 3 | **Mode (octal)**, Linux and macOS (C1) | Compare or sort permissions as numbers; spot `4755` and `1777`. `0755` | The same `stat` as Permissions; `UnixPermissions.Octal` exists (`UnixPermissions.cs:42`) | **Next increment.** A companion column, not a format switch: a per-column format would need a new settings field, a companion needs none. Until then the octal form is in the Permissions detail |
| 4 | **Allocated size** (C2) | Find sparse, compressed and cloned files; see what a file takes on this volume. `4,0 kB` beside a 2 GB size | One metadata read a visible row; the inspectors already read it on all three systems | **Next increment**, with its own native gates: each system measures something different (§5.3) |
| 5 | **Packed size and ratio in archives** (C3) | See what compresses, find stored members. `1,2 MB`, `37 %` | Already in the listing tags; no I/O | **Next increment.** Needs a listing-data column route because members have no path (§5.3) |
| 6 | **Links** (hard-link count) | Find files with more than one name before deleting or deduplicating. `3` | The same native call as Allocated size on every system | **With C2, optional.** Files only: a folder's link count follows each file system's own convention and is not a count of names |
| 7 | Git status as text | Sort or group changed files. `changed` | The per-folder snapshot exists (`src/FileCat.App/Services/GitStatusReader.cs`); labels exist (`FileListControl.cs:2206-2214`) | **Defer.** Icon, tooltip and spoken label already show it. If wanted, it rides C3's route; never a Git run per row |
| 8 | Status changed, Last accessed | Forensics. `12.11.2026 09:14:07` | Same read as C2 | **Defer.** The record inspector shows all times exactly. Status-change time is not creation; access time is not a history of anyone opening the file and is often not updated at all |
| 9 | Raw SID, UID, GID columns | Compare IDs across mounts; sort by RID | In the value already | **Defer.** The detail and Inspect show them. A column needs a demonstrated repeated task. An ID means something within its authority (a domain, a computer, a mount), not by its display name alone |
| 9a | Link kind (symbolic link, junction, mount point) | Tell link kinds apart | In the inspector | **Defer.** Link target, the `L` flag in Attr and the link overlay already mark links; a kind column is worth adding only where a real ambiguity is shown, without redesigning links |
| 10 | ACL indicators | "Does anything here have unusual access?" | A DACL read on Windows; an ACL query on Unix | **Defer by default.** "Has an ACL" is true of every NTFS file. A useful Windows form would be "inheritance turned off" or "has explicit entries", defined precisely; on Linux the presence of the `system.posix_acl_access` attribute specifically, not of extended attributes in general. Unknown must stay unknown |
| 11 | macOS tags, BSD and inode flags, cloud state | Triage by tag or flag | Tag decoder exists (`src/FileCat.Core/HiddenData/HiddenDataDecoder.cs:214`, `:247-263`); flags are in the inspector; cloud state is an icon overlay from attributes | **Defer.** Attr already shows `O`. The offline attribute is not proof of a cloud provider's sync state |
| 12 | Type / Kind | Read a type instead of an extension. `PNG image` | Extension or association lookup | **Reject for now.** The Ext column and the icons carry it. A content-verified type means reading every file; a shell type name means shell handlers. Neither per row |
| 13 | File ID, inode | Spot two names of one file | In the inspector | **Reject as a column.** An inode number alone is not unique across devices. Links answers the task |
| 14 | Full ACL text, effective permissions, Windows primary group | Audit access | Inspector | **Reject as columns.** They belong in the existing security section (`WindowsFileRecords.cs:657-702`). No single permissions string means the same on all three systems, and a Windows primary group is not a POSIX group |
| 15 | Hashes, recursive folder size, deep media or document metadata | — | Content reads | **Reject as automatic columns.** They stay explicit commands with their own progress and cancel |
| 16 | SELinux labels, Linux capabilities | Specialist review | Extended attributes | **Defer** to a separate, evidence-based proposal. Supported Linux is Ubuntu, which labels nothing with SELinux by default |

### 5.3 Definitions for the three follow-ups

These are recorded so the follow-ups start from decisions, not from a blank page. None is part of the first delivery.

**C1 — Mode (octal).** ID `mode`, header "Mode", right-aligned. Linux and macOS file-system rows, files and folders.
Text: three digits, or four when a set-ID or sticky bit is set, exactly `UnixPermissions.Octal`. Sort key: the mode's
twelve bits as a number. Producer, cache, refresh and failure states: those of Permissions. Not offered on Windows.

**C2 — Allocated size.** ID `allocated`, header "Allocated", right-aligned, files only, no folder totals.
Meaning: the bytes the file system reports as allocated to the entry itself.

| System | Source | Documented meaning [D] | Existing read [S] |
|---|---|---|---|
| Windows | `FILE_STANDARD_INFO.AllocationSize` on a handle opened without following the final link | "The amount of space that is allocated for the file" ([FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info)) | `WindowsFileRecords.cs:134-141` |
| Linux | `stx_blocks` × 512 | "The number of blocks allocated to the file on the medium, in 512-byte units" ([statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html)) | `src/FileCat.Core/Records/UnixFileRecords.cs:66`, which reads it without checking the returned mask; the column must check |
| macOS | `ATTR_FILE_ALLOCSIZE` through `getattrlist` | Foundation's equivalent key is "the total allocated size on-disk for the file" ([fileAllocatedSizeKey](https://developer.apple.com/documentation/foundation/urlresourcekey/fileallocatedsizekey)); the "total" variant adds metadata | `UnixFileRecords.cs:604-712` |

Not `GetCompressedFileSizeW`: it takes a path, and for a symbolic link "returns the file size of the target"
([GetCompressedFileSizeW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) [D]).
What the column is not: bytes freed by deleting the file (hard links, clones, snapshots and shared blocks say
otherwise); a sum over named streams or resource forks; a compressed size where the system reports whole clusters.
Each of those is a native check in C2, not an assumption here. Links comes from the same read
(`NumberOfLinks`, `stx_nlink`, `ATTR_FILE_LINKCOUNT`) and never from `FileSystemItemInfo.LinkCount`, whose default of
1 is not a measurement (`src/FileCat.Core/FileSystem/FileSystemOperations.cs:46`, `:420`).

**C3 — Packed size and ratio.** Archive panels only. Packed: the member's compressed size from its tag
(`ZipMemberTag.CompressedSize`, `ArchiveMemberTag.CompressedSize`). Ratio: packed ÷ size as a percentage, smaller is
better; empty for an empty member; `—` when the packed size is unknown, which is every tar member
(`ArchiveFormats.cs:417` passes −1) and members of a solid block [G]. No per-member "allocation" is invented from
shared compressed blocks. It needs a column whose text and sort key come from the entry's tag with no I/O. The Folder
column of result sets is the precedent (`Columns.cs:46`; its key at `TabViewModel.cs:1164`).

Each follow-up is validated for its own remaining risk when it is built, not with this feature's checklist. C1: mode
fixtures (0644, 0755, 4755, 1777) and numeric order; nothing native beyond what Permissions already has. C2: native
fixtures per system for a sparse file, a compressed file, a clone or reflink, a file with a second stream or fork, and
a hard-linked file, each compared with what the system's own tools report. C3: tags of a ZIP, a 7z, a solid archive
and a tar, and proof that an archive panel triggers no file read for the column.

### 5.4 One finding about an existing column

Created is filled from .NET's `CreationTimeUtc` (`src/FileCat.Core/FileSystem/LocalFileSystemProvider.cs:202`). On
Unix file systems without a birth time that property "returns the older of the time of the last status change and
the time of the last modification"
([FileSystemInfo.CreationTime](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesysteminfo.creationtime) [D]),
so the column can show an inferred time as "Created". Recorded here; fixing it is separate work with its own scope.
No second creation column is proposed.

---

## 6. Implementation design

### 6.1 Shape

Nothing new is built beside the existing mechanism. Owner stays a `MetadataField`; the chooser, profiles, painting,
demand, scheduling, cache and stale-publication protection are the ones every metadata column uses.

```
painting (UI thread)                    file's device queue (Background)          name workers (own threads)
RenderRow
  TabViewModel: cell for (row, field)
    MetadataService.Get
      cached → Format(value) ─────────── PrincipalNames.Peek: memory only
      missing or due → Schedule ───────→ Owner.Produce(path)
                                           IOwnership.ReadOwner(path)      one metadata read, no names
                                           PrincipalNames.Request(id) ──────────→ IOwnership.LookUp(id)   may block
                                           Store → ValuesChanged → repaint        Changed → ValuesChanged → repaint
```

Three decisions carry the design:

1. **The value is the ID.** A cached Owner value is a `Principal` *(new)*: namespace and canonical ID. The name is
   read from the name cache when the cell is formatted. One file read per row; one lookup per distinct owner; a name
   that arrives later repaints without touching any file.
2. **Reading and naming never share a thread.** The file read runs on the file's device queue, where it belongs. Name
   lookups run on the resolver's own small set of threads, so a slow domain controller or LDAP server cannot hold a
   device queue or a thread-pool thread.
3. **A producer can say why.** A producer may return a ready `MetadataValue`, so "refused", "not supported" and
   "gone" reach the cell and the detail instead of collapsing into null.

### 6.2 Metadata service (`src/FileCat.Core/Metadata/MetadataService.cs`)

| # | Change | Why |
|---|---|---|
| M1 | Append `Denied` to `MetadataState`. | The one outcome the existing states cannot say (§3.2). Appended last so no existing value changes. |
| M2 | `MetadataField` gains an optional trailing parameter `Func<object?, string?>? Detail = null`. | Describes an available value for the tooltip and Inspect. Trailing and optional: every existing construction compiles unchanged. |
| M3 | In the private `Produce`: a producer result that is already a `MetadataValue` is returned as it is; null stays `Absent`; anything else stays `Available`. | G10. No new framework: one type test. |
| M4 | `Compute` returns `Unsupported` with the placeholder reason for an `EntryFlags.Offline` entry, as `Get` does at `:107`. For every field (D7). | G9. Analyze must not download cloud files. This changes what an existing analysis does: Dimensions or Version in a cloud folder no longer reads placeholder content. §9.4 names the flip. |
| M5 | `Compute` reuses a cached value only when the field has no `RefreshAfter` or the value was produced within it, and stores with the production time recorded (the `refreshing` argument of `Store`). | G8. A repeated analysis re-reads owners; values Analyze produced become refreshable by painting. |
| M6 | In `Store`, during a refresh: a `Failed` result does not replace an `Available` value. The old value stays, its `Detail` says the re-read failed, and the production time is updated so the next attempt waits one interval. `Denied`, `Unsupported` and `Absent` do replace. | "Useful fallback where a raw ID is already known", with a bounded retry. |
| M7 | `Get` uses six times the field's `RefreshAfter` when `slowLocation` is true, in both places that test it (`:114`, `:156-159`). | 30 s instead of 5 s on network locations (§6.7). |
| M8 | `ExpireRefreshable()` *(new)*: marks every recorded production time as due. Values stay on screen until replaced. | Activation freshness (§6.7). |
| M9 | A public way to raise the throttled `ValuesChanged` (the private `Notify`). | The name resolver repaints through the one existing event. |
| M10 | The three reason strings at `:105-109` become sentences. `MaxCache` gains an internal settable companion for tests. | They are now shown to users (§3.2); the capacity test of §8 needs a small cache. |

Not changed: the key, the cache size, the backlog guard, `Peek`, `Forget*`, `Invalidate`, the demand test, the device
scheduler. `MaxOutstandingPerDevice` keeps its name; ADR-10 is corrected instead (O7).

### 6.3 Identity value and ownership source (Core, *new*)

New file `src/FileCat.Core/FileSystem/Ownership.cs`:

| Type *(new)* | Content | Note |
|---|---|---|
| `PrincipalKind` | `WindowsSid`, `UnixUser`, `UnixGroup` | The namespace an ID is meaningful in. |
| `Principal` | `Kind`, `Id` (the SID string, or the decimal UID or GID), `Authority` (empty in this delivery: "this computer") | Immutable reference type with value equality, comparable: by kind, then numerically (UIDs as numbers, SIDs sub-authority by sub-authority), then authority. One shared instance per distinct principal, so a 100,000-row analysis does not allocate 100,000 of them. |
| `NameStatus` | `Pending`, `Resolved`, `NotFound`, `Unavailable` | |
| `PrincipalName` | `Status`, `Name`, `Type`, `Reason` | What the name cache answers. |
| `OwnerRead` | `Status` (`Ok`, `Denied`, `Unsupported`, `Gone`, `Failed`), `Principal`, `Reason` | What one read returns. |
| `IOwnership` | `IsSupported`; `OwnerRead ReadOwner(string path)`; `OwnerRead ReadGroup(string path)`; `PrincipalName LookUp(Principal principal)` | The platform boundary. |

Why this interface is enough. The column needs three operations and no more: read the owner of one entry, read its
group where the platform has one, and name one principal. ACLs, effective access and the inspector's report are not
needed and are not reachable through it. Reading and naming are separate members because they must run on different
threads and fail differently. `Authority` exists so that N1 can key a server's answers apart from this computer's
without changing a stored value; this delivery always passes the empty authority. Nothing here is a general metadata
or plug-in framework: fields remain `MetadataField`s.

Implementations and composition:

- `UnixOwnership` *(new, Core)* wraps the existing natives in `UnixPermissions`. It needs two small additions there:
  a stat that reports why it failed (`ENOENT`/`ENOTDIR` → `Gone`; `EACCES`/`EPERM` → `Denied`; anything else →
  `Failed` with the system's message; a missing `statx` → `Unsupported`), which on Linux also checks the returned mask
  for UID and GID (G14; "the kernel may … fail to return fields that were requested",
  [statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) [D]); and an uncached lookup that distinguishes found,
  not found and failed. `getpwuid_r` returns 0 with a null result when there is no entry, and an error number
  otherwise ([getpwuid_r(3)](https://man7.org/linux/man-pages/man3/getpwuid_r.3.html) [D]): null result, or
  `ENOENT`/`ESRCH`/`EBADF`/`EPERM`, is `NotFound`; `EIO`, `EINTR`, `EMFILE`, `ENFILE`, `ENOMEM` are `Unavailable`.
  `UnixPermissions.Stat`, `UserName`, `GroupName` and `NameCache` stay exactly as they are for their other callers
  (the record inspector, trash, the permissions dialog), with their bounds and their tests. This is the smallest
  compatible way to improve freshness: giving the existing cache an expiry would change those callers and the two
  tests that pin its bounds, so expiry lives in the resolver the columns use (§6.4) and nowhere else.
- `NoOwnership` *(new, Core)*: `IsSupported` false; every read is `Unsupported`.
- `WindowsOwnership` *(new, `src/FileCat.Platform.Windows/WindowsOwnership.cs`)*: §6.5.
- `IPlatform` gains `IOwnership Ownership { get; }` beside `HiddenData` (`src/FileCat.Core/Platform/IPlatform.cs:19`).
  `PortablePlatform` supplies `UnixOwnership` on Linux and macOS and `NoOwnership` otherwise; `WindowsPlatform`'s
  constructor sets `WindowsOwnership` (`src/FileCat.Platform.Windows/WindowsPlatform.cs:10-23`).
- `BuiltInFields` gains `OwnershipSource` and `Names`, set in `AppServices` next to
  `BuiltInFields.HiddenDataSource = Platform.HiddenData` (`AppServices.cs:44`), the existing pattern [S] E22. On Linux
  and macOS an unset source falls back to `UnixOwnership`, so Core used alone behaves as it does today.
- The Owner and Group fields are also constructible from an explicit source and resolver (a factory beside the
  static fields). Core tests run in parallel and must not swap process-wide state; they register a field built over
  a fake on their own `MetadataService` with the existing `Register` (`MetadataService.cs:95`).
- `BuiltInFields.All` on Windows becomes `[Version, Dimensions, LinkTarget, Zone, Hidden, Verified, Owner]`: appended,
  so the chooser's existing order is undisturbed. Group and Permissions stay off Windows.

The Owner field keeps its ID, title, cost (`Cheap`), `Folders: true` and `RefreshAfter` of 5 s. Its producer maps
`OwnerRead` to `MetadataValue` (`Ok` → `Available` with the principal, after `Names.Request`; `Denied` → `Denied`;
`Unsupported` → `Unsupported`; `Gone` → `Absent`; `Failed` → `Failed`; each with its reason). Its formatter returns
the cached name or the ID. Its `Detail` builds §3.3. Its sort key is §6.6.

### 6.4 Name resolver (`PrincipalNames`, Core, *new*)

One instance, created in `AppServices` over `Platform.Ownership.LookUp`, disposed with the services.

| Property | Rule |
|---|---|
| Reads | `Peek(principal)` answers from memory under a short lock: the cached answer, or `Pending`. It never starts work. Formatting, sort keys, tooltips and Inspect use only this. |
| Requests | `Request(principal)` is called by producers only. A fresh entry: nothing. A queued or running one: nothing (repeated requests for one principal coalesce). An expired one: the old answer keeps showing while one new lookup is queued. A full queue (256): the request is dropped; the row's next refresh asks again. |
| Workers | Dedicated background threads, never the thread pool and never a device queue: two normally. A blocked native call cannot be cancelled, so the bound is on threads: when a lookup has run longer than 3 s, one replacement may start, up to a hard limit of four. Idle workers exit. |
| A slow or failing name service | A lookup that ends in an error, or takes longer than 3 s whatever it returns, is recorded as `Unavailable` for 30 s. A "not found" that took that long is not trusted as "not found": the Windows documentation says the same error results when "a network time-out prevents the function from finding the name" [D]. While that record stands, other IDs of the same lookup domain (the same Windows domain prefix; all IDs on Unix) are answered `Unavailable` at once without calling out. After 30 s one probe goes out. A dead domain costs one blocked thread per 30 s, not one per row. |
| Lifetimes | Resolved: 10 minutes, then refreshed on the next request while the old name shows. Not found: 2 minutes. Unavailable: 30 seconds. |
| Memory | At most 4,096 entries and 262,144 retained characters, least recently used out first. A name longer than 1,024 characters is not kept; the ID shows. |
| Invalidation | `Invalidate()` clears the cache and advances a generation. A lookup that started before it finishes into nothing. Reread calls it (§6.7). |
| Notification | `Changed`, coalesced like `MetadataService.Notify`, wired to the service's `ValuesChanged`. |
| Waiting | `WhenIdle(timeout, token)` completes when nothing is queued or running, or at the timeout. Only Analyze uses it (§6.10). |
| Logging | Counts and error codes only. Never an account name or an ID. |

A folder crafted to carry thousands of distinct or bogus owner IDs therefore costs a bounded queue, a bounded cache
and a bounded number of lookups.

### 6.5 Windows reader (`WindowsOwnership`, *new*)

`ReadOwner(path)`:

1. `CreateFileW(WindowsFileOperations.Long(path), READ_CONTROL, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, null, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, null)`.
   `Long` is the existing long-path helper (`src/FileCat.Platform.Windows/WindowsFileOperations.Native.cs:114`).
   `READ_CONTROL` is required: "To read the owner, group, or DACL … the calling process must have been granted
   READ_CONTROL access when the handle was opened"
   ([GetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getsecurityinfo) [D], [P3]).
   `FILE_FLAG_BACKUP_SEMANTICS` is required to open a folder, and "appropriate security checks still apply when this
   flag is used without SE_BACKUP_NAME and SE_RESTORE_NAME privileges"
   ([CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) [D]); FileCat
   enables neither. No data access is requested, so nothing is read from the file and no time changes.
2. `GetSecurityInfo(handle, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, out owner, null, null, null, out descriptor)`.
   The owner pointer points into the returned descriptor, which is freed with `LocalFree` in a `finally` [D]. The SID
   is copied out (`SecurityIdentifier`, already used in this assembly) before the descriptor is freed. The handle is a
   `SafeFileHandle` in a `using`.
3. Mapping. Unexpected exceptions are not caught.

| Native result | Outcome |
|---|---|
| Open fails with 2 or 3 | `Gone` |
| Open fails with 5; query fails with 5 | `Denied`, "did not allow reading this item's security information" |
| Open fails with 32 or 33 [P4] | `Denied`, "open exclusively by the system or another program" |
| Query fails with 1 or 50 | `Unsupported` |
| A null owner pointer (a descriptor may have no owner SID, as the documentation of the path-based variant states [D]) | `Unsupported`, "the item's security descriptor names no owner" |
| Owner is `S-1-1-0` and `GetVolumeInformationByHandleW` on the same handle succeeds without `FILE_PERSISTENT_ACLS` | `Unsupported`, "<file system> volumes keep no owner" [G] GW3. If that call fails (the documentation lists it as unsupported over SMB [D]), the owner stands as read |
| Any other error | `Failed` with the system's message |
| Otherwise | `Ok` with `Principal(WindowsSid, <SID string>)` |

`LookUp(principal)`: `LookupAccountSidW(null, sid, …)` with 512-character buffers and one retry on
`ERROR_INSUFFICIENT_BUFFER`. Success → `Resolved`, name `DOMAIN\name` or the bare name when the domain is empty [P5],
type from `SID_NAME_USE`. Error 1332 → `NotFound`. Any other error → `Unavailable` with the message. With a null
system name "the account name translation begins on the local system" and then tries "domain controllers trusted by
the local system" [D]: that can leave this computer, which is why it runs on the resolver's threads.

`ReadGroup` returns `Unsupported`; the Group field is not registered on Windows.

Alternatives weighed and not chosen:

| Alternative | Why not |
|---|---|
| Reuse the inspector (`WindowsFileRecords`) | It opens with more access, reads owner, group, DACL and label, converts to SDDL and builds a report (`:657-702`). Its name translation maps every failure to null (`:711-715`), which cannot tell "not found" from "unavailable" and gives no account type. The pattern is reused, not the code. |
| `GetNamedSecurityInfoW` (path-based) | It behaved the same on the probe [P2], but its documentation says nothing about links, and the handle route's link behavior is documented. |
| The managed `FileInfo.GetAccessControl` | Path-based, allocates a full security object per row, and hides the native error. |
| The elevation broker's reader (`ElevationBroker.FileTrust.cs:47-78`) | Private to the broker's trust decision; coupling a display column to it would be wrong in both directions. |

Showing the column triggers no elevation, no privilege, no write, no account enumeration and no credential prompt.

### 6.6 Sorting

**Contract.**

1. The parent row first; then folders before files when that option is on. Unchanged.
2. Items whose owner is known come before items whose owner is not (`Pending`, `Denied`, `Unsupported`, `Failed`,
   gone, not read), in both directions. Unchanged rule, `EntrySorting.cs:64-65`.
3. Among known owners, ascending: owners with a name, by the name as shown, case-insensitively in the current
   culture; then owners without a name, by ID numerically (UID 9 before UID 10; RID 500 before RID 1001). Descending
   reverses that whole order, so unnamed IDs come first: a quick way to find orphaned owners.
4. Equal names with different IDs stay apart: the ID decides.
5. Then the file name, then the store index. Unchanged.

**Mechanics.**

- Owner's `SortKey` returns a small comparable *(new)* holding (has name, text, principal). It reads the name with
  `PrincipalNames.Peek`. No lookup, no provider call.
- `TabViewModel.MetadataKey` (`TabViewModel.cs:1159-1169`) uses `MetadataService.Peek` instead of `Get` (G6). `Peek`
  returns only what `Get` or `Compute` stored, so the gates those apply are already reflected.
- `EntryComparer` (`EntrySorting.cs:121-161`) remembers each store index's key for the duration of one sort pass, for
  listings up to 1,000,000 rows (G7). Within a pass a key is read once and cannot change; the provider is called once
  per row instead of twice per comparison. Above that size the comparer reads live as today, so the external sort
  keeps its bounded memory (`src/FileCat.Core/Listing/ExternalViewBuilder.cs:12-51`). `StableSort` is untouched.
- A name that resolves later never reorders the list by itself. The order changes only when the user sorts, when
  Analyze finishes, or when the listing is read again; each of those starts a new pass with the names known then.
- The Sort button's description uses the field's title when the sorted column is not in the profile (G15).

### 6.7 Freshness

Ownership can change while size and modified time stay the same, so the cache key cannot notice it.

| Event | What happens |
|---|---|
| A row is painted and its value is older than 5 s (30 s on a UNC path or mapped network drive) | Re-read on the device queue; the old value stays until the new one arrives; a repaint follows only if it differs. Existing mechanism, `MetadataService.cs:111-116`, with M7. |
| A row is not painted | Nothing. FileCat is not an ownership monitor. An idle panel does not poll. |
| The window is activated, or a tab becomes the active one | `ExpireRefreshable()`: painted rows re-read at once. This is the "changed it in a terminal, came back" case. Hooked where Git badges already refresh on activation (`src/FileCat.App/Views/MainWindow.axaml.cs:209-216`) and in `OnIsActiveTabChanged` (`TabViewModel.cs:284`). |
| Reread (Ctrl+R) | Everything is forgotten, as today (`MainViewModel.Commands.cs:237`, `MainViewModel.LocationContext.cs:122`), and the name cache is invalidated too. |
| An Attributes job finishes | Everything is forgotten, as today (`MainViewModel.Operations.cs:704`). |
| Analyze folder runs, again or for the first time | Values older than the interval are read again (M5). |
| The same path now holds another file | A different size or time is a different key. With both equal, the rows above apply. A read that raced the replacement is stored under the old entry's key and is not used again once the listing refreshes. |
| An account is renamed | The name is refreshed after 10 minutes, or at once on Reread. The owner ID did not change and is not re-read for this. |
| A read fails once on a flaky share | The last value stays, with a note (M6); retried at the interval. |

Considered and left out: adding security changes to the folder watcher's filter
(`src/FileCat.Core/FileSystem/ChangeMonitor.cs:35-36`). It would re-list a folder on every ACL change for all users
whether or not the column is shown, and activation already covers the common case.

### 6.8 Demand, scheduling and placeholders

| Situation | Required behavior |
|---|---|
| Owner not in the profile, no Owner sort, no analysis | No owner read and no name lookup, whatever is hovered, focused, spoken or scrolled. Guaranteed structurally: only a producer calls the source and the resolver, and only painting or Analyze runs a producer. |
| Owner in the profile | Reads for painted rows only, on their device's queue at `Background` priority behind listing and user work; at most the scheduler's workers at a time; no more than 256 queued per device. |
| Scrolling | Queued reads for rows that left the view are skipped by the existing demand test (`TabViewModel.cs:1130`); no new mechanism. |
| Another panel navigates | Its listing and its queue are its own. On one device the navigation's work has priority over metadata. Name lookups never occupy a device queue. |
| Sorting, hovering, Inspect, the screen reader | Memory only (`Peek` on both caches). |
| A cloud placeholder (`EntryFlags.Offline`, set from the listing's attributes: `LocalFileSystemProvider.cs:217`) | Not opened, in `Get` and in `Compute`. `FILE_ATTRIBUTE_RECALL_ON_OPEN` means "Opening the item will be more expensive than normal, e.g. it will cause at least some of it to be fetched from a remote store" ([File Attribute Constants](https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants) [D]), and no metadata-only open has been shown safe for the other placeholder kinds. Until one is, the value is `—` with the reason. |
| Network and removable locations | Owner is `Cheap` and is read there for painted rows. `IsSlowLocation` (`TabViewModel.cs:1113`) recognizes UNC paths and mapped network drives on Windows only; it is used here only to lengthen the refresh interval, never as proof that a location is fast. |

### 6.9 App integration

| Where | Change |
|---|---|
| `TabViewModel` | A cell method *(new)* beside `GetMetadataText` (`:1115`) that returns text, detail and pending: `Denied` → `no access`; detail from `field.Detail` for `Available`, from `MetadataValue.Detail` otherwise, "Being read." for `Pending`. `GetMetadataText` keeps its signature and calls it (tests use it). A peek-only variant for Inspect. |
| `FileListControl` | `RenderedCell` (`:68`) gains the detail; the metadata branch of `RenderRow` (`:1331-1342`) stores it. `UpdateTip` (`:1812-1845`) shows `Header: text` and the detail for a metadata cell that has one, clipped or not; the Verified column keeps its own tip. `FocusedInspectionText` (`:1867-1900`) adds the detail lines, uses the peek for unpainted metadata columns, and names the two "not available" cases of §3.2. `DescribeItem` is not touched. |
| `Columns.cs` | The Owner choice's default width is 160 on Windows. `ColumnChoice` (`:217-218`) gains an optional description. No profile, key or `Create`/`Parse` rule changes. |
| `ColumnProfilesEditor.cs` | One note under the add box (`:29`, `:101`) showing the selected choice's description, also set as the box's help text. |
| `MainViewModel.Workspaces.cs` | `AnalyzeFolderAsync` (`:310`) stops filtering by hard-coded IDs: `Services.Metadata.Fields` already holds only this system's fields [S] E4. The Owner description changes (§3.5). |
| `CommandRegistry.cs` | The description at `:502` and the keywords at `:621` gain owners. |
| `AppServices.cs` | Creates `PrincipalNames`, sets the two `BuiltInFields` properties at `:44`, wires `Changed` after `Metadata` is created (`:137`), exposes the resolver for Reread, disposes it. |
| `MainWindow.axaml.cs`, `MainViewModel.Commands.cs`, `MainViewModel.LocationContext.cs` | The activation and Reread hooks of §6.7. |

### 6.10 Analyze folder

`TabViewModel.AnalyzeAsync` (`:1175-1249`) keeps its shape: one background pass over the current listing, progress,
Esc, cancellation on leaving or closing, the store instance as identity. No descendant is read. Changes:

1. **Which rows.** The parent row is skipped. A row is considered when it is a file, or a folder and the field
   declares `Folders`, and it has a file-system path. `e.IsContainer` is no longer the test (G3): it also covers
   drives, shares and Registry keys, and `EntryFlags.Container` marks archive *files*.
2. **Counting.** Every considered row is counted by the state `Compute` returns: has a value; has none (`Absent`);
   refused (`Denied`); not read (`Unsupported`, which includes placeholders); failed. Rows not considered are counted
   as not applicable.
3. **Keeping the values.** The pass keeps each row's computed value in an array indexed by store index *(new)*, tied
   to this store instance and this field. `MetadataKey` reads it first and falls back to the cache. The order then
   uses every value the pass computed, whatever the 50,000-value cache evicted meanwhile (G5), and reversing the sort
   afterwards still has them. Bounds: at most 1,000,000 rows and 16,000,000 retained characters of string keys; both
   are constants a test can shrink. Beyond either, values past the bound are not kept and the result says so.
4. **Names.** For a field whose values are principals, the pass then waits for the resolver to go idle, at most 10 s,
   cancellable. Then it sorts. Finishing the pass and having a name for every owner are different things, and the
   result says which happened.
5. **Lifetime.** The kept values are dropped when the tab's store is replaced (navigation, Reread, a watcher
   refresh), when another analysis starts, when the sort moves to another field, and when the tab closes. If a
   "complete" result was showing when the store was replaced, the banner is replaced by the partial-order note: the
   new listing is sorted with what the cache still has.
6. **Result text.** Built from the counts. Examples:

| Outcome | Banner |
|---|---|
| Everything read, all named | Sorted by Owner: 12,480 items read. |
| Some owners without a name | Sorted by Owner: 12,480 items read. 3 owners have no name here and are ordered by ID. |
| Some names still being looked up | Sorted by Owner: 12,480 items read. Names for 2 owners were still being looked up; they are ordered by ID until you sort again. |
| Some rows not readable | Sorted by Owner: 12,474 of 12,480 items read; 4 with no access, 2 cloud placeholders not read. Those are listed last. |
| A field most rows do not have | Sorted by Version: 37 of 1,210 items have a value. |
| Over the bound | Analyzed 2,300,000 items. FileCat kept the values of the first 1,000,000 for the order; the rest are listed last. |
| More owners than the name cache holds | …names could be kept for 4,096 owners; the others are ordered by ID. |
| The column is not in the profile | …appended: Add the column with Set up columns… (right-click the column header) to see the values. |
| Cancelled | Analysis canceled after 5,120 of 12,480 items; the order remains partial. (unchanged) |

The header-click banner ("…using the values computed so far…", `:1156`) is unchanged.

### 6.11 Compatibility

| Area | Statement |
|---|---|
| Settings | No schema change (`AppSettings.CurrentSchema` stays 1). Keys, widths, order, the ten-profile cap and the Name rule are untouched. |
| Existing Windows profiles | None can contain `meta:owner`: until now the key was dropped when settings were read [S] E7. Nothing to migrate. |
| Profiles carried between systems | `meta:owner` is now kept on all three. `meta:group` and `meta:permissions` are still dropped on Windows, and `meta:version` elsewhere, when settings are read. No portability promise is made or changed. |
| Three cases that stay distinct | A field this system does not have (a known ID from another system): dropped when settings are read, as today. An unknown or damaged key: dropped, as today. A field this system has whose value is unavailable for a row or a location: the column stays in the profile and the cell says why (§3.2). |
| Running an older FileCat afterwards | It drops the unknown `meta:owner` when it next saves settings. Acceptable for a downgrade; stated in the release note. |
| Built-in profiles and shortcuts | Unchanged (D1, D12). |
| Unix behavior | The same cells for resolved names. An unresolved ID still shows its number. New: the detail, numeric order for unnamed IDs, `no access` and `—` where the cell used to be empty, and names arriving a moment after the number instead of with it. |
| Other metadata fields | M4 (placeholders in Analyze), M5 (Analyze re-reads refreshable fields), M6 and M7 apply to all. Their cells and keys are otherwise unchanged. |

### 6.12 Files expected to change

| File | Responsibility of the change | Package |
|---|---|---|
| `src/FileCat.Core/Metadata/MetadataService.cs` | M1–M10; Owner and Group over the ownership source; Permissions detail; Windows `All` | O1, O2, O4 |
| `src/FileCat.Core/FileSystem/Ownership.cs` *(new)* | Identity types, `IOwnership`, `UnixOwnership`, `NoOwnership` | O2 |
| `src/FileCat.Core/FileSystem/PrincipalNames.cs` *(new)* | The name resolver | O2 |
| `src/FileCat.Core/FileSystem/UnixPermissions.cs` | A stat that reports why it failed and checks the returned mask; an uncached, three-way name lookup. Existing members untouched | O2 |
| `src/FileCat.Core/Platform/IPlatform.cs` | `Ownership` on the interface and `PortablePlatform` | O2 |
| `src/FileCat.Platform.Windows/WindowsOwnership.cs` *(new)* | Owner read and name lookup | O4 |
| `src/FileCat.Platform.Windows/WindowsPlatform.cs` | Supplies `WindowsOwnership` | O4 |
| `src/FileCat.Core/Listing/EntrySorting.cs` | One-pass key memory in `EntryComparer` | O5 |
| `src/FileCat.App/Services/AppServices.cs` | Composition of source and resolver | O2 |
| `src/FileCat.App/ViewModels/TabViewModel.cs` | Cell with detail; `Peek`-based keys; Analyze rows, counts, kept values, result text; sort description; activation expiry | O1, O3, O5, O6 |
| `src/FileCat.App/Controls/FileListControl.cs` | Detail in rendered cells, tooltip, Inspect | O3 |
| `src/FileCat.App/Controls/Columns.cs`, `src/FileCat.App/Views/ColumnProfilesEditor.cs` | Width, description, the note | O4 |
| `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs`, `src/FileCat.Core/Commands/CommandRegistry.cs` | Analyze chooser and copy | O4 |
| `src/FileCat.App/Views/MainWindow.axaml.cs`, `MainViewModel.Commands.cs`, `MainViewModel.LocationContext.cs` | Freshness hooks | O6 |
| `docs/CAPABILITIES.md`, `docs/adr/ADR-10-metadata-scheduling.md` | Corrections and the new capability | O7 |

Not expected to change: `EntryData`, `EntryStore` and the spill format; `ListingModel`; `ColumnProfileSet`'s
persistence; `FileListAutomation.cs`; `PanelView.axaml.cs`; `WindowsFileRecords.cs`; every provider; the device
scheduler. If an implementer finds one of these must change, that is a signal to stop and re-read this plan.

---

## 7. Ordered work packages

Order: O1 → O2 → O3 → O4 → O5 → O6 → O7. O3 needs only O1. O5's generic parts need only O1; its Owner tests need O2.
Each package ends with the product working and its own tests green. "Checklist" refers to §8.2.

### O1 — Metadata service groundwork

| | |
|---|---|
| **Behavior** | No new column. For every metadata field: Analyze folder no longer reads cloud placeholders; a repeated Analyze re-reads values that can change without a new modified time; a failed re-read keeps the last value with a note; a refused read can be said as `no access`. |
| **For whom, why** | Everyone who uses Analyze or a Unix permission column today. These are the defects G8–G10 that would make Owner untruthful if it were added on top of them. |
| **Source** | `MetadataService.cs` (M1–M10). `TabViewModel.GetMetadataText`'s state switch (`:1131-1144`): one case for `Denied`. |
| **Data and limits** | No new native call. The refresh factor for slow locations has no visible effect until a refreshable field exists on Windows (O4). |
| **Errors, freshness, cancellation** | As M3–M8. Cancellation is unchanged: a canceled producer still stores nothing (`:214-217`, `:246`). |
| **Done when** | Checklist OWN-07 (service half), OWN-08, OWN-16 (service half), OWN-24, and the existing `MetadataTests`, `MetadataDemandTests`, `MetadataPeekTests`, `MetadataOutstandingRetirementTests`, `MetadataOrderRetirementTests` pass (reason strings updated where a test asserts them). |
| **Outside** | Any change to Owner, to sorting, to the UI beyond the one cell text. |

### O2 — Identity, name resolver, Unix source

| | |
|---|---|
| **Behavior** | Linux and macOS: Owner and Group show the same names as before; an ID without a name shows its number and now sorts as a number; the number can appear a moment before the name; a refused read shows `no access` and a file system without owners `—`, where the cell used to be empty. Windows: nothing visible yet. |
| **For whom, why** | Linux and macOS users of the existing columns; and the foundation Windows needs. Closes G12–G14 and the Unix half of G10. |
| **Source** | New `Ownership.cs`, `PrincipalNames.cs`. `UnixPermissions.cs` (two additions). `IPlatform.cs`. `MetadataService.cs` (Owner, Group, Permissions detail). `AppServices.cs` (composition). |
| **Data and limits** | `statx` / `lstat`; `getpwuid_r` / `getgrgid_r`. Names are this system's view of the mount's numbers (§4, row 5). |
| **Identity, errors** | §6.3. UID 0 and GID 0 are values. The existing `NameCache`, its bounds and its two tests are untouched. |
| **Freshness, cancellation** | Resolver lifetimes and invalidation, §6.4. Nothing a lookup returns after `Invalidate` or `Dispose` is published. |
| **Done when** | Checklist OWN-04–OWN-06 (deterministic half), OWN-13, OWN-14, OWN-17 (resolver half), OWN-26. `UnixPermissionTests.Permission_columns_cover_files_and_folders` asserts the formatted name after waiting for the resolver; the two cache-bound tests pass unchanged. |
| **Outside** | The Windows reader. Tooltip and Inspect. Server-directed lookups. |

### O3 — Cell detail, tooltip, Inspect

| | |
|---|---|
| **Behavior** | All systems, every metadata column: a cell that has a detail shows it on hover; Inspect focused row shows the detail and the reason for an unavailable value, and the cached value of a metadata column that is not in view. |
| **For whom, why** | Pointer, keyboard and screen-reader users. Closes G11 and gives Owner its accessible route (§3.6). |
| **Source** | `TabViewModel.cs` (the cell method and its peek-only variant). `FileListControl.cs` (`RenderedCell`, `RenderRow`'s metadata branch, `UpdateTip`, `FocusedInspectionText`). |
| **Data and limits** | Rendered cells and `Peek` only. No I/O on pointer movement, key press or accessibility query. The existing 8,192-character tip limit and 1 MiB Inspect limit apply. |
| **Done when** | Checklist OWN-22, OWN-23. `ClippedCellInspectionTests` and `FileListAutomationTests` pass, with the existing "hover reveals only clipped cells" rule intact for cells without a detail. |
| **Outside** | `DescribeItem`, `FileListAutomation.cs`, any per-row node, the Inspect dialog's structure. |

### O4 — Windows reader, registration, chooser, copy

| | |
|---|---|
| **Behavior** | Windows: Owner can be added in Settings → Columns (with its description, 160 px), shows per §3, appears in the Sort menu when in the profile, and is offered by Analyze folder. |
| **For whom, why** | Windows users; the requested feature. Closes G1, G2, G16. |
| **Source** | New `WindowsOwnership.cs`. `WindowsPlatform.cs`. `MetadataService.cs` (`All`). `Columns.cs`, `ColumnProfilesEditor.cs`. `MainViewModel.Workspaces.cs`. `CommandRegistry.cs`. |
| **Data and limits** | §6.5. Local and SMB paths through the existing provider. FAT and exFAT, placeholders, refused and in-use files per §3.2. Names by local lookup only. |
| **Identity, errors, freshness** | §6.3–§6.5, §6.7. No elevation, privilege, write, enumeration or prompt. |
| **Done when** | Checklist OWN-01–OWN-03, OWN-04–OWN-06 (native half), OWN-07, OWN-09–OWN-11, OWN-25 (deterministic half). Gate GW1 passed on the CI's Windows x64 and ARM64 runners; GW2–GW5 passed, or recorded as not run with the limit stated in `docs/CAPABILITIES.md`. `ColumnProfileTests.Built_in_profiles_come_back_from_settings_as_they_were` passes with its expectations unchanged. |
| **Outside** | Default profiles. Server-directed lookups. Group or a primary-group column on Windows. Any ACL display. |

### O5 — Truthful sorting and Analyze

| | |
|---|---|
| **Behavior** | All systems, every metadata field: Analyze includes folders for fields that cover folders; its result states what was read; the order uses every value the pass computed even in folders larger than the cache; a sort pass has stable keys and schedules nothing. |
| **For whom, why** | Anyone sorting by a metadata column. Closes G3–G7 and G15. |
| **Source** | `EntrySorting.cs` (`EntryComparer`). `TabViewModel.cs` (`MetadataKey`, `AnalyzeAsync`, `RefreshSortDescription`, dropping kept values in `OnListingChanged`). |
| **Data and limits** | No new I/O. Kept values: 1,000,000 rows, 16,000,000 characters; key memory: 1,000,000 rows. Beyond them the result is labeled, not silently partial. |
| **Sorting, cancellation** | §6.6, §6.10. Esc, leaving the folder and closing the tab cancel as today (`:1187-1202`); a canceled pass keeps nothing and says so. |
| **Done when** | Checklist OWN-18–OWN-21. `SortingTests`, `EntryStoreTests`, `SortFocusContinuityTests`, `AnalysisLeaveTests`, `AnalysisCurrentDemandTests`, `AnalyzeFolderChoiceTests` pass (completion-text assertions updated). |
| **Outside** | The external sort, `StableSort`, `ListingModel`. Persisting a metadata sort in tab state. Re-sorting when values arrive. |

### O6 — Freshness wiring

| | |
|---|---|
| **Behavior** | All systems: returning to FileCat or to a tab re-reads the painted rows' refreshable values at once; Reread also forgets names. |
| **For whom, why** | The "changed it elsewhere, came back" case, without polling. |
| **Source** | `MainWindow.axaml.cs` (`Activated`), `TabViewModel.cs` (`OnIsActiveTabChanged`), `MainViewModel.Commands.cs:237`, `MainViewModel.LocationContext.cs:122`. |
| **Data and limits** | Painted rows only. An idle panel in a background window reads nothing. |
| **Done when** | Checklist OWN-15, OWN-16, OWN-17. |
| **Outside** | The folder watcher's filter. Any timer. |

### O7 — Documentation and evidence

| | |
|---|---|
| **Behavior** | `docs/CAPABILITIES.md` describes Owner on Windows with its limits (shares, FAT and exFAT, placeholders, not a monitor) and no longer says Owner is in the Unix Details profile. ADR-10 lists the real built-in fields, describes the backlog guard as it is, and records the kept values and the name resolver. |
| **Source** | `docs/CAPABILITIES.md:46`; `docs/adr/ADR-10-metadata-scheduling.md`. Native results go to `docs/validation/UX_UI_IMPLEMENTATION_STATUS.md` in that file's dated, build-specific form. |
| **Done when** | Checklist OWN-27; §8.2 and §8.3 carry a status for every row. `tests/FileCat.NativeAcceptance/check-ux-acceptance-ledger.py` passes if the ledger was touched at all. |
| **Outside** | Reopening accepted ledger rows (§8.5). |

---

## 8. Validation and evidence checklist

### 8.1 Rules

- This section plans validation. Nothing in it has been run. The probe of §2.4 is an observation, not a result.
- Statuses: **Planned**, **Passed**, **Failed**, **Blocked** (names the missing prerequisite), **Not run**. A native
  environment that is not available leaves its row *Not run*; it never becomes *Passed* by inference.
- Deterministic fakes for refused reads, unresolved and slow names, flipping owners and cancellation. Native fixtures
  only for what a fake cannot prove. Fixtures live in the tests' own temporary folders and are removed; no user file
  and no system permission is changed. No test names a real account or a profile path.
- A row is added only for a new concrete risk. A passed row is not repeated without a relevant code change or a new
  failure.

### 8.2 Feature checklist

One checklist for the feature. "Where" names the existing file to extend, or a new one. "Evidence" is filled in when
a row is run: the test names and the run that executed them, or the dated native record. Every row starts **Planned**.

| ID | Expected outcome | Method | Where | Evidence | Status |
|---|---|---|---|---|---|
| OWN-01 | Owner is offered in the column chooser on Windows, Linux and macOS; Group and Permissions are not offered on Windows; the Unix list is unchanged. | Headless | `tests/FileCat.App.Tests/ColumnProfileTests.cs` | — | Planned |
| OWN-02 | A profile with Owner comes back after a restart with its width and position; built-in profiles come back as they were; the column is reachable by horizontal scrolling at narrow widths and large fonts; the header menu and the View menu's profile entries behave as before. | Headless | `ColumnProfileTests.cs` (the restart pattern at l.262); `HorizontalColumnTests.cs` (the theory at l.28, with a profile that has Owner); `ColumnHeaderMenuTests.cs` and `CustomColumnProfileMenuTests.cs` unchanged and passing | — | Planned |
| OWN-03 | Owner sorts from the header and from the Sort menu when shown; Analyze folder offers it on Windows with the new description. | Headless | `tests/FileCat.App.Tests/PanelSortControlTests.cs`, `AnalyzeFolderChoiceTests.cs` | — | Planned |
| OWN-04 | Local, domain, service and group owners format as qualified names; a name with an empty domain shows alone. | Fake lookup; native lookup of `S-1-5-18`, `S-1-5-32-544`, `S-1-1-0` and the test's own owner | new `tests/FileCat.Core.Tests/OwnershipFieldTests.cs`; new `tests/FileCat.Platform.Windows.Tests/WindowsOwnershipTests.cs` | — | Planned |
| OWN-05 | An owner without a name shows its SID, UID or GID; UID 0 and GID 0 are values; the ID is in the detail when a name shows. | Fake; existing unknown-ID case | `OwnershipFieldTests.cs`, `tests/FileCat.Core.Tests/UnixPermissionTests.cs` | — | Planned |
| OWN-06 | Two owners with the same shown name and different IDs stay apart in order and detail. Long names and names with control or direction characters are escaped in cell, tooltip and Inspect. | Fake; headless | `OwnershipFieldTests.cs`, `tests/FileCat.App.Tests/ClippedCellInspectionTests.cs` | — | Planned |
| OWN-07 | Refused, in use, not supported, gone and failed each give their own cell text and reason; none shows an owner; a failed name lookup is distinct from a failed read. | Fake source; native refused read where `System Volume Information` is present and unreadable | `OwnershipFieldTests.cs`, `WindowsOwnershipTests.cs` | — | Planned |
| OWN-08 | A re-read that fails keeps the last value with a note and is tried again once per interval, not more. | Fake field | `tests/FileCat.Core.Tests/MetadataTests.cs` | — | Planned |
| OWN-09 | A file and a folder report their own owner. A junction, and a symbolic link where one can be made, report the link's owner, not the target's; a link whose target is gone still reports. | Native: `Junction.Create` to the system folder (Windows); a link to a root-owned file (Linux, macOS) | `WindowsOwnershipTests.cs`, `UnixPermissionTests.cs` | — | Planned |
| OWN-10 | The parent row, drives and shares, archive members and SFTP rows have no Owner value, and the source is never called for them. | Headless with a counting fake source | new `tests/FileCat.App.Tests/OwnerColumnTests.cs` | — | Planned |
| OWN-11 | Reading a folder's owner reads nothing inside the folder. | Counting fake: the paths read are exactly the listed rows | `OwnerColumnTests.cs` | — | Planned |
| OWN-12 | With Owner not in the profile: no read and no lookup through scrolling, hovering, Inspect, accessibility queries, and sorting by other columns. | Counting fake | `OwnerColumnTests.cs` | — | Planned |
| OWN-13 | With Owner shown: only painted rows are read; rows scrolled away are dropped; many rows with one owner cause one lookup. | Counting fake; gated producer | `tests/FileCat.Core.Tests/MetadataDemandTests.cs`, new `PrincipalNamesTests.cs` | — | Planned |
| OWN-14 | With every lookup blocked: IDs show at once, frames keep rendering, at most four lookups are in flight, at most 256 wait, and work queued on a device is not delayed by them. | Gated fake lookup | `PrincipalNamesTests.cs`, `OwnerColumnTests.cs` | — | Planned |
| OWN-15 | An owner that changes while size and time stay the same is shown after the interval at the next paint, at once on window or tab activation, and at once on Reread; an unpainted row is not read. | Flipping fake source | `OwnerColumnTests.cs` | — | Planned |
| OWN-16 | A file replaced at the same path gets its own value. A second Analyze re-reads owners. | Fake; fake field | `MetadataTests.cs`, `OwnerColumnTests.cs` | — | Planned |
| OWN-17 | Navigating away, closing the tab or Reread while a read or a lookup is blocked: nothing arrives afterwards, and no value lands on another entry. | Gated fakes, the pattern of `A_sidecar_change_cannot_restore_an_inflight_old_verification` | `MetadataDemandTests.cs`, `PrincipalNamesTests.cs` | — | Planned |
| OWN-18 | The order of §6.6: names, then IDs numerically, ties by ID, unknown last in both directions, parent row first, folders first. | Unit | `tests/FileCat.Core.Tests/SortingTests.cs` | — | Planned |
| OWN-19 | Within one pass each row's key is read once and cannot change; a sort queues no work and starts no lookup; focus and marks survive the sort. | Counting provider; headless | `SortingTests.cs`, `tests/FileCat.App.Tests/SortFocusContinuityTests.cs` | — | Planned |
| OWN-20 | Analyze counts folders for a folder field and not for a files-only field; the result text matches the counts for each outcome of §6.10; canceling says how far it got. | Headless with fakes | `tests/FileCat.App.Tests/AnalysisLeaveTests.cs`, `AnalysisCurrentDemandTests.cs` | — | Planned |
| OWN-21 | With the cache limit set below the listing size the final order still uses every computed value. With the kept-values bound set below the listing size the result says so. Replacing the store drops the kept values and withdraws a "complete" banner. | Shrunk limits, 100 rows | `OwnerColumnTests.cs` | — | Planned |
| OWN-22 | Tooltip and Inspect show the full identity and the reason for an unavailable value, from cached data, including for a column scrolled out of view. No producer and no item reference is requested. | Headless, existing counters | `ClippedCellInspectionTests.cs` | — | Planned |
| OWN-23 | The focused row's spoken name and value do not change, and no property-changed event is raised, when a value or a name arrives. No accessibility node is added. | Event count | `tests/FileCat.App.Tests/FileListAutomationTests.cs` | — | Planned |
| OWN-24 | A cloud-placeholder row shows `—` with the reason when painted and when analyzed; its producer is not called. | Fake field, `EntryFlags.Offline` | `MetadataTests.cs` | — | Planned |
| OWN-25 | On a network location the re-read interval is 30 s; a dropped connection gives `error` with the reason and one retry per interval. | Unit with `slowLocation`; native in GW4 | `MetadataTests.cs` | — | Planned |
| OWN-26 | Linux and macOS: named owners, groups and permissions show as before; the two name-cache bound tests pass unchanged. | Existing tests | `UnixPermissionTests.cs` | — | Planned |
| OWN-27 | `docs/CAPABILITIES.md` and ADR-10 say what the source does. | Review | — | — | Planned |

### 8.3 Native gates

Claims that only a real system can settle. Each is a named check at implementation time, not a reason to keep planning.

| ID | Claim | How | Status |
|---|---|---|---|
| GW1 | Windows: the link's own owner for a junction, a directory symbolic link and a file symbolic link, on x64 and ARM64. | `WindowsOwnershipTests` on the CI's Windows x64 and ARM64 runners; symbolic links where the runner may create them | Junction on x64 observed [P1]. Otherwise **Not run** |
| GW2 | Windows: a junction or link whose target is gone still reports its owner. | Same tests | **Not run** |
| GW3 | Windows: on FAT32 and exFAT the reported owner is `S-1-1-0` and the volume lacks `FILE_PERSISTENT_ACLS`. | A small virtual disk formatted each way on the Windows test VM, read only | **Not run** (no such volume was mounted) |
| GW4 | Windows over SMB, UNC and mapped drive, against a Windows share and a Samba share: the read works with the long-path prefix; the number of round trips; a server's local SIDs and `S-1-22-…` stay IDs; a dropped connection gives `error` and a retry; no prompt appears; whether the volume-flag call works there. | Manual on the test machines, with a packet or SMB client trace for the round trips | **Not run** |
| GW5 | Windows joined to a domain whose controller is unreachable: how long a lookup takes, which error it returns, and that IDs, local names and the rest of the UI are unaffected. | A domain lab. Until one exists this is covered by the gated fakes of OWN-14 only, and the documentation says so | **Not run** |
| GW6 | Windows on ReFS: the same reads work. | A ReFS volume, if one is available | **Not run** (optional) |
| GL1 | Ubuntu: `statx` returns UID and GID with their mask bits on ext4, btrfs, vfat and an NFS or CIFS mount; `getpwuid_r` and `getgrgid_r` return "no entry" as 0 with a null result. | The Ubuntu test VM, as an ordinary user | **Not run** |
| GM1 | macOS on Apple silicon: `lstat` on APFS and on an SMB mount; the "no entry" return of `getpwuid_r`; `lstat` does not download an iCloud dataless file. | The Mac test machine | **Not run** |
| GA1 | A screen reader reads the longer Inspect text. | Not repeated: the journey is accepted as AC-S3-061 and O3 changes its content, not its structure (§8.5) | Covered unless O3 changes the dialog |

### 8.4 Performance and capacity checks

No threshold is invented here. Each check records a before and an after on the same machine and build settings, and
the acceptable band is the spread of the "before" runs.

| ID | Check | Baseline |
|---|---|---|
| PF1 | Feature not in use: paint and scroll a 1,000,000-row synthetic listing (`src/FileCat.App/Services/SyntheticListingProvider.cs`, the TV-01 fixture; timing through `FileListControl.RenderTimed`, `FileListControl.cs:73`). After the change the figures stay within the spread of five runs before it. | The same run before the change |
| PF2 | Owner shown over a fake source with a handful of distinct owners: frame time and allocations per frame, recorded. The band is set from PF1's spread once measured, and written into the evidence. | PF1 |
| PF3 | Sort 100,000 and 1,000,000 synthetic rows by a metadata field before and after the key memory: time and allocations; expected not worse. | The same run before the change |
| PF4 | Capacity, not speed: OWN-21 with the cache limit at 8 and 100 rows. A small configurable limit replaces creating tens of thousands of files to mirror a constant. | — |
| PF5 | Reference only: owner reads per second by the real reader on local NTFS, beside the probe's 45–114 microseconds an item [P6]. | The probe |

### 8.5 The UX ledger and repeat rules

`docs/validation/UX_UI_ACCEPTANCE_LEDGER.md` and `UX_UI_SESSION_WORKFLOW.md` stay the source of truth for required
UX work. This feature adds no finding to them and reopens none.

- Accepted rows this feature touches by content only: AC-S3-061 (clipped-cell tooltip and Inspect focused item),
  AC-S2-026 (columns reachable at narrow widths; sort keeps focus), AC-S1-010 (list accessibility). The deterministic
  tests named in §8.2 guard their invariants.
- No repeat is authorized by this plan. A repeat of one of those rows needs an entry in the ledger's repeat
  authorization record with the scenario ID, the concrete reason and a stopping condition, and is justified only if
  an implementation step changes what the row accepted: the Inspect dialog's structure or focus (O3), or focus
  behavior on sort (O5). A docs-only change never is.
- New native results (the gates of §8.3) are recorded as dated, build-specific entries in
  `docs/validation/UX_UI_IMPLEMENTATION_STATUS.md`: build or revision, system and backend, fixture, input, result,
  evidence path.
- Desktop input for native checks happens on the isolated test machines, not on the owner's workstation.

---

## 9. Risks, assumptions and exclusions

### 9.1 What is established

Source facts are in §2.2 and §2.3 with their lines. Vendor statements are linked where they are used. The probe of
§2.4 established, on one machine, the junction behavior, the need for `READ_CONTROL`, the three open failures, the
lookup results and the cost of a local read.

### 9.2 Source-derived risks (reasoned from code, not measured)

| Risk | Consequence | Handling |
|---|---|---|
| The cache evicts an analysis's values (G5) | A sort claimed complete is not | Kept values (§6.10); OWN-21 |
| A slow or dead name service | Names missing for a while | IDs show; four threads at most; 30 s probes (§6.4); OWN-14; GW5 |
| Lookups that leave this computer | On a domain-joined machine a shown Owner column causes directory lookups for unknown SIDs | Only when the column is shown or analyzed; bounded; documented. D1 keeps it opt-in |
| A `READ_CONTROL` open on a file another program holds with an opportunistic lock | That program may be asked to release the lock | Same as Explorer's owner column; reads are per painted row; noted, not mitigated |
| A server that audits successful "read permissions" access | Each owner read can add an entry to its security log | Reasoned, not measured. Same as any tool that shows owners; per painted row and per refresh interval; stated in `docs/CAPABILITIES.md`. One more reason for D1 |
| `GetVolumeInformationByHandleW` over SMB | The FAT check cannot run there | The owner stands as read (§6.5); GW4 records what a share of a FAT volume reports |
| All Unix mounts share one device queue | A stalled mount delays other local metadata | Existing; lookups at least no longer add to it (O2). Deferred: per-mount keys |
| Formatting reads the name cache under a lock on the UI thread | Frame cost with many Owner cells | One dictionary read per cell; PF2 |
| A duplicated machine SID (cloned installations) | A foreign local account's SID resolves to this computer's account | Rare; the qualified name shows this computer's name, and the detail shows the SID. Documented |
| `main` moves under the plan | Line numbers drift | Symbols are named beside every line; re-check before editing |

### 9.3 Assumptions

- The feature is built on `main`, which since 2026-11-16 contains the archive-writing branch the plan was drafted on.
  Every line the plan cites is the same on both (§2.1).
- The CI keeps running tests on Windows x64, Windows ARM64, Ubuntu and macOS (ADR-12).
- FileCat does not enable backup or restore privileges, including when started elevated.

### 9.4 Open questions, with the default taken

None blocks implementation.

| Question | Default | What flipping it costs |
|---|---|---|
| Should Owner be in the built-in Windows Full profile for users who never customized columns? | No (D1): no owner reads and no directory lookups without a choice, and Name keeps its width | One entry in `WindowsDefaults()` (`Columns.cs:115-124`) and the expectation at `tests/FileCat.App.Tests/ColumnProfileTests.cs:34` |
| Should Analyze skip cloud placeholders for every field, or only for ownership? | Every field (D7) | Limit M4 to fields that declare it; Dimensions and Version would again download placeholder content when analyzed |
| A built-in "Security" or "Audit" profile? | No (D12). A custom profile does it in a few steps today | A sixth default profile, appended so no shortcut moves |
| Rename the Unix "Attributes" column (extended-attribute count), which reads like Attr? | No; the add-column note explains it | A title change visible in existing profiles |
| Start- or middle-elision for long qualified names, so the account part stays visible? | No; widen the column or read the tooltip | New truncation in the cell text layout |

### 9.5 Exclusions

Taking ownership, changing owners or permissions, any elevation or privilege, account or group enumeration, content
reads, server-directed name lookups, ownership for SFTP, FTP and archive members, any column in result sets or the
dedicated layouts, any new or changed built-in profile, the folder watcher, the listing row format (`EntryData`,
`EntryStore`), the sort engine, and every column of §5 other than Owner.

### 9.6 Existing issues noticed and deliberately left

| Issue | Where |
|---|---|
| A metadata sort is not restored with a tab: only the sort field is saved, not which metadata field. | `TabViewModel.cs` `ToState` / `ApplyState` |
| After Analyze on a column the profile does not show, the list is sorted by something invisible. This plan only adds the hint of §6.10. | `MainViewModel.Workspaces.cs:305-324` |
| `IsSlowLocation` does not recognize network or removable mounts on Linux and macOS, so expensive fields run there unasked. | `TabViewModel.cs:1113` |
| Two different "slow location" tests exist. | `TabViewModel.cs:1113`; `MainViewModel.Commands.cs:934` |
| Created can show an inferred time on Unix (§5.4). | `LocalFileSystemProvider.cs:202` |
| `FileSystemItemInfo.LinkCount` and `FileId` are never filled with real values. | `FileSystemOperations.cs:37-50`, `:420` |
| Analyze on a result set would queue on the listing's device, not each item's. Unreachable today: result sets have no metadata columns. | `TabViewModel.cs:1205` |

---

## 10. Definition of done and launcher

### 10.1 Done means

1. O1–O7 are merged in order, each with its tests.
2. Every row of §8.2 is **Passed**. Every row of §8.3 is **Passed**, or **Not run** or **Blocked** with the reason, and
   `docs/CAPABILITIES.md` states the limit that follows from it.
3. PF1 and PF3 are within their bands; PF2's band is recorded.
4. On Windows a user can add Owner from the header menu's Set up columns…, sees qualified names or SIDs with the
   states of §3.2, can read the full identity and any reason with the pointer and with Ctrl+Shift+I, can sort by it,
   and can run Analyze folder on it with a result text that matches what happened.
5. On Linux and macOS the existing columns behave as before for named owners, and gain the ID, the detail and the
   honest states.
6. With Owner not shown, FileCat performs no owner read and no name lookup.
7. No built-in profile, saved profile, shortcut or settings schema changed. No file, owner or permission is written.
8. CI is green on Windows x64, Windows ARM64, Ubuntu and macOS.

### 10.2 Launcher

```
Implement docs/design/FILECAT_COLUMN_OWNERSHIP_IMPLEMENTATION_PLAN.md.

Work packages O1..O7 in order (section 7). One package per reviewable change; finish its "Done when" before the next.
Before editing, re-locate every cited symbol: the line numbers are those of main at 11314715, and main moves.

Hold to the decisions D1..D12 (section 1.4) and the contracts of sections 3 and 6. In particular:
- the cached value is the ID; names are read from PrincipalNames.Peek when formatting and never stored in the value;
- only a producer calls IOwnership or PrincipalNames.Request; painting, sorting, hover, Inspect and accessibility use
  Peek on both caches and start no work;
- name lookups run only on the resolver's own threads (2, at most 4), never on a device queue or the thread pool;
- the Windows read is CreateFileW(READ_CONTROL, all sharing, BACKUP_SEMANTICS | OPEN_REPARSE_POINT) +
  GetSecurityInfo(OWNER), mapped per section 6.5; no elevation, privilege, write, enumeration or prompt;
- no built-in profile, settings key, schema or shortcut changes; EntryData, EntryStore, ListingModel, the providers
  and FileListAutomation.cs do not change. If one seems to need it, stop and re-read the plan.

Tests: extend the files named in section 8.2; use fakes for refused reads, slow or missing names, flipping owners and
cancellation; native fixtures only for the gates of section 8.3, in the tests' own temporary folders. No test names a
real account or profile path. Record each checklist row and gate as Passed, Failed, Blocked or Not run; never mark a
native claim Passed from inference. Do not reopen accepted UX ledger rows (section 8.5).

Do not add any column of section 5 other than Owner. Report anything the plan got wrong instead of working around it.
```

---

## Appendix A — Where the cited lines were before the merge

`main` at `9cc9f2ac`, before `codex/archive-writing` merged into it. Only for reading the plan against that older tree;
on the baseline the plan's own numbers apply. Files not listed have the same lines in both.

| File | This plan (baseline) | `main` at `9cc9f2ac` |
|---|---|---|
| `src/FileCat.App/ViewModels/TabViewModel.cs`, metadata section | `:1107`–`:1258` | 73 lines lower in number: `VisibleStoreIndices` 1034, `IsSlowLocation` 1040, `GetMetadataText` 1042, `SortByMetadata` 1075, `MetadataKey` 1086, `AnalyzeAsync` 1102 (its `Compute` call 1142, its completion text 1158), `CancelAnalysis` 1178 |
| `TabViewModel.cs`, `OnIsActiveTabChanged`; sort description | `:284`; `:142-147` | 283; 141–146 |
| `src/FileCat.App/ViewModels/MainViewModel.Commands.cs`, Reread; `IsSlowLocation(string)` | `:237`; `:934` | 235; 932 |
| `src/FileCat.App/ViewModels/MainViewModel.Operations.cs`, Attributes job | `:704` | 621 |
| `src/FileCat.App/Services/AppServices.cs`, hidden-data source; `Metadata` | `:44`; `:137` | 43; 128 |
| `src/FileCat.App/Views/MainWindow.axaml.cs`, `Activated` | `:209-216` | 208–215 |
| `src/FileCat.Platform.Windows/WindowsFileOperations.Native.cs`, `Long`; `Junction.Create` | `:114`; `:795` | 103; 784 |
| `src/FileCat.Core/Resources/Entries.cs`, `ItemRef.FileSystemPath` | `:189` | 184 |
| `src/FileCat.Core/FileSystem/FileSystemOperations.cs`, `FromInfo` | `:420` | 398 |
| `src/FileCat.Core/Threading/DeviceIoScheduler.cs`, constructor | `:47-52` | 45–50 |
| `src/FileCat.Core/Archives/ZipProvider.cs`, `ZipMemberTag`; `GetItemRef` | `:12-18`; `:370` | 12; 312 |
| `src/FileCat.Archives/ArchiveProvider.cs`, `ArchiveMemberTag`; `GetItemRef` | `:10-26`; `:223` | 10–22; 202 |
| `src/FileCat.Archives/ArchiveFormats.cs`, TAR listing | `:377-418` | 522–536 |
| `src/FileCat.Archives/Writing/*` | present | absent |

## Appendix B — Platform references

Read on 2026-11-15 and 2026-11-16. They establish what the platforms promise, not what FileCat does.

| Topic | Reference |
|---|---|
| Owner by handle, required access, freeing the descriptor | [GetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getsecurityinfo) |
| Owner by path, null owner | [GetNamedSecurityInfoW](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getnamedsecurityinfow) |
| Name lookup, where it searches, `ERROR_NONE_MAPPED` | [LookupAccountSidW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountsidw), [SID_NAME_USE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-sid_name_use) |
| Opening folders and links without following them | [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) |
| Volumes that keep ACLs | [GetVolumeInformationByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew) |
| Placeholder attributes | [File Attribute Constants](https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants) |
| Allocation and link count; compressed size | [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info), [GetCompressedFileSizeW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) |
| Creation time on Unix | [FileSystemInfo.CreationTime](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesysteminfo.creationtime) |
| `statx`: no-follow, returned mask, blocks, links | [statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) (man-pages 6.19) |
| Account lookup returns | [getpwuid_r(3)](https://man7.org/linux/man-pages/man3/getpwuid_r.3.html) (man-pages 6.19) |
| Mode bits and ACLs | [acl(5)](https://man7.org/linux/man-pages/man5/acl.5.html) |
| Allocated size on macOS | [fileAllocatedSizeKey](https://developer.apple.com/documentation/foundation/urlresourcekey/fileallocatedsizekey), [totalFileAllocatedSizeKey](https://developer.apple.com/documentation/foundation/urlresourcekey/totalfileallocatedsizekey) |

Not consulted, and flagged where it matters: the archived Apple `stat`/`lstat` manual (the plan relies on the
existing `lstat` code and gate GM1), and the SSH.NET and FluentFTP attribute APIs (P1 is deferred).
