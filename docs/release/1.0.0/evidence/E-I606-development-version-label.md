# I606 — development About version used an obsolete preview label

**Disposition:** remediated preliminarily at product commit `98b4851182d2226e5eb82f658a0bd1c5b8e3b85b`; latest-source five-lane CI and the final installed-candidate version check remain separate. Public preview.5 is unchanged and correctly stamped.

## Consequence and cause

The owner saw `0.1.0-preview` in **Help → About FileCat** while the current public release was `v1.0.0-preview.5`. A native Cyberpunk Windows VM reproduction on a direct source build showed `$ filecat --version 0.1.0-preview (5597db2)` in About. `Directory.Build.props` still supplies `0.1.0-preview` as the internal default; About read the App assembly's informational version, and the local source build inherited that obsolete label. The explicit **Check for updates** route also used the same old version and could present a public preview as a newer build despite newer development source.

This did **not** prove the published preview.5 was mislabeled. Its independently verified hosted Windows x64 portable ZIP returned `FileCat 1.0.0-preview.5` from `--version`; its manifest and server SHA-256 were reconciled in [the preview.5 tag record](E-PREVIEW5-TAG-20261031.md). Tagged package producers pass the tag version explicitly.

## Correction and checks

- Direct `FileCat.App` source builds now carry informational version `1.0.0-dev` plus the source commit. Internal `0.1.0-preview` project/package identities and existing locked dependency graphs are left intact. An explicit tagged producer version bypasses the development override; MSBuild property checks returned `1.0.0-dev` at the ordinary source build and an empty informational override with `Version=1.0.0-preview.5`, letting the SDK use the supplied tag.
- Local Linux, macOS and Windows publish defaults and non-tag Linux/macOS manual CI package names now say `1.0.0-dev` instead of `0.1.0-*`. Tagged package branches still derive their version from the immutable tag.
- The preview update interpreter identifies a `dev` pre-release segment. It reports the latest published release as information without calling it a newer upgrade or calling the source build itself the latest release. A focused result test includes a draft preview, a published preview and `1.0.0-dev`.
- Release App build passed with 0 warnings/errors; focused About/UpdateCheck tests passed **5/5**. The rebuilt App DLL SHA-256 is `B37511B3ED04F24C51C50DAAAF763EE48942F11AC465DCA061D501932B8289DA`, its informational attribute is `1.0.0-dev+98b4851182d2226e5eb82f658a0bd1c5b8e3b85b`, and `dotnet FileCat.dll --version` returned `FileCat 1.0.0-dev`.
- In the isolated Windows 11 Insider VM, that exact DLL replaced only the stopped app's DLL. Cyberpunk **About** then displayed `$ filecat --version 1.0.0-dev (98b4851)`, with the other dialog content still readable. The VM is offline: its native update request reported `offline or blocked`, so the development-release wording has focused interpreter evidence, not a live GitHub UI receipt. The owned 22-byte source fixture retained SHA-256 `2E4D7623E27757AD2CD4E0C43DA7CD8B8943E56B3CF66F3731F0DCFE152A413E`; the separate target had zero entries.

Ignored native captures are `.tmp/i602-vm/about-old-dialog.png` (SHA-256 `9C499759B05F8DBF6182C85603CB6D6BC9B68EAFBCF013CDBAF9937400B02A85`) and `about-new-dialog.png` (`AF8DFA14CA554CF9B46DD1E8EC5160D32786C82543F51ACA29050B4CDE48A053`). These captures establish the direct-source-build before/after, not a final installed candidate.

## Finite acceptance and stop

The first pushed-head run [41332440939](https://github.com/benny-cz/FileCat/actions/runs/41332440939) failed **before platform jobs** in producer release-table validation: the new I606 row and 956th evidence entry were added while the issue/evidence index headers still stated 605/955. Local `python eng/validation/validate-release-issue-register.py` reproduced the sequential header failures. Correcting both headers made that validator pass with **606 IDs / 585 preliminary / two closed / 19 unresolved, 956 unique evidence entries and 82 acceptance cases**. The failed run is not product or package qualification; a fresh five-lane run is required.

1. Latest product source passes producer, Windows x64, Windows ARM64, Ubuntu and macOS CI with tag-only skips reconciled.
2. An ordinary direct source build reports `1.0.0-dev` with its commit in About and `--version`; a package built with an explicit release tag reports that exact tag in both places. The already verified preview.5 `--version` is the tag positive control; the final installed 1.0.0 candidate remains a separate future artifact.
3. Development update checks identify the latest published release without an upgrade claim. The offline native VM result does not substitute for the deterministic interpreter check; repeat live network UI only if a candidate or owner report gives a consequential reason.

Stop this issue after the latest-source CI result is recorded and the final candidate's established version gate is run on its exact installed bytes. Do not reopen public preview.5 or alter its immutable tag for a development-label defect.
