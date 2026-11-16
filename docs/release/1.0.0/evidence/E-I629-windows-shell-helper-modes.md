# I629 — Windows Shell helper modes added after their accepted evidence

**Source:** `ff14b6b2` (parent `42829297`), committed 2026-11-13; no hosted CI has run at it. **Disposition:** remediated preliminarily at source. The new helper modes have no native or candidate qualification. This record names them and the re-check they need. It leaves the dated [E-I556 lifecycle record](E-I556-windows-context-menu-lifecycle.md) and the [E-DPI B12 inventory row](E-DPI-review.md#trust-boundaries-v23) unchanged.

## Why a new record

Product `ff14b6b2` adds three things: the folder's own Windows menu on a panel's empty space, Shift+right-click extended verbs, and inline rename after the Shell's New command. It also takes type icons from the file association. Two helpers that run Shell code gained behavior after their release evidence was accepted:

- `I556-WINDOWS-CONTEXT-MENU-LIFECYCLE` was accepted at `c257bb08` (2026-10-28) for the item menu at screen coordinates and its `--probe`. Its native probe invoked no Shell verb.
- E-DPI's B12 row (2026-10-01) lists `FileCat --native-context-menu x y paths…` with its `--probe`, and `FileCat.ShellHost --serve [--test-faults]`. It also omits `--probe-verbs`, which `11078d80` added for I601 on 2026-11-01. It cannot list the modes below.

Earlier ShellHost evidence, such as [E-I493's restricted-token controls](E-I493-worker-administrator-authority.md), predates request kind 5. Those results stay valid for their own sources but do not cover the modes below.

## What changed at `ff14b6b2`

### Native context-menu helper (`FileCat.exe --native-context-menu`)

| Mode or output | Behavior |
|---|---|
| `--background X Y FOLDER` | Builds the folder's own Shell menu from `IShellFolder::CreateViewObject` instead of the items' UI object. This is Explorer's empty-space menu: New, Paste, the folder's handlers and Properties. It takes exactly one fully qualified folder. Properties still exits 13 to FileCat's sheet. Paste and the folder's other verbs stay the Shell's: the Send-to-ZIP (14), Copy (15), Cut (16) and Share (17) exits apply only to item menus. |
| `--extended` | Passes `CMF_EXTENDEDVERBS` to `QueryContextMenu` for Shift+right-click, on item and folder menus. `--probe` and `--probe-verbs` accept both `--background` and `--extended`. |
| `--probe-new VERB FOLDER` | Test mode. It builds the folder's menu and fills its submenus. It then writes `FILECAT_CONTEXT_READY` and `FILECAT_CONTEXT_INVOKE` and **invokes** one New command without showing a menu. `--probe` never invokes. `VERB` must be `NewFolder` or one plain extension such as `.txt`. Any other verb exits 1 before Shell code loads. A verb missing from the menu exits 1 after readiness. |
| Created-item report | This follows a New command from a folder menu whose entry makes the item by itself. When exactly one new entry of that kind appears, the helper writes the last stdout line `FILECAT_CONTEXT_CREATED <base64 of the UTF-8 name>` and exits **19**. Entries with a ShellNew `Command` or `Handler`, and `.lnk` and `.url`, are never reported. The folder must resolve to a fixed or RAM drive and list within 2 seconds and 100,000 entries. Otherwise nothing is reported. |
| FileCat's reading of the report | After `INVOKE`, FileCat drains the helper's output in bounded pieces and keeps no line over 4,096 characters. It counts the report only as the last line. It accepts one plain name: at most 2,048 payload characters, strict UTF-8, 1–255 characters and not blank, no control, separator, wildcard or invalid-name character, and not `.` or `..`. Accepted exit codes are 0, 10–17 and 19. The name is used only to focus the item and open it for renaming with the ordinary Rename command. That happens once the listing shows it, within 5 seconds, and only while cursor, marks, tab and focus are as the menu left them. |
| Failures | `RunHost` now answers every exception except `OutOfMemoryException` with one stderr line (type and HRESULT) and exit 1. At `42829297` it answered only COM, external, Win32, argument and invalid-operation exceptions, and any other exception crashed the helper. |

Unchanged against `42829297`: the high-integrity refusal still comes before any helper start in FileCat and before any argument parsing in the helper. The `FILECAT_CONTEXT_READY` → `FILECAT_CONTEXT_INVOKE` protocol is unchanged, as are the 20-second construction, 10-minute selection and 20-second invocation deadlines.

### Shell helper (`FileCat.ShellHost --serve`)

| Change | Behavior |
|---|---|
| Request kind 5, `TypeIcon` | The request's path is an extension. FileCat and the helper both refuse anything but one plain extension: 2–64 characters, one dot at the start, no invalid-name, separator or wildcard character and no trailing space. They also refuse the 16 shortcut-like types of the existing exclusion list, such as `.lnk`, `.url`, `.library-ms` and `.theme`. The helper makes a simple ID list (`SHSimpleIDListFromPath`) for `%SystemRoot%\System32\FileCat type probe\file<ext>`, a name in a folder that does not exist. It then asks the Shell's image factory for an icon only (`SIIGBF_ICONONLY`). By design no file is opened. |
| When FileCat asks | Only when a type's association names a packaged app's resource (`@{Package?ms-resource://…}`, such as Notepad's for `.txt`), which only the Shell resolves. FileCat reads other DefaultIcon locations itself through `AssocQueryString`, and only from resource files on this computer's fixed drives. |
| Wait | FileCat waits up to 8 seconds for a `TypeIcon` answer, as for thumbnails and icon resources. Ordinary and overlay icons get 3 seconds. The first packaged-app icon loads the package resource machinery. |

The same `--serve` process, framing and request loop serve kind 5 and kinds 1–4.

## Source-level checks

The product commit reports six independent review rounds and the full App suite passing at medium integrity (2,785 tests). It reports no Platform.Windows run. Nothing was re-run for this record.

| Area | Tests added at `ff14b6b2` | Limit |
|---|---|---|
| Real helper | `WindowsContextMenuTests.The_folders_own_Windows_menu_offers_New_and_reports_the_folder_New_makes` runs `--probe-verbs --background`, then `--probe-new NewFolder` and `.txt`, and expects exit 19 with the created name. `A_folder_gone_before_its_menu_is_built_is_answered_not_crashed_on`. | Both skip under a high-integrity token, without the apphost beside the tests, or when the temporary folder is not on a fixed drive. Such a skip does not qualify the route. |
| Report protocol | `A_New_command_reports_its_item_on_the_helpers_last_line_whatever_the_code_page`, `A_created_report_counts_only_as_the_last_line_and_overlong_output_is_not_kept`, `Only_one_plain_name_is_taken_from_a_helpers_report`, `A_report_that_is_not_base64_of_strict_UTF8_or_is_too_long_names_nothing`, `Only_New_commands_that_make_one_named_item_are_followed_by_renaming`, `Only_New_entries_that_make_their_item_by_themselves_are_offered_for_renaming`, `A_folders_own_menu_needs_one_full_path` | The lifecycle case uses a `cmd.exe` fixture, not Shell code. |
| Panel routing | `BackgroundContextMenuTests`: Shift for extended verbs, FileCat actions and the unavailable-menu fallback, and renaming only an undisturbed, listed new item. | These replace the helper with a test seam. |
| Type icons | `AssociatedTypeIconTests.The_restricted_helper_draws_a_types_icon_at_each_size_asked_for_and_refuses_the_rest` (real ShellHost), `Pictures_refuse_a_type_request_the_policy_refuses_without_asking_the_helper`, `The_Shell_draws_only_plain_types_whose_handlers_follow_no_paths`, and two association-classification tests | Platform.Windows suite. The commit reports no run of it. |

## Finite re-check and stop

1. **Hosted CI.** At `ff14b6b2`, or a descendant with the same helper code, the producer, Windows x64, Windows ARM64, Ubuntu and macOS jobs pass. Reconcile the new tests' outcomes. A native helper test skipped for its token, apphost or drive does not qualify its route.
2. **Helper token and lifecycle for the new modes, as I556 checked the item menu.** On Windows 11, from a high-integrity token, `--native-context-menu --background X Y FOLDER` and `--native-context-menu --probe-new NewFolder FOLDER` exit 1 with no `FILECAT_CONTEXT_READY` and nothing created. From a medium-integrity token, three routes must pass:
   - `--probe --background FOLDER` builds the folder's own menu (New, Paste, Properties) through readiness and exits 0.
   - `--probe-new NewFolder FOLDER` and `--probe-new .txt FOLDER` each write READY, INVOKE and a last `FILECAT_CONTEXT_CREATED` line naming exactly the one item made, then exit 19.
   - A folder removed before its menu is built gives exit 1 with one stderr line and no crash.
3. **Installed candidate, unelevated.** Right-clicking a panel's empty space shows the folder's Windows menu with "FileCat actions…". Shift+right-click adds extended verbs to item and folder menus. New → Folder and New → Text Document focus the new item with its name open for editing. A cursor move or keystroke within 5 seconds prevents the rename. Paste is the Shell's, so an item cut in Explorer moves. Properties opens FileCat's sheet for the folder. From an elevated FileCat no helper starts, and FileCat's own folder actions open instead.
4. **Type icons.** Test the installed candidate on Windows 11 with Notepad and Photos as the default apps. `.txt` and `.jpg` rows must show the packaged app's icon, not the blank page, at each icon size the panels use. `.lnk` and `.url` must never reach the helper. The serving helper must be the restricted low-integrity ShellHost, by E-I493's token checks. `%SystemRoot%\System32\FileCat type probe` must not exist afterwards.
5. **B12 inventory.** A new dated review adds these to the startup/internal/test-mode inventory and confirms its "Held" statement for each:
   - `--background` and `--extended`;
   - `--probe-new`, a shipped test mode that makes one item with its caller's token;
   - the exit-19 report;
   - the earlier `--probe-verbs`;
   - ShellHost request kind 5.

Stop when these pass in the exact candidate and the CI gate is recorded; `I629-WINDOWS-SHELL-HELPER-MODES` may then be accepted. Reopen it only for:

- a changed helper argument, protocol line, exit code, request kind, verb routing or token;
- a demonstrated regression;
- an untested mandatory Windows architecture.
