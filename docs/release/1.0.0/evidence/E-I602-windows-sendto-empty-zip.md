# I602 — Windows Send to compressed folder creates a zero-byte ZIP

## Report and consequence

On 2026-11-01 the owner reported that **right-click → Send to → Compressed (zip) folder** in FileCat creates a **zero-byte ZIP** for both local files and directories. A zero-byte output cannot contain the selected item, so an apparently completed Shell action may mislead users into keeping an unusable archive. The owner has not yet supplied the exact package hash, selected path, or an Explorer control on the same item. FileCat's own Pack action is a distinct reviewed route and does not qualify this native Shell route.

The report followed I601's Windows Properties correction. It is a consequential reason to examine the `IContextMenu::InvokeCommand` verb boundary, but it does not reopen I556's accepted token and finite-lifecycle containment controls. I556's native check constructed a menu without invoking a verb.

## Source correction under test

Product `c491e8922e21d838b883ecbb40d1917cf43442ad` adds `CMIC_MASK_NOASYNC` (`0x00000100`) when invoking a selected Shell command in the short-lived isolated host. The installed Windows SDK maps it to `SEE_MASK_NOASYNC` and says it blocks until invocation completes for callers that exit after `ShellExecuteEx`. [Microsoft's `CMINVOKECOMMANDINFOEX` contract](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ns-shobjidl_core-cminvokecommandinfoex) calls this flag **advisory**: a handler may still return before its work finishes. Thus this source change is a hypothesis to verify, not a claim that the reported ZIP is fixed.

The Windows x64 Release app builds with zero warnings/errors; focused `WindowsContextMenuTests` pass **7P/0F/0S**. A read-only native Windows menu probe over the owned `.tmp/i601-shell-menu/owned.txt` still shows the `Send to` submenu and exits successfully without creating an archive. Built app DLL SHA-256: `D2808F76A3074C7E48C65693B9995EB2DD5D14E9D80BB5A32F1D98B779D50B49`. Exact-source main CI [41297672046](https://github.com/benny-cz/FileCat/actions/runs/41297672046) was in progress at this record. These checks prove the code builds and the menu exists; they do not prove the selected ZIP verb completes.

## Disposition and finite stopping rule

**High data-integrity/UX issue, Open.** On a rebuilt Windows FileCat containing `c491e892` or later, choose the Shell `Send to` compressed-folder action on an owned nonempty local file and an owned folder with a nested nonempty file. Require a readable nonzero ZIP with the exact expected member paths and independently matching uncompressed byte hashes. Compare Explorer's same action on the same selections if FileCat fails. Check source files and unrelated neighbors remain unchanged, and no zero-byte partial ZIP is presented as success. If the advisory flag is insufficient, diagnose the helper's COM/source lifetime and add a proved correction before closing the issue. The final installed 1.0.0 candidate must repeat the affected route. Public preview.5 predates I602; stable 1.0.0 remains **NO-GO**.
