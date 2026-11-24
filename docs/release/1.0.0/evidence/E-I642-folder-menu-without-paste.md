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

Branch `fix/i642-folder-menu-paste`, for rc.3. Product source before it equals `v1.0.0-rc.2` (`7269ec61`).

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

Explorer then offered "Undo Move" for these moves, as after its own Paste.

### Change

`src/FileCat.App/Services/WindowsContextMenu.cs`: for a folder's own menu (`--background`, not `--probe-new`), the helper also builds the folder's menu as an item of its parent with `CMF_NOVERBS` (`BuildFolderPaste`). When the Shell offers `paste` there, the helper puts the Shell's item, with its label and state, at the top of the folder's menu, followed by a separator. Choosing it runs `InvokeCommand` on the menu it came from, with the same flags and owner window as every Shell verb. `--probe` and `--probe-verbs --background` list it, the latter with its canonical verb `paste`. If that menu cannot be built or offers no Paste, the folder's menu is unchanged.

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

The selection route itself was checked with a local helper build, not committed. That build chose Paste where the popup would open, without showing a window or taking the foreground:
- A Cut of a file and a folder: READY, INVOKE, exit 0, and both were moved with their contents.
- A Copy: copied.
- A Cut across volumes: moved.
- Text only: no Paste, exit 0.

### Tests

`WindowsContextMenuTests.The_folders_own_Windows_menu_offers_the_Shells_Paste_while_the_clipboard_holds_files`, a real-Shell test in the real helper:
- It puts a file on the clipboard as Explorer's Copy, then as its Cut. Each time `--probe-verbs --background` must list exactly one `paste`, as the menu's first line.
- With text only, the menu must have no `paste`.
- The probes must leave the file and the folder untouched.
- It borrows the clipboard: each format held in memory is put back afterwards, unless another program wrote to it meanwhile. Its own content is kept out of clipboard history.
- Like the other real-helper tests, it skips under a high-integrity token or without the apphost beside the tests.

At medium integrity, `explorer.exe` → `.vbs` → `.cmd`, Debug build, development PC:
- **`WindowsContextMenuTests` and `BackgroundContextMenuTests`:** 56 tests, 0 failed, 0 skipped.
- **Those two with `ArchiveContextMenuTests`, `BreadcrumbMenuTests`, `ClipboardIntentTests`, `MenuInteractionTests` and `MenuUnavailableReasonTests`:** 119 tests, 0 failed, 1 skipped. The skip is `ClipboardIntentTests.Distinct_case_resources_remain_distinct_in_a_cut_publication`, whose fixture needs a case-sensitive file system.

### Remaining checks

1. **Installed candidate with this fix, in the Windows 11 25H2 qualification VM:** this record's finite check above (an Explorer Cut moves, a Copy copies, through the real menu).
   - Also compare Explorer's own empty-space menu there. If it lists "Paste shortcut", FileCat's menu lacks it, and that difference should be recorded.
2. **Hosted CI:** the new test in the medium-integrity App shard. A skip does not qualify the route.
3. **I629:** the change alters verb routing, so its step 3 ("Paste is the Shell's, so an item cut in Explorer moves") and B12's `--background` entry are re-checked on that candidate.
