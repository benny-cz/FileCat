# I642 — The folder's Windows menu offers no Paste

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c37, I629 step 3), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)). **Status:** open, awaiting the owner's decision.

## Observation

A right-click on a panel's empty space opens the folder's own Windows menu: "Give access to", "New", "Properties", and FileCat's "FileCat actions…". It never had **Paste**, in any of these cases:
- **With an empty clipboard.** The medium-integrity `--probe --background` run of c36 also listed only "Give access to", "New" and "Properties".
- **After Copy in Explorer.** Ctrl+C on `copy-me.txt` in an Explorer window; the clipboard then held exactly that file.
- **After Cut in Explorer.** Ctrl+X on `cut-me.txt`; the clipboard again held exactly that file.

In both clipboard cases the file stayed where it was, and nothing arrived in the panel's folder. The menu's other parts work: New → Folder and New → Text Document, Properties in front, and FileCat actions.

## Why it matters

I629 says the route exists so that "Paste is the Shell's, so an item cut in Explorer moves". The helper's source makes the same promise: "A folder's own Paste stays the Shell's: it moves what another program cut, as in Explorer, where FileCat's paste would only copy it."

So an item cut in Explorer can be moved into a FileCat folder by neither route:
- the folder menu offers no Paste;
- FileCat's own paste copies it, and the original stays in place.

No data is lost, but a Cut does not do what the user asked.

## Cause (not confirmed)

The helper builds the menu from `IShellFolder::CreateViewObject(IID_IContextMenu)`. The menu it returns had New, Give access to and Properties, but no Paste. Explorer's empty-space Paste may come from the folder view (`IShellView`) rather than from that object. This is an inference from the observed menu and has not been checked against Windows' documentation or a debugger. I629's tests replace the helper with a seam, so no test saw the real menu.

## Remedy (proposed) and its cost

Take the background menu from the folder view, or add a Paste item to FileCat's folder menu that hands an external Cut to the Shell. Either way, check it live against Explorer's Cut. This changes product source, so it reaches users only in a candidate after rc.2.

## Finite check and stop

On the candidate that carries a fix, cut a file in Explorer and paste it through a panel's empty-space menu: the file moves, with the same bytes, and Explorer's folder no longer has it. Then copy one, paste it the same way, and the original stays. Stop when both hold. If the owner accepts the behavior for 1.0.0 instead, record the acceptance, correct I629's statement, and stop.

## Fix

Branch `fix/i642-folder-menu-paste`, for rc.3. Product source before it equals `v1.0.0-rc.2` (`7269ec61`). It came in two rounds:
- **Round 1 (`d7f386a1`):** the folder's menu offers the Shell's Paste.
- **Round 2:** choosing it pastes. Round 1's Paste did nothing in the VM after a Cut or Copy in Explorer (see "Round 2" below).

### Cause, confirmed

Explorer's empty-space Paste belongs to its folder view, not to the object the helper builds. Microsoft documents `IShellFolder::CreateViewObject` as the request for an object for the folder itself ([docs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)). It documents `SVGIO_BACKGROUND` with `IShellView::GetItemObject` as the way to get a view's background menu ([docs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)).

A scratch probe (outside the repository) built each candidate menu without showing it. It ran at medium integrity, started through `explorer.exe`, on the development PC (Windows 11 Pro build 26320), on 2026-11-24. The clipboard held text only, a file copied as Explorer copies (`Preferred DropEffect` 5), or a file cut (2):

| Menu | Text only | File copied | File cut |
|---|---|---|---|
| The folder's view object, `CreateViewObject(IID_IContextMenu)` (rc.2's route) | no Paste | no Paste | no Paste |
| Explorer's own desktop view, `GetItemObject(SVGIO_BACKGROUND)` (read through `IShellWindows`; nothing shown or invoked) | Paste, grayed | Paste | Paste |
| A folder view hosted by `ExplorerBrowser` in a hidden window, the same call | Paste, grayed | `paste` | `paste` |
| A folder view without a window (`SHCreateShellFolderView`), the same call | Paste, grayed | Paste, grayed | Paste, grayed |
| The folder as an item of its parent (`BHID_SFUIObject`, as the helper builds item menus), `CMF_NORMAL` | no Paste | `paste` | `paste` |
| The same, `CMF_NOVERBS` | no Paste | `paste` | `paste` |

So the view object never has Paste. Explorer's comes from the view, whose own commands (View, Sort by, Group by, Refresh, Paste, Undo) surround the view object's menu. A view works only with a window and a browser hosting it. Without a window it never enables Paste. With one it brings commands that would act on a hidden view. Neither Explorer's desktop view nor the hosted view listed "Paste shortcut", with or without Shift (`CMF_EXTENDEDVERBS`), so FileCat adds none.

The folder's own menu as an item of its parent has the same Paste, which Explorer also shows when the folder itself is right-clicked. It appears only while the clipboard holds what the folder accepts. With `CMF_NORMAL` that menu loads every folder handler: 35 items in 593–676 ms here. With `CMF_NOVERBS`, which the Shell passes for a Send To target's menu ([docs](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu)), no handler loads: Cut, Copy, Paste, Create shortcut, Delete and Properties, 13–117 ms. It also offered Paste for `C:\`, `E:\`, the OneDrive Desktop, Documents and root, and System32.

Invoking that `paste` (scratch probe, the helper's invoke flags, nothing shown) pasted as Explorer's does:
- A Cut of two files and a folder moved them. When `InvokeCommand` returned, the sources were gone and the copies were in place.
- A Copy copied, and the original stayed.
- A Cut from C: to E: moved.
- The hosted view's own Paste gave the same result for a Cut.

Explorer then offered "Undo Move" for these moves, as after its own Paste. These round-1 checks used clipboard data that a program had written and flushed, not Explorer's live data object; round 2 shows why that mattered.

### Change (round 1)

`src/FileCat.App/Services/WindowsContextMenu.cs`: for a folder's own menu (`--background`, not `--probe-new`), the helper also builds the folder's menu as an item of its parent with `CMF_NOVERBS` (`BuildFolderPaste`). When the Shell offers `paste` there, the helper puts the Shell's item, with its label and state, at the top of the folder's menu, followed by a separator. `--probe` and `--probe-verbs --background` list it, the latter with its canonical verb `paste`. If that menu cannot be built or offers no Paste, the folder's menu is unchanged. In round 1, choosing it ran `InvokeCommand` on the menu it came from; round 2 replaces that.

Unchanged:
- arguments, the `FILECAT_CONTEXT_READY` and `FILECAT_CONTEXT_INVOKE` lines, and the exit codes. Paste ends with 0, or 11 when the Shell's command fails, as other Shell verbs do;
- the high-integrity refusal and item menus;
- `--probe-new`, which does not build the Paste menu.

For B12: the paste runs in the helper with its starter's token, like every Shell verb there, and the helper still listens nowhere.

### Probe output, medium integrity (development PC)

`FileCat.exe --native-context-menu --probe-verbs --background <folder>`, with a file cut in the clipboard. The host's own handlers (Visual Studio, Git, TortoiseGit) are shortened:

```
&Paste	paste
Open with &Visual Studio	AnyCode
…
&Give access to	Windows.Share
…
Ne&w	New
	&Folder	NewFolder
	&Shortcut	NewLink
	Text Document	.txt
	…
P&roperties	properties
FILECAT_CONTEXT_READY
```

- **Before (rc.2 source):** the same list without `&Paste	paste`, whatever the clipboard held.
- **After, text only in the clipboard:** no Paste line.
- **After, file copied or cut:** `&Paste	paste` heads the menu. With a Cut it does so also with `--extended`, and on `C:\` with `--probe --background`.
- **Other modes, after:**
  - `--probe-new NewFolder` still printed READY, INVOKE and the CREATED line, and exited 19.
  - A removed folder still gave exit 1 and one stderr line (`FileNotFoundException 0x80070002`).
  - The probes moved nothing.

In round 1 the selection route was checked with a local helper build, not committed, with flushed data. That build chose Paste where the popup would open, without showing a window or taking the foreground:
- A Cut of a file and a folder: READY, INVOKE, exit 0, and both were moved with their contents.
- A Copy: copied.
- A Cut across volumes: moved.
- Text only: no Paste, exit 0.

### Tests (round 1)

`WindowsContextMenuTests.The_folders_own_Windows_menu_offers_the_Shells_Paste_while_the_clipboard_holds_files`, a real-Shell test in the real helper:
- It puts a file on the clipboard as Explorer's Copy, then as its Cut. Each time `--probe-verbs --background` must list exactly one `paste`, as the menu's first line.
- With text only, the menu must have no `paste`.
- The probes must leave the file and the folder untouched.
- It borrows the clipboard: each format held in memory is put back afterwards, unless another program wrote to it meanwhile. Its own content is kept out of clipboard history.
- Like the other real-helper tests, it skips under a high-integrity token or without the apphost beside the tests.

At medium integrity, `explorer.exe` → `.vbs` → `.cmd`, Debug build, development PC:
- **`WindowsContextMenuTests` and `BackgroundContextMenuTests`:** 56 tests, 0 failed, 0 skipped.
- **Those two with `ArchiveContextMenuTests`, `BreadcrumbMenuTests`, `ClipboardIntentTests`, `MenuInteractionTests` and `MenuUnavailableReasonTests`:** 119 tests, 0 failed, 1 skipped. The skip is `ClipboardIntentTests.Distinct_case_resources_remain_distinct_in_a_cut_publication`, whose fixture needs a case-sensitive file system.

Hosted CI (PR #27, Windows x64 App tests): the test passed, not skipped.

### Round 2: the Paste itself

**Finding (qualification VM, Windows 11 25H2 build 26200, a pre-rc.3 build with round 1).** After Ctrl+X in Explorer the clipboard held `Shell IDList Array`, `DataObjectAttributes`, `DataObjectAttributesRequiringElevation`, `Preferred DropEffect` (2), `AsyncFlag`, `FileDrop`, `FileNameW`, `FileName`, `FileContents` and `FileGroupDescriptorW`. The folder's menu listed Paste, but choosing it moved nothing. The helper, run by hand, printed READY and INVOKE and ended about 10.5 s after the click; the file stayed in Explorer's folder.

**Cause.** Explorer's Cut and Copy leave the Shell's own data object on the clipboard, served by Explorer and marked for transfer in the background: the `AsyncFlag` format. The menu's paste command then does not transfer within `InvokeCommand`. It hands the transfer to a Shell work thread, which reads the data through the helper's apartment, and the helper ended as soon as the call returned. In the VM the call waited about 10 s first (`CMIC_MASK_NOASYNC`); here it returned at once. Either way the transfer never ran.

Reproduced on the development PC (build 26320, medium integrity) with a stand-in for Explorer. A process put the Shell's own data object for a file on the clipboard, with `Preferred DropEffect` and Explorer's `AsyncFlag`, and kept serving it:
- rc.2's invoke moved nothing.
- The same invoke moved the file when the helper went on dispatching its messages after the call.
- Without `AsyncFlag` (data a program wrote and flushed) the transfer completes inside the call, which is why round 1's checks passed.

**Why the helper cannot simply wait for that work.** The documented host contract is the process reference ([SetProcessReference](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-setprocessreference), [GetProcessReference](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-getprocessreference)); shell32's `SHSetInstanceExplorer` and `SHGetInstanceExplorer` jump straight to those. Offered by the helper, the reference was taken only briefly after a transfer, never during one. A 1 GiB Cut across volumes took and released it at once, 770 ms after the call returned, so the reference cannot say when a long transfer ends.

**Change (round 2).** Choosing Paste on a folder's own menu, or on a folder item's menu, now does a paste as Microsoft documents it ([Handling Shell Data Transfer Scenarios](https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios)):
- The helper takes the clipboard's data object (`OleGetClipboard`) and the folder's own drop target. The target comes from the folder's parent (`IShellFolder::GetUIObjectOf`, `IID_IDropTarget`) with the helper's owner window, so the copy engine's progress and questions belong to the window that had the foreground for the menu.
- It drops the data as a left-button drop. A Cut (`Preferred DropEffect` exactly `DROPEFFECT_MOVE`) is dropped with Shift, so the target moves the items itself (the optimized move the Shell does for files). Anything else is dropped with Ctrl, so it copies (or links).
- The data goes in without the asynchronous capability: no `IDataObjectAsyncCapability`, and `AsyncFlag` hidden. The documented target transfers in the background only for that capability, so the transfer, its progress and any question it asks run within `Drop`, and are done when it returns.
- No delete-on-paste report (`CFSTR_PASTESUCCEEDED`) goes to the source. A move from a virtual source, one the target can only copy from, therefore stays a copy, and Explorer keeps its cut list.
- After the drop the helper offers its process reference and stays while any follow-up Shell work holds it, and at least 1 s.
- Other verbs keep the synchronous invoke.

**New test mode `--native-context-menu --probe-paste FOLDER`.** It builds the folder's menu and runs its Paste exactly as when chosen from it, without showing the menu, and prints:
- `FILECAT_CONTEXT_READY`, then `FILECAT_CONTEXT_INVOKE`.
- Then one line: `paste: Preferred DropEffect N; Drop 0x… with effect E after T ms; …; the helper ended T ms after the paste`.

It exits 0 after the paste. It exits 11 with one stderr line when the paste failed, and 1 with "No Paste here." when the menu offers no Paste. Like `--probe-new`, it acts with its caller's token, and FileCat never starts it. Exit codes and protocol lines are otherwise unchanged.

**Incident on the development PC (uncommitted code, 2026-11-24 18:34).** An intermediate version passed no mouse button in the drop's key state. Windows' drop target took it for a right-button drop and showed its drop menu on the owner's desktop: handlers, "Copy here", "Create shortcuts here", Cancel. After one such menu was dismissed, the same version still sent the delete-on-paste report, and the source deleted the test file `live-cut.bin` to the Recycle Bin although nothing had been pasted. The shipped code fixes both:
- the drop is a left-button drop;
- no report is sent.

No paste has run on that PC since. Pastes are checked only in the VM and on CI runners.

**What is and is not verified.**
- **Verified on the development PC:** the cause above, and that the round-1 route with continued dispatching moved and copied correctly (including the 1 GiB Cut). That route is not shipped.
- **Not yet run anywhere:** the shipped drop route. It compiles, and the headless tests pass. Its first runs are hosted CI and the VM.

**Tests (round 2).** Two real-Shell tests run `--probe-paste` and check the files byte for byte:
- `A_folders_own_Paste_moves_what_was_cut_and_copies_what_was_copied` uses flushed data.
- `A_folders_own_Paste_moves_what_Explorer_cut_while_Explorer_serves_the_clipboard` uses an Explorer stand-in on its own STA thread: the Shell's data object, asynchronous mode and `AsyncFlag`.

They run only where a paste may show Windows' own windows: on CI runners (`CI=true`) or with `FILECAT_TEST_SHELL_PASTE=1`. At medium integrity on the development PC, `WindowsContextMenuTests` and `BackgroundContextMenuTests` gave 58 tests, 0 failed, 3 skipped:
- the two paste tests, by design;
- the menu-listing test, because the locked session's clipboard could not be opened.

### Remaining checks

1. **The pre-rc.3 build with round 2, in the Windows 11 25H2 qualification VM:**
   - this record's finite check (an Explorer Cut moves, a Copy copies) through the real menu, and with `--probe-paste`;
   - a Paste whose name already exists in the folder: the copy engine asks, and Skip or Cancel loses nothing;
   - Explorer's clipboard after the paste (the cut list stays, as described above);
   - Explorer's own empty-space menu: if it lists "Paste shortcut", record that FileCat's menu lacks it.
2. **Hosted CI:** all three new tests pass in the medium-integrity App shard. A skip does not qualify the route.
3. **I629 and B12:** verb routing changed and `--probe-paste` is new, so I629's step 3 ("Paste is the Shell's, so an item cut in Explorer moves") and B12's inventory are re-checked on that candidate.
