# E-V09 — owned multi-disk fixture attempt, 2026-10-15

This is a **fixture limitation**, not a FileCat pass or defect. The V09 multi-disk volume and physical/candidate gates remain open.

At source `b35310ce000331d48e92af56b0e9e8bded2c7eb0`, the disposable signed-in Windows VMware guest received two newly created, owned 256 MiB VHDX files. DiskPart associated their exact paths with disks 1 and 2; the pre-existing VMware boot disk was disk 0. `convert dynamic` on the selected owned disk failed with Virtual Disk Service **“The operation is not supported by the object.”** Both file-backed virtual disks reported `CanPool=false` to Storage Spaces. No striped volume, FileCat scan or recovered-byte claim resulted. The guest cleanup detached both VHDs and verified their files, scripts and owned staging root absent.

SSH key authentication to the Ubuntu VMware guest was unavailable. A VMware guest command returned ordinary UID 1000 but no paths for the probed `losetup`, `pvcreate` or `dmsetup` commands; no Linux multi-disk topology was created. This is a limited prerequisite observation, not proof that those tools or an alternative topology cannot be installed. The next V09 attempt needs an actually available multi-device fixture, exact topology/identity capture and source-write/byte-ground-truth checks.

The private archive `V:\FileCat\artifacts\release-evidence\v09-multidisk-topology-20261015-v1` contains ten original scripts and raw receipts plus a ten-member `manifest.json`. Its file lengths and SHA-256 hashes were independently rechecked after writing; manifest SHA-256 is `2e4b504a0fa032e7e1c0cb308f5d835f789ebddff378158cb994e37a95020b19`. No guest disk or owned process from this attempt remains.
