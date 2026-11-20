# FileCat: Owner column and useful panel columns — implementation plan

**Status:** O1–O7 complete on `codex/column-ownership`, including the passing bounded performance gates and
[green full CI/package workflow](https://github.com/benny-cz/FileCat/actions/runs/43936482729) at dc22579a (attempt 2).
See the [finite acceptance record](../validation/column-ownership/ACCEPTANCE-20261116.md)
and dated [O1](../validation/column-ownership/O1-20261116.md), [O2](../validation/column-ownership/O2-20261116.md),
[O3](../validation/column-ownership/O3-20261116.md), [O4](../validation/column-ownership/O4-20261116.md),
[O5](../validation/column-ownership/O5-20261116.md), [O6](../validation/column-ownership/O6-20261116.md),
[O7](../validation/column-ownership/O7-20261116.md) evidence.
GW1/GW2 Windows x64 core, GL1 Ubuntu x64 core and [GA1 native NVDA](../validation/column-ownership/GA1-20261116.md) have evidence.
Windows/Linux ARM64 and the new guarded macOS ownership route remain conservatively unavailable. Historical PF1/PF3
misses are preserved; the predeclared controlled comparison passes without widening acceptance bands.
**Revised:** 2026-11-16 to address AR-001–AR-013 in the independent review; implementation progress is recorded above.
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
| **[P]** | Author-reported observation from one disposable probe (§2.4). Raw script/output were unavailable to the independent review; not an independently reproduced test result. |
| **[D]** | Stated by vendor documentation; link beside the claim. Says nothing about what FileCat implements. |
| **[G]** | Not verified. An implementation-time gate in §8.3 must settle it. |

Names marked *(new)* are proposals: keep the responsibility, fit the name to the code around it.

## Review record

The original draft reported three reviews on 2026-11-16. Their full records were unavailable to the independent
review, so their conclusions are historical claims, not evidence overriding the design below.

| Review | Outcome |
|---|---|
| User experience and scope | 12 findings, all merged. The larger ones: a dropped share keeps values already read; the tooltip rule is narrowed so the accepted criterion (FCUX-061) holds; Inspect handles columns out of view and fields that do not apply; one description per field; the short form for long SIDs; Analyze wording that matches its counts; checklist rows and gates assigned to packages. |
| Platform correctness | Originally reported 9 merged findings and confirmed quotations. The later independent review corrected the clear-mask, lookup-routing, provenance and side-effect claims; this revision uses those corrections. |
| Architecture and performance | Unfinished when the original draft was written. The independent review subsequently examined the cache, resolver, sort and retention design and found the corrections recorded below. |

The independent review referenced by this plan (its separate file is not present in this checkout) assessed the original
plan with SHA-256 `128545c20e770ddc8856d540f00fec689ff5113de298ed9e59f816ddf82f6406` as **Not ready**.
This revision addresses its findings as follows; it does not relabel unrun implementation checks as passed.

| Finding | Correction in this revision |
|---|---|
| AR-001 | Valid returned UID/GID masks only; conservative unsupported fallback (§3, §6.3). |
| AR-002 | Immutable ordering snapshot at every size, including streaming/external merges (§6.6). |
| AR-003 | Targeted ownership retirement and captured-store publication checks (§6.7, §6.10). |
| AR-004 | Shared byte reservations, bounded identity retention and truthful capacity coverage (§6.6, §6.10). |
| AR-005 | Typed last-known/gone outcomes, consistent Compute result, busy retry and snapshot invalidation (§3, §6.2, §6.10). |
| AR-006 | Per-instance resolver ownership, bounded retiring calls and demand-based retries; no cross-domain circuit (§6.3–§6.4). |
| AR-007 | Shared eligibility, effective intervals and explicit activation repaint (§6.2, §6.7–§6.8). |
| AR-008 | Detected-placeholder scope, guarded macOS ownership reads and qualified OS side effects (§3.4, §6.3, §6.8). |
| AR-009 | Evidence-based FAT/exFAT handling and exact extended-path conversion (§6.5). |
| AR-010 | Newly enabled Windows Owner analysis restricted to filesystem panels (§4, §6.9–§6.10). |
| AR-011 | Stage-specific acceptance, mandatory shipping gates, actual performance pass rules and corrected launcher (§7–§10). |
| AR-012 | Accurate downgrade behavior, vertically visible focused-row demand and cached inspection context (§3.6, §6.9, §6.11). |
| AR-013 | Qualified evidence, pinned runtime sources, factual deferred-column rationale and scoped Permissions promises (§2, §5, Appendix B). |

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
- Sorting by Owner uses immutable keys and never performs provider I/O or name lookup. Analyze folder includes folders,
  retains values within a shared budget, and states actual read and retained coverage.

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
- In a file-system panel a value FileCat cannot read says why (`no access`, `—`, `error`), instead of an empty cell
  that reads as "no owner". In panels without file-system paths (archives, SFTP, phones) the column stays empty, as
  every such column does today, and Inspect says why.
- Sorting a large folder by Owner gives an order that is what the banner says it is.

### 1.4 Product defaults chosen

These settle the product choices the request left open. D1 and D12 are small optional default/profile changes;
D7's detected-placeholder scope and native guard are explicit correctness constraints, not a claim of complete
cross-platform cloud detection (§6 and §9.4).

| ID | Decision |
|---|---|
| D1 | Windows Owner is **optional**: offered in Settings → Columns and in Analyze folder for filesystem panels; in no built-in profile. |
| D2 | One column on all systems: header "Owner", settings key `meta:owner`, field ID `owner`. |
| D3 | The displayed entry's own owner. The final path component is never followed; parent folders are traversed normally. |
| D4 | The stored value is a typed principal (namespace + canonical ID). Names are never stored in it. |
| D5 | Names come from this computer's account lookup only. No lookup is directed at a file server in the first delivery. |
| D6 | One new metadata state, **Denied**; a producer may return a ready `MetadataValue`. A small typed observation marker distinguishes Current, LastKnown and Gone (§6.2). |
| D7 | Entries marked `Offline` invoke no producer, automatically or explicitly, for every field. This is not a complete cross-platform cloud detector. macOS Owner/Group additionally use the non-materializing guard of §6.3. |
| D8 | Accessible route: the Sort menu and Inspect focused row. The spoken row summary does not gain Owner. |
| D9 | Sort order: names A–Z, then IDs without a name (numerically), unknown last in both directions. Keys stay immutable across the whole ordering operation at every size (§6.6). |
| D10 | Analyze retains a deterministic subset within shared byte, row and character bounds; it reports read outcomes and actual retained coverage. Unretained rows have fixed unknown sort keys. |
| D11 | Automatic freshness: eligible demanded rows re-read after 5 s (30 s on recognized Windows network locations), with expiry and repaint on activation. Reread invalidates metadata. Analyze explicitly reads the listing. Genuine Denied/Unsupported results do not retry on timer/activation; Reread, Attributes jobs, listing replacement and Analyze can retry them. Busy/transient failures retry at the interval. |
| D12 | No new built-in profile, no renamed column, no change to any default profile. |

---

## 2. Baseline and evidence

### 2.1 Source fingerprint

- The baseline is `main` at `11314715`. The plan was drafted against the working tree of `codex/archive-writing` at
  `04bbd2b5`, as directed by the owner during planning. That branch was then 494 commits ahead of its merge base,
  `main` at `9cc9f2ac883db59621f2b5f22d65c73fea283c5a` (2026-11-14), and it merged into `main` on 2026-11-16. From
  `04bbd2b5` to the baseline ten files changed. The only one cited here is `MainWindow.axaml.cs`: two lines added at
  833 and one changed at 1027 (menu keys), all below the one range this plan cites there (`:209-216`).
- The independent review inspected `main` through `20899295ee01322834e97218291b3e27e5376700`; intervening changes
  from `11314715` were documentation only, with no `src`/`tests` difference. The intended baseline remains unchanged.
- Relevant Git-tree differences between `9cc9f2ac` and the baseline:
  - **Identical on both** (line endings aside): `MetadataService.cs`, `Columns.cs`, `ColumnProfilesEditor.cs`,
    `PanelView.axaml.cs`, `FileListControl.cs`, `FileListAutomation.cs`, `MainViewModel.Workspaces.cs`,
    `TabViewModel.Verification.cs`, `EntrySorting.cs`, `ExternalViewBuilder.cs`, `EntryStore.cs`, `UnixPermissions.cs`,
    `UnixFiles.cs`, `UnixFileRecords.cs`, `LocalFileSystemProvider.cs`, `ChangeMonitor.cs`, `PathUtil.cs`,
    `IPlatform.cs`, `WindowsPlatform.cs`, `WindowsFileRecords.cs`, `ElevationBroker.FileTrust.cs`, the cited
    `FileCat.Remote/Sftp` and `Ftp` adapters (not their whole directories), `ResultSetProvider.cs`, ADR-02/10/12, the UX ledger and workflow, and
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
| E13 | `Forget`, `ForgetFolder` and `Invalidate` retire matching running productions. Store replacement alone does not call these for ownership. | `MetadataService.cs:75-80`, `:241-246`, `:326-358`; `TabViewModel.Verification.cs:36` |
| E14 | Reread invalidates all metadata, and so does a finished Attributes job. | `src/FileCat.App/ViewModels/MainViewModel.Commands.cs:237`; `MainViewModel.LocationContext.cs:122`; `MainViewModel.Operations.cs:704` |
| E15 | Sorting by a metadata field calls the key provider twice per comparison; unknown keys sort last in both directions; ties fall to name, then store index. | `src/FileCat.Core/Listing/EntrySorting.cs:55-74` |
| E16 | Every `Load` and applied `Refresh` uses a new `EntryStore`. This identity guards queued paint demand, but is absent from the metadata key and from Analyze's same-location completion check. | `src/FileCat.Core/Listing/ListingModel.cs:395`, `:444`; `TabViewModel.cs:1130`, `:1190-1194`, `:1228` |
| E17 | Hover shows a tip for clipped cells, with three existing exceptions that show one regardless: the Verified column's own tip, the Git label over the icon, and the hidden-character note for a name. A plain value shown in full has none (accepted as FCUX-061). Ctrl+Shift+I reports every column of the focused row from rendered text; a metadata cell that rendered empty or was not painted reads "(not yet displayed)". Neither starts metadata work. | `FileListControl.cs:1812-1845`, `:1867-1900`; `src/FileCat.App/Views/PanelView.axaml.cs:1395-1411` |
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

The author reported one disposable PowerShell probe (P/Invoke through `Add-Type`), run 2026-11-15 on Windows 11 Pro
build 26320, x64, NTFS, in an elevated shell and a scratch folder removed afterwards. Raw script/output were not
available to the independent review. These observations do not establish ordinary-user or other-platform behavior.

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

**Owner** is the principal the mounted file system/OS reports as the owner of the listed entry itself. Reported
ownership can be synthesized; persistent storage provenance is stated only when established.

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

Owner/Group cell text, detail and behavior follow this table. Shared rendering uses the same meaning when another
field produces one of these states; Permissions retains its existing native failure semantics (§6.3).
"Detail" is what the tooltip and Inspect focused row show.

| Situation | State | Cell | Detail (examples) | Retry |
|---|---|---|---|---|
| Owner read, name known | `Available` | `CONTOSO\alice` | `User account · S-1-5-21-3623811015-3361044348-30300820-1104` | Value re-read per §6.7; name kept 10 minutes |
| Owner read, no name here | `Available` | `S-1-5-21-…-1013` (the short form of §3.5; every other ID in full) | `This computer has no name for this ID. It may belong to another computer or domain, or to an account that no longer exists.` | Eligible after 2 minutes; next producer demand retries |
| Owner read, lookup failed or slow | `Available` | the ID | `The name is unavailable just now. It becomes eligible for retry after 30 seconds, when ownership is next read.` | Next producer demand after 30 seconds |
| Owner read, lookup queued/running | `Available` | the ID | `Name lookup in progress.` | — |
| Owner read, name request not retained or dropped | `Available` | the ID | `No name is cached. The next ownership read can request it.` | Next producer demand |
| Ownership being read | `Pending` | `…` (muted, as today) | `Being read.` | — |
| Reading the owner was refused | `Denied` *(new)* | `no access` | `Windows did not allow reading this item's security information. The item has an owner; FileCat may not see it.` | No timer/activation retry; Reread, Attributes job, listing replacement or Analyze (§6.7) |
| No usable ownership here | `Unsupported` | `—` | `The file system did not provide a usable owner ID.` / `Not read for a detected cloud placeholder.` / verified filesystem-specific explanation (§6.5) | As `no access`; uncached eligibility gates are checked on each demand |
| OS-reported owner whose storage provenance is unknown | `Available`, with a note | the name or ID | `Owner reported by the operating system; persistent storage of ownership was not verified.` | With the value; a clear Linux UID/GID mask never supplies this row |
| Item no longer there | `Absent`, observation `Gone` | empty | An empty cell has no tooltip. Inspect prints `Owner: (none) — No longer there.` | With the value; the row leaves at the next listing refresh |
| Transient failure, including temporary sharing/locking conflict | `Failed`, or `Available` with observation `LastKnown` | `error`, or `<last value> (last known)` | `Could not be read just now (…). FileCat retries while the row is shown.` A retained value adds `Showing the last value read.` | Once per effective interval; a last-known value is not counted as freshly available |
| Column not painted yet | `NotRequested` | empty | Inspect says `(not read yet: bring the row into view and scroll the column into view, or use View → Analyze folder)` | None until painted or explicitly analyzed; the vertically visible focused row has the horizontal exception (§3.6) |
| Not applicable (parent row, drive, share) | — | empty | — | — |
| Location without file-system paths | — | empty | Inspect says `(not available for items in this location)` | — |

Rules behind the table:

- A valid ID whose name cannot be found is ownership data, not a failure. The text never asserts that an account was
  deleted: `LookupAccountSidW` returns the same error for a missing account and for a lookup that timed out
  ([LookupAccountSidW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountsidw) [D]).
- `Denied` never implies that the item has no owner.
- Automatic demand comes from painting, including the vertically visible focused row's horizontal exception (§3.6).
  Explicit Analyze can read an off-profile field. Tooltip, inspection, accessibility and sorting never initiate work.
- Name lifetimes are eligibility times, not timers: the next ownership producer requests an expired or missing name.
- No state uses a warning color. Another account owning a file is not a fault.

### 3.3 Detail text

The detail is cached data formatted for reading. It is built from the cached value and the name cache, never from I/O.

| Field | Detail |
|---|---|
| Owner, Windows, resolved | `<type> · <SID>`. Types from `SID_NAME_USE`: `User account`, `Group`, `Local group` (alias), `Built-in identity` (well-known group), `Computer account`, `Windows reports a deleted account` (only for that reported type; FileCat never concludes it), otherwise no type ([SID_NAME_USE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-sid_name_use) [D]). |
| Owner or Group, Unix, resolved | `User · UID 1001` / `Group · GID 20` |
| Owner or Group, Unix, unresolved | `This system has no user name for UID 1001.` / `… no group name for GID 20.` |
| Permissions | `Octal 755 · mode bits only; an ACL or the mount can allow or deny more.` (`UnixPermissions.Octal`, `UnixPermissions.cs:42`, which gives four digits only when a set-ID or sticky bit is set). Shown by Inspect, not on hover (§3.5). |
| Any field, `Available` with a note | The field's detail, then `MetadataValue.Detail`: for example a failed re-read (typed `LastKnown`, §6.2 M6) or unverified storage provenance. |
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
| Automount point | Linux adds `AT_NO_AUTOMOUNT` for the terminal component; parent traversal can still have effects ([statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) [D]). That Linux guarantee is not macOS evidence. macOS autofs behavior remains a qualified GM1 observation, not an unconditional no-mount claim. |
| A name Windows would rewrite (`name.`, `name `, `NUL`) | Read as listed. The Windows reader always opens the extended path form, so a name ending in a dot or a space, or a device name, is not silently replaced by another object (§6.5). |
| Folder | Its own owner. Nothing inside it is read. |
| Parent row `..` | No value. `Applies` admits only `File` and, for folder fields, `Directory` (`MetadataService.cs:126-127`). |
| Drives, servers, shares, Registry keys | No value: these are other providers' rows without file-system paths [S] E9. |
| Archive file in a folder listing (`EntryFlags.Container`) | It is a file: its own owner. The flag marks a file that can be entered, not a directory. |
| Detected placeholder (`EntryFlags.Offline`) | `—` with the reason; no producer runs (§6.8). Unflagged macOS dataless entries require the ownership-specific guard of §6.3. |

Final-component no-follow prevents following that link's target for ownership. It does not prevent effects of
parent traversal or redirectors, including implicit authentication, auditing or network traffic. FileCat requests
no credential UI, content read, write, elevation or privilege enablement for ownership.

File systems without ownership:

- **Windows, FAT and exFAT.** `FILE_PERSISTENT_ACLS` documents ACL preservation/enforcement, not a universal
  ownership-provenance test ([GetVolumeInformationByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew) [D]).
  `Everyone` is also a valid SID. Suppress a synthesized owner only for an independently identified FAT/exFAT case
  validated by GW3. Otherwise retain a successfully read SID with storage provenance unverified (§6.5), or show
  the actual read failure. No unverified filesystem-specific absence claim ships merely because the flag is clear.
- **Linux and macOS mounted filesystems.** Valid OS-reported IDs describe the mounted view; they need not identify
  the server's account database or prove persistent per-file ownership. On Linux each UID/GID result-mask bit must
  be set. A cleared bit returns Unsupported, never a guessed zero or mount-supplied value; compatibility values
  are not unconditionally guaranteed ([statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) [D]). GL1 records
  actual mount behavior without weakening this fallback. macOS uses the validated lstat result under §6.3's guard.

### 3.5 Defaults, width and discovery

- **Defaults.** No built-in profile changes (D1, D12). A custom profile is never rewritten. Profile shortcut numbers
  do not move.
- **Width.** The chooser's default for Owner on Windows is 160 px. `BUILTIN\Administrators` and `NT AUTHORITY\SYSTEM`
  are expected to fit at the default font [G]: OWN-29 measures it and adjusts the number if they do not. Longer names
  clip and get the tooltip. Linux and macOS keep 110 in the chooser and 90 in the built-in Full profile. The Name
  column keeps its fill share; horizontal scrolling, border dragging, large fonts and high DPI behave as they do for
  every metadata column.
- **Long SIDs.** An account SID of a computer or domain (`S-1-5-21-`, the domain's three numbers, the account's
  number) is shown in the cell as `S-1-5-21-…-1013`. Clipped at the end, the full form would show only what every
  account of that domain has in common, and all unnamed owners would look alike. The full SID is in the detail, the
  tooltip and Inspect; sorting uses the full ID. Every other ID (`S-1-5-18`, `S-1-22-1-1000`, a UID) is shown in full.
  Two accounts of different domains with the same last number look the same in the cell and differ in the detail.
- **Tooltips.** The accepted rule stays: a cell that shows a plain value in full has no tooltip (FCUX-061). A metadata
  cell gets one when it is clipped (as today, now followed by the detail), or when what it shows needs explaining: a
  state text (`no access`, `—`, `error`), a last-known value or one with a provenance note, or an ID
  shown because no name is known. A resolved name shown in full, a permissions string shown in full and `…` stay
  quiet. The pointer still reaches every detail through Inspect focused row in the context menu.
- **Discovery.** The established flow only: header menu → Set up columns… → Settings → Columns → Add. The add box
  shows one line of description for the selected column (§6.9). Analyze folder lists Owner on Windows. Command search
  finds Settings by "columns" and "owner", and Analyze folder by "owner" and "ownership".
- **Explanatory copy touched** (nothing else is reworded). One description per field serves both the Analyze chooser
  and the add box:

| Place | Now | Becomes |
|---|---|---|
| Owner description, Windows (`MainViewModel.Workspaces.cs:333`) | The user each item belongs to | The account each item belongs to (a user, group, or service; not who created or last changed it) |
| Owner description, Linux and macOS (same line) | The user each item belongs to | The user account each item belongs to (not who created or last changed it) |
| Permissions description (`MainViewModel.Workspaces.cs:332`) | Who may read, write, and run each item | The mode bits: what owner, group, and others may read, write, and run. ACLs, where present, are not shown |
| Group, Streams or Attributes, and the other fields | — | Unchanged. Now also shown under the add box, where "Extended attributes each item carries, counted" tells the Unix Attributes column apart from the Attr flags. The six fixed columns have no description and the note is hidden for them |
| Analyze folder command (`src/FileCat.Core/Commands/CommandRegistry.cs:502`) | …(versions, picture sizes, link targets, where files came from)… | …(versions, picture sizes, link targets, owners, where files came from)… |
| Analyze chooser hint (`MainViewModel.Workspaces.cs:318`) | …· reads every item ·… | …· reads the whole folder ·… |
| Header-click banner (`TabViewModel.cs:1156`) | …Choose View → Analyze folder to compute every value. | …Choose View → Analyze folder to read the whole folder. |

The last two change because Analyze no longer promises "every" (G4): it does not read placeholders, and some items
have no value to compute.

### 3.6 Accessibility

- **Sort.** The Sort button's menu already lists Owner when the profile has it [S] E19. A metadata sort on a column
  the profile does not show is described by the field's title instead of "Metadata" (G15).
- **Reading a value.** Inspect focused row (Ctrl+Shift+I, or the context menu) is the keyboard and screen-reader route.
  It opens the existing read-only "Full values of focused item" box, and gains the detail lines of §3.3. For a
  metadata column whose cell was not painted or rendered empty, it applies the shared eligibility/cache policy of
  M11 using retained memory-only row/path context. It never starts work. A field that does not apply adds no line,
  where today it prints "(not yet displayed)".
- **The vertically visible focused row.** A column scrolled out of view horizontally is not asked for (E10), so a screen-reader user
  whose Owner column starts off-view would find nothing cached. Painting therefore also asks for the focused row's
  metadata columns that are horizontally out of view: one row through normal admission, including eligible expensive
  fields already in the profile. A vertically offscreen focused row is not requested. Inspect explains `(not read yet:
  bring the row into view and scroll the column into view, or use View → Analyze folder)`; Right arrow alone does
  not bring a vertically offscreen row into view.
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
| 1 | **Local Windows** files and folders (NTFS; x64 and ARM64) | No Owner | Optional Owner, files and folders; ReFS remains qualified until GW6 | Extended-path `CreateFileW` with `READ_CONTROL`, all sharing, `BACKUP_SEMANTICS` and `OPEN_REPARSE_POINT`; owner-only `GetSecurityInfo`; `LookupAccountSidW(NULL, …)` | Access refusal: `no access`; temporary use conflict: bounded retry. FAT/exFAT suppression only for a validated GW3 case; otherwise OS-reported SID with unverified provenance. Architecture/link claims require GW1/GW2 | — |
| 2 | **Windows UNC paths and mapped SMB drives** | No Owner | Same optional field through the filesystem provider; reads queue per server (`WindowsFileSystemProvider.cs:13–23`) | Same native calls through normal Windows redirector handling; implicit session establishment/authentication may occur. FileCat invokes no credential UI. Names use this computer’s lookup, which may contact trusted domains | Eligibility interval 30 s. Transient failures keep visibly last-known values or show `error`; genuine denial requires an explicit retry. Server-local/Samba identities may remain raw IDs; no universal resolution promise. SMB volume query may be unavailable. GW4/GW5 qualify interoperability | N1: server-directed lookup |
| 3 | **Local macOS** | Owner, Group, Permissions | Owner/Group gain IDs, detailed states and separate names; Permissions gains detail and retains existing failure semantics | `lstat` (`lstat$INODE64` on x64); Owner/Group use the scoped no-materialization guard (§6.3); `getpwuid_r`/`getgrgid_r` | Supported Apple silicon ABI/native owner behavior requires GM1. Policy failure or materialization-required result is conservative Unsupported; no general autofs/no-download promise for other fields | — |
| 4 | **Ubuntu / Linux** | Owner, Group, Permissions | Owner/Group gain IDs and honest states; Permissions gains detail and terminal no-automount | `statx` with `AT_SYMLINK_NOFOLLOW` and `AT_NO_AUTOMOUNT`; accept UID/GID only when its result-mask bit is set. Names use configurable NSS on separate workers | Missing symbol/syscall or unavailable identity field: Unsupported. Clear mask never becomes root or a provenance claim. NSS may block even local names. GL1 validates supported native reads; mount/NSS diversity is qualified coverage | — |
| 5 | **Mounted network and removable file systems** | As 3 and 4; on Windows as 1 | What the mounted file system reports, stated as such | Unix: the mount's UID and GID numbers, named by this system's name service, exactly as `ls -l` does. Windows: a removable NTFS drive from another computer carries that computer's SIDs, which stay IDs | Unix: the numbers mean what the server or the mount options say, not necessarily this system's accounts. All local mounts share the device key `/` (`src/FileCat.Core/FileSystem/PathUtil.cs:159-165`), so a stalled mount delays other local folders' metadata until the scheduler adds workers. Existing behavior, unchanged | Per-mount device keys on Unix |
| 6 | **SFTP** | No metadata columns: items have no file-system path. The listing contract has no owner, group or mode (`src/FileCat.Remote/Sftp/Channel.cs:8-28`, `:31`); the SSH.NET adapter reads name, type, size and time (`SshNet.cs:379-391`) | None. Cells stay empty; Inspect says the value is not available in this location | — | — | P1: numeric UID, GID and mode from the attributes the listing already receives. Scoped to the server. Never named through this computer's accounts, never a shell command per row |
| 7 | **FTP, FTPS** | As 6: the same provider and contract (`src/FileCat.Remote/Ftp/FluentFtp.cs:149`, `:549-561`) | None | — | Ownership exists only as server-formatted text in raw listings. Unknown is acceptable | At most the server's own text, with P1 |
| 8 | **ZIP and other archives** | No metadata columns: members have no path (`src/FileCat.Core/Archives/ZipProvider.cs:370`; `src/FileCat.Archives/ArchiveProvider.cs:223`). Listing tags carry sizes, encryption, kind and link target, no owner (`ZipProvider.cs:12-18`; `ArchiveProvider.cs:10-26`; `src/FileCat.Archives/ArchiveFormats.cs:31`, `:377-418`) | None | — | Never the archive file's owner, never a temporary extracted file's | A member-owner column showing what the archive records. The baseline's write engines already parse and preserve it for rewrites: tar mode, UID, GID and names (`src/FileCat.Archives/Writing/StrictTarCatalog.cs:11`, `:85`, `:99-102`), the ZIP UID/GID extra field (`StrictZipCatalog.cs:334`), the RAR5 owner record (`StrictRar5Framing.cs:9`). Recorded numbers and the names stored in the archive only; a recorded UID is never looked up locally. Members added on Windows are written with neutral 0/0 and no names by design (`TarRewriteEngine.cs:111`) |
| 9 | **Search results, flat and mixed result sets, working sets** | Fixed layout without metadata columns (`Columns.cs:139–147`), but generic Analyze is reachable; some item refs expose filesystem paths (`ResultSetProvider.cs:269–272`) | No new layout; newly enabled Windows Owner Analyze is filtered and execution-guarded to filesystem panels (§6.9). Existing unrelated and Unix result analysis is unchanged | — | Generic Analyze currently uses the listing’s device (`TabViewModel.cs:1205`); the fixed layout does not prevent it | Per-item scheduling and a layout decision before extending Windows Owner here |
| 10 | **Registry, recovery, Recycle Bin, change journal, hidden data** | Dedicated layouts with their own meaning (`Columns.cs:246`) | Unchanged. File ownership is not pushed into them | — | Registry key owners stay in the existing key-ACL inspection | — |
| 10a | **Phones and cameras (MTP)** | Ordinary profiles, items without file-system paths: metadata columns are empty, as in 6 to 8 | None. Cells stay empty; Inspect says the value is not available in this location | — | The device protocol offers no owner | — |
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
| 2 | **Linux and macOS Owner, Group, Permissions** | Existing | Existing reads | **Retain.** First delivery fixes G10 for Owner/Group and G12–G14, and adds detail. Permissions retains its nullable failure semantics. No new column |
| 3 | **Mode (octal)**, Linux and macOS (C1) | Compare or sort permissions as numbers; spot `4755` and `1777`. `755` | The same `stat` as Permissions; `UnixPermissions.Octal` exists (`UnixPermissions.cs:42`) | **Next increment.** A companion column, not a format switch: a per-column format would need a new settings field, a companion needs none. Until then the octal form is in the Permissions detail |
| 4 | **Allocated size** (C2) | Find sparse, compressed and cloned files; see what a file takes on this volume. `4,0 kB` beside a 2 GB size | One metadata read a visible row; the inspectors already read it on all three systems | **Next increment**, with its own native gates: each system measures something different (§5.3) |
| 5 | **Packed size and ratio in archives** (C3) | See what compresses, find stored members. `1,2 MB`, `37 %` | Already in the listing tags; no I/O | **Next increment.** Needs a listing-data column route because members have no path (§5.3) |
| 6 | **Links** (hard-link count) | Find files with more than one name before deleting or deduplicating. `3` | The same native call as Allocated size on every system | **Next increment, inside C2.** Files only: a folder's link count follows each file system's own convention and is not a count of names |
| 7 | Git status as text | Sort or group changed files. `changed` | The per-folder snapshot exists (`src/FileCat.App/Services/GitStatusReader.cs`); labels exist (`FileListControl.cs:2206-2214`) | **Defer.** Icon, tooltip and spoken label already show it. If wanted, it rides C3's route; never a Git run per row |
| 8 | Status changed, Last accessed | Forensics. `12.11.2026 09:14:07` | Same read as C2 | **Defer.** The record inspector shows all times exactly. Status-change time is not creation; access time is not a history of anyone opening the file and is often not updated at all |
| 9 | Raw SID, UID, GID columns | Compare IDs across mounts; sort by RID | In the value already | **Defer.** The detail and Inspect show them. A column needs a demonstrated repeated task. An ID means something within its authority (a domain, a computer, a mount), not by its display name alone |
| 9a | Link kind (symbolic link, junction, mount point) | Tell link kinds apart | In the inspector | **Defer.** Link target, the `L` flag in Attr and the link overlay already mark links; a kind column is worth adding only where a real ambiguity is shown, without redesigning links |
| 10 | ACL indicators | "Does anything here have unusual access?" | A DACL read on Windows; an ACL query on Unix | **Defer by default.** "Has an ACL" is true of every NTFS file. A useful Windows form would be "inheritance turned off" or "has explicit entries", defined precisely; on Linux the presence of the `system.posix_acl_access` attribute specifically, not of extended attributes in general. Unknown must stay unknown |
| 11 | macOS tags, BSD and inode flags, cloud state | Triage by tag or flag | Tag decoder exists (`src/FileCat.Core/HiddenData/HiddenDataDecoder.cs:214`, `:247-263`); flags are in the inspector; cloud state is an icon overlay from attributes | **Defer.** Attr already shows `O`. The offline attribute is not proof of a cloud provider's sync state |
| 12 | Type / Kind | Read a type instead of an extension. `PNG image` | Extension/association label, cacheable by extension without content reads; [AssocQueryStringW](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-assocquerystringw) and `ASSOCSTR_FRIENDLYDOCNAME` provide a Windows route | **Defer by product priority.** Ext and icons cover the common task. An association label is not content-verified type; per-row content sniffing remains outside scope |
| 13 | File ID, inode | Identify entries naming the same object | In the inspector; equality needs filesystem/device scope plus file identity | **Defer** as specialist diagnostics. Link count says how many names exist, not which rows identify the same object |
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
| macOS | `getattrlist`: either `ATTR_FILE_ALLOCSIZE`, the bytes on disk of all the file's forks, or `ATTR_FILE_DATAALLOCSIZE`, the data fork alone. C2 must choose so that the three systems mean the same [G] | [getattrlist(2)](https://keith.github.io/xcode-man-pages/getattrlist.2.html). Foundation describes its own key as "the total allocated size on-disk for the file" ([fileAllocatedSizeKey](https://developer.apple.com/documentation/foundation/urlresourcekey/fileallocatedsizekey)); Apple documents no mapping between that key and either attribute | `UnixFileRecords.cs:604-712`, which asks for the all-forks attribute |

Not `GetCompressedFileSizeW`: it takes a path, and for a symbolic link "returns the file size of the target"
([GetCompressedFileSizeW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) [D]).
What the column is not: bytes freed by deleting the file (hard links, clones, snapshots and shared blocks say
otherwise); a sum over named streams on Windows; a compressed size where the system reports whole clusters. Whether
the macOS figure includes the resource fork follows from the attribute C2 chooses. Each of those is a native check
in C2, not an assumption here. Links comes from the same read
(`NumberOfLinks`, `stx_nlink`, `ATTR_FILE_LINKCOUNT`) and never from `FileSystemItemInfo.LinkCount`, whose default of
1 is not a measurement (`src/FileCat.Core/FileSystem/FileSystemOperations.cs:46`, `:420`).

**C3 — Packed size and ratio.** Archive panels only. Packed: the member's compressed size from its tag
(`ZipMemberTag.CompressedSize`, `ArchiveMemberTag.CompressedSize`). Ratio: packed ÷ size as a percentage, smaller is
better; empty for an empty member; `—` when the packed size is unknown, which is every tar member
(`ArchiveFormats.cs:417` passes −1) and members of a solid block [G]. No per-member "allocation" is invented from
shared compressed blocks. It needs a column whose text and sort key come from the entry's tag with no I/O. The Folder
column of result sets is the precedent (`Columns.cs:46`; its key at `TabViewModel.cs:1164`).

Each follow-up is validated for its own remaining risk when it is built, not with this feature's checklist. C1: mode
fixtures (644, 755, 4755, 1777) and numeric order; nothing native beyond what Permissions already has. C2: native
fixtures per system for a sparse file, a compressed file, a clone or reflink, a file with a second stream or fork, and
a hard-linked file, each compared with what the system's own tools report. C3: tags of a ZIP, a 7z, a solid archive
and a tar, and proof that an archive panel triggers no file read for the column.

### 5.4 One finding about an existing column

Created is filled from .NET's `CreationTimeUtc` (`LocalFileSystemProvider.cs:202`). The independent review checked
`Directory.Build.targets:17–38`: SDK 10.0.101 is selected, but self-contained/runtime packs are pinned to **10.0.14**.
That runtime's [native status conversion](https://github.com/dotnet/runtime/blob/v10.0.14/src/native/libs/System.Native/pal_io.c)
does not supply birth time on the supported Linux path; its
[managed creation-time selection](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/IO/FileStatus.Unix.cs)
uses the earlier of mtime and ctime when birth time is absent. This is source-verified, not a native test result.
macOS uses the native birth-time result where supplied, without establishing provenance for every mount.
Framework-dependent installations can run a different serviced runtime.

The existing Created column can therefore present an inferred timestamp as creation time. FileCat's inspector
already reads Linux birth time (`UnixFileRecords.cs:57`, `:69`). Any correction remains separate work; no second
creation column or creation-time implementation is part of Owner.

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
   read from the name cache when the cell is formatted. Reads follow row demand/refresh; duplicate owner lookups
   coalesce within the resolver's lifetime and capacity rules. A name
   that arrives later repaints without touching any file.
2. **Reading and naming never share a thread.** The file read runs on the file's device queue, where it belongs. Name
   lookups run on the resolver's own small set of threads, so a slow domain controller or LDAP server cannot hold a
   device queue or a thread-pool thread.
3. **A producer can say why.** A producer may return a ready `MetadataValue`, so "refused", "not supported" and
   "gone" reach the cell and the detail instead of collapsing into null.

### 6.2 Metadata service (`src/FileCat.Core/Metadata/MetadataService.cs`)

| # | Change | Why |
|---|---|---|
| M1 | Append `Denied` to `MetadataState`. | Preserve existing enum values; distinguish genuine access refusal. |
| M2 | Add optional trailing `MetadataField.Detail` as proposed. Add a trailing typed observation on `MetadataValue`, default `Current`, with `LastKnown` (Available after failed refresh) and `Gone` (Absent after disappearance). | Cached presentation and counts distinguish these outcomes without parsing reason text; existing constructions retain their meaning. |
| M3 | `Produce` passes an already structured `MetadataValue` through; null stays ordinary Absent, anything else Available. NotRequested remains a canceled/retired, non-publishable result. | G10; no generic state hierarchy. |
| M4 | `Compute`, Get and gated Peek reject `EntryFlags.Offline` before production/cache use for every field. | G9; skip detected placeholders in explicit as well as automatic work. This does not detect all macOS dataless entries (§6.3/6.8). |
| M5 | Compute reuses cached Available/Absent only if the field is nonrefreshable or its recorded time is within the effective interval. Missing timestamps on a refreshable field are not fresh. Denied/Unsupported/Failed are explicitly retried by Analyze. Store the time for every completed refreshable production, including Compute, subject to M6. Return the effective stored value, not the attempted value that Store may have replaced with a LastKnown result. Retired/canceled publication returns NotRequested. | G8; paint and analysis agree on freshness and outcome. Cached LastKnown within its interval remains typed LastKnown, never a fresh success. |
| M6 | Only a genuine refresh of a refreshable field may preserve old Available after Failed: retain its value, set LastKnown and the latest failure reason, and update the timestamp. A successful read clears LastKnown. Denied, Unsupported and Absent replace old data; cached Denied/Unsupported have their timestamp removed to disable automatic retries. Explicit retry/invalidation remains possible. | Bounded transient recovery, truthful last-known state, and no repeated access-refusal audit on each paint. Do not change nonrefreshable verification preservation semantics. |
| M7 | Derive `effectiveRefresh` once from the field and location (six times RefreshAfter when slow). Carry it through Get, Schedule's locked admission recheck (`:156–159`) and Compute reuse; add the missing context to those call paths. | A network refresh is 30 s at every admission point, not 30 s outside and 5 s inside the lock. |
| M8 | `ExpireRefreshable()` marks recorded refreshable production times due and raises the existing throttled repaint signal. It changes no values and schedules no work. | Activation causes an actual repaint; only demanded rows then read. Shared expiry intentionally makes other tabs' values due for their next demand. |
| M9 | Expose the throttled ValuesChanged signal for resolver completions. Add a small synchronous field-publication/invalidation revision mechanism for retained snapshots: effective value changes and Forget/Invalidate retire relevant snapshot claims before a later sort can use them (§6.10). | Names repaint without native reads; ownership changes cannot be hidden indefinitely by a retained array. Name-only changes do not invalidate ownership values. |
| M10 | Reasons become sentences; cache/time/retention limits and clock have internal test controls. | Deterministic finite boundary and retry tests. |
| M11 | Share eligibility and memory-only cache selection between Get and a gated Peek used by inspection and sort snapshot capture. Applicability and Offline gates take precedence over cached values. An expensive field on a slow location may display/use an already cached explicit result; on a miss the gate returns Unsupported and Get schedules nothing. | Plain Peek does not encode uncached gates. The slow-cost rule prohibits automatic production, not reading the result of explicit Analyze. |

Keep the key, default 50,000-value cache, backlog guard, basic demand test and scheduler admission policy. Existing plain Peek
remains available to its callers; the new consumers use the gated path. Extend targeted Forget/Invalidate publication
bookkeeping for snapshot retirement without changing which unrelated fields they invalidate. `PublishFreshVerification`
keeps its deliberate nonrefreshable timestamp behavior. `MaxOutstandingPerDevice` keeps its name; ADR-10 is corrected.

For snapshot notification, a publication carries field/key/revision and an optional analysis-operation origin.
Callbacks only mark retained state retired, under bounded synchronization; they do not sort, read providers or wait
on the UI. An analysis ignores its own publications because it retains their returned effective outcomes, but a
conflicting external publication/invalidation retires the affected analysis. The final revision/current-operation
check and snapshot registration must be atomic with this bookkeeping, closing a last-result/publication race.
This is narrowly scoped lifetime tracking for §6.10, not a new metadata event framework.

### 6.3 Identity value and ownership source (Core, *new*)

New file `src/FileCat.Core/FileSystem/Ownership.cs`:

| Type *(new)* | Content | Note |
|---|---|---|
| `PrincipalKind` | `WindowsSid`, `UnixUser`, `UnixGroup` | The namespace an ID is meaningful in. |
| `Principal` | `Kind`, `Id` (the SID string, or decimal UID/GID), `Authority` (empty in this delivery: this computer's mounted/account view) | Immutable value equality; compare kind, full numeric ID (SID authority/subauthorities numerically), then authority. Deduplication is bounded and snapshot-scoped, with all references charged to §6.6's budget; no permanent global interner. |
| `NameStatus` | `NotRequested`, `Pending`, `Resolved`, `NotFound`, `Unavailable` | Pending means actually queued/running; a cache miss alone is NotRequested. |
| `PrincipalName` | `Status`, `Name`, `Type`, `Reason` | What the name cache answers. |
| `OwnerRead` | `Status` (`Ok`, `Denied`, `Unsupported`, `Gone`, `Failed`), `Principal`, `Reason` | What one read returns. Ok may carry a qualified provenance note; a clear Linux identity mask is never Ok. |
| `IOwnership` | `IsSupported`; `OwnerRead ReadOwner(string path)`; `OwnerRead ReadGroup(string path)`; `PrincipalName LookUp(Principal principal)` | The platform boundary. |

Why this interface is enough. The column needs three operations and no more: read the owner of one entry, read its
group where the platform has one, and name one principal. ACLs, effective access and the inspector's report are not
needed and are not reachable through it. Reading and naming are separate members because they must run on different
threads and fail differently. `Authority` exists so that N1 can key a server's answers apart from this computer's
without changing a stored value; this delivery always passes the empty authority. Nothing here is a general metadata
or plug-in framework: fields remain `MetadataField`s.

Implementations and composition:

- `UnixOwnership` *(new, Core)* uses the same natives as `UnixPermissions`, through two additions there.
  - **A stat that reports why it failed.** It needs its own imports: the existing ones do not capture the error
    number (`UnixPermissions.cs:248-255` have no `SetLastError`). `ENOENT`/`ENOTDIR` → `Gone`; `EACCES`/`EPERM` →
    `Denied`; missing symbol/syscall (`ENOSYS`) or a documented unsupported-operation outcome → `Unsupported`;
    other errors → `Failed`. Use each OS's errno definitions. Linux passes `AT_SYMLINK_NOFOLLOW | AT_NO_AUTOMOUNT`
    for the terminal component and validates `STATX_UID`/`STATX_GID` independently. A clear bit returns Unsupported
    with `The file system did not provide a usable owner/group ID.` Never consume its zero/nonzero buffer value or
    infer mount provenance; a set-bit zero is valid ([statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) [D]).
  - **macOS non-materializing ownership read.** On the same worker thread, capture
    `getiopolicy_np(IOPOL_TYPE_VFS_MATERIALIZE_DATALESS_FILES, IOPOL_SCOPE_THREAD)`, set the policy to
    `IOPOL_MATERIALIZE_DATALESS_FILES_OFF`, perform lstat, and restore the prior policy in `finally` before any
    other job uses the thread. If policy capture/set fails, do not perform the ownership read; report Unsupported.
    A materialization-required `EDEADLK` is Unsupported with an explanation. If restoration fails, do not let that
    worker execute another job with altered policy: restoring/retiring the worker is a narrow scheduler integration
    allowance, with a failure recorded. GM1 must validate the wrapper and restoration paths before this route ships.
    [Apple TN3150](https://developer.apple.com/documentation/technotes/tn3150-getting-ready-for-data-less-files)
    documents why a metadata read can materialize intermediate folders. This guard is for Owner/Group; it does not
    assert complete dataless detection for every metadata field or universal macOS autofs behavior.
  - **An uncached lookup that tells found, not found and failed apart.** `getpwuid_r` and `getgrgid_r` return 0 with
    a null result when there is no entry ([getpwuid_r(3)](https://man7.org/linux/man-pages/man3/getpwuid_r.3.html)
    [D]): that, and only that, is `NotFound`. `ERANGE` grows the buffer as the existing code does
    (`UnixPermissions.cs:229-239`); `EINTR` is retried; **every other nonzero return is `Unavailable`**. The error
    list in the manual is a set of examples, and a stopped name-service source can return numbers that look like "no
    entry". GL1 records what a missing ID and a stopped source really return.
  - **One change to an existing member:** `LinuxStat` (`UnixPermissions.cs:123-131`) gains `AT_NO_AUTOMOUNT` too. It
    feeds the Permissions column of the default Unix Details profile, which has the same automount effect today. No
    other behavior of it changes; the cited equivalence with stat/lstat is Linux-specific [D]. Permissions retains
    its existing nullable-stat failure semantics and gains its octal detail; Owner/Group use the new detailed reader.

  Otherwise `UnixPermissions.Stat`, `UserName`, `GroupName` and `NameCache` stay exactly as they are for their other
  callers (the record inspector, trash, the permissions dialog), with their bounds and their tests. This is the smallest
  compatible way to improve freshness: giving the existing cache an expiry would change those callers and the two
  tests that pin its bounds, so expiry lives in the resolver the columns use (§6.4) and nowhere else.
- `NoOwnership` *(new, Core)*: `IsSupported` false; every read is `Unsupported`.
- `WindowsOwnership` *(new, `src/FileCat.Platform.Windows/WindowsOwnership.cs`)*: §6.5.
- `IPlatform` gains `IOwnership Ownership { get; }` beside `HiddenData` (`src/FileCat.Core/Platform/IPlatform.cs:19`).
  `PortablePlatform` supplies `UnixOwnership` on Linux and macOS and `NoOwnership` otherwise; `WindowsPlatform`'s
  constructor sets `WindowsOwnership` (`src/FileCat.Platform.Windows/WindowsPlatform.cs:10-23`).
- Keep static `BuiltInFields.All` as the chooser/descriptor catalogue; its ownership descriptors do not own a resolver
  or dynamically dereference mutable globals. An ownership-field factory captures an explicit source and resolver.
  `MetadataService` registers those instance fields in place of the catalogue's Owner/Group entries.
- `AppServices` creates the source/resolver, passes them into metadata construction and owns their disposal. Core-only
  MetadataService construction uses UnixOwnership plus a service-owned resolver on Unix, NoOwnership elsewhere;
  the service becomes disposable for that owned fallback. App/test disposal paths release the appropriate owner,
  detach events and let blocked native calls retire under §6.4. A borrowed resolver is never disposed twice.
  Tests use explicit fake instances/factory registrations and dispose owned services, never swap process-wide sources.
- `BuiltInFields.All` on Windows becomes `[Version, Dimensions, LinkTarget, Zone, Hidden, Verified, Owner]`: appended,
  so the chooser's existing order is undisturbed. Group and Permissions stay off Windows.

The Owner field keeps its ID, title, cost (`Cheap`), `Folders: true` and `RefreshAfter` of 5 s. Its producer maps
`OwnerRead` to `MetadataValue` (`Ok` → `Available` with the principal, after `Names.Request`; `Denied` → `Denied`;
`Unsupported` → `Unsupported`; `Gone` → `Absent` with observation `Gone`; `Failed` → `Failed`; each with its reason, and an `Ok` note carried
in `MetadataValue.Detail`). Its formatter returns the cached name, or the ID in the form of §3.5. Its `Detail` builds §3.3. Its sort key is §6.6.

### 6.4 Name resolver (`PrincipalNames`, Core, *new*)

One resolver per owning services instance, over its captured ownership source; Core-only ownership is defined in §6.3.

| Property | Rule |
|---|---|
| Reads | Peek is memory-only under a short lock. It returns the cached answer; Pending only when a request is queued/running; otherwise NotRequested. Formatting, snapshot capture, tooltip and Inspect never initiate lookup. |
| Requests | Only producers call Request. Fresh/queued/running requests coalesce. Expired entries keep the old answer while one request queues. At 256 queued requests, drop the new request; the next legitimate producer demand can retry. Dropped/evicted work is not described as in progress. |
| Workers | Dedicated background threads: two normally, at most four per resolver when calls exceed 3 s. A process-shared admission bound permits at most four native calls across live and retiring resolvers. Acquire a slot before a native call; count running/blocked/retiring calls until actual return. Invalidation/disposal never releases a slot early; idle or canceled waiting workers exit. No thread-pool/device-queue lookup. |
| Slow or failing service | Errors are Unavailable for 30 s. A slow NotFound may conservatively use the same retry interval; elapsed time is a heuristic, not proof of a timeout. No cross-prefix circuit or known-fast-prefix set. LookupAccountSid may route to matching trusted domains; Unix NSS order/actions are configurable. Four occupied native slots can delay all new names, including local ones; raw IDs and the UI remain usable. |
| Lifetimes | Resolved 10 minutes, NotFound 2 minutes, Unavailable 30 seconds. These are eligibility times: only the next ownership producer retries, including after a dropped request. A cached-only read or sort does not refresh names. |
| Memory | At most 4,096 cached entries and 262,144 retained UTF-16 characters across IDs, names, types and reasons; LRU eviction. Names over 1,024 characters are not retained; keep a bounded Unavailable/retry record instead. Include queue/in-flight principal state in accounting; no unbounded interner or session-prefix set. Bound native lookup buffers before allocation (Unix's existing 1 MiB ceiling; Windows sizes checked against a fixed bounded limit). |
| Invalidation | Clear queued/cache state and advance generation; older native completions cannot publish. Running calls keep their slots until return. Reread invokes it; valid shared names need not be discarded merely because one tab navigated. |
| Disposal | Reject new requests, retire queued work and publications, detach events, and return without an unbounded native join. Retiring threads retain their own source references until return and cannot notify disposed services. Service recreation must not accumulate a fresh quota beside still-blocked retired calls: a process-wide four-call admission counter contains only slot accounting, not mutable source/name state. |
| Notification | Changed is throttled into Metadata.ValuesChanged; a disposed service receives none. |
| Waiting | Global WhenIdle completes when no request is queued/running, at timeout, or cancellation. Analyze waits at most 10 s; unrelated tabs may consume that wait. Counts come from the captured principals' actual states, not from global idleness. |
| Logging | Counts and error codes only; never an account name or ID. |

[LookupAccountSidW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountsidw) and
[nsswitch.conf](https://man7.org/linux/man-pages/man5/nsswitch.conf.5.html) define routing, not a local-name latency
guarantee. GW5/GL1 record native environments when available; OWN-14 proves bounded fallback without requiring an
uncached local name while all slots are blocked. Canonical equality/coalescing includes kind and authority.

### 6.5 Windows reader (`WindowsOwnership`, *new*)

`ReadOwner(path)`:

1. `CreateFileW(<extended path>, READ_CONTROL, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, null, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, null)`.
   The path is **always** passed in its extended form, `\\?\C:\…` or `\\?\UNC\server\share\…`, made from the listed
   path as it is, with no normalizing call. Preserve an already extended DOS/UNC path unchanged; prefix an absolute
   drive path once; convert an ordinary absolute UNC prefix to `\\?\UNC\`. Reject relative and unsupported device
   forms rather than double-prefix or reinterpret them. Do not trim trailing dots/spaces. The existing helper
   `WindowsFileOperations.Long` is not enough: it adds
   the prefix only from 240 characters up (`src/FileCat.Platform.Windows/WindowsFileOperations.Native.cs:113-117`).
   Without the prefix Win32 rewrites the name first: a listed `name.` or `name ` opens `name`, and `NUL` opens the
   null device ([Naming Files, Paths, and Namespaces](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)
   [D]). The row would then show another object's owner, or report an existing item as gone, exactly where look-alike
   names are planted. `READ_CONTROL` is required: "To read the owner, group, or DACL … the calling process must have been granted
   READ_CONTROL access when the handle was opened"
   ([GetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getsecurityinfo) [D], [P3]).
   `FILE_FLAG_BACKUP_SEMANTICS` is required to open a folder, and "appropriate security checks still apply when this
   flag is used without SE_BACKUP_NAME and SE_RESTORE_NAME privileges"
   ([CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) [D]); FileCat
   enables neither. FileCat requests no content access or timestamp modification; OS/redirector side effects remain
   possible (§3.4).
2. `GetSecurityInfo(handle, SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION, out owner, null, null, null, out descriptor)`.
   The owner pointer points into the returned descriptor, which is freed with `LocalFree` in a `finally` [D]. The SID
   is copied out (`SecurityIdentifier`, already used in this assembly) before the descriptor is freed. The handle is a
   `SafeFileHandle` in a `using`.
3. Mapping. Unexpected exceptions are not caught.

| Native result | Outcome |
|---|---|
| Open fails with 2 or 3 | `Gone` |
| Open fails with 5; query fails with 5 | `Denied`, "did not allow reading this item's security information" |
| Open fails with 32 or 33 [P4] | `Failed`, "temporarily in use by the system or another program"; retry at the effective interval, preserving a visibly LastKnown value when present |
| Query fails with 1 or 50 | `Unsupported` [G]: a guess for redirectors without security descriptors; no gate has seen it. Harmless if wrong: anything else falls to `Failed` |
| A null owner pointer (a descriptor may have no owner SID, as the documentation of the path-based variant states [D]) | `Unsupported`, "the item's security descriptor names no owner" |
| A successfully read SID may be synthesized | Query volume information on the same handle when provenance needs qualification. Only an independently identified FAT/exFAT case validated by GW3 may map to Unsupported with filesystem-specific copy. Neither `S-1-1-0` nor a clear `FILE_PERSISTENT_ACLS` bit alone establishes absence of ownership. For other/unavailable capability results, keep the SID as OS-reported with storage provenance unverified; SMB may not support the volume query |
| Any other error | `Failed` with the system's message |
| Otherwise | `Ok` with `Principal(WindowsSid, <SID string>)` |

`LookUp(principal)`: `LookupAccountSidW(null, sid, …)` with 512-character buffers and one retry on
`ERROR_INSUFFICIENT_BUFFER`, rejecting requested buffer sizes beyond the resolver's fixed limit before allocation.
Success → `Resolved`, name `DOMAIN\name` or the bare name when the domain is empty [P5],
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

FileCat invokes no elevation, privilege enablement, write, account enumeration or credential UI for the column.
Opening a network pathname can establish/authenticate an OS session; it is not guaranteed to reuse an existing one
([SMB2 open procedure](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/61c68667-0b8c-4300-ac8a-246a86f2b11d)).

### 6.6 Sorting

**Contract.** Parent first; then existing container/directory partitioning when enabled. Within each partition,
known keys precede unknown keys in both directions. Ascending known Owner keys are resolved names, case-insensitive
in the captured current culture, then unnamed full numeric IDs. Descending reverses known-key order, not the
unknown-last rule. Equal names tie by canonical principal, then existing file-name and store-index ties. LastKnown
values may participate, but cells and analysis counts identify them as such. Abbreviated SID text is never a key.

**One immutable ordering snapshot, at every size.**

- Capture eligible metadata values using gated Peek (M11), outside comparisons. Copy the resolver's bounded name
  map under its lock once per snapshot; Owner/Group keys use that copy, not a later live formatter/SortKey lookup.
  Retained principals absent from that copy have unnamed-ID keys. Capture
  culture/comparison policy too. No comparator reads a live cache, requests an item reference, or starts work.
- Scope the snapshot to store instance, field, view/sort generation and captured count. `EntryComparer` receives
  fixed keys for the entire operation; the same snapshot survives every external run and merge. Rows not admitted
  by the resource bounds below have a fixed unknown key. There is **no live-key fallback above a threshold**.
- For streaming appends, retain the existing snapshot and give newly appended rows fixed unknown metadata keys
  until an explicit resort/new snapshot. They still use normal filename/index ties. This keeps previously sorted
  rows valid for `StableSort.Merge` without re-sorting on every batch or accepting live keys. A user sort, completed
  Analyze, or new listing starts a new snapshot; ordinary metadata/name arrivals do not reorder the list.
- Filtering retains the same ordering snapshot. Direction reversal starts a new bounded key snapshot over valid
  retained analysis values or current cache data, so it can use newly resolved names. A canceled/replaced sort
  never publishes into another generation; it keeps its memory reservation until its users actually finish.
- Make the narrow snapshot-lifetime changes in `ListingModel` (`:1356–1371`) and the external-sort call boundary
  (`ExternalViewBuilder.cs:17–40`) needed to pass/hold the same snapshot. `StableSort`, row/spill formats, and the
  ordinary sorting algorithms stay unchanged. `MetadataKey` becomes a snapshot-capture input, not a live comparer.

**Resource bounds shared with retained analysis.** Reuse `AppServices.ListingIndexes` / `IndexMemoryBudget`, whose
process-shared default is 512 MiB; do not raise it implicitly. Existing pipeline/index reservations (`40*n` in
`ListingModel.cs:1304`) remain charged. Reserve bytes before allocating retained values, key tables, name snapshots,
principal deduplication and their strings/overhead. Account conservatively for referenced payloads, including detail
strings and non-string values; if a value's size cannot be bounded, do not retain it for the snapshot.

Retain the existing additional caps of 1,000,000 rows and 16,000,000 UTF-16 characters, as test-shrinkable ceilings,
not allocation targets. Charge strings per retained reference (even if shared) for a simple conservative character
bound. Use a growable/chunked table keyed by store index, allocating only admitted entries, never an array sized to
a larger unbounded listing. Admit the first eligible rows in store-index order until any cap or reservation fails;
all remaining rows are unknown for that snapshot. No cache fallback may fill holes after capture. Report actual
coverage and whether row, character or byte capacity limited it. If no capacity is available, metadata keys are all
unknown and existing ties still produce a deterministic partial order.

Retirement on cancellation, navigation, field change or disposal detaches the snapshot; reservation release waits
until the last sort/analysis owner relinquishes its buffers. This includes overlapping canceled operations and
multiple tabs. Snapshot-scoped principal deduplication ends with its snapshot; there is no unbounded interner.
The Sort button uses the field title when it is absent from the profile (G15).

### 6.7 Freshness

Ownership can change without size/mtime changing. The existing cache key is retained, so explicit retirement is
necessary; a new EntryStore alone is not invalidation.

| Event | What happens |
|---|---|
| Eligible demanded row older than 5 s (30 s on recognized Windows network locations) | Get and Schedule use the same effective interval; old value stays during refresh. Failure makes it visibly LastKnown; success replaces it. No timer polls idle panels. |
| Row not demanded | No automatic read; explicit Analyze can still read it. |
| Window/tab activation | ExpireRefreshable marks recorded times due and raises ValuesChanged so visible controls repaint. This intentionally expires shared cache entries for other tabs too, but hidden rows are not scheduled. Denied/Unsupported without timestamps remain suppressed. |
| Reread (Ctrl+R) | Existing metadata invalidation plus resolver invalidation. Retained analysis/sort claims are retired; current visible demand asks again. |
| Attributes job finishes | Existing metadata invalidation retires retained claims too. |
| Analyze | Compute uses the same location-adjusted interval. Denied/Unsupported/Failed are explicitly retried; cached current/LastKnown values within the interval retain their truthful outcome. |
| Filesystem listing load/replacement, including same-location watcher refresh | Before the new listing captures keys or demands ownership, retire Owner/Group values and running productions for the affected folder through existing ForgetFolder or an equivalent generation hook. Wire the retirement at the store/pipeline transition, not only after sorting. Same path/size/mtime may describe a new object. Also retire that store's retained analysis and completion banner. |
| Owner changes after analysis | The synchronous publication/invalidation tracking of M9 retires the affected retained snapshot and withdraws completeness; it does not reorder the display automatically. The next sort captures current eligible values (§6.10). |
| Account renamed; name missing/expired | Resolved answers become eligible after 10 minutes (other states per §6.4). Next legitimate ownership production requests the name; it normally re-reads the owner too. Reread invalidates both. Cached inspection/sorting alone initiates neither. |
| Transient network/busy failure | LastKnown with a note if a value existed, otherwise error; retry once per effective interval while demanded. |
| Genuine Denied or cached Unsupported | No timer/activation retry. Reread, Attributes invalidation, listing replacement and Analyze can retry. Uncached applicability/Offline gates are evaluated anew on each demand. |

Use the existing activation sites (`MainWindow.axaml.cs:209–216`, `TabViewModel.cs:284`) plus an explicit repaint
signal, not incidental Git/free-space activity. Extend only the narrow listing transition hook needed to retire
ownership before replacement use. Do not add ACL-change events to `ChangeMonitor` or build an ownership monitor.

Navigation/close cancel analysis and prevent old row results from publication. Recheck captured store and operation
identity after native/awaited work and before publishing retained data, status, banner or sort. A shared principal's
late name may serve other current rows; it is suppressed only by resolver generation/disposal, not by tab navigation.

### 6.8 Demand, scheduling and placeholders

| Situation | Required behavior |
|---|---|
| Owner absent from profile and no explicit Owner analysis | No new owner read/name request through scrolling, hover, sorting or accessibility. Already-started work may finish under its lifetime rules. |
| Owner in profile | Paint demand for visible rows, plus horizontally hidden fields on the vertically visible focused row, goes through normal per-device Background admission and the existing 256-request backlog guard. |
| Scrolling/navigation | Queued work whose store/viewport is obsolete is dropped. Running native calls may finish but retired ownership publications are refused. Navigation retains scheduler priority. |
| Sorting, hover, Inspect, screen reader | Memory only. Snapshot capture/inspection use shared eligibility (M11); comparisons use immutable keys only. |
| `EntryFlags.Offline` | All metadata producers are skipped by Get and Compute, even if an older cached value exists. Windows `MapFlags` already includes Offline and both recall bits (`0x440000`, `LocalFileSystemProvider.cs:210–218`). |
| macOS dataless entry/parent | .NET's Unix attributes do not map SF_DATALESS to Offline. Owner/Group therefore require §6.3's non-materializing policy; M4 alone does not promise that all existing metadata fields skip iCloud content. Broader detection is deferred. |
| Recognized Windows network location | Owner remains Cheap; Get/Schedule/Compute use a 30 s effective interval. Expensive fields are not automatically produced there; an already cached explicit result may be displayed/sorted. |
| Other mounted network/removable location | IsSlowLocation is not a proof of locality and does not detect every Unix mount. Existing device policy remains; do not claim a universal fast/local read. |

FileCat requests no content access or credential UI for Owner. Normal OS path resolution, implicit authentication,
name service traffic, auditing and parent traversal remain possible. Linux AT_NO_AUTOMOUNT protects only the final
component; macOS materialization and autofs claims use their own evidence (§3.4, GM1).

### 6.9 App integration

| Where | Change |
|---|---|
| `TabViewModel` | Cell presentation adds Denied and a visible `(last known)` suffix; Detail includes the typed outcome and cached reason, and needs-explaining is true for states, LastKnown/provenance notes and unnamed IDs. Keep GetMetadataText’s signature. Inspect uses M11 with retained row/path context and never schedules. Windows Owner Analyze validates filesystem-panel scope before side effects. Own retained snapshot subscriptions/publication checks and release them on lifecycle retirement (§6.10). |
| `FileListControl` | RenderedCell gains detail/needs-explaining plus bounded row/path inspection context captured during painting. The vertically visible focused row requests horizontally hidden profile metadata through normal admission; offscreen focus requests nothing. UpdateTip uses cached details for clipped/explained values; Verified keeps its tip. FocusedInspectionText uses rendered/context data or reports the not-read hint without calling a provider. Retain context only for the current focused/visible rows and store, and clear it on replacement. The box stays a snapshot; DescribeItem and automation structure stay unchanged. |
| `Columns.cs` | The Owner choice's default width is 160 on Windows. `ColumnChoice` (`:217-218`) gains an optional description. No profile, key or `Create`/`Parse` rule changes. |
| `ColumnProfilesEditor.cs` | One note under the add box (`:29`, `:101`) showing the selected choice's description, also set as the box's help text; hidden when the choice has none. |
| `MainViewModel.Workspaces.cs` | AnalyzeFolderAsync stops the blanket Windows owner-ID exclusion, but offers Windows Owner only when Location.IsFileSystem; apply that check before SortByMetadata and again in AnalyzeAsync. Other platform/provider analysis remains unchanged. Share AnalysisDescription with the add box and update copy (§3.5). |
| `CommandRegistry.cs` | The Analyze description at `:502` and its keywords at `:621` gain owners; the Settings keywords at `:555` gain "columns" and "owner". |
| `AppServices.cs` | Create source/resolver instances and pass them to metadata registration; no mutable BuiltInFields ownership globals. Wire Changed after Metadata exists, expose names for Reread, dispose subscription/metadata/resolver owners in order without native joins. Reuse ListingIndexes for metadata snapshot reservations. |
| `MainWindow.axaml.cs`, `MainViewModel.Commands.cs`, `MainViewModel.LocationContext.cs` | The activation and Reread hooks of §6.7. |

### 6.10 Analyze folder

Keep one cancellable background pass over the current listing; never read descendants. Newly enabled Windows Owner
analysis requires a filesystem-panel location, checked both before chooser/sort changes and in AnalyzeAsync. Existing
unrelated result analysis remains unchanged. Capture the operation and store identity, and use `EntryStore.Lease()`
for the pass. Queue it through the existing `DeviceIoScheduler.RunOwned` (`:80`) so cancellation of a started call
does not release its lease/buffers before the delegate returns; queued cancellation can release immediately.
Same-location replacement cancels just as navigation/close does, without blocking the UI on a native return.

1. **Rows.** Skip parent/synthetic rows. Consider files, and directories when `field.Folders`, with filesystem paths.
   Do not use IsContainer as the test: archive container files remain files. Apply the field's name applicability
   and shared eligibility; an ordinary inapplicable/no-value result is Absent, not Gone.
2. **Outcomes.** Use Compute's effective result (M5/M6). Every completed considered row has exactly one category:
   current Available; Available/LastKnown; ordinary Absent; Absent/Gone; Denied; detected placeholder; other
   Unsupported; Failed. Pending/NotRequested is not a completed read. A canceled/retired result stops the pass with
   no success claim; progress reports completed considered rows separately from total considered rows.
3. **Retention and keys.** Retain the deterministic subset admitted by §6.6's shared byte/row/character budget,
   indexed by store index and tied to this field/store/operation. Processing may continue to count unretained rows,
   but their sort keys stay fixed unknown. Never fall back to the live cache for unretained rows. After the read pass
   and optional name wait below, capture the bounded name map and immutable sort keys. A reservation failure can reduce
   retained coverage and must update the actual retained count before publishing. Reversal can recapture names over
   still-valid retained values.
4. **Names.** A principal field waits for global resolver idle for at most 10 s: `Analyzing Owner: looking up names…
   (Esc sorts now)`. Esc here skips only the wait; navigation, store replacement, close or operation supersession
   still cancels everything. Global activity from other tabs may consume the wait. Snapshot name states are counted
   per distinct retained principal: Resolved, NotFound, Unavailable, Pending and not cached/evicted. These may coexist;
   report every nonzero nonresolved category. ID fallback does not imply ownership read failure.
5. **Freshness/lifetime.** Track relevant effective publications/invalidation synchronously from the start of the
   pass (M9), with an origin marker for its own Compute calls. A conflicting external change retires the pass/snapshot
   rather than let a previously captured owner override fresh data; report `Analysis stopped because metadata changed;
   the order remains partial.` Own returned results do not invalidate their analysis. At final publication atomically
   verify operation, store, captured row count and revision, so a last-read race or streaming append cannot announce
   whole-listing success for a changed listing. Publish the completion banner only with the accepted sort publication;
   a changed count stops this pass as partial without reading newly appended rows. After completion,
   a relevant publication/invalidation drops the retained values and replaces the complete banner with the ordinary
   partial-order note, without automatic reorder. An active sort keeps its immutable keys and reservation until it
   ends; obsolete generations cannot publish. Name-only changes repaint but do not retire ownership retention.
6. **Other retirement.** Store replacement, another analysis, changing sort field, Reread, Attributes invalidation
   and tab close retire retained values and their complete claim. Memory remains charged while old work owns it.
   A streaming append after completion clears the whole-listing complete claim; valid retained values may remain,
   with appended rows using fixed unknown keys under §6.6.
7. **Completion copy.** Distinguish current values, last-known data, and retained sort coverage. With m completed
   considered rows, n current Available and s LastKnown: `Read <Title> for m items: n current values, s last-known
   values.` Omit the last-known clause when zero. List nonzero missing/denied/placeholder/unsupported/failed/gone
   counts in a second sentence. LastKnown sorts as known but is never counted in n. Then report `The order retains
   r of m items' results.` If limited, add `<row/character/shared-memory> limit reached; unretained items have unknown
   keys and sort last within their folder/file partition.` Known unavailable outcomes also have unknown keys.
   Never claim first 1,000,000 when a different bound stopped retention. For principals add the actual nonresolved
   name categories; do not infer counts from the cache's maximum size. If the field is off-profile, append the existing
   Settings → Columns hint. A snapshot's completed observations are not a claim that the live filesystem is frozen.

| Outcome | Example banner |
|---|---|
| All current, fully retained | Read Owner for 12,480 items: 12,480 current values. The order retains all 12,480 items' results. |
| Missing names | Read Owner for 12,480 items: 12,480 current values. The order retains all 12,480 items' results. 3 owners have no name here; 2 name lookups are unavailable; 1 is pending. These owners are ordered by ID until you sort again. |
| Read failures with last-known data | Read Owner for 12,480 items: 12,473 current values, 1 last-known value. 4 with no access; 2 placeholders not read. The order retains all 12,480 items' results; unavailable values sort last within their partition. |
| Capacity limit | Read Owner for 1,210 items: 1,210 current values. The order retains 860 of 1,210 items' results. Character limit reached; unretained items have unknown keys and sort last within their partition. |
| Canceled | Analysis canceled after 5,120 of 12,480 considered items; the order remains partial. No retained success snapshot is published. |

Preserve progress, Esc and focus/marks. Header-click/Analyze chooser copy says “read the whole folder,” not “compute
every value.” An unchanged size/mtime or a reason string is never used as proof of current object identity or Gone.

### 6.11 Compatibility

| Area | Statement |
|---|---|
| Settings | No schema change (`AppSettings.CurrentSchema` stays 1). Keys, widths, order, the ten-profile cap and the Name rule are untouched. |
| Existing Windows profiles | No migration. Imported/raw Unix settings may already contain meta:owner; the new Windows parser recognizes it. Previously parsed Windows profiles omitted it. |
| Profiles carried between systems | meta:owner is now recognized on all three. Unsupported platform keys remain omitted from parsed profiles as today; rewriting profiles may remove them from saved settings. No broader portability migration. |
| Three cases that stay distinct | A known field unavailable on this OS and an invalid unknown key are both omitted from the parsed profile. An available field with unavailable row/provider data stays in the profile and reports the appropriate state. Parsing alone is not necessarily a rewrite of Settings.ColumnProfiles. |
| Running an older FileCat afterwards | Older Windows hides unknown meta:owner; a profile edit/resize can reserialize profiles without it. Ordinary SaveSettings need not remove the raw key (AppServices.cs:149–154, :439–447). Older Unix already supports Owner. State this accurately in release copy. |
| Built-in profiles and shortcuts | Unchanged (D1, D12). |
| Unix behavior | Owner/Group retain names/numeric IDs and gain typed identities, detailed failures and separate lookup. Permissions gains octal detail and Linux terminal no-automount; its existing nullable-stat failure semantics otherwise remain. macOS ownership uses the validated guard or conservative Unsupported. |
| Other metadata fields | M4 skips detected Offline rows, M5/M7 unify refresh eligibility, and M6 affects only genuine refreshable-field refreshes. LastKnown presentation is explicit; sorting/Analyze gain shared bounded snapshots and truthful counts. Existing verification-specific rules remain. |

### 6.12 Files expected to change

| File | Responsibility of the change | Package |
|---|---|---|
| `src/FileCat.Core/Metadata/MetadataService.cs` | M1–M11; instance ownership-field factory/registration and fallback disposal; Permissions detail; Windows catalogue entry | O1, O2, O4 |
| `src/FileCat.Core/FileSystem/Ownership.cs` *(new)* | Identity types, `IOwnership`, `UnixOwnership`, `NoOwnership` | O2 |
| `src/FileCat.Core/FileSystem/PrincipalNames.cs` *(new)* | The name resolver | O2 |
| `src/FileCat.Core/FileSystem/UnixPermissions.cs` | Detailed stat/mask validation and uncached name lookup; Linux terminal no-automount. macOS ownership policy wrapper may live beside the new UnixOwnership reader. Existing name caches/callers retain their semantics | O2 |
| `src/FileCat.Core/Platform/IPlatform.cs` | `Ownership` on the interface and `PortablePlatform` | O2 |
| `src/FileCat.Platform.Windows/WindowsOwnership.cs` *(new)* | Owner read and name lookup | O4 |
| `src/FileCat.Platform.Windows/WindowsPlatform.cs` | Supplies `WindowsOwnership` | O4 |
| `src/FileCat.Core/Listing/EntrySorting.cs` | EntryComparer consumes immutable bounded metadata snapshot keys; existing ties/algorithms remain | O5 |
| `src/FileCat.App/Services/AppServices.cs` | Captured source/resolver composition, ownership-aware disposal and shared memory budget integration | O2, O5 |
| `src/FileCat.App/ViewModels/TabViewModel.cs` | Typed cell outcomes/detail, scoped Analyze execution, snapshot capture/counts/publication guards, retention retirement, sort title and activation expiry | O1, O3, O4, O5, O6 |
| `src/FileCat.App/Controls/FileListControl.cs` | Detail in rendered cells, tooltip, Inspect | O3 |
| `src/FileCat.App/Controls/Columns.cs`, `src/FileCat.App/Views/ColumnProfilesEditor.cs` | Width, description, the note | O4 |
| `src/FileCat.App/ViewModels/MainViewModel.Workspaces.cs`, `src/FileCat.Core/Commands/CommandRegistry.cs` | Analyze chooser and copy | O4 |
| `src/FileCat.App/Views/MainWindow.axaml.cs`, `MainViewModel.Commands.cs`, `MainViewModel.LocationContext.cs` | Freshness hooks | O6 |
| `docs/CAPABILITIES.md`, `docs/adr/ADR-10-metadata-scheduling.md` | Corrections and the new capability | O7 |

Additional narrow integration files: `src/FileCat.Core/Listing/ListingModel.cs` (metadata snapshot lifetime across
streaming merges and ownership retirement before replacement use; O5/O6), `ExternalViewBuilder.cs` (hold the same
snapshot through runs/merge; O5), and `IndexMemoryBudget.cs` only if needed for explicit metadata reservations (O5).
`DeviceIoScheduler.cs` may gain only a worker-retirement signal for failure to restore macOS thread policy (O2);
its priorities, backlog and concurrency limits do not change.

Unchanged: `EntryData`, `EntryStore` and spill format; profile persistence/schema; `FileListAutomation.cs`;
`PanelView.axaml.cs`; record inspectors and providers. Use existing `EntryStore.Lease` rather than changing its
lifetime format. No general sorting, metadata, provider or scheduler rewrite is authorized by these corrections.

---

## 7. Ordered work packages

Order: O1 → O2 → O3 → O4 → O5 → O6 → O7. O3 needs only O1. O5's generic parts need only O1; its Owner tests need O2.
Each package ends with the product working and its own tests green. "Checklist" refers to §8.2. Before O1 starts,
take the "before" measurements of PF1 and PF3 (§8.4): they cannot be taken afterwards.

### O1 — Metadata service groundwork

| | |
|---|---|
| **Behavior** | No new column. Shared service work skips detected Offline entries, unifies refresh intervals/cache reuse, preserves typed LastKnown after transient refresh failure, distinguishes Denied/Gone, and provides synchronous snapshot retirement hooks. |
| **For whom, why** | Everyone who uses Analyze or a Unix permission column today. These are the defects G8–G10 that would make Owner untruthful if it were added on top of them. |
| **Source** | MetadataService.cs (M1–M11); TabViewModel.GetMetadataText adds Denied and LastKnown rendering and passes effective slow-location context to Compute. |
| **Data and limits** | No new native call. The refresh factor for slow locations has no visible effect until a refreshable field exists on Windows (O4). |
| **Errors, freshness, cancellation** | As M3–M8. Cancellation is unchanged: a canceled producer still stores nothing (`:214-217`, `:246`). |
| **Done when** | OWN-07, OWN-08, OWN-16 (service halves only), OWN-24 (flagged-entry service half), OWN-25 (interval/admission half), and existing MetadataTests, MetadataDemandTests, MetadataPeekTests, MetadataOutstandingRetirementTests and MetadataOrderRetirementTests pass. Test effective Store/Compute outcome, NotRequested retirement and explicit retries with a fake clock. |
| **Outside** | New Owner/native reader, ordering snapshots and tooltip/Inspect UI. OWN-08 detail presentation is completed by O3. |

### O2 — Identity, name resolver, Unix source

| | |
|---|---|
| **Behavior** | Unix Owner/Group keep canonical IDs, resolve names independently and expose truthful errors; numeric key semantics are unit-tested here, integrated immutable sorting in O5. Permissions gains detail and Linux terminal no-automount, retaining its failure semantics. Windows has no visible Owner yet. |
| **For whom, why** | Linux and macOS users of the existing columns; and the foundation Windows needs. Closes G12–G14 and the Unix half of G10. |
| **Source** | Ownership.cs, PrincipalNames.cs, UnixPermissions.cs, IPlatform.cs, MetadataService.cs, AppServices.cs and owned-service test disposal. A narrow DeviceIoScheduler worker-retirement signal is allowed only for macOS policy-restoration failure (§6.3). |
| **Data and limits** | `statx` / `lstat`; `getpwuid_r` / `getgrgid_r`. Names are this system's view of the mount's numbers (§4, row 5). |
| **Identity, errors** | Accept UID/GID including zero only with valid result masks. Missing symbols/syscalls or unusable identities are Unsupported. macOS guarded reads restore thread policy. Existing Unix NameCache bounds/tests remain unchanged. |
| **Freshness, cancellation** | Resolver lifetimes and invalidation, §6.4. Old-generation lookups cannot publish after `Invalidate`; no lookup publishes after `Dispose`. New-generation requests remain allowed after invalidation. |
| **Done when** | OWN-04/05 fake/source-independent and Unix halves; OWN-06 key/cell half; OWN-12 (Unix), OWN-13/14 service/resolver and visible-column halves, OWN-17 (resolver half), OWN-26/28. Test Core-only disposal, separate service lifetimes, blocked retiring calls and request expiry/drops. GL1/GM1 core shipping checks pass or the new unverified route remains conservatively unavailable under §8.3; optional diversity subcases may remain Not run. Permission_columns_cover_files_and_folders disposes its owned service and asserts the formatted resolved name; old name-cache bound tests pass unchanged. |
| **Outside** | The Windows reader. Tooltip and Inspect. Server-directed lookups. |

### O3 — Cell detail, tooltip, Inspect

| | |
|---|---|
| **Behavior** | Cached details and explicit LastKnown reasons on tooltip/Inspect; vertically visible focus can demand horizontally hidden fields. Offscreen focus uses cached context or explains how to bring the row into view. |
| **For whom, why** | Pointer, keyboard and screen-reader users. Closes G11 and gives Owner its accessible route (§3.6). |
| **Source** | `TabViewModel.cs` (the cell method and its Inspect variant). `FileListControl.cs` (`RenderedCell`, `RenderRow`'s metadata branch and the focused-row request, `UpdateTip`, `FocusedInspectionText`). |
| **Data and limits** | Rendered cells and the gated `Peek` only. No I/O on pointer movement, key press or accessibility query. The focused-row request is one row's metadata columns, through the normal demand path. The existing 8,192-character tip limit and 1 MiB Inspect limit apply. |
| **Errors, freshness** | A shown tip is retired when a value or a name arrives (`FileListControl.cs:253-258`). The Inspect box is a snapshot of the moment it opened. |
| **Done when** | OWN-06 (tooltip/Inspect escaping), OWN-08 (detail presentation), OWN-13 (focused horizontal-demand half), OWN-22/23, ClippedCellInspectionTests and FileListAutomationTests pass. Plain fully shown values keep no tooltip; ownership/name arrivals do not change the spoken summary. Existing verification speech semantics remain. |
| **Outside** | `DescribeItem`, `FileListAutomation.cs`, any per-row node, the Inspect dialog's structure. |

### O4 — Windows reader, registration, chooser, copy

| | |
|---|---|
| **Behavior** | Windows: Owner can be added in Settings → Columns (with its description, 160 px), shows per §3, appears in the Sort menu when in the profile, and is offered by Analyze folder. |
| **For whom, why** | Windows users; the requested feature. Closes G1, G2, G16. |
| **Source** | New `WindowsOwnership.cs`. `WindowsPlatform.cs`. `MetadataService.cs` (instance field registration). `Columns.cs`, `ColumnProfilesEditor.cs`. `MainViewModel.Workspaces.cs` and the `TabViewModel.AnalyzeAsync` location guard. `CommandRegistry.cs`. |
| **Data and limits** | §6.5 exact extended paths and owner-only read. New Windows Owner Analyze is filesystem-panel-only. FAT/exFAT provenance uses GW3 evidence or the conservative OS-reported fallback; busy reads use transient retry. No default-profile change. |
| **Identity, errors, freshness** | §§6.3–6.5: FileCat requests no elevation, privilege enablement, write, enumeration or credential UI; implicit OS authentication can occur. Activation/listing retirement is completed by O6. |
| **Done when** | OWN-01–03, OWN-04/05 Windows native halves, OWN-07/09–11 Windows halves, OWN-12 Windows, OWN-25 network read/retry half and OWN-29. PF1/PF2 meet §8.4 criteria. Required GW1/GW2 and GA1 evidence pass for enabled shipping claims under §8.3; optional GW4/GW5 environments may remain unverified with honest fallback. GW3 is required only to enable categorical FAT/exFAT suppression. ColumnProfileTests built-in-profile expectations remain unchanged. |
| **Outside** | Default profiles. Server-directed lookups. Group or a primary-group column on Windows. Any ACL display. |

### O5 — Truthful sorting and Analyze

| | |
|---|---|
| **Behavior** | All metadata fields: applicable folders analyzed, typed outcomes counted, bounded shared-budget retention with actual coverage, immutable keys across streaming/external merges at all sizes, and no work from comparison. Later publications retire superseded retained claims. |
| **For whom, why** | Anyone sorting by a metadata column. Closes G3–G7 and G15. |
| **Source** | EntrySorting.cs, narrow ListingModel/ExternalViewBuilder snapshot integration, existing IndexMemoryBudget reservations, TabViewModel.MetadataKey/AnalyzeAsync and synchronous snapshot subscriptions using M9. |
| **Data and limits** | Shared byte reservation plus 1,000,000-row/16,000,000-character ceilings; deterministic admitted subset, fixed unknown remainder. Count objects/strings and overlapping owners; release only after final buffer owner retires. No new metadata I/O. |
| **Sorting, cancellation** | §6.6/§6.10: Esc during reads cancels; Esc during the name wait skips only that wait. Navigation, store replacement and close cancel both phases. No canceled pass publishes retained success; buffers stay charged until workers release them. |
| **Done when** | OWN-18–21 and OWN-25 explicit-result/snapshot half, including streaming/external boundary, row/character/byte limits, simultaneous tabs, old-generation completion, LastKnown and count races. PF3 meets time/memory criteria; PF4 passes. SortingTests, EntryStoreTests, SortFocusContinuityTests, AnalysisLeaveTests, AnalysisCurrentDemandTests and AnalyzeFolderChoiceTests pass with truthful copy. |
| **Outside** | StableSort algorithms, row/spill formats, general external-sort rewrite, persisting metadata sort in tab state, and automatic reordering on name/value arrival. Narrow snapshot lifetime integration is in scope. |

### O6 — Freshness wiring

| | |
|---|---|
| **Behavior** | Activation expires eligible shared metadata and explicitly repaints demanded rows; Reread also forgets names. Listing transitions retire same-key old ownership before replacement use, with current store/operation checks on analysis publication. |
| **For whom, why** | The "changed it elsewhere, came back" case, without polling. |
| **Source** | MainWindow.Activated; TabViewModel.OnIsActiveTabChanged/current-store lifecycle; MainViewModel.Commands.cs and LocationContext.cs Reread; narrow ListingModel pre-replacement ownership-retirement hook using existing ForgetFolder. |
| **Data and limits** | Automatic reads require current paint demand; idle panels have no polling timer. Already-started work may finish. Denied/Unsupported are not retried by activation (§6.7). |
| **Errors, cancellation** | Reuse the demand/publication protection, with explicit targeted retirement at listing transitions and current-store guards for analysis. |
| **Done when** | OWN-15/16/17 including same path/size/mtime replacement, blocked old read, same-location watcher refresh during final Analyze, activation without incidental repaint, and no hidden-row demand. Active old sorts stay immutable and charged but cannot publish to the new generation. |
| **Outside** | The folder watcher's filter. Any timer. |

### O7 — Documentation and evidence

| | |
|---|---|
| **Behavior** | Capabilities/ADR-10/release notes describe the actual optional feature, bounded partial snapshots, known-placeholder scope, OS side effects and native evidence limits. Correct Unix Details and profile-downgrade wording (§6.11), pin runtime references and preserve deferred-column scope. |
| **Source** | `docs/CAPABILITIES.md:46`; `docs/adr/ADR-10-metadata-scheduling.md`. Native results go to `docs/validation/UX_UI_IMPLEMENTATION_STATUS.md` in that file's dated, build-specific form. |
| **Done when** | Checklist OWN-27; §8.2 and §8.3 carry a status for every row. `tests/FileCat.NativeAcceptance/check-ux-acceptance-ledger.py` passes if the ledger was touched at all. |
| **Outside** | Reopening accepted ledger rows (§8.5). |

---

## 8. Validation and evidence checklist

### 8.1 Rules

- This section originally planned validation. Executed results are now linked in §8.2/§8.3 and the dated acceptance record. The probe of §2.4 remains a historical observation, not a test result.
- Statuses: **Planned**, **Passed**, **Failed**, **Blocked** (names the missing prerequisite), **Not run**. A native
  environment that is not available leaves its row *Not run*; it never becomes *Passed* by inference.
- Deterministic fakes for refused reads, unresolved and slow names, flipping owners and cancellation. Native fixtures
  only for what a fake cannot prove. Fixtures live in the tests' own temporary folders and are removed; no user file
  and no system permission is changed. No test names a real account or a profile path.
- A row is added only for a new concrete risk. A passed row is not repeated without a relevant code change or a new
  failure.

### 8.2 Feature checklist

One checklist for the feature. "Where" names the existing file to extend, or a new one. "Evidence" is filled in when
a row is run: the test names and the run that executed them, or the dated native record. Rows began **Planned**. The dated acceptance and package records below now supply executed evidence; native qualifications remain separate.

| ID | Expected outcome | Method | Where | Evidence | Status |
|---|---|---|---|---|---|
| OWN-01 | Owner is offered in the column chooser on Windows, Linux and macOS; Group and Permissions are not offered on Windows; the Unix list is unchanged. | Headless | `tests/FileCat.App.Tests/ColumnProfileTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-02 | Owner profile width/order survives restart; built-ins unchanged. Imported Unix meta:owner is recognized on new Windows; old-Windows parse versus profile reserialization has the documented difference. Narrow panels/large fonts keep horizontal access and existing menus. | Headless round trips and geometry | ColumnProfileTests.cs, HorizontalColumnTests.cs, ColumnHeaderMenuTests.cs, CustomColumnProfileMenuTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-03 | Owner sorts from the header and from the Sort menu when shown; Analyze folder offers it on Windows with the new description. | Headless | `tests/FileCat.App.Tests/PanelSortControlTests.cs`, `AnalyzeFolderChoiceTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-04 | Local, domain, service and group owners format as qualified names; a name with an empty domain shows alone. | Fake lookup; native lookup of `S-1-5-18`, `S-1-5-32-544`, `S-1-1-0` and the test's own owner | new `tests/FileCat.Core.Tests/OwnershipFieldTests.cs`; new `tests/FileCat.Platform.Windows.Tests/WindowsOwnershipTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-05 | Raw SID/UID/GID survives failed naming; set-bit zero is valid. Independently clear UID/GID masks with zero/nonzero garbage yield Unsupported and no principal/lookup. Missing symbol/syscall is Unsupported. | Fake native response plus existing unknown-ID case; O2 fake/Unix, O4 Windows | OwnershipFieldTests.cs, UnixPermissionTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-06 | Two owners with the same shown name and different IDs stay apart in order and detail. Long names and names with control or direction characters are escaped in the cell (with O2) and in tooltip and Inspect (with O3). | Fake; headless | `OwnershipFieldTests.cs`, `tests/FileCat.App.Tests/ClippedCellInspectionTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-07 | With no old value: refused, temporarily busy, unsupported, typed Gone and Failed have truthful state/reason and no fabricated owner. Busy maps to transient Failed. Name failure leaves a valid raw owner. | Fake source; Windows native error fixture only where stable and ordinary-user safe | OwnershipFieldTests.cs, WindowsOwnershipTests.cs; service half in MetadataTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-08 | A failed refresh returns/stores the same typed LastKnown value with visible marker and detail; current-success count excludes it. Busy then success recovers once per interval. Genuine Denied/Unsupported suppress automatic retry but explicit Analyze/invalidation/replacement retries. Nonrefreshable Verified semantics remain. | Fake clock/field; service half O1, tooltip/Inspect presentation O3 | MetadataTests.cs, ClippedCellInspectionTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-09 | File/folder/final link and broken link report their own owner. Extended path conversion preserves existing prefixes, absolute drive/UNC, trailing dot/space and reserved-looking names; refuses unsupported relative/device input. Everyone/ACL flags alone never prove no owner. | Native ordinary-user fixtures for promised paths/kinds; deterministic adapter and volume-query true/false/failure cases | WindowsOwnershipTests.cs, UnixPermissionTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-10 | Parent/synthetic, archive and SFTP rows do not acquire local ownership. New Windows Owner Analyze is unavailable/execution-guarded in mixed results, even when invoked directly; filesystem Analyze works. Unrelated existing analysis remains unchanged. | Headless counting fake, mixed filesystem/archive results | OwnerColumnTests.cs, AnalyzeFolderChoiceTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-11 | Reading a folder's owner reads nothing inside the folder. | Counting fake: the paths read are exactly the listed rows | `OwnerColumnTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-12 | Owner absent and no explicit Owner analysis initiates no owner/name work through scroll, hover, Inspect, accessibility or any cached sort. Explicit off-profile Owner Analyze is allowed only in its supported scope. | Counting fake; Unix O2, Windows O4 | OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-13 | Only demanded visible rows, including horizontally hidden fields of vertically visible focus, are automatically read; obsolete queued rows drop. Same-owner requests coalesce; vertically offscreen focus causes no demand. | Counting fake and gated producer | MetadataDemandTests.cs, PrincipalNamesTests.cs, OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-14 | Blocked lookup calls leave raw IDs/UI/device queues usable; at most four native calls including retiring calls, at most 256 queued. All further names may wait, even local ones. Releasing a slot permits queued local/reachable-domain work; one slow prefix never suppresses another. | Gated fake; invalidate/dispose/recreate while calls block; configurable routing fake | PrincipalNamesTests.cs, OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-15 | Same-size/mtime ownership change appears on next due demand, activation with explicit repaint or Reread. Hidden rows do not read. A later effective owner change retires retained analysis/completeness; reversing uses current keys. | Fake clock/flipping source; no incidental Git repaint | OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-16 | Same-path/size/mtime replacement retires cached and running ownership before new rows use it. Repeated Analyze respects effective local/network intervals and explicit Denied retry. Same-location refresh during final read cannot publish old success. | Gated source/field; service timing O1, store lifecycle O6 | MetadataTests.cs, OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-17 | Navigation, same-location replacement, Reread, close or superseding operation suppress old row ownership/status/retained arrays/sort publication. Resolver invalidation/disposal suppresses old generation names, but valid shared names may serve other current tabs. Retiring calls and buffers remain counted until return/release. | Gated fakes; final-read race; separate and Core-only service lifetimes | MetadataDemandTests.cs, PrincipalNamesTests.cs, AnalysisLeaveTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-18 | The order of §6.6: names, then IDs numerically, ties by ID, unknown last in both directions, parent row first, folders first. | Unit | `tests/FileCat.Core.Tests/SortingTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-19 | Immutable keys through streaming append, external runs/merge and publication above/below shrunk limits; appended rows use fixed unknown until a new snapshot. One captured name answer per principal. No comparison I/O/cache read, no work from sort, no focus/mark regression; off-profile sort uses field title. | Counting provider; forced external path; delayed names between batches/runs; headless | SortingTests.cs, SortFocusContinuityTests.cs, existing listing/external-sort tests | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-20 | Analyze includes applicable folders and distinguishes Current, LastKnown, ordinary Absent, Gone, Denied, detected placeholder, Unsupported and Failed. NotRequested/Pending cannot count as completed; cancellation/change reports partial. Mixed nonresolved name states are captured after the optional wait. Completion accompanies accepted sort publication; a streaming append cannot leave a whole-listing complete claim. | Headless fakes including final-Compute invalidation, append and Esc-wait versus navigation | AnalysisLeaveTests.cs, AnalysisCurrentDemandTests.cs, OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-21 | With cache smaller than listing, admitted analysis values survive eviction. Shrink row, character and shared-byte caps independently: actual coverage/limiting resource is shown and unretained keys stay unknown despite cached values. Two tabs/overlapping canceled sorts remain budgeted; release waits for last owner. Later B/Denied/Gone/LastKnown publication retires old A retention and complete banner. | Small configurable limits, 100 rows; held old buffer owner and store/field replacement | OwnerColumnTests.cs, focused listing budget/snapshot tests | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-22 | Tooltip/Inspect expose full sanitized identity and truthful state/LastKnown reasons without work. Plain full values remain tooltip-free. Vertically visible focus can paint hidden columns; offscreen focus uses retained context or actionable not-read text. No field-inapplicable line; typed Gone reason is shown. No new provider item reference from inspection. | Headless counters and rendered/focused context cases; existing limits unchanged | ClippedCellInspectionTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-23 | Ownership/name completions do not change the focused spoken row summary or raise its property-change event; no per-row nodes. Existing verification-driven summary behavior remains unchanged. | Focused ownership-event count | FileListAutomationTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-24 | Detected Offline/recall rows invoke no producer in Get/Compute; same-key flag transition suppresses old cached sort/display value. macOS ownership policy set/read/restore and failures are covered separately; no assertion that Unix attributes detect all dataless files. | Flag fake O1; macOS wrapper fake O2 and native GM1 gate | MetadataTests.cs, OwnershipFieldTests.cs, UnixPermissionTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-25 | Slow-location Get, Schedule recheck and Compute use 30 s consistently. Dropped network: visibly LastKnown or error, one retry per interval. Expensive explicit cached results display/sort consistently, without automatic expensive production. | Fake clock/slowLocation, later native GW4 coverage | MetadataTests.cs, OwnerColumnTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-26 | Unix Owner/Group named/raw values retain meaning and reject unavailable mask fields. Permissions formatting, nullable failure semantics and old name-cache tests stay unchanged apart from the stated Linux flag/detail. macOS guard is used or conservative Unsupported is explicit. | Existing and targeted native/fake checks | UnixPermissionTests.cs, OwnershipFieldTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-27 | `docs/CAPABILITIES.md` and ADR-10 say what the source does. | Review | — | [O7 source/document review, release copy and ledger check](../validation/column-ownership/O7-20261116.md) | Passed |
| OWN-28 | Expiry is producer-demand-based (10 min/2 min/30 s); old answer may remain during retry. Dropped/evicted requests are not Pending; later valid production retries. Bound entries, all retained characters, buffers and auxiliary state, including oversize answers and Core-only/disposed services. | Fake clock/lookup, small limits and disposal | PrincipalNamesTests.cs | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |
| OWN-29 | The add box shows the selected field's description and hides the note for a fixed column; the Analyze chooser shows the same text. Owner's default width on Windows is 160 px, and `BUILTIN\Administrators` and `NT AUTHORITY\SYSTEM` are not clipped at the default font (if they are, the default is raised). A long account SID shows in its short form, with the full SID in the detail. | Headless measurement | `ColumnProfileTests.cs`, `AnalyzeFolderChoiceTests.cs`, `OwnerColumnTests.cs` | [Dated finite acceptance](../validation/column-ownership/ACCEPTANCE-20261116.md) and O1–O6 package runs | Passed for enabled routes; native qualifications below |

### 8.3 Native gates

These are implementation-time checks, not results obtained by revising this plan. A mandatory check must pass
before enabling its shipping claim; otherwise withhold that route or use the conservative behavior specified below.
“Not run” records missing evidence, never permission to advertise an unverified guarantee. Run normal-token owner
reads as an ordinary user; elevated setup, if necessary, is separate and recorded. Keep fixtures disposable and owned.

| ID | Claim and finite check | Shipping treatment | Status |
|---|---|---|---|
| GW1 | Windows x64/ARM64: owner-only reads for files/folders, junctions, directory and file symlinks, exact extended drive/UNC forms and unusual listed names. Record ordinary-user token and link-fixture creation capability. | Required for enabled architecture/item-kind claims. A skipped symlink fixture leaves that subclaim unverified; do not silently count it Passed. | **Passed for Windows x64 local fixtures**, ordinary token, 51/51 WindowsOwnershipTests; [O4 evidence](../validation/column-ownership/O4-20261116.md). ARM64 stays Unsupported; UNC conversion has adapter coverage, interoperability is unverified under GW4. |
| GW2 | A final link/junction with a missing target still reports its own owner. | Required for the categorical broken-link claim; otherwise withhold that item-kind claim with a conservative Unsupported result. | **Passed Windows x64** final file/directory links and junctions, [O4](../validation/column-ownership/O4-20261116.md); Linux x64 final link, [O2](../validation/column-ownership/O2-20261116.md). |
| GW3 | Identified FAT32/exFAT: record actual SID, filesystem identity and ACL flags on owned fixtures. | Required only to enable filesystem-specific synthesized-owner suppression/copy. Without it retain an OS-reported SID with provenance unverified (or actual failure); no categorical FAT/exFAT absence claim. | **Not run** |
| GW4 | Representative UNC/mapped SMB against Windows/Samba: extended paths, read errors, dropped connection, retry, OS authentication/UI observation and volume-query availability. Optional redirector without descriptors if at hand. Trace round trips only as environment evidence. | Interoperability coverage may remain unverified with stated limits and safe ID/failure fallback. Never advertise universal no-authentication, exact round-trip count, or guaranteed naming. | **Not run** |
| GW5 | Unreachable domain controller: native duration/error and bounded resolver behavior; IDs/UI usable even if local names must wait. | Domain-lab coverage optional when unavailable; deterministic OWN-14 is mandatory. No cross-prefix circuit or local-name latency claim. | **Not run** |
| GW6 | ReFS native owner read. | Optional environment; qualify ReFS until verified. | **Not run** |
| GL1 | Ubuntu ordinary-user core owner/group/link read, valid masks/zero and missing-ID lookup; terminal no-automount. Optional ext4/btrfs/vfat/NFS/CIFS and stopped/slow NSS samples record actual routing/results. | Core supported Linux route must pass before release; unavailable identities use conservative Unsupported. Mount/NSS diversity may remain Not run without fabricated values or latency claims. | **Passed Ubuntu x64 WSL2 ordinary-user core**, native stat comparison and missing UID; [O2](../validation/column-ownership/O2-20261116.md). Masks/zero additionally have deterministic coverage. Mount/NSS diversity unverified; Linux ARM64 Unsupported. |
| GM1 | Apple silicon: APFS lstat owner/group/link ABI, missing-name result, scoped materialization policy including dataless final/intermediate entries, restoration after success/error and restoration-failure handling. SMB/autofs samples if available. | Core macOS ownership and non-materializing wrapper require evidence; until validated the new guarded route is conservatively unavailable. SMB/autofs diversity remains qualified; no universal no-mount/all-field no-download claim. | **Not run**; new guarded macOS Owner/Group route stays Unsupported. Policy/restoration/worker-retirement fakes passed; no native dataless claim. Existing Permissions unchanged. |
| GA1 | Windows/NVDA: the existing Inspect dialog reads Owner identity/detail and an unavailable/LastKnown reason. | Required new Windows feature readout check. Keep accepted Orca/dialog evidence closed; one focused check, not an accessibility re-audit. | **Passed**, coordinated disposable Windows guest, ordinary token, signed NVDA 2026.3.1; full identity/SID and detected-placeholder reason spoken. [Dated evidence and cleanup](../validation/column-ownership/GA1-20261116.md). |

### 8.4 Performance and capacity checks

Before O1, record PF1/PF3 baselines on the same machine/build settings with fixed fixtures, warm-up and five timed
runs; preserve median, spread, allocations and peak/retained memory. Before measuring the changed implementation,
record a justified regression allowance and comparison method from those measurements and existing responsiveness
requirements. Do not choose a pass band after seeing the result. The five-run spread describes noise, not by itself
acceptable feature cost; no universal numerical threshold is invented here.

| ID | Check | Pass decision |
|---|---|---|
| PF1 | Feature off: paint/scroll the existing 1,000,000-row synthetic TV-01 fixture, using RenderTimed; compare same workload before/after. | Meet the recorded feature-off regression allowance; diagnose any excess. |
| PF2 | Owner shown with a fixed fake source and a few principals: frame time and per-frame allocations; measure incremental cost relative to PF1 and steady-state versus first demand separately. | Meet an explicit evidence-based Owner-on allowance set before the after measurement. Merely recording a band is not a pass. |
| PF3 | Metadata sort of 100,000/1,000,000 rows: elapsed time, allocation, peak/retained memory, plus two tabs and overlapping canceled sorts. | Meet recorded timing allowance and shared reservation limits; expected extra retained memory is measured/charged, not required to be zero. Retired buffers stay charged until final release; no memory-growth escape through interning. |
| PF4 | Capacity via OWN-21 with cache at 8 and 100 rows, separately shrunk row/character/byte caps. | Actual coverage, fixed unknown tail, coherent counts and correct reservation lifetime all pass. No giant native fixture needed. |
| PF5 | Reference only: local NTFS throughput compared with author-reported P6. | Not a release threshold or an ordinary-user baseline; report environment/privilege differences. |

Use one bounded before/after set. Repeat only to diagnose a concrete failure or after a relevant change, not to
search for a favorable run. Performance criteria apply in O4 (PF1/PF2) and O5 (PF3/PF4), and remain required in DoD.

**Recorded outcome:** PF1/PF3 **Passed** in the predeclared processor-controlled ABBA set, preserving every earlier
miss and all four raw runs; the final twenty-sample actual analysis also passed elapsed/reservation limits
([O5](../validation/column-ownership/O5-20261116.md)). PF2 **Passed** its predeclared Owner-on band
([O4](../validation/column-ownership/O4-20261116.md)). PF4 **Passed** the independently shrunk-cap, competing-tab
and final-owner lifetime fixtures (O5/O6 records). PF5 was not rerun and remains a historical reference only.

### 8.5 The UX ledger and repeat rules

`docs/validation/UX_UI_ACCEPTANCE_LEDGER.md` and `UX_UI_SESSION_WORKFLOW.md` stay the source of truth for required
UX work. This feature adds no finding to them and reopens none.

- AC-S3-061 (clipped-cell tooltip and Inspect focused item). O3 adds content to both, and it changes when a tooltip
  appears in exactly one way: a metadata cell that shows something other than a plain value gets one even when it is
  not clipped (§3.5). The accepted criterion, that a cell showing a plain value in full has no tooltip, is kept and
  is asserted by OWN-22. The Inspect dialog's structure and focus do not change.
- AC-S2-026 (columns reachable at narrow widths; sort keeps focus) and AC-S1-010 (list accessibility) are touched by
  content only. The deterministic tests named in §8.2 guard their invariants.
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
§2.4 reports those observations on one elevated Windows machine; its raw material was unavailable, so it is not
independent ordinary-user/native acceptance evidence. The independent review’s source/API corrections are incorporated;
implementation gate outcomes are recorded in the dated acceptance record and §8.

### 9.2 Source-derived risks (reasoned from code, not measured)

| Risk | Consequence | Handling |
|---|---|---|
| The cache evicts an analysis’s values (G5) | An order claimed complete can lose earlier data | Bounded immutable retained snapshot with actual coverage and fixed unknown remainder (§6.6/6.10); OWN-21 |
| A slow or dead name service | Any new name, including a local one, can wait | Raw IDs/UI remain usable; process-wide four native-call slots include retiring calls, per-ID retry and bounded queue; OWN-14/28, GW5/GL1 |
| OS operations that leave this computer | Name service traffic and implicit network-session authentication can occur | Only legitimate visible/explicit demand initiates ownership work; no FileCat credential UI or server-directed lookup. Document effects, without a no-network/no-sign-in promise |
| A `READ_CONTROL` open on a file another program holds with an opportunistic lock | That program may be asked to release the lock | Same as Explorer's owner column; reads are per painted row; noted, not mitigated |
| A server that audits file access | Owner reads and refused opens can add audit entries | Automatic genuine Denied retry is suppressed; explicit retry and listing retirement may read again. Busy/transient failures retry at the effective interval. Auditing is OS/server policy, not controlled by this feature |
| Resolver/service retirement while native calls block | Disposed work can outlive the UI and a new service instance | Calls keep their slots and source references until return; generation/disposal rejects publication; no cross-prefix circuit or unbounded known-fast state |
| `GetVolumeInformationByHandleW` over SMB | Storage provenance may be unknown | Retain a successfully read SID as OS-reported with qualification; no ACL-bit/SID-only absence claim. GW3/GW4 qualify specific cases |
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
| Should explicit Analyze skip detected Offline entries for every field? | Yes (D7/M4); macOS Owner/Group additionally require the guarded read | Limiting M4 would re-enable content-reading fields on detected placeholders. Complete cross-platform cloud detection is separate work, not a small policy flip |
| A built-in "Security" or "Audit" profile? | No (D12). A custom profile does it in a few steps today | A sixth default profile, appended so no shortcut moves |
| Rename the Unix "Attributes" column (extended-attribute count), which reads like Attr? | No; the add-column note explains it | A title change visible in existing profiles |
| Start- or middle-elision for long qualified names, so the account part stays visible? | No; widen the column or read the tooltip | New truncation in the cell text layout |

### 9.5 Exclusions

Taking ownership, permission editing, elevation/privilege enablement, account enumeration, server-directed name
lookup, owner columns for remote/archive members or dedicated layouts, new built-in profiles, and every extra
column in §5 remain excluded. Ownership requests no content read. Complete cloud detection for all pre-existing
metadata fields and the Created correction are separate work.

No general sort-engine, scheduler, provider or metadata rewrite. The narrow snapshot/store-transition hooks and
macOS policy-failure retirement allowance in §6.12 are in scope. EntryData/EntryStore/spill format, watcher filters,
profile schema and accessibility-node structure remain unchanged. Existing unrelated Analyze behavior is preserved;
new Windows Owner analysis is explicitly filesystem-panel-only.

### 9.6 Existing issues noticed and deliberately left

| Issue | Where |
|---|---|
| A metadata sort is not restored with a tab: only the sort field is saved, not which metadata field. | `TabViewModel.cs` `ToState` / `ApplyState` |
| After Analyze on a column the profile does not show, the list is sorted by something invisible. This plan only adds the hint of §6.10. | `MainViewModel.Workspaces.cs:305-324` |
| `IsSlowLocation` does not recognize network or removable mounts on Linux and macOS, so expensive fields run there unasked. | `TabViewModel.cs:1113` |
| Two different "slow location" tests exist. | `TabViewModel.cs:1113`; `MainViewModel.Commands.cs:934` |
| Created can show an inferred time on Unix (§5.4). | `LocalFileSystemProvider.cs:202` |
| `FileSystemItemInfo.LinkCount` and `FileId` are never filled with real values. | `FileSystemOperations.cs:37-50`, `:420` |
| Generic Analyze is reachable on result sets and queues on the listing’s device, not each item’s. This pre-existing limitation is left; newly enabled Windows Owner Analyze is explicitly restricted to filesystem panels. | MainViewModel.Workspaces.cs:305–323; TabViewModel.cs:1205; ResultSetProvider.cs:269–272 |

---

## 10. Definition of done and launcher

### 10.1 Done means

1. O1–O7 are completed in order with the stage-specific service/UI/fake/native halves stated in §7. Integrated
   ordering/lifetime behavior is complete by O5/O6; an earlier package's test does not claim later behavior passed.
2. Every deterministic checklist requirement in §8.2 passes. Required native evidence for enabled shipping claims
   passes under §8.3, or the affected route is withheld/conservatively unavailable and the capability limit is explicit.
   Optional environment subcases may be Not run/Blocked with a reason. Neither a skipped test nor documentation alone
   verifies an identity, architecture, link or no-materialization claim. The intended Windows Owner feature needs its
   core Windows evidence; do not call it delivered if that entire route remains disabled.
3. PF1/PF2/PF3 meet their recorded, evidence-based allowances and PF4 passes; recording a band alone is insufficient.
   Retained values/keys/strings/identity state, concurrent tabs and retiring operations stay within shared reservations.
4. Windows filesystem users can add optional Owner, see names/raw SIDs and truthful states including LastKnown, inspect
   full identity/reasons by pointer or keyboard, sort under immutable keys, and explicitly Analyze with accurate outcomes
   and actual retained coverage. New Windows Owner Analyze is guarded out of result sets.
5. Unix Owner/Group preserve identity meaning with conservative native fallback and separate names; Permissions retains
   existing failure semantics and gains only the stated detail/Linux flag. No invalid result-mask slot becomes an ID.
6. With Owner absent from the profile and no explicit Owner analysis requested, FileCat initiates no Owner read/name
   request. Paint may enqueue bounded work; sort/hover/Inspect/accessibility may not. Already-started work follows its
   publication/disposal rules.
7. Same-key replacement and late completion cannot attach old ownership or success claims to a new listing. Retained
   snapshots cannot override later ownership; canceled buffers/calls stay budgeted until their actual owners retire.
8. No built-in/custom profile rewrite, shortcut or schema change. FileCat requests no ownership/permission write or
   credential UI; OS side effects and detected-placeholder/native limits are documented accurately.
9. CI is green on Windows x64/ARM64, Ubuntu and macOS for the applicable enabled routes; native statuses are honest.
   No unrelated accepted UX scenario is reopened without its documented concrete reason and stopping condition.

### 10.2 Launcher

```
Implement docs/design/FILECAT_COLUMN_OWNERSHIP_IMPLEMENTATION_PLAN.md, revised for AR-001..AR-013.

Use O1..O7 in order, one reviewable change per package. Complete its stated stage-specific acceptance; do not require
O3 presentation in O1 or O4 Windows native behavior in O2. Before O1, capture PF1/PF3 baselines and record the bounded
comparison method; set justified allowances before measuring the changed implementation. Re-locate source symbols:
the intended baseline is main at 11314715; the inspected later tree through 20899295 differs only in documentation.

Hold to D1..D12 and sections 3/6, including:
- canonical IDs remain separate from names; clear Linux UID/GID result-mask bits produce Unsupported, never IDs;
- painting may enqueue bounded demand through Get; explicit Analyze can read an off-profile field in supported scope;
  sorting, hover, Inspect and accessibility read memory only. Comparisons use immutable captured keys;
- resolver source/field instances have explicit owners. Native name calls use dedicated threads with at most four
  process-wide slots including blocked/retiring calls, bounded queue/cache, no cross-prefix circuit or local-name promise;
- Windows uses exact extended absolute DOS/UNC paths (already extended preserved), READ_CONTROL, all sharing and
  BACKUP_SEMANTICS | OPEN_REPARSE_POINT, then owner-only GetSecurityInfo. Errors 32/33 are transient Failed;
- Linux uses AT_SYMLINK_NOFOLLOW | AT_NO_AUTOMOUNT for the terminal component. macOS Owner/Group use the scoped
  non-materializing policy with restoration; failure is conservative. Offline gates cover detected entries, not all clouds;
- genuine Denied/Unsupported are not retried by timers/activation, but explicit requests and lifecycle invalidation can retry;
- immutable bounded sort snapshots span streaming/external merges at every size. Unretained/newly appended rows have
  fixed unknown keys until a new snapshot; no live cache fallback. Report actual row/character/byte coverage;
- reserve shared bytes for retained metadata, keys and auxiliary state. Cancellation detaches state; release reservations
  only when the last operation owning the charged buffers ends;
- retire ownership before replacement listings use the same cache key. Guard every analysis publication by captured
  store/operation/revision and complete claims by captured row count; typed LastKnown/Gone and effective Compute results keep counts truthful;
- keep existing tooltip/accessibility structure; focus demand is only the vertically visible row's horizontal exception;
- guard newly enabled Windows Owner Analyze to filesystem panels in both chooser and execution;
- no new columns/default profiles/schema/spill format. Allow only the narrow integration changes listed in 6.12.

Use the finite OWN checklist with small limits, fake clocks and gated fakes. Native fixtures establish only claims
fakes cannot prove. Required shipping checks pass or the affected claim/route is withheld with its conservative
fallback; optional environments can be Not run/Blocked. Record fixture, token, build and evidence, never infer a pass.
Keep the accepted UX ledger closed except for a concrete relevant changed behavior and authorized stopping condition.
Do not implement deferred columns or the separate Created correction. Report a concrete design contradiction rather
than silently changing scope; stop after the finite acceptance is satisfied.
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

Original references were reported read on 2026-11-15/16; the independent review rechecked material API/runtime claims.
These establish platform contracts, not FileCat native acceptance. Pin versioned sources where available.

| Topic | Reference |
|---|---|
| Owner by handle, required access, freeing the descriptor | [GetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getsecurityinfo) |
| Owner by path, null owner | [GetNamedSecurityInfoW](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getnamedsecurityinfow) |
| Name lookup, where it searches, `ERROR_NONE_MAPPED` | [LookupAccountSidW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-lookupaccountsidw), [SID_NAME_USE](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ne-winnt-sid_name_use) |
| Opening folders and links without following them | [CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) |
| Names Win32 rewrites unless the extended path form is used | [Naming Files, Paths, and Namespaces](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file) (read by the platform review) |
| Volumes that keep ACLs | [GetVolumeInformationByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationbyhandlew) |
| Placeholder attributes | [File Attribute Constants](https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants) |
| Allocation and link count; compressed size | [FILE_STANDARD_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_standard_info), [GetCompressedFileSizeW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) |
| Creation time on Unix | [FileSystemInfo.CreationTime](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesysteminfo.creationtime) |
| `statx`: no-follow, returned mask, blocks, links | [statx(2)](https://man7.org/linux/man-pages/man2/statx.2.html) (man-pages 6.19) |
| Account lookup returns | [getpwuid_r(3)](https://man7.org/linux/man-pages/man3/getpwuid_r.3.html) (man-pages 6.19) |
| Mode bits and ACLs | [acl(5)](https://man7.org/linux/man-pages/man5/acl.5.html) |
| Allocated size on macOS | [fileAllocatedSizeKey](https://developer.apple.com/documentation/foundation/urlresourcekey/fileallocatedsizekey), [totalFileAllocatedSizeKey](https://developer.apple.com/documentation/foundation/urlresourcekey/totalfileallocatedsizekey); [getattrlist(2)](https://keith.github.io/xcode-man-pages/getattrlist.2.html), a mirror of Apple's manual page, read by the platform review |
| Shipped .NET 10.0.14 creation-time path | [native pal_io.c](https://github.com/dotnet/runtime/blob/v10.0.14/src/native/libs/System.Native/pal_io.c), [managed FileStatus.Unix.cs](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/IO/FileStatus.Unix.cs), [FileSystemEntry.Unix.cs](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/IO/Enumeration/FileSystemEntry.Unix.cs); pinned by Directory.Build.targets, source-verified, not natively tested here |

Additional primary references used for the corrections:

- [NSS source order/actions](https://man7.org/linux/man-pages/man5/nsswitch.conf.5.html).
- [Apple TN3150: dataless files](https://developer.apple.com/documentation/technotes/tn3150-getting-ready-for-data-less-files), including the thread policy and intermediate-folder materialization.
- [Apple stat structure](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/stat.h): moving-branch ABI reference, not a fingerprint of the native GM1 machine.
- [SMB2 open/session procedure](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/61c68667-0b8c-4300-ac8a-246a86f2b11d): implicit authentication can occur.
- [AssocQueryStringW](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-assocquerystringw) and [ASSOCSTR_FRIENDLYDOCNAME](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/ne-shlwapi-assocstr): association labels do not require content sniffing.

The Xcode manual above is a mirror, not current primary Apple evidence. The archived Apple stat/lstat manual is not
used to infer current macOS automount/materialization behavior. SSH.NET/FluentFTP attribute APIs remain unverified
for the deferred remote extension; no claim of current provider ownership support is made.
