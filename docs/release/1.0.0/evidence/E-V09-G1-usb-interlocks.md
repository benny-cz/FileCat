# E-V09-G1 — disposable USB interlocks and Windows census availability

2026-10-03. Preliminary static/automated/preflight evidence, not candidate qualification.
Guard correction: `1df5dffe42bb820d21dd9fae86004a9eccedc02e`. Initial local tests use the retained working inputs
at clean `92579674ee144d7c74ac35326ee53e347eb05411`; the native payload is built from clean 1df5dff.

The owner connects G: and explicitly authorizes any necessary use of this USB disk. Host Storage and CIM queries
identify one USB disk with serial `2F2000129618`, model UFD_2.0 Silicon-Power8G, disk 5. Get-Disk capacity is
7,796,162,560 bytes; its sole partition starts at 1,048,576 and spans 7,795,113,984 bytes. Volume GUID is
`bd052877-2d83-11f1-a457-18c04da6742d`, FAT32, label ESD-USB. Disk/partitions are not boot/system targets.
The older CIM geometry capacity differs; it is not substituted for the Storage byte capacity. Earlier absent-device
observations remain historical evidence. No format, fixture write, raw source read or recovery scan occurs here.

Audit before modification finds the existing physical opt-ins check bus/serial but incompletely guard boot/system,
capacity, instance/volume identity and protected backing disks. Drive text is interpolated before strict validation;
the destructive harness formats by letter and checks again after the format. Original sources and audit are retained.

The shared LiveUsbGuard now requires literal drive input, exact serial/capacity and an absolute marked evidence folder
on another backing disk. It pins the hardware instance, native device path, volume GUID, partition bounds and disk
number, independently checks the volume's disk extents, and rejects boot/system disks and disks backing profile,
application, current, temporary or evidence folders. Unknown topology refuses. The physical suites share a collection
that excludes concurrent tests. Identity is rechecked before format/delete phases and source access. Format, cache
flush and fixture file operations use the pinned volume GUID. The destructive case retains expected generated file
hashes outside the source. Native Storage command parameters are checked against Microsoft's
[Format-Volume](https://learn.microsoft.com/en-us/powershell/module/storage/format-volume) and
[Write-VolumeCache](https://learn.microsoft.com/en-us/powershell/module/storage/write-volumecache) references.

Fourteen negative cases reject command-bearing drive text, malformed roots, boot/system targets, changed serial/
capacity/bus, missing instance, invalid volume identity and invalid bounds, with a valid identity control. Real host
USB preflight captures/rechecks the identity and protected backing disks: 15/15 including those cases. The first
preflight fails before source access because PowerShell progress CLIXML appears on stderr; its complete failure and
inputs are retained. Suppressing progress fixes the harness without suppressing errors. With opt-ins absent, the
affected Windows inventory is 14 pass/seven explicit skips; affected App live case compiles and explicitly skips.
Independent direct TRX parsing checks every case/outcome. No skip is treated as a physical-device pass.

Clean 1df5dff self-contained Windows test payload is staged in an owned guest root. All 300 inputs are verified
after extraction; its fourteen synthetic guard cases pass in the elevated SDK-free guest, exit 0. Direct native
XML outcomes and every retrieved output's size/hash are independently verified. No physical source is accessed.
The staged runner defaults to synthetic verification; physical preflight/read-only/scenario phases are separate,
recheck the exact serial/capacity, and use owned evidence/temp folders outside the USB. Actual scenario execution
still requires guest USB routing at this initial collection. [CI 37083622189](https://github.com/benny-cz/FileCat/actions/runs/37083622189)
then passes Windows x64, Ubuntu and macOS while ARM64 remains in progress. Packaging is skipped.

Final CI collection subsequently confirms all four lanes pass, including ARM64. Direct Windows TRX inventories:
App 248 pass/15 skips, Core 699/47, Windows platform 148/33, Remote 88/28, zero failures. All fourteen new guard
cases pass; the hardware preflight explicitly skips. Direct Unix boundary XML inventories independently verify
74 pass/22 declared skips (96 outcomes) in each Unix lane. Complete run identity/log and result artifacts retained.

## USB routing retry and host setup gate

Guest metadata at 01:07:59 UTC identifies the same serial/capacity on disk 1, E:, with non-boot/system partition
bounds matching the host. Its guest volume GUID is `5219b0b4-bead-11f1-bb40-000c2980895c`; this is a new guest
identity, not a drive-letter substitution for the host binding. VMware logs record disconnection at 01:08:15.653 UTC.
Subsequent guest metadata has no matching source and the host regains disk 5, G:. The physical-phase wrapper
refuses the absent device before invoking a native test. An earlier wrapper metadata predicate also refuses
because serialized BusType is the string `USB`, rather than numeric 7; that failure and its correction are retained.
Neither wrapper refusal performs a format, fixture write, source raw read or recovery scan.

On resume at 09:13 UTC, host metadata again identifies the authorized non-boot/system USB, with its recorded
capacity, partition bounds and volume GUID. All 300 clean 1df5dff inputs are independently rehashed with no changes.
An owned host launcher is prepared before asking the owner for administrator setup. It verifies that exact input
manifest, source commit and every input; requires the unique serial/capacity-bound USB; runs the metadata/backing-disk
preflight; and requires exactly one passing native XML case, with no skips or errors, before proceeding to the three
FAT32/exFAT/NTFS destructive component scenarios. The shared guard rechecks the pinned identity before each mutation
phase. XML/logs, generated expected hashes, phase exits and output hashes stay in a fresh owned folder off the USB.
Each phase is bounded to 45 minutes. The launcher has zero PowerShell parser errors; it has not been elevated or run.

The host session is not an administrator. The next setup gate is the owner's launch of
`artifacts/release-evidence/v09-usb-physical-20261003/LaunchUsbHostTests.cmd` and UAC approval. Prior authorization
already covers formatting this disposable stick; no further data-use permission is requested. The prepared run is
component known-ground-truth recovery only. It supplies neither installed-helper authorization/refusal evidence nor
independent source-write tracing/full source hashes, and it does not override the production I106 census refusal.

Private evidence is under `artifacts/release-evidence/v09-usb-guards-20261003`,
`artifacts/release-evidence/v09-usb-physical-20261003` and
`artifacts/release-evidence/i106-windows-census-20261003`.

| Evidence | SHA-256 |
|---|---|
| independent guard TRX inventory | `ea7c79d00842f7ac268fdaa3e53d1c7e54a4189900f57ea943b1add335ceb01f` |
| Storage USB identity record | `477119cfd34f493b243fb521c9e88c349592b67e7fb0123fe5928457dac5ad3a` |
| clean 9257967 census input manifest, 261 files | `6c6f5408ec0c180645069443c10cbbe8b6739a15effce9097f94a3ca46e07b29` |
| census self-contained bundle, 106,612,707 bytes | `00afff4edc2ae7b74936747cc058f7809a43569aa88c0ac8f4199f3a5c29bf6a` |
| census App DLL / smoke apphost | `f9dd6a8faf4af6070cd1a70d0351d290e5719de771e0a88d0638a6821ada0fbd` / `31e97dd6993c73650f559343cad2ef99397539e85abdeab98e9367b28410c6da` |
| elevated guest positive output inventory | `63cf87e58a40bbefae94e0fccd2eecf31915585eabb2f5817649b74bad7d7057` |
| elevated guest post-teardown output inventory | `8fb497bea6c62343163006d331eaf4477413ca90537ad53e0a3fec9d0b3476a1` |
| elevated guest post-teardown visibility inventory | `5c4c4347af0f121ad42e7f63618ebd74b7e43a5c923e03fc65f8aea19099e885` |
| clean 1df5dff native USB input manifest, 300 files | `b6835a2baee21534b7bd6b9442734cfd1664ee639f49ca9e30566b6287ffdc64` |
| native USB bundle, 68,304,166 bytes | `a841347c75b799ffcbdaa5d21131494bf211e7388b25e47c9295c7a267a4b86f` |
| native synthetic guard result / direct XML | `fb022132e15325f6fd6492a3e1d98c856b79091f72797979afaca4b3c389fc3d` / `e818c64b8f3f46cbeb1b22f290e82b9bdf8651378bf11e77d1182eaa108701aa` |
| original static interlock audit | `07df65ac5ee19a8c11577a92e175a1907e62803f3bdef4f85e90d17ed45d364e` |
| independent native guard inventory | `46ad85c1a706db3baba5d1f41eddf8152c46efebe0b80b53a24b35e3472dc7e9` |
| complete CI run identity / full log | `0102a66cf1a36e157307b1e456ca151dd76dbf7e72dcff23ba6578c8072a29da` / `b35da28a008c3a036a16d74def7db5c39929fe42c24fa60c8e414619c2ad0c5f` |
| independent CI Windows / strict Unix inventories | `2de26d2f10e0de9333ab917cab72157f20c2270267adb8e9ac3c40071a31e822` / `ab143f644404d31c451f2eb612f38a783ac7501386d891c7fdabf3f13671bbce` |
| routing-attempt inventory, eleven files | `4560bf0c0589d331ec68c4561a88f50f173bae2bcd66ec6207a7b5d0a571ecf5` |
| guest connected USB identity / after disconnection | `c68f5da4f221bad9acb10b159f8eec092c04edd9560daf4c3c97305c2f53b93d` / `a30e5777f33cf1252ca4cf6c9d99be14e77c1e16f81b507fabe08a1542f7c2f8` |
| VMware USB disconnection log extract | `c4b239f77244b3d267933420ee7a4d4ea749b4323001b65d445f6783a4f987eb` |
| final host runner / launch command | `985436eb7f7be904ce3babd9d6437be701105d6518a3405d57e32c07dfc90720` / `db63cae1fe034af162daf3746634b0ddb3877dbcdbe7197e6a33a8d2c38228c9` |
| final host launcher preparation record | `c037da8715a50e567cc0a41e77f93d303bbc685c863606a2c087680ea6ef17bb` |
| cumulative host setup inventory, seventeen files | `a07bd433d1f9b802080ae68fd3d863fc65e5a3e86ca218f1b6c9ee5bcb58956a` |

## I106 availability finding

The real production `other-processes` role at clean 9257967 returns null on the ordinary Windows host, exit 0,
4.735 seconds, with no FileCat.exe/dotnet.exe in the subsequent CIM inventory. In the elevated SDK-free Windows guest
(UUID 9D224D56-1161-A849-ABA7-2581A980895C, Insider 26300), all 261 inputs are verified; the real census first returns
true with the completed owned menu fixture running. Only that exact path/command/hash-verified PID is terminated for
test teardown. The next real census returns null, exit 0, 0.435 seconds. An immediate CIM teardown snapshot briefly
still includes the dying PID; the later independent visibility inventory has 159 processes and no FileCat/dotnet name.
Every retrieved output matches guest size/hash.

Sixteen guest processes have no readable MainModule filename. A diagnostic comparison using
[QueryFullProcessImageName](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew)
with limited query rights reduces this to four: Registry, Memory Compression, System and Idle. Ordinary-account host
visibility remains much narrower. This comparison is diagnostic only; no production census change or name-based
exemption is made. Microsoft's [OpenProcess restrictions](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openprocess)
also explain why an inaccessible process cannot simply be assumed absent. I106 remains Open: Windows device scan
availability and broader runtime/lifetime races require remediation/evidence. The safe refusal is not bypassed.

The owner subsequently launches the host component run: preflight and all three filesystem scenarios pass, with
325 generated deleted files recovered exactly per filesystem. A checker audit and stronger prepared rerun are
recorded in [E-V09-G2](E-V09-G2-physical-usb-oracle.md). Installed-helper authorization/refusal/removal,
independent write tracing/full source hashes and exact-candidate evidence remain pending. Component scans cannot
substitute for the product admission route.
