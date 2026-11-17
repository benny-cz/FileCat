# FileCat Windows settings transfer plan

Add **Copy appearance between sessions…** to Settings on Windows. The default direction is normal → administrator. The user copies a small set of display preferences from the ordinary profile and applies them to the current elevated session. The next elevated launch starts with FileCat’s defaults. The ordinary profile remains the baseline. Only a separate, confirmed action in a normal window can change it.

Use a disposable profile with schema 1, the `Psychedelic` theme, compact rows, and list and content font sizes of 13 for acceptance tests. The usual installed profile stores its settings under `%APPDATA%\FileCat\settings.json`.

## User experience

| Where | Action | Result |
|---|---|---|
| Normal Settings | **Copy saved appearance for administrator** | Copies the active profile’s saved display preferences. Shows the source profile and path. An open Settings draft stays separate. |
| Administrator Settings | **Use copied appearance for this session** | Shows the incoming changes and applies them to the running UI. The success message says “Appearance applied to this administrator session. The next administrator launch starts with defaults.” Closing FileCat rolls it back. |
| Administrator Settings, under **Copy back…** | **Copy this session’s appearance** | Copies the current display preferences, including changes made since import. The administrator process performs no profile write. |
| Normal Settings, under **Copy back…** | **Review administrator appearance** | Shows old and proposed values and the exact destination `settings.json`. **Keep my settings** has default focus. Only **Replace these appearance settings** commits. Opening the review, pressing Escape, or closing it leaves both live and saved normal settings alone. |

Use an explicitly copied, versioned plain-text value. Keep it available after the source window closes, and allow manual entry when the two desktops do not share a clipboard. The same transport works in both directions without introducing an elevated settings store, helper, or startup import.

## Transfer scope and security decisions

Use the same allowlist in both directions. Review each new `AppSettings` property before admitting it.

| Category | Decision |
|---|---|
| Appearance | Include built-in theme, theme animations, row density, list and content font sizes, built-in date formats, toolbar visibility and style, function-key bar, Places row visibility and compactness, and command-line visibility. |
| Other presentation data | Exclude custom font families and format strings, pinned destinations, and column profiles. Some columns request metadata as files are listed. |
| Commands | Exclude editor, viewer, diff tool, terminal executable or shell, user commands, associations, arguments, hotkeys, and key bindings. Showing the command line transfers only its visibility flag. |
| Startup and operations | Exclude single-instance behavior, update checks, saved searches, workspace restoration, shell previews, verification, delete confirmations, and diagnostics. |
| Connections and credentials | Exclude remote profiles, hosts, usernames, key paths, credential references, passwords, trust decisions, and keys. The transfer never queries Windows Credential Manager. |
| Files outside settings | Keep workspace, history, journals, recovery records, caches, diagnostics, and the privileged helper’s exchange directory outside the feature. |

Validate on receipt even if FileCat produced the value. Accept only the supported version, named fields, built-in choices, booleans, and finite sizes within the Settings UI’s ranges. Reject malformed, duplicate, unknown, or oversized input as a unit. Read only bounded plain text on an explicit paste action. The payload needs no paths or account identifiers. [AppSettings](../../src/FileCat.Core/State/StateModels.cs) · [Remote profiles and secret references](../../src/FileCat.Core/State/Secrets.cs) · [Windows clipboard guidance](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata)

## Implementation

1. **Define the transfer format.** Add `AppearanceTransferV1` alongside `AppSettings` in Core, with explicit export, validation, diff, and field-by-field apply. The ordinary exporter reads only the current `AppServices.Paths.SettingsFile`, using a bounded, side-effect-free read. A missing, damaged, or newer primary gets a clear message instead of silently exporting defaults or a backup. Keep the existing high-integrity guards in `JsonFileStore.Load` and `SaveCore` intact. [JsonFileStore](../../src/FileCat.Core/State/JsonFileStore.cs)

2. **Separate visual refresh from saving.** Extract the appearance refresh from `MainViewModel.ApplySettings()`. Keep the existing Settings OK behavior. The elevated action applies validated fields in memory and refreshes theme, density, bars, and Places directly. Content font size takes effect for newly opened viewers and comparisons, matching current behavior. In `SettingsDialog.ShowAsync`, expose the transfer action beside the elevated read-only notice. Leave the ordinary read-only handling for newer or unreadable profiles intact. [ApplySettings](../../src/FileCat.App/ViewModels/MainViewModel.Settings.cs) · [Settings dialog](../../src/FileCat.App/Views/SettingsDialog.cs)

3. **Make reverse replacement a checked normal-session operation.** Add a result-returning `AppServices.TryReplaceNormalAppearance`. The current `void SaveSettings()` reports failures separately. Patch only the reviewed fields in the current normal JSON, preserving unrelated and unknown properties. Coordinate ordinary settings saves by resolved file and verify the reviewed file revision at commit. A stale review or another FileCat window with an older revision must reload before writing. Keep the candidate outside live `AppSettings` until commit succeeds so autosave cannot publish a canceled or failed import. Refresh or reopen the Settings draft after success. [AppServices.SaveSettings](../../src/FileCat.App/Services/AppServices.cs) · [Workspace autosave also saves settings](../../src/FileCat.App/ViewModels/MainViewModel.cs)

4. **Keep a rollback copy that survives autosave.** Before explicit replacement, retain a unique, byte-for-byte backup beside the normal file with equivalent access protection. Show its path. The ordinary rotating `.bak` is insufficient because a later save can rotate it again. Flush the staged candidate and require a filesystem-supported atomic replacement. If backup, revision check, or replacement fails, report the outcome and retain the original or its verified backup. Refuse an unsafe overwrite fallback. Offer **Undo appearance replacement** against the committed revision. If later edits exist, preview restoration of just the previous appearance fields. [Windows replacement and backup semantics](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)

## Accounts, profiles, and storage

Same-account UAC uses the normal window as the source. With alternate administrator credentials, the user copies from their normal window and the administrator explicitly accepts those values. Each window displays its own account and profile.

The normal window uses its already-resolved path, covering installed roaming settings, `profiles\<name>`, portable `Data`, portable fallback to roaming, and `--data`. The receiver never guesses another account’s roaming directory. If a same-profile launch would forward to the existing window, the existing `--new-instance` option opens a separate window. [AppPaths.Resolve](../../src/FileCat.Core/State/AppPaths.cs) · [SingleInstance.TryForward](../../src/FileCat.App/SingleInstance.cs)

## Acceptance tests

- **Baseline preservation.** In a disposable normal profile matching the baseline presentation settings, record hashes of `settings.json` and its backup. Copy normal → elevated, verify `Psychedelic` is visible, exercise autosave and close, then relaunch elevated. The new elevated session starts with defaults. The transfer produces no change to either normal file.
- **Explicit copy back and rollback.** Export elevated changes, preview in normal, cancel, and trigger autosave. No proposed value reaches disk or live normal settings. Confirm replacement separately and verify that only the listed fields change, the retained backup matches the original bytes, and rollback succeeds. Repeat with a stale draft and a second normal instance.
- **Boundary and failure handling.** Exercise genuine medium/high Windows tokens, alternate credentials, named profiles, portable fallback, and `--data`. Cover invalid clipboard input, newer or corrupt settings, read-only storage, backup failure, and interrupted replacement. Retain the existing elevated persisted-tool and linked-state tests. Verify keyboard access and that Enter on the reverse review keeps the baseline. [Settings preservation tests](../../tests/FileCat.App.Tests/SettingsPreservationTests.cs) · [Elevated state tests](../../tests/FileCat.App.Tests/ElevatedStateDiagnosticsTests.cs)

## Effort estimate

Allow **4–6 engineering days** for one developer, including real Windows UAC testing. The forward session-only copy is the smaller part. Most of the effort belongs to making a deliberate reverse replacement recoverable and proving that a canceled review or stale autosave cannot overwrite the ordinary baseline.
