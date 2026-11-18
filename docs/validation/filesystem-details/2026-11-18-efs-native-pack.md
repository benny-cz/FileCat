# EFS admission for native archive packing

The native Windows archive job refuses EFS-protected file inputs and selected folders with an EFS default. Refusal applies during initial selection and on the actual capture or reopen. Packing handles sample protection before and after reading main data or download-mark bytes. Directory download marks receive the same checks against the held directory and its stream. The ordinary archive read path stays available for the invoking user when Windows grants access.

The Windows 11 x64 VM passed 33 tests, with five unrelated fixture-specific skips. The native ZIP job refused an encrypted direct file, an encrypted recursively discovered file and an encrypted folder default before creating the output or a stage. The held read fixture acquired a clear packing view, encrypted its source while the view stayed live, and verified that the next read failed with the original buffer untouched. The native clear-input capture and directory controls passed. See `.tmp/filecat-efs-native-pack-tests.xml` and `.tmp/filecat-efs-native-pack-clear-tests.xml`.

This covers the native candidate portion of E-OUT6 on the tested Windows NTFS route. The later [legacy ZIP checkpoint](2026-11-18-efs-legacy-pack.md) covers legacy packing and source download-mark admission.
