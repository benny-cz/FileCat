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
