# Cloud Files metadata and storage review

The Windows 11 x64 VM ran two opt-in Cloud Files cases at medium integrity (RID 8192). FileCat registered an owned temporary sync root and connected a test provider with a `FETCH_DATA` callback counter. The provider created an in-sync 5,000-byte placeholder with zero bytes resident. The fixture removed its placeholder and unregistered its provider on completion.

The canonical file-system details summary recognized the file and its Cloud Files tag without reading a link target. A separate metadata-only full request kept the same object identity and provider state, produced a fresh observation revision, and omitted data and layout collectors for the placeholder. The compression and EFS candidates refused review at the registered sync-root ancestor before creating storage evidence. The provider received zero data requests across these routes. The subsequent legacy record control also observed zero resident bytes and no data requests.

The folder case separately verified that the registered sync root and placeholder folder retained directory semantics with the existing file, icon and folder operations. Both cases passed with zero failures or skips. The exact result is in `.tmp/filecat-cloud-detail-validation-tests.xml`. This covers the prepared Windows provider. macOS dataless and other provider cases need their own fixtures.
