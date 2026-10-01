# E-R04 — Step 4, first pass: the plan's code anchors against the source

Static evidence for plan step 4 ("reconcile C01–C29, all original Section 5 registers plus U/F/L/T, CW, DPI and B rows
against refreshed source"): a mechanical check that every code identifier those rows name still exists. It shows the
plan's anchors are not orphaned. It does not show that each claim is true (the plan's own rule: no claim-as-proof).

- Source: `906f1e9` (main). Script `r04/anchor_check.py` `8115aed29821407f7ec6e0cce926a522054dff44485b736c35557a5e72f75a9a`,
  output `r04/anchor-check-906f1e9.txt` `0db3d51b2a31d6beb1baf69fe67d9640b3e7b447fc029483d2402b5e9c956a17`.
- Rows read: every table row of plan §3.1 (C01–C29), §4, §5.1–5.10 (requirements, invariants, decisions, ADRs,
  technical validations, capability gaps, phases and non-goals, assumptions and risks, the U/F/L/T items, trust
  boundaries B01–B10), §8.0 (CW) and §8.3 (DPI): 421 rows.
- Identifiers: every CamelCase name in those rows (types, files, members, packages), checked against the type, member
  and file names under `src/`, `tests/` and `eng/` and the workflow files: 137 names.

## Result

All 137 exist, except eight rows whose names are not code, or are code by another spelling:

| Row | Name | Disposition |
|---|---|---|
| §4.2 MAC, §4.3 Screen, §4.4 PSD-MAC | VoiceOver | Apple's screen reader, not FileCat code |
| §5.1 OPS-002 | OperationCenter | `OperationCenterViewModel` (with `OperationsView`) |
| §5.3 macOS page title | PageEngineSmoke | the project `tests/FileCat.PageEngineSmoke` (extended `85d512d`) |
| §5.4 ADR-07 | DiscUtils, SharpCompress | package references of `FileCat.Archives` (`SharpCompress`, `LTRData.DiscUtils.Iso9660`, `LTRData.DiscUtils.Udf`) |
| §5.9 F12 | DiskMap | a style of view the row says is deferred, not code |
| §8.3 P15 | UninstallDelete | the installer's recursive `[UninstallDelete]`, removed on purpose by I15's fix (`5b061cc`) |

No row names code that is gone.

## Reachable routes of C01–C29

The command registry holds 154 commands. The main menu (`MainMenuModel.Layout`, which also tells the command search
where each command is) places 144. The other ten are keyboard or context routes: Open (Enter), the context-menu key,
menu activation, the numbered-bookmark prefixes, command history, clearing history, marking operations complete, and
the viewer window list. Each capability row's route, matched with the menu:

| Row | Route in the plan | In the menus (or other route) |
|---|---|---|
| C01 | Panels, tabs, path/place controls | Navigate, Panels (tabs, arrange), place and path controls |
| C02 | Keyboard, menus, visible search | Mark; Tools › Palette (command search) |
| C03 | F5–F8, Operations | File › Copy, Move, Make folder, Delete; Tools › Operations |
| C04 | Operations/restart | File › Undo; Tools › Operations; recovery at start |
| C05 | Columns, Analyze, Space, Count | Mark › Count folder sizes; View (columns); badges without a command |
| C06 | Alt+F7, flat view, working sets | Commands › Find files, Flat view; Navigate › Working sets; File › Add to working set |
| C07 | Compare commands and preview | Commands › Compare directories, Compare files (Synchronize from the comparison) |
| C08 | F3/quick view | File › View; Panels › Quick view; File › Checksum |
| C09 | Explicit hex editor | File › Hex edit |
| C10, C11 | Viewer modes, F3 Page | modes inside the viewer |
| C12 | Enter, F3/F5, ZIP commands | File › Pack, Unpack, Test archive |
| C13 | F4 then Commit/Discard | File › Edit, Edit sessions |
| C14 | Connection UI, remote panels | Commands › Connect (SFTP/FTP), Disconnect |
| C15 | Network place, place menus | Commands › Connect/Disconnect network drive; the Network place |
| C16 | Registry place, contextual F3–F8 | File › Registry (export, import, data, writable, view) |
| C17 | Retry as administrator; title/About | the retry offered by refused operations; Help › About |
| C18 | Streams and attributes | File › Hidden data |
| C19 | File-system record; drive journal | File › File record; Tools › Change journal |
| C20 | Badges/tooltips; verification jobs | File › Checksum, Verify checksums |
| C21 | Recover deleted files | Tools › Find deleted |
| C22 | Devices in This PC | the This PC place (no command) |
| C23 | Browsing, quick view, menus/drop | File › Reveal, Open with system; Commands › clipboard |
| C24 | Settings, all controls | Tools › Settings |
| C25 | Startup, Settings, Help | Help › Check for updates; Tools › Settings |
| C26 | Install/extract | the packages themselves (no command) |
| C27 | Attribute/time commands | File › Attributes, Properties |
| C28 | F4, command line, terminal, association | File › Edit, Apply command; Commands › Command line, Open terminal, User menu |
| C29 | Rename tool; create-link command | File › Bulk rename, Create link |

No capability lacks a route. Whether each route works on each platform is V01–V24's and V17/V18's to show.

## Not covered (the rest of step 4)

- Most rows describe behaviour in words rather than name code (137 names in 421 rows). Whether each claim holds is
  checked by the validation cases (V01–V24, CW) and the DPI review (E-DPI), not here.
- The "enabled condition" column (settings, permissions, platform) was not walked.
