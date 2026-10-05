# E-I140 — Unix recovery authorization accepted a replaced device

Native baseline and working correction verified, 2026-10-05. The baseline calls the actual
clean `593583e585d4a79cbb7ff961770a2d826858e14d` component through the separately pinned
metadata instrument in [E-V09-M7](E-V09-M7-macos-authopen-device-binding.md).

The owner runs the staged binding launcher and reports **“ran and approved.”** The ordinary
UID-501 worker 11463 requests access to the original owned 25-MiB image at `/dev/rdisk4`.
While helper 11465 waits for authorization, the controller normally detaches that image and
attaches the distinct equally sized replacement at the same path. Native node inode changes
from **845 to 849**. The verified replacement marker precedes the helper's successful source
open by **19.208 seconds**. Actual UnixDeviceSource.Open returns the replacement descriptor
and its length is still 25,165,824: the existing higher-level size comparison would pass.

The probe inspects metadata and closes the handle without calling a source-content read.
Native raw/formatted pairs verify direct/helper EACCES, then helper O_RDONLY success;
the received FD 60 is read-only and its successful close is followed by F_GETFL/EBADF.
There are no observed read/write/truncate calls on that received source-FD lifetime.
This establishes a component binding defect; it does not attest a drawn recovery workflow
or complete mapping/source-write coverage. Testing of the defective path stops here.

Worker, recorder, all recorded attachment/info/permission/detachment commands and two
ordinary offline decoders exit zero. Both image hashes remain unchanged, both images are
detached, and **88 retained/200 input pins plus eight owned absences** verify independently.
The requested 120-second recorder completes; raw **143,049,640 bytes, 2,142,873 events,
125.982 seconds** of observed span. Three nonreturning exit/bsdthread_terminate starts
and the finite absence of three inspected loss-marker IDs remain explicit. No zero-loss or
whole-source claim follows. SIP stays enabled; G: is untouched/HOLD and both VMs stay running.

## Correction and working validation

UnixDeviceSource.Open now captures a native path identity before requesting access. Before
constructing the source or asking its size, it requires both the received descriptor and the
current path to match that entry's device/inode. Unknown initial identity refuses before
requesting access. A changed, removed or unverifiable returned/current entry closes the
received handle and reports that the user must select the device again. Direct and authorized
opens share this check; symlinks use the identity of their target.

Six new actual-file cases cover replacement with the same size, removal, a wrong returned
descriptor, path replacement after opening the original descriptor, an unchanged source/link
with exact bytes, and unavailable identity before any access request. On the Mac, all six
pass within affected UnixDeviceTests/UnixFilesTests **19/23, four declared skips**, exit zero.
All **321 SDK-free payload pins** verify before/after and the owned test process is absent.
The Windows host compiles and runs that affected set **7/23, sixteen declared native skips**;
its skips are not Unix behavior evidence.
Affected host recovery admission/UI/write-trace checks also pass **25/35, ten declared
skips**, exit zero. These do not supply a native whole-source trace.

The native working producer has base `300c52ada77244b0679c05b1874ee4a4c5306c7c` plus
the exact separately retained working patch. It is **not a committed producer**. Clean
committed-source CI/native checks and fresh native authorization with replacement remain
required. Approval/refusal passes on the older component remain historical. Physical media
changes that keep the same kernel node, earlier selection-to-open transitions, Windows paths,
broader topology and exact-candidate qualification are not established by this correction.

The first independent trace verifier incorrectly assumes an ISO offset in the raw walltime,
which carries a CEST suffix. The failed verifier remains retained; the corrected verifier uses
the named Prague zone, validates CEST/+02:00, then compares the native timestamps. Native
inputs/results are unchanged. Runner help `-help` exits 3; `--help` exits 2 and supplies the
native usage used for the actual class-filtered run. Those command receipts remain retained.

## Private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005.

- `authopen-binding-executed-v1/independent-executed-v1.json` SHA-256
  `07bf9f815c1db974ebf6fbe4c9f916944fa5f542ccb493a9515cb6785caeac63`.
- Raw SHA-256 `589f9e4d8ec2e6eb8a590cd49522364c2847628b80820bc0708296e2db9ffe7b`;
  collection `4415bfcaa507a950ea3b12d8371a0306b61ed036c9ea5d0b7311db368497b68f`;
  transport ZIP `039b818db23e923b15aaca94a94bc79af69974470542d11761e7cf5237581d4f`.
- `i140-native-working-v1/independent-working-v1.json` SHA-256
  `fd9ce01d9ff8b796026deeb682c7cb863d876a8a1fcb3220b51b3542555de2f5`.
- Working patch SHA-256
  `68a9e6c57d333a34f7d262895fb35ae6741fdf2ccc20fbcf674ab0a2cf48cb1c`;
  native payload ZIP `4fa52566019a1a95a9a3b77b20b6697fb2e713b0e9b79a38d66bb84c214156e6`.

I140 is remediated preliminarily, not Closed. Native authorization revalidation and final
qualification remain. No candidate or human GO. **NO-GO** remains.
