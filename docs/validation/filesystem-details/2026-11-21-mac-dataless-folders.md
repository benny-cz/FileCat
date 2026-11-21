# Dataless macOS folders and packages — 21 November 2026

This receipt closes the last F-PARSER item and the macOS part of N-PROVIDER: a folder and a package that a File Provider keeps dataless. The earlier live runs covered dataless files only, because the fixture's folder had been listed and was no longer dataless ([native rows receipt](2026-11-21-native-rows.md#live-file-provider-rerun)).

## Fixture

The prepared File Provider fixture on the Mac (`/Applications/FileCatProviderFixture.app`, domain `fixture-12760a2e-d`) was upgraded in place to version 2. Its sources and the upgrade script are in [`eng/validation/mac-file-provider-fixture`](../../../eng/validation/mac-file-provider-fixture/).

- **What version 2 adds:** two containers that nothing lists, `unvisited-folder` (a folder) and `unvisited.bundle` (a package, content type `com.apple.bundle`). The provider reports them through a sync-anchor change. It keeps the version 1 items, and its packages are no longer flattened.
- **Upgrade:** the script made a byte-exact backup of version 1, then installed the new provider and manager with ad hoc signatures, under the same bundle identifiers. A working-set signal followed. The domain stayed enabled. The system asked for changes from anchor 1 and created both containers as `uchg,compressed,dataless` directories. The provider logged no enumeration of either one. See the [upgrade log](receipts/2026-11-21-mac-dataless-folders/provider-v2-install.log).

## Result

**Environment:** macOS 27.2.1 (26B131), Apple silicon, .NET SDK 10.0.101. Release build of commit `53476fd01` on the Mac; test DLL `feb7784a…`, Core DLL `6c4a0bf9…`.

`Live_Mac_File_Provider_metadata_keeps_unvisited_folders_and_packages_dataless` passed 1/1, with no skips ([TRX](receipts/2026-11-21-mac-dataless-folders/dataless-dirs.trx), `281a6645…`). For the folder and for the package:

- Summary and the metadata-only full report: a directory with a known, stable identity and the `SF_DATALESS` flag. Both carry the dataless note.
- The full report holds only the scalar getattrlist section; no volume sections are added for a dataless item.
- The names-only supplementary inventory issued no value-bearing query.
- The caller's materialization policy was restored after every request.
- Both containers were still dataless afterwards ([run log](receipts/2026-11-21-mac-dataless-folders/run.log)).
- The provider logged nothing during the run: no enumeration and no fetch.

The two earlier live tests passed again against the upgraded domain, 2/2 ([TRX](receipts/2026-11-21-mac-dataless-folders/prepared.trx), `62e8eb11…`). These are the dataless root file and nested child, and the listed folder.

## Scope

This is evidence for this fixture provider and the metadata-only route. It is not a promise that another provider's metadata queries never cause network activity. FileCat has no package-specific details logic: a dataless package is reported as a dataless directory, and nothing claims it is a package.
