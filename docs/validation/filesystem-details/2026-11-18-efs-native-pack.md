# EFS admission for native archive packing

The native Windows archive job refuses EFS-protected file inputs and selected folders with an EFS default. Refusal applies during initial selection and on the actual capture or reopen. Packing handles sample protection before and after reading main data or download-mark bytes. Directory download marks receive the same checks against the held directory and its stream. The ordinary archive read path stays available for the invoking user when Windows grants access.

The Windows 11 x64 VM passed 33 tests, with five unrelated fixture-specific skips. The native ZIP job refused an encrypted direct file, an encrypted recursively discovered file and an encrypted folder default before creating the output or a stage. The held read fixture acquired a clear packing view, encrypted its source while the view stayed live, and verified that the next read failed with the original buffer untouched. The native clear-input capture and directory controls passed. See `.tmp/filecat-efs-native-pack-tests.xml` and `.tmp/filecat-efs-native-pack-clear-tests.xml`.

This covers the native candidate portion of E-OUT6 on the tested Windows NTFS route. The later [legacy ZIP checkpoint](2026-11-18-efs-legacy-pack.md) covers legacy packing and source download-mark admission.

## Held protection during file reads

On the tested Windows 11 x64 NTFS volume, native packing now acquires a transacted hold on each clear input file when the capture or worker opens it. The transaction binds to the identity of the actual opened file. It stays live through the payload and file-origin metadata reads and ends when that input closes. Directory download marks retain the existing before-and-after EFS checks. [Microsoft's transactional file I/O contract](https://learn.microsoft.com/en-us/windows/win32/fileio/programming-considerations-for-transacted-fileio-#encrypted-file-system).

The held-view fixture attempted encryption while the native reader was live. Windows refused the change, and the reader returned the original bytes. After release, encryption succeeded and ordinary read-only viewing could still open the file. The six initial EFS refusals and the two legacy late-admission refusals passed in the same run. The Windows suite passed 115 cases with six fixture-specific skips. The ZIP and archive-move suites passed 1,252 cases with 25 skips on Ubuntu and 1,248 cases with 29 skips on macOS. See `.tmp/archive-packing-held-efs-r1.xml` and `archive-packing-held-efs-r1.trx` in the portable test checkouts.

## Sparse input packing

On 2026-11-19, the Windows 11 x64 VM used 4 KiB NTFS and a medium-integrity process. Native ZIP packing with the self-contained confined worker and legacy ZIP packing each copied and moved a 4 MiB sparse input alongside an ordinary file. Both members decoded to the expected bytes, including the zero-filled holes. Copies kept the sparse originals. Moves removed the sources after publication. Operations showed one warning per saved archive that extracting the sparse input could use more disk space. Native packing uses the captured input's attribute and legacy packing reads it from the same handle checked for EFS during the rebuild.

Both routes refused the sparse input with EFS enabled before creating an archive or stage. The fixture verified the source bytes and EFS flag after refusal. The Windows suite passed 104 cases and skipped 10 unrelated platform or prepared-volume cases. See `.tmp/filecat-sparse-pack-44817f84-r5.xml` and `.tmp/filecat-sparse-pack-44817f84-r5.log` on the build host. The worker's executable SHA-256 is `3AB20BDAD640E3501151ED1D12A9EBF94108676F990E1567953EFB069D0A0A0E`.

Ubuntu passed 1,252 portable ZIP and archive-move cases with 25 platform or fixture skips. The TRX is in `.tmp/filecat-sparse-pack-44817f84-r5-portable.trx` on the build host.
