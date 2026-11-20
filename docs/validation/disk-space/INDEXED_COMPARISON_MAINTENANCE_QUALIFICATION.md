# Indexed private comparison maintenance — 2026-11-17

The indexed saved-comparison dialog now offers the existing guarded private-copy deletion review. Keep comparison has initial focus and remains the default; Delete comparison uses danger styling. Editing the filename away from the selected catalog entry disables its removal action. Maintenance remains available when all eight analysis places are occupied. Deleting a private copy invalidates its open view's saved marker and preserves its captured observations and source files.

Closing the window cancels the review and waits for the actual private owner to close. The shared review also receives the legacy view's lifetime cancellation. A successful removal is reported after the native store closes.

The [immutable r1165 evidence](source-manifest-r1165-indexed-comparison-maintenance.json) records five new native-filesystem/headless UI cases, original controllers, exact input/source hashes, raw XML/logs and 32 output artifacts per platform.

| Runtime | Focused maintenance | Broad rendering | Settings |
| --- | --- | --- | --- |
| Windows VM | 5 pass | 772 pass / 1 existing skip | 35 pass / 1 existing skip |
| Mac | 5 pass | 773 pass | 36 pass |
| Ubuntu VM | 5 pass | 773 pass | 36 pass |

Only the App and rendering-test DLL/PDB pairs changed. Every Core, native, settings-test and other dependency byte is the exact r1164 input; its passing Core/native phases are reused explicitly. The preparation helper checks the complete source manifests and refuses dependency or project changes before staging. Both refusal controls and the four-file input deltas are retained. This avoids retesting binaries that changed only because of generated build metadata.

All original r1165 controllers ended successfully, and all three post-run process inventories are empty. The earlier unidentified Windows PID remains preserved in the parent checkpoint with its later empty inventory; it was never stopped by this task.

This completes the scoped indexed private-copy maintenance route. It does not close the full resource/scale, physical input, accessibility, package, capability or remaining W0–W8 gates. Public registration stays off. The final independent UX/UI/functional/security review remains required.
