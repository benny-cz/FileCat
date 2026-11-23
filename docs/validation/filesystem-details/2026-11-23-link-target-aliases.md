# Link target for app aliases and WSL links — 23 November 2026

The column audit of 22 November found **Link target** blank for app execution aliases and WSL symbolic links on Windows. The column read `FileInfo.LinkTarget`, and .NET reads a target only from a symbolic link or a junction. This receipt covers the fix.

## Before

On the Windows x64 workstation, PowerShell listed the aliases in the account's `%LOCALAPPDATA%\Microsoft\WindowsApps` with the attributes `Archive, ReparsePoint` and an empty `LinkTarget`. `fsutil reparsepoint query` showed the first one's payload: tag `0x8000001b`, version 3, then four strings. They were the package `MicrosoftWindows.Client.CBS_cw5n1h2txyewy`, the application `MicrosoftWindows.Client.CBS_cw5n1h2txyewy!App`, the program `C:\WINDOWS\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\WindowsCopilotRuntimeActions.exe`, and the type `0`.

## What FileCat does now

- When .NET finds no target and the item is a reparse point, the column reads the item's own reparse data, with `FSCTL_GET_REPARSE_POINT` on a handle that neither follows the item nor reads its content. FileCat's existing payload reader parses it.
- An app execution alias shows the program it starts. Pointing at it says that Windows starts this program for the application, from the package, and that FileCat neither starts nor follows it.
- A WSL symbolic link shows its Linux path. Pointing at it says that programs in WSL follow it, and Windows programs and FileCat do not.
- Both are typed descriptive values (`LinkTargetText`), not paths: nothing navigates by the column's value. They sort by their text, with the other targets.
- Ordinary items are not opened a second time: only an item with the reparse point attribute is.

## Tests

`LinkTargetColumnTests` (Core):

- A file given an app execution alias payload (four strings), or a WSL link payload (version 2 and a UTF-8 path), through `FSCTL_SET_REPARSE_POINT`. .NET reads no target from it; the column shows the program or the Linux path and says what it is.
- The first real alias in the account's WindowsApps folder shows the program its payload names (skipped where there is none).
- A plain file still has no target, and a symbolic link still shows its stored target, as a plain value.

On the Windows x64 workstation at medium integrity, `LinkTargetColumnTests`, `ReparsePresentationTests` and `ReparseOperationPolicyTests` ran 37 tests and all passed, none skipped: the token could set both payloads, and a real alias was there.
