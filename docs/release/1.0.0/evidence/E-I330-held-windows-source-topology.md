# E-I330 — Windows recovery held-source topology

2026-10-11 CEST. **Potential Critical, deleted-data safety (I106/V09), remediated preliminarily.** Canonical baseline **12a80396e0bde055f35f7a4ec60aa9b0ca386d15 /1512 raw Git blobs**, with declared test/product overlays.

An opened Windows direct/helper reader holds one device, but recovery destination admission classified the selected path again. A changed path could therefore describe a different source. Five unsafe controlled cases reproduce: overlap, unknown source, identity becoming overlapping/unknown after an earlier healthy check, and a closed reader. The first four actually write all 10,000 recovered bytes under the wrong admission; the closed reader writes 10,000 warning-qualified zero bytes. The healthy separated case preserves all original recovered bytes. These are controlled helper answers and real recovery jobs over an owned regular-file FAT image, **not a reproduced kernel device replacement**.

Once a source is open, its **IDeviceDestinationGuard** now takes precedence. Direct readers query their held handle; helper readers ask the existing authenticated session for bounded source disk numbers. The new query accepts no path, opens no additional source and has no write operation. It rechecks at each destination admission; unknown, closed, truncated, invalid or unsupported answers refuse copying. At most 32 complete volume extents/disk numbers are accepted; complete native returned lengths and extent fields are checked before classification. Legacy helpers that stop on an unknown operation fail closed.

The same eight baseline controls, including [I331](E-I331-failed-pipe-disposal.md), produce **seven failures/one healthy pass**, both locally and in the elevated Windows guest. The corrected batch passes **28 local controls** and **28 each under native guest medium/high parents**, **56 fixed native passes**. Parent/child integrity RIDs 8192/12288 and session identities are measured. Five unsafe copies now produce zero bytes/no output; the preserved marker/source and full 10,000-byte healthy output hashes verify. Real held-volume metadata is queried with **access zero**; helper queries over an owned regular file remain unknown and following reads return exact bytes.

All **992 predecessor outcomes/messages and 25 exact skips** remain: Core 928 passes/24 skips and Windows helper 39 passes/one skip. Independent postchecks rehash **156 staged/579 runtime files** across three native phases and find no owned payload process or temporary tree. The guest console stays hidden/minimized; no workstation UI, physical source reader, native authorization dialog, policy/account/runtime installation or persistent configuration changes.

Original attempts remain: v1 baseline has eight failures because its healthy assertion also required the newly introduced query; a fresh v2 removes that implementation assertion while preserving all byte/copy/safety checks. V1 fixed has 23 passes/five malformed-reply failures because InvalidDataException escaped its filter. V2 catches it and passes every control. No original failure is promoted.

Windows disk numbers are not persistent across removal/restart. Physical device-number reuse, atomic source/destination topology races, actual removed/spanned device cases, direct raw-device read qualification, broader visibility and installed candidate remain. The I106/I110 physical-source HOLD and all owner/freeze/publication decisions remain in force; no release acceptance is claimed.

Native query contracts: [Microsoft device-number IOCTL](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_get_device_number), [volume extents and returned lengths](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddvol/ni-ntddvol-ioctl_volume_get_volume_disk_extents).

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, commands, raw failures/skips, observed bytes and independent cleanup.

| File | SHA256 |
|---|---|
| `i330-i331-held-windows-source-20261011-v1/independent-remediation-v1.json` | `8fb1feaff14c8b5eb6f80d00f8d186d912e83a8fec827a981a060dd095403b9b` |
| `i330-i331-held-windows-source-20261011-v1/retained-tool-sources-v1.json` | `3065f525dffdedd4aa45929fbcdc4f2b8251994201e163724df5a98f1c1e4b04` |
