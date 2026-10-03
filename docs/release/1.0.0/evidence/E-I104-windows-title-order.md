# E-I104 — FileCat first in Windows window titles

Preliminary formatting correction `a50b3b885b0685b884f5dab989855216dfa4e220`, 2026-10-03.
The owner requests application name, selected location, then account/elevation. A directory named FileCat may
legitimately repeat the word; this correction retains the selected name.

Before the change, `ProcessAccount.WindowTitle` puts the location first and prefixes elevated Windows titles with
`Administrator:`. The Windows branch now produces `FileCat — Downloads — marek (elevated)`,
`FileCat — Downloads — marek (administrator, not elevated)`, or `FileCat — Downloads — marek (standard user)`.
No-location titles already start with FileCat. Unix title order and runtime account/privilege detection are unchanged.

Existing account-title assertions updated to the requested ordering fail against baseline b5ce744: one failure,
one passing control. After correction both pass. The existing headless window-title case also passes, checking
directory/tab/account propagation. This does not claim a live desktop check or validation of the owner's reported
elevation detection. Exact candidate interaction remains pending. The first CI run at a50b3b8 fails solely in the
unrelated synthetic lease helper (E-I108); its Core and App suites pass. Successor 6cad380 CI 37119313116 passes
all four lanes, and its direct Windows inventory independently confirms both account and the window-title cases
pass. The title implementation is unchanged between those commits; exact candidate interaction remains required.

Raw original sources, baseline audit, working input hashes, logs and direct TRX are retained under
`artifacts/release-evidence/i104-title-order-20261003`. The independent local inventory is retained under
`ci-reliability-20261003/independent-local.json` and also includes the CI repair's synthetic results.

| Evidence | SHA-256 |
|---|---|
| original title audit | `7997d9dfcc1d867e47067bae9e3c49dc9b94696c4d3f012775890c4a6f66f29d` |
| baseline account-title TRX | `2f304e727490e99567b305a6f654ec920ba9d9a3fc217defcb811d10ea988570` |
| corrected account-title TRX | `3156ed24b6ab5fecbff6129d42861c929067f69c1ec2fc0f4102d7b7dbd131e5` |
| headless window-title TRX | `5e4feab574fcbb54b6de9e2a79aae3001081d434b3978fbb863a9e8f6071f91f` |
| independent direct local inventories | `27a5a63d319cbab8392176612db27f783b44f9ce11ced8c16a061f12aca1be28` |
