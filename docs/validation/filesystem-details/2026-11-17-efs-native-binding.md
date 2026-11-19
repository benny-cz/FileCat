# EFS native binding checkpoint — 17 November 2026

Status: **implemented native candidate; partial E-ID/N-E1/N-E2 evidence, product EFS remains gated**. This implements the [approved pathname boundary](../../design/FILECAT_EFS_PATH_API_CONCURRENCY_AMENDMENT_PROPOSAL.md) and [independent decompression consent](../../design/FILECAT_EFS_COMPRESSION_CONSENT_AMENDMENT_PROPOSAL.md). It does not close EFS key recovery, output admission, typed job/journal/UI consent or whole-protocol qualification.

The internal `WindowsStorageTransforms.InspectEfsRoot` / `OpenEfsPath` seam owns an original live review pin, action/identity-specific acknowledgments, the original native allowance, a fresh no-follow case-aware path and protected-Windows anchor. It checks the normal invoking SID again at bind, preparation and call observations. The EFS edition candidate is Windows 11 x64 Pro/Enterprise/Education (including N); other editions/architectures are refused. Volume encryption support is separate from compression's 4 KiB allocation-unit limit. Sparse inputs remain explicitly unqualified pending the new plan §5.5 fixtures.

When a change is needed, the adapter opens read/list guards relative to retained parents, validates identities and storage backing, and keeps them without delete sharing through actual API return and post-observation. Promotion uses one rolling charged scratch slot while retaining continuity with the prior metadata handle. The root is reopened at its held physical device root. No-op EFS needs no data guard, stream conversion, key setup or EFS mutation call. A no-op preparation becoming change-needed requires fresh preparation; it cannot borrow the old admission.

The supported EFS API pathname consists of the held physical root plus the captured exact components, under `\\?\GLOBALROOT\Device\HarddiskVolume…\`. It does not chase a renamed leaf or rely on a drive letter resolving to the same volume later. Microsoft documents the [NT root query flags](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew) and [GLOBALROOT namespace](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file); the round-trip fixture below establishes this API spelling on the tested host, not every filesystem/Windows version.

`WindowsEfsStreams` queries the held object with `NtQueryInformationFile(FileStreamInformation)` and uses its actual returned byte count. The inventory grows 64 KiB → 256 KiB → 1 MiB, at most two retries, and refuses failed/truncated/duplicate/ambiguous records or more than 1,024 streams / 1 MiB retained names metadata. Exact named streams open relative to the held base file; their held volume/object identity and compression state are checked. No stream payload or EA values are read. Already-satisfied items do not need this conversion inventory. Native EFS status retains its exact reason code and cannot turn Home/unknown edition into support.

After one prepared call is consumed, the adapter observes the held object and reopens the captured spelling through held parents without following links. A native success without the desired held EFS state is Uncertain. A failed return with the desired state observed is Changed with an issue. Read failures or detected path/storage churn after a call are Uncertain; no unbound retry or automatic undo occurs. No cancellation token can bypass post-observation after admission.

## Executed checks

Host builds only: the Windows platform and test projects built with zero warnings/errors. Tests ran off the user's workstation.

| Target / exact filter | Result |
|---|---|
| Disposable Windows VM, interactive `admin`, medium integrity (`S-1-16-8192`), x64 .NET 10.0.15; `WindowsEfsPathTests` + `WindowsStorageInspectionTests`, `FILECAT_TEST_EFS=1` | **18 passed, 0 failed, 0 skipped**; 13 EFS-class cases (3 parser + 10 native), 5 existing C inspection cases. |
| Ubuntu VM; `WindowsEfsPathTests.Complete_stream_parser_*` | **3 passed, 0 failed, 0 skipped**. Managed parser only. |
| Mac; same parser filter | **3 passed, 0 failed, 0 skipped**. Managed parser only. Awake assertion remained active and `IOConsoleLocked=No`. |

The Windows native cases establish synthetic file and folder default Encrypt/Decrypt round trips through the physical pathname, main/ADS payload preservation, an unchanged ReadOnly folder marker, independent consent refusal, stale/disposed review refusal, ordinary `File.Move` / `File.Delete` / ancestor `Directory.Move` exclusion after guard preparation, one-shot preparation, native false/true return handling, ReadOnly no-op, a change arising after no-op preparation, cancellation before preparation, and post-observation despite a Stop requested inside an admitted call. The false-return/desired-state fixture intentionally supplies a failed return after a successful native Encrypt; it verifies outcome handling, not an OS-generated failure with that effect.

The deterministic competitor converts an empty held folder into a junction **inside the call delegate, after the last adapter check**, then invokes supported EncryptFileW. The unselected target's EFS default changes. The adapter returns Uncertain after detecting reparse churn on the held original. This is an executed example of the user's accepted boundary; it is not evidence of wrong-object prevention, independent-process race stress, file payload redirection coverage or permission to undo the target.

Two findings changed the candidate before the passing run. Attribute-only metadata handles allowed ordinary rename despite omitted delete sharing; the final adapter promotes to read/list guards. A ReOpenFile prototype failed access checks on this fixture and was replaced with exact held-parent opens; no general statement about ReOpenFile support follows. EncryptFileW removed compression from the regular file's named stream, while the folder's named stream remained compressed. The tests record these separate observations; decompression consent permits removal and does not promise removal from every stream.

## Inputs and local artifacts

Source SHA-256:

- `WindowsStorageTransforms.cs`: `D5B6862E8820AC1DA6C99EA63E1861DE83CD0B67AAE1DA1E340696F26F852062`
- `WindowsStorageTransforms.Efs.cs`: `66F2D313AFB1D63CCEDC5348921BE6F2A1A056A8698397F0BD0CB7F740C7A1E4`
- `WindowsEfsStreams.cs`: `40CE7EE68F2F71124E17BEA78F3F5108A9004FB4F785340EC0A982958A4AE64F`
- `WindowsEfsPathTests.cs`: `9CA4EA60467F3A0451F9537EC1DA0E66E260C08909DFE621A59E68F17E47C612`

Ignored local artifacts: `.tmp/filecat-efs-binding-results.log`, `.tmp/filecat-efs-binding-tests.xml`, `.tmp/filecat-efs-binding.ps1`, `.tmp/filecat-efs-binding-launch.ps1`. The native six-file binary patch SHA-256 is `BAF550DD7512911B1D05EAC4A16C2B098C8D7886C0794C4FADD8515C42684C36`; the managed parser package is `48A2B684D36DB03F1D86BE39D4B80E17BD6208EE51DB6E967CC8ED9C5CD14B83`. The native log ends `Native exit: 0` / `FILECAT_EFS_BINDING_FINISHED`; XML records 18 passed and zero errors/failures/skips. Synthetic fixtures were deleted after their handles retired; the disposable VM's EFS keys remain intact.

Still open: independently qualified file/ancestor/case-distinct and extended rename/deletion adversaries, existing writers, stream churn/large inventories on actual native objects, all no-hydration/device/policy fixtures, sparse preservation, key setup/recovery and post-call key association, output routes, durable action/scope/revision-bound consents, recursive EFS execution and UI/lifecycle qualification, architecture-specific release gates. `IStorageTransforms.Unavailable`, Core action admission and product composition still refuse EFS. No whole E release gate is marked Passed by this checkpoint.

## Architecture-specific fixture

The initial native ARM64 qualification ran the same 17-case pathname and stream suite. Fourteen cases exercised the native adapter and three exercised its parser. The fixture uses the same explicit architecture and EFS opt-ins as the native storage job suite.

The Windows 11 x64 rerun at medium integrity in the disposable interactive profile passed all 17 cases without skips. This includes ordinary rename/deletion guards, both native outcomes after held reinspection, the disclosed directory-link boundary, and all three late-compression cases. Ubuntu passed the three parser cases. The receipts are `.tmp/filecat-efs-live-166b6897-r1/binding-x64-r4.trx` and `.tmp/filecat-efs-live-166b6897-r1/arm-binding-portable.trx`. The test assembly SHA-256 was `83CE339C9E1D9797521845A6BB2D6C2F2800518E19C69BFFB56AE0CC6CF5BE79`.

At `fab6d73d`, Windows 11 Enterprise build 26200 on native ARM64 ran the suite at medium integrity on 4 KiB NTFS. The downloaded `native-efs-binding.trx` recorded **17 passed, 0 failed, 0 skipped**. The test assembly SHA-256 was `7E264937814D074452AAA8A3F700E6906FB2E88B483430F9D50DF14A4B625564`, and the TRX SHA-256 was `441F54D105DCABD5C86964F2A55B43C390C9A747D3478059DA9DB28DDAD527A5`. The result qualifies the tested pathname and stream cases on ARM64 under the accepted redirection boundary. N-E2 remains partial pending the wider adversary and platform fixtures. [Native ARM64 qualification run](https://github.com/benny-cz/FileCat/actions/runs/44554236526)

### Conflicting writer before EFS preparation

On Windows 11 x64 at medium integrity, an existing write handle denied read sharing after the review pin was acquired. FileCat refused to prepare the EFS call because its rename/deletion guard could not open the file. Applying the failed preparation made no call. After the writer closed, fresh preparation on the same binding succeeded and the native Encrypt call preserved both main and named-stream bytes. This covers the tested incompatible sharing mode.

The Windows VM executed and passed all 18 cases without skips. The test assembly SHA-256 was `D8A93E67BA56A49587FB501195CCFDC6E7B7D59456DAF4375A031713FA2FABAE`. The receipt is `.tmp/filecat-efs-live-166b6897-r1/efs-writer-20e3354f/efs-native-x64.trx`, SHA-256 `F010D4090122C137034E3BDAAF12048BA152EC9E51C9A9AC64CCDA682E45434C`.

Native ARM64 at `f195e471` also passed all 18 cases without skips on Windows 11 Enterprise build 26200 and 4 KiB NTFS. The test assembly SHA-256 was `AB31D4FA35321EB6B54E7B96D8CD989A87B6739B255207B3546E1CE2D0642FFA`. The downloaded `native-efs-binding.trx` SHA-256 was `C4BF5775A5107772731C406C7A6EFB9498893CCA19E86FEF974EB86F4675E64D`. [Native ARM64 qualification run](https://github.com/benny-cz/FileCat/actions/runs/44566195285)

### New hard link and alternate local path spellings

On Windows 11 x64 build 26340 at medium integrity, an ordinary hard-link creation succeeded after FileCat prepared and guarded the reviewed file. The final held-object check refused with `efs-path-changed` before calling EFS. Both names still showed the same clear main and named data.

The same guest encrypted a file selected through an extended drive path and decrypted it through its volume-GUID path. Both reviews retained the original object identity and both streams retained their bytes. A separate file with a path longer than 260 characters reached native status inspection. Windows reported `FILE_UNKNOWN`, FileCat reported that it could not determine EFS status, and neither stream changed. This receipt covers the observed long-path refusal. [Windows file encryption status values](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-fileencryptionstatusw)

All 21 cases ran and passed on this x64 VM. The test assembly SHA-256 was `1279A17803112AA61EB112EE92E6E6F5BF5110D80B78E8706EC155BDA874DA58`. The receipt is `.tmp/filecat-efs-live-166b6897-r1/efs-pathnames-249232a-r2/efs-pathnames-native-x64.trx` with SHA-256 `16E3198855BCC6DBC74309EAEB14FA923A7E1B0EDBE336D93A4B3FB90538B51A`.
