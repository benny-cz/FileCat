# Cloud Files metadata and storage review

The Windows 11 x64 VM ran two opt-in Cloud Files cases at medium integrity (RID 8192). FileCat registered an owned temporary sync root and connected a test provider with a `FETCH_DATA` callback counter. The provider created an in-sync 5,000-byte placeholder with zero bytes resident. The fixture removed its placeholder and unregistered its provider on completion.

The canonical file-system details summary recognized the file and its Cloud Files tag without reading a link target. A separate metadata-only full request kept the same object identity and provider state, produced a fresh observation revision, and omitted data and layout collectors for the placeholder. The compression and EFS candidates refused review at the registered sync-root ancestor before creating storage evidence. The provider received zero data requests across these routes. The subsequent legacy record control also observed zero resident bytes and no data requests.

The folder case separately verified that the registered sync root and placeholder folder retained directory semantics with the existing file, icon and folder operations. Both cases passed with zero failures or skips. The exact result is in `.tmp/filecat-cloud-detail-validation-tests.xml`. This covers the prepared Windows provider. macOS dataless and other provider cases need their own fixtures.

The archive target probe reported the cloud-folder refusal on both the registered root and its placeholder folder. The separate junction and ordinary-directory controls passed without using a machine's OneDrive folder. This moves the sync-root assertion to the fixture that actually registers the provider. See `.tmp/filecat-owned-cloud-archive.log` for the Windows x64 results.

## No-fetch legacy sparse and supplementary-data checks

A Windows 11 x64 VSTest rerun reported one fetch while reading the prepared sparse placeholder through the legacy inspector. Counters after placeholder creation, the canonical summary and full reports, and the storage reviews were all zero. Removing the legacy supplementary-data collector alone still produced one fetch. The legacy sparse-range probe separately opened the placeholder with `FILE_READ_DATA`.

For recognized Cloud Files placeholders and items carrying offline or recall-on-data-access evidence, the legacy report now omits both the sparse-range query and the supplementary-data collector. The report retains the sparse attribute and identifies the omitted queries. The fixture recorded zero fetches through setup, details, both storage reviews, legacy inspection, and its final control. It retained the 5,000-byte logical size, zero resident bytes and the provider's in-sync observation.

All five Windows x64 cases passed. They included the placeholder folder, an ordinary sparse-file range report, an ordinary named-stream report with its excerpts, and the canonical metadata-only report. The receipt `cloud-tag-native-guard.trx` has SHA-256 `3D4835CDB2E11CF763382AAED05A5E2DAF8738F8D899AB23AD35A8DC83DED358`. The native ARM64 details workflow now includes both Cloud Files cases.

The native ARM64 run at `c40fc92be81c0809242789ff0b81e0d435adba30` passed both Cloud Files cases and all 16 other selected details cases. The fixture checked compression and EFS review refusal alongside the summary, full and legacy read paths. The provider received zero fetches. The SHA-256 of `native-file-details.trx` is `3F79CD71359EB59EF16E1F426946F27E632A8A56853349C5F2C3971DF14B94FF`. [Native ARM64 qualification](https://github.com/benny-cz/FileCat/actions/runs/44608233723)
