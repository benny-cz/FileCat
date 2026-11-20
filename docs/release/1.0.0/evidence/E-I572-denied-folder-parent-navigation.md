# I572 — leave a denied folder through its parent row

## Finding and cause

On Windows, the owner entered `E:\System Volume Information`. Its listing was denied, as expected, but double-clicking or pressing Enter on `..` left the panel in that folder. Back still worked. The panel had a focused `EntryKind.Parent` row; `MainViewModel.OpenFocused` handled `ListingState.Failed` by retrying the denied location before checking that row. This was a navigation trap, not evidence of access to protected content.

## Correction and verification

Product commit `0715f5c9` handles a focused parent row before the failed-listing retry. `GoUp()` uses the provider's parent location. The remaining failed-listing path keeps its prior retry/credential behavior when no parent row exists.

An owned provider regression reproduced the pre-fix failure: after a denied enumeration, Open left `Location.Path == "blocked"` instead of navigating to `"parent"`. At the correction, Open (the list double-click command), Enter, and Open with System each leave the denied location. A separate denied root with no parent row still retries and remains at that root. Release configuration `DeniedParentNavigationTests|NavigationIntentTests|FileListSmokeTests` passed **15/15** locally. The test did not inspect or modify System Volume Information.

## 1.0.0 stopping rule

In the exact installed Windows 1.0.0 candidate, enter `E:\System Volume Information` or an equivalent denied folder with a visible `..` row. Double-click `..`, re-enter and press Enter on `..`; both must reach the parent without another denied-location retry. Back must still navigate out. A denied location without a parent row must retain its retry path; UNC credential behavior must remain intact. Record native input and location observations, then reconcile all five required CI lanes and skips at the latest product source. Until those checks and the signed installed candidate pass, this finding is remediated preliminarily and stable 1.0.0 remains NO-GO.
