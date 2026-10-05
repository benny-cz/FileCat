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
committed-source checks were subsequently produced at
`8f75856802668f7d21c09f33aa876e4fdc4409d3`: all 852 archive-source pins verify;
the self-contained native Mac set again passes **19/23, four declared skips**, including
all six new cases, with all 321 payload pins unchanged and the test process absent.
The original raw source-equality receipt remains false: independent comparison proves
only CRLF versus LF differences between the committed archive and working copies.
No source changes or receipt replacement were used to hide that difference.

The same clean Recovery/Core DLLs are byte-identical in a fresh binding instrument.
All **200 inputs/46 retained pins**, four native metadata controls, equal-size same-path/
different-inode rehearsal, golden image hashes, detachment and five absences verify.
`AuthopenBinding-20261005-v2.command` is staged for held approval using the separate root
account for capture setup. Root CLI identity verifies separately; it does not approve the
macOS dialog. The native replacement/authorization case remains unexecuted at preparation.

CI 37354452437 on 8f75856 has **three passing lanes and one ARM64 App failure** in the
picture-lifetime fixture (I141). Three server artifact digests/six complete TRX inventories
verify; this is not a passing required-CI gate. The preceding documentation commit 300c52a
has four passing lanes, four server digests/six inventories. Fresh native authorization with
replacement and clean successor CI remain required.
Approval/refusal passes on the older component remain historical. Physical media
changes that keep the same kernel node, earlier selection-to-open transitions, Windows paths,
broader topology and exact-candidate qualification are not established by this correction.

The first independent trace verifier incorrectly assumes an ISO offset in the raw walltime,
which carries a CEST suffix. The failed verifier remains retained; the corrected verifier uses
the named Prague zone, validates CEST/+02:00, then compares the native timestamps. Native
inputs/results are unchanged. Runner help `-help` exits 3; `--help` exits 2 and supplies the
native usage used for the actual class-filtered run. Those command receipts remain retained.

The v2 SSH/root launch authenticates and starts the recorder, but native authopen says
authorization is denied because no user interaction is possible. The worker returns
not-approved/no source/no timeout; no replacement is installed. This is an unavailable
authorization-session setup, not a human refusal or a passing binding case. All 200 input/
71 retained pins, both unchanged detached images, five recorded command exits and four
known owned PID absences verify. The helper PID was not independently observed for an
absence claim. The 13,008,560-byte raw capture is shortened after setup failure; no completed
120-second or whole-source trace is claimed. Detached supervisor exit code is not recorded.

Fresh local-Terminal v3 setup uses the same committed Recovery/Core bytes and owned golden
images in `authbind-d8b09642eead468882d8dd2cf0204d97`. Four native controls, same-path/different-inode
rehearsal, 200 input/46 retained pins, both hashes/detachment and five absences verify again.
`AuthopenBinding-20261005-v3.command` uses the previously verified local sudo route.
Its held native approval remains pending; v2 is completed/single-use and must not be rerun.

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
- `i140-clean-v1/core/independent-clean-v1.json` SHA-256
  `dcfb54fe7e412814082fc2c547c4deaddc8912b0f4b9d1661edc087775cd276c`.
- Committed source archive `3d7a09901915b7959e69d244d8b2df65ff4d3f44d6f31addf1b4f06a5083bab6`;
  native Core payload ZIP `0d85d8cbf55bd5bb07e5034f88489050297abd899736a194c6bf3374dc9bf8bf`;
  independent line-ending comparison `921fe0aae44fd780ee234be58965eb91011db0d31b2860260ca76a5da6e30da0`.
- `i140-clean-v1/binding/independent-prepared-v2.json` SHA-256
  `2c59af362f9f503b88f3ff833cc81b4b2792834aa894d14f71a0c9d079aed827`.
- Binding input ZIP `24c5808e84080dad65edebb7c6ba40ccf78fece71256665de6e3e71d4835983c`;
  stage `152449252a02897aedfbae15a4e3d53d264c22d8424bf9b54260159486476606`;
  launcher `3176d708c3b7594305c67991c172276875430a1762a3ff142cc712b24448b817`;
  preparation collection `d5f3df1210f1ded4b7ee8cf1510ce0b94836bff3e59fbbf9dd9074a72db17b68`;
  preparation transport ZIP `2aea6d5421453a2d1a78ae9ad6e7dae0313451f2fb020582addbc4fcfe079b82`.
- CI 37354452437 failed proof `ee60fcb662a62e1e8582ac73193e43cec4b24a8765010de5bcd11671fb378516`;
  preceding 37350516988 pass proof `87e23f774db19799eb5777d9500747494f558633b1f33036a4b63c0090f146b4`.

- V2 unavailable-session independent proof `34ee326e682de01bf0ec88b3688d2212cae2339ae5a7f15370bf2856c0442875`;
  raw `f86bf61750f5d14f8ae71cce18088bb18445794e92a717ac057f2de9529e9daa`, collection `a46ffa929bc932e9db1b39a17d3762d6fe0102ee8d54bd45794074d58962e18a`,
  transport ZIP `ad4fec65e18c2add133c62ba905157da20f5cbed27a4b6b4febc7c77040449f6`.
- V3 fresh local preparation independent proof `65b9ec7922da670120e716bcd7c815cd5b270f26e1b2500fa28599c97de1f949`;
  stage `0ecda7f76d3e6fefff8daaeb1a44389790ac0aed264b9851eaf11492407e7619`, launcher `de36a441970ad00c3a65cc1b711f51f688554663773124ee5b72707917e2794e`,
  collection `76085ae6eda47aa40c437087a16484b6ec212b00483e95f0514bef06013e50ec`, transport ZIP `95ff6431168af4c5e85664b2982d77a108264c95f4847bf508831b5eda96df90`.

I140 is remediated preliminarily, not Closed. Native authorization revalidation and final
qualification remain. No candidate or human GO. **NO-GO** remains.
