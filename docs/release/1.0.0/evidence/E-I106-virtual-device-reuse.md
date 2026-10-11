# I106 — bounded virtual disk-number reuse with a held Windows source

2026-10-11 CEST. **Preliminary virtual direct-reader check; I106/I110 physical-source validation remains on HOLD.** The disposable Windows 11 VMware guest initially had only boot disk 0. Two owned 64 MiB read-only file-backed VHDs mounted sequentially as disk 1 with distinct native UniqueIds; after each case, only disk 0 remained. The first independent number reader rejected a Windows unsigned/signed exit-code mismatch, and a fresh reader verified the unchanged raw result and absence of the owned guest files. This establishes a reproducible *virtual* number-reuse control, not physical USB behavior.

The next native control opened VHD A with a `GENERIC_READ` handle, dismounted A while that handle stayed open, and mounted distinct VHD B at the same `PhysicalDrive1` path. A held-handle `IOCTL_STORAGE_GET_DEVICE_NUMBER` query then failed with native error 55. Its first launcher failed before opening the handle because PowerShell interpreted the read-access constant as signed; the corrected run retains that original failure. The guest inventory and owned files were restored.

Finally, a separate compiled probe called the current `DirectDeviceSource` product code from exact clean `5508a790a2374d59736818d0c5817f2d7c41a612` source. Against VHD A, destination admission for the guest's disk-0 test directory returned **separate**, and a 512-byte read returned the independently verified zero-byte hash. After A detached and B reused number 1, the *same held FileCat reader* returned **unknown** for destination admission and its next read threw `IOException` (device unavailable). The `RecoveryProvider` code treats unknown admission as refusal; this control did not run an end-to-end recovery job or the helper route. The child exited successfully; the independent reader rehashed all 21 staged payload members, source/probe/build inputs and native result, verified raw-handle precursors and checked fresh guest cleanup. The temporary build retained an unrelated `WindowsBase` version-conflict warning with exit 0; the first payload packer stopped on a nested runtime directory, and its partial archive remains retained. Neither setup failure was promoted to a product observation.

This bounds only this VM's read-only VHD detachment/reuse sequence and the direct held-reader path. Abrupt physical removal, real source-entry/number reuse, volume and helper readers, filesystem/topology races, actual recovered-file publication, reference hardware and the installed candidate remain unqualified. The historical physical-source hash/capture attribution problem still blocks further FileCat physical USB source testing; disposable G: authorization does not waive the hold. No product change, contract freeze, candidate or release GO is claimed.

## Selected immutable receipts

Paths are relative to the private `FileCatReleaseEvidence` root. Each proof pins raw commands, identities, hashes, failures and guest restoration.

| File | SHA-256 |
|---|---|
| `i106-vdisk-number-20261011-v1/independent-virtual-number-v2.json` | `463ffe2787ae14cff84c3258c2c524a9c2cd6a4b25dca3f52b8a94826c7eb737` |
| `i106-vdisk-held-20261011-v2/independent-held-virtual-v1.json` | `5e0eba27e7f7c5950e73b9e07c95e69c7cc051a9197877d41f01d56edd87f078` |
| `i106-product-held-20261011-v1/independent-product-held-v1.json` | `c3ac7e42887e209272b80fb6535daea96d2bb12ee9ed090ec810c64e2ba31217` |
