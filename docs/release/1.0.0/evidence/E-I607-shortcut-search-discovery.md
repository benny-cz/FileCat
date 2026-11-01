# I607 — shortcut editing was absent from Search commands

**2026-11-01 · Medium UX · preliminary remediation at `5c0a973092f27774477d0dd3f692d65853b9e1f7` · final candidate open.**

## User consequence and scope

The owner searched for “change shortcut” and found no command. The existing editor was reachable through F1 → Keyboard reference → select the target command → Change shortcut, but Search commands had no matching synonym or useful direct result. The owner also tried `Ctrl+Alt+H` after an earlier isolated exploratory shot. That chord was assigned only temporarily in the disposable VM to **View as hex / alternate viewer** and then reset; its default is `Alt+F3`. No `Ctrl+Alt+H` binding was shipped or added by this correction. The Ctrl+Alt+letter family is avoided for defaults because it can act as AltGr on European keyboards.

## Reproduction and correction

On the isolated Windows 11 Insider VM, an exact `change shortcut` query in the previous `98b48511` product build showed “No command matches” and only the generic Keyboard reference fallback. Capture `.tmp/i602-vm/shortcut-query-before.png` SHA-256 `47496686B41FD7FAA20EA8AA35B891B21DFF49029AF5949DB613D1791D71BFBC`.

Product `5c0a9730` adds `change shortcut`, `edit shortcut` and `remap key` to the existing Keyboard reference command's search terms. Its result description tells the user to find the target command there and choose Change shortcut. While that result is selected, the search hint says Enter opens Keyboard reference, avoiding the generic F2 hint that would edit the reference command's own key.

The source-matched App DLL SHA-256 `86D1FC34F38EDDAD36B1DB69AA0F5D7D0A49922C380A54215EF2956629CE4ECA` and Core DLL SHA-256 `BB07B53DE78D3859905195049CE8E918A0F4CFF33887B5CFADE64F7CB4EC4195` were overlaid on that disposable VM's isolated self-contained app. The exact query then selected **Keyboard reference** with the explicit next-step hint; Enter opened its searchable list and visible **Change shortcut…** button. After capture `.tmp/i602-vm/shortcut-query-after.png` SHA-256 `029C5660A1421B28C6406015404099854BFC7D682DEB403F0DD20A28B12CD42A`; opened reference capture `.tmp/i602-vm/shortcut-query-opened.png` SHA-256 `512D39C8BE4101BAFFC42935A70CF78C5F6A06BC9EE3EB7088B450F58A81EF25`. The earlier same-VM exploratory step had already verified choosing a target command, assigning a temporary key, invoking it, and restoring the original key with a byte-identical settings file. That passing editor behavior was not reopened beyond the newly failed discovery route.

## Adjacent command coverage

The actual command search takes its entries from the command registry. It excludes only platform-inapplicable commands, numbered bookmark bindings handled by the bookmark list, and column-profile slots without a saved profile. A focused App test enumerated every applicable main-menu command and confirmed that the search contains exactly one entry for it and finds it by its displayed name. The native Keyboard reference showed 166 of 166 available commands in this VM. Existing and new focused command-search controls passed **31/31**, including exact `change shortcut` and `edit shortcut` ranking and opening the reference by Enter. This is menu/registry coverage, not a claim that every possible colloquial phrase is indexed.

## Stopping rule

Preliminary remediation requires the exact-query native result and open reference, plus focused command-search coverage; these passed. Final 1.0.0 acceptance additionally requires the same query and a target-command key edit in the frozen installed candidate, plus its source CI producer, Windows x64, Windows ARM64, Ubuntu and macOS lanes with skip reconciliation. Public unsigned preview.5 predates this change. Stable 1.0.0 remains NO-GO.
