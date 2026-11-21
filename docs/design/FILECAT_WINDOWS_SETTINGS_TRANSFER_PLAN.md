# FileCat Windows settings transfer plan

Add **Copy appearance and workspace between sessions…** to Settings on Windows. The default direction is normal → administrator. Users can copy saved appearance and the current workspace together or transfer either one separately. Transfers can be remembered for future launches under that administrator account and FileCat profile. The ordinary profile remains the baseline. Only a separate, confirmed action in a normal window can change it.

Use a disposable profile with schema 1, the `Psychedelic` theme, compact rows, and list and content font sizes of 13 for acceptance tests. The usual installed profile stores its settings under `%APPDATA%\FileCat\settings.json`.

## User experience

| Where | Action | Result |
|---|---|---|
| Normal Settings | **Copy appearance and workspace for administrator** | Copies saved appearance and the current panel layout and local-folder tabs as a single value. |
| Administrator Settings | **Use copied appearance and workspace** | Accepts one paste and shows one review for both parts. **Apply and remember** restores both together on later launches. Clear **Remember for future administrator launches** to apply for the current session. |
| Normal Settings | **Copy saved appearance for administrator** | Copies the active profile’s saved display preferences. Shows the source profile and path. An open Settings draft stays separate. |
| Administrator Settings | **Use copied appearance for this session** | Shows the incoming changes and applies them to the running UI. Select **Remember for future administrator launches** to load these values again. |
| Normal Settings | **Copy current workspace for administrator** | Copies the current panel arrangement and open local-folder tabs from the running window. |
| Administrator Settings | **Use copied workspace for this session** | Previews proposed folders and asks before replacing the current panels and tabs. **Restore previous workspace** is available after applying it. Select **Remember for future administrator launches** to reopen the copied workspace. |
| Administrator Settings | **Forget remembered transfers…** | Clears the remembered appearance and workspace for the current account and profile. The current window keeps its appearance and panels. |
| Administrator Settings, under **Copy back…** | **Copy this session’s appearance** | Copies the current display preferences, including changes made since import. The administrator process performs no profile write. |
| Normal Settings, under **Copy back…** | **Review administrator appearance** | Shows old and proposed values and the exact destination `settings.json`. **Keep my settings** has default focus. Only **Replace these appearance settings** commits. Opening the review, pressing Escape, or closing it leaves both live and saved normal settings alone. |

Use an explicitly copied, versioned plain-text value. Keep it available after the source window closes, and allow manual entry when the two desktops do not share a clipboard. The combined review selects remembrance by default where it is available. Separate transfers start with remembrance off.

## Transfer scope and security decisions

Use the same allowlist in both directions. Review each new `AppSettings` property before admitting it.

| Category | Decision |
|---|---|
| Appearance | Include built-in theme, theme animations, row density, list and content font sizes, built-in date formats, toolbar visibility and style, function-key bar, Places row visibility and compactness, and command-line visibility. |
| Other presentation data | Exclude custom font families and format strings, and pinned destinations. |
| Column profiles | Include from version 2 of the workspace value (21 November 2026), with each tab's profile. Some columns read file contents as files are listed, so an administrator session shows only columns that read file-system metadata: the built-in columns, Owner, Group, Permissions, Link target, Extra data names, Allocated, Hard links and Reparse type (`ColumnProfileSet.ElevatedKeys`). Version, Dimensions, Origin, Verified and any column not on that list stay in the user's own session. The review names them. The administrator session shows the profiles without writing the user's settings. |
| Commands | Exclude editor, viewer, diff tool, terminal executable or shell, user commands, associations, arguments, hotkeys, and key bindings. Showing the command line transfers only its visibility flag. |
| Startup and operations | Exclude single-instance behavior, update checks, saved searches, workspace restoration, shell previews, verification, delete confirmations, and diagnostics. |
| Connections and credentials | Exclude remote profiles, hosts, usernames, key paths, credential references, passwords, trust decisions, and keys. The transfer never queries Windows Credential Manager. |
| Files outside settings | Keep persisted workspace files, history, journals, recovery records, caches, diagnostics, and the privileged helper’s exchange directory outside the feature. |

The workspace value contains panel order, split directions and sizes, active panel and tab, panel targets, and open local folder paths. From version 2 it also contains the column profiles and the profile each tab shows; version 1 values still apply, with every tab on the first profile. The transfer creates fresh panel IDs and checks the folder paths after confirmation and at each remembered launch. Network locations, virtual views, and folders behind reparse points are excluded. An empty panel opens Home or This PC when Home is unavailable. Workspace copy flows from normal to administrator. [Workspace transfer format](../../src/FileCat.App/Services/WorkspaceTransfer.cs)

Validate on receipt even if FileCat produced the value. Accept only the supported version, named fields, built-in choices, booleans, and finite sizes within the Settings UI’s ranges. Reject malformed, duplicate, unknown, or oversized input as a unit. Read only bounded plain text on an explicit paste action. The payload needs no paths or account identifiers. [AppSettings](../../src/FileCat.Core/State/StateModels.cs) · [Remote profiles and secret references](../../src/FileCat.Core/State/Secrets.cs) · [Windows clipboard guidance](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata)

## Implementation

1. **Define the transfer format.** Add `AppearanceTransferV1` alongside `AppSettings` in Core, with explicit export, validation, diff, and field-by-field apply. The ordinary exporter reads only the current `AppServices.Paths.SettingsFile`, using a bounded, side-effect-free read. A missing, damaged, or newer primary gets a clear message instead of silently exporting defaults or a backup. Keep the existing high-integrity guards in `JsonFileStore.Load` and `SaveCore` intact. [JsonFileStore](../../src/FileCat.Core/State/JsonFileStore.cs)

2. **Separate visual refresh from saving.** Extract the appearance refresh from `MainViewModel.ApplySettings()`. Keep the existing Settings OK behavior. The elevated action applies validated fields in memory and refreshes theme, density, bars, and Places directly. Content font size takes effect for newly opened viewers and comparisons, matching current behavior. In `SettingsDialog.ShowAsync`, expose the transfer action beside the elevated read-only notice. Leave the ordinary read-only handling for newer or unreadable profiles intact. [ApplySettings](../../src/FileCat.App/ViewModels/MainViewModel.Settings.cs) · [Settings dialog](../../src/FileCat.App/Views/SettingsDialog.cs)

3. **Make reverse replacement a checked normal-session operation.** Add a result-returning `AppServices.TryReplaceNormalAppearance`. The current `void SaveSettings()` reports failures separately. Patch only the reviewed fields in the current normal JSON, preserving unrelated and unknown properties. Coordinate ordinary settings saves by resolved file and verify the reviewed file revision at commit. A stale review or another FileCat window with an older revision must reload before writing. Keep the candidate outside live `AppSettings` until commit succeeds so autosave cannot publish a canceled or failed import. Refresh or reopen the Settings draft after success. [AppServices.SaveSettings](../../src/FileCat.App/Services/AppServices.cs) · [Workspace autosave also saves settings](../../src/FileCat.App/ViewModels/MainViewModel.cs)

4. **Keep a rollback copy that survives autosave.** Before explicit replacement, retain a unique, byte-for-byte backup beside the normal file with equivalent access protection. Show its path. The ordinary rotating `.bak` is insufficient because a later save can rotate it again. Flush the staged candidate and require a filesystem-supported atomic replacement. If backup, revision check, or replacement fails, report the outcome and retain the original or its verified backup. Refuse an unsafe overwrite fallback. Offer **Undo appearance replacement** against the committed revision. If later edits exist, preview restoration of just the previous appearance fields. [Windows replacement and backup semantics](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)

## Accounts, profiles, and storage

Same-account UAC uses the normal window as the source. With alternate administrator credentials, the user copies from their normal window and the administrator explicitly accepts those values. Each window displays its own account and profile.

The normal window uses its already-resolved path, covering installed roaming settings, `profiles\<name>`, portable `Data`, portable fallback to roaming, and `--data`. The receiver never guesses another account’s roaming directory. If a same-profile launch would forward to the existing window, the existing `--new-instance` option opens a separate window. [AppPaths.Resolve](../../src/FileCat.Core/State/AppPaths.cs) · [SingleInstance.TryForward](../../src/FileCat.App/SingleInstance.cs)

Remembered transfers live in an administrator-only Windows registry key, scoped by the elevated account’s SID and the resolved profile path. An elevated launch reads the appearance and workspace from one record. Separately stored values from earlier FileCat versions remain readable. Portable installations keep these administrator preferences on the machine that received them. `--data` launches apply transfers for one session only. `--reset-layout` skips the remembered workspace for that launch. [Administrator registry storage](../../src/FileCat.Platform.Windows/AdministratorTransferRegistry.cs)

Closing an administrator window also saves its size, position, and maximized state in a separate registry value for that account and profile. `--reset-layout` opens with default window placement. Administrator launches with `--data` keep window placement for the current session only.

## Acceptance tests

- **Baseline preservation.** In a disposable normal profile matching the baseline presentation settings, record hashes of `settings.json` and its backup. Copy normal → elevated with remembrance off, verify `Psychedelic` is visible, exercise autosave and close, then relaunch elevated. The new elevated session starts with defaults. The transfer produces no change to either normal file.
- **Remembered startup.** Enable remembrance for appearance and the local-folder workspace. A fresh elevated session with the same account and profile loads the appearance and prepares the workspace again. Another profile starts with defaults. Verify that a normal token cannot read or write the administrator store, that forgetting restores defaults at the next launch, and that the ordinary settings, workspace, and their backups remain byte-identical.
- **Explicit copy back and rollback.** Export elevated changes, preview in normal, cancel, and trigger autosave. No proposed value reaches disk or live normal settings. Confirm replacement separately and verify that only the listed fields change, the retained backup matches the original bytes, and rollback succeeds. Repeat with a stale draft and a second normal instance.
- **Boundary and failure handling.** Exercise genuine medium/high Windows tokens, alternate credentials, named profiles, portable fallback, and `--data`. Cover invalid clipboard input, newer or corrupt settings, read-only storage, backup failure, and interrupted replacement. Retain the existing elevated persisted-tool and linked-state tests. Verify keyboard access and that Enter on the reverse review keeps the baseline. [Settings preservation tests](../../tests/FileCat.App.Tests/SettingsPreservationTests.cs) · [Elevated state tests](../../tests/FileCat.App.Tests/ElevatedStateDiagnosticsTests.cs)

### Windows x64 verification, 19 November 2026

The disposable VM's medium-integrity run passed 43 cases. A separate high-integrity run passed all four cases that need an administrator token. It verified session-only appearance, workspace apply and undo, remembered appearance and workspace on startup, and refusal of an administrator store with inherited permissions. The ordinary settings, workspace and backups in the preservation fixtures remained byte-identical. The medium TRX SHA-256 is `B43DC23C25FFCF2ABB03CF1DE038CDCDFEA2FEF607CA16D88829780201FFDF50`. The high-integrity xUnit XML SHA-256 is `F3BC9392D1C8CFD594636E7E50BFC001A04698BB20BB5604CE814CBBCD5E6DAE`.

## Effort estimate

Allow **4–6 engineering days** for one developer, including real Windows UAC testing. The forward session-only copy is the smaller part. Most of the effort belongs to making a deliberate reverse replacement recoverable and proving that a canceled review or stale autosave cannot overwrite the ordinary baseline.
