# I591 — saved column profiles were absent from View

## User consequence and distinct scope

In an isolated Mac FileCat state at product `55242553`, **Tools → Settings → Columns** created a sixth profile named `Audit` and listed it as `Alt+5   Audit`. OK saved it to `settings.json`, SHA-256 `d80ef88bcde57ec902cc8580edb72fa8ee44664aff7a36b7505b78b60f0ac644`. **View** nevertheless showed only the five built-in profiles, even after the settings dialog closed. The user could use a shortcut or command search, but the menu that lists all visible profiles omitted the newly created one. This is a discoverability gap in the save → menu handoff, separate from the accepted built-in Media profile and Settings rollback tests. It did not change files or claim the custom column data itself was wrong.

The owned folder had one 20-byte `record.bin`, SHA-256 `918d7973825afd873f01f412a0e69316c35781070123048192bf46ac8d1cb629`; the sibling target was empty. The before-state native View menu exposed Details, Brief, Full, Media and Links, with no Audit entry. A headless UI test constructed a sixth saved profile, refreshed the menu and **failed 0P/1F** on the old source because no `view.columns5` item existed.

## Correction and bounded verification

Product `984b6c92` includes all ten supported profile slots in the View menu model, omits slots beyond the saved count when building the menu, and names an existing profile immediately. The focused Windows Release `CustomColumnProfileMenuTests` control passes **1/1** after the correction, requiring the sixth saved profile by ID/name and the absent seventh slot.

A clean exact-source Mac checkout built the local `1.0.0-exploratory` package ZIP, SHA-256 `304166753bd73212d50ca359ce58937f1f13e8291229815e28204d742a2c5dbc`; staged App DLL SHA-256 `4e7be712a41ebf633cf53cb8c2901c43a39445c2704072ab514154a27dc7537d`. The extracted bundle passed strict deep code-signature verification. Reopening the same isolated profile in the rebuilt app showed **View → Columns: Audit** beside the five defaults. Choosing it reported **Columns: Audit (Alt+5)**, with the owned row still visible and status `0 folders, 1 file · 20 bytes`. Independent rehash matched the source and settings before oracles; the target stayed empty. Both exact test processes were stopped. This proves one saved custom-profile discovery/use path, not every column configuration or installed-candidate persistence case.

## Disposition and finite candidate rule

Medium UX discoverability issue, **remediated preliminarily**. On the exact installed 1.0.0 candidate, create and save a sixth custom profile in an isolated user state, restart the app, require its name and shortcut in View and require no nonexistent seventh profile. Apply it to an owned folder and verify the selected profile, row identity and unchanged file bytes. Stop after these affected routes and latest-product five-lane CI with skip reconciliation. Public preview.5 predates this fix; stable 1.0.0 remains **NO-GO** until candidate gates and explicit owner GO. The active exploratory clean streak resets to **0/20**.
