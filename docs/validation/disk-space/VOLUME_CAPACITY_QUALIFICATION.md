# Held-volume capacity and first preview

Standard analysis shows live filesystem capacity in a separate ribbon. The sample comes from the held source and records its measurement time, volume identifier, reported capacity, used and free space, and availability to the caller. A folder result explains that its file totals cover only the scanned scope. APFS samples are shown individually with a shared-pool warning because the pool identity is unavailable. An offline saved or imported inventory supplies no live capacity.

A finished analysis of one verified filesystem root can show the signed difference between reported used space and known observed allocation. It is available in the unfiltered top-level On disk folder view on one measurement basis. The explanation keeps possible causes separate from the measured values. The preview and its live recorded-result tab use the same captured samples. Fast NTFS and unsupported backends do not supply samples in this slice.

| Runtime | Focused r1565 | Native compact folder workflow | Dedicated whole-volume fixture |
|---|---:|---|---|
| Windows VM | 30 passed, one Unix alias case skipped | r1566 passed | r1567, NTFS VHD, one passed |
| Mac | 31 passed | r1566 passed, including Retina compositor capture | r1568, HFS+ disk image, one passed |
| Ubuntu VM | 31 passed | r1569 passed | r1568, ext4 disk image, one passed |

The native workflow starts a Standard scan, checks the folder scope explanation in the live preview and recorded result, and confirms that the compact dialog keeps its Close action visible. The test fixture remained unchanged and each owned FileCat process closed. On Ubuntu, r1566 timed out in the indexed workflow. r1569 passed with unchanged application binaries and a diagnostic-only harness update. The original timeout was not reproduced on that retry. The dedicated native filesystem fixtures checked unfiltered signed difference and its disappearance with an applied filter. All three test volumes detached and the post-run process inventories were empty.

The previous Windows full-suite attempt exposed a first-preview race. The dispatcher could build an empty view before the first append and then wait on a blocked directory enumeration. The first append now reports progress before entering child enumeration. The regression forces an initial empty projection and an independently held child read, then observes the root before the producer returns. It passed in r1565 on all three runtimes.

The [run manifest](source-manifest-r1565-volume-capacity.json) records the source, binary and receipt hashes. This checkpoint covers Standard live capacity and the first-preview regression. Full runtime regression, additional filesystems and final independent review have separate gates.
