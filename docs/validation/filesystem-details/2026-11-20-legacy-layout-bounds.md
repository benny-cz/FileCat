# Windows legacy layout inventory bounds — 20 November 2026

The older direct `WindowsFileRecords.Read` report still exposed a layout section outside the newer metadata-only `ReadFull` route. Its extent loop could retry a header-only `ERROR_MORE_DATA` response without advancing, and its sparse-range loop could accept malformed or capped output as a complete byte count. This change routes both through the existing bounded, validating collectors. Incomplete extent observations show only a validated prefix and its reason; they no longer infer a complete fragment or resident-file result. Sparse observations report an incomplete inventory rather than a stored-byte total. The sparse query now reopens the held object by handle instead of resolving its pathname again. Complete range counts are described as allocated byte ranges; they do not claim nonzero content or physical savings.

Verification:

- `dotnet build tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj -c Release --no-restore -v quiet` on SDK 10.0.101: 0 warnings, 0 errors.
- Windows 11 x64 VM, native NTFS: `A_file_reads_with_its_exact_times_IDs_links_layout_and_permissions` and `A_junction_names_its_target_and_a_sparse_file_its_stored_ranges` passed, 2 executed, 0 failed/skipped. The VM checked for other .NET processes before the run. Platform DLL SHA-256 `543D6867B2BC821BFFCC6217000293B5027E8A171CF4BB284C24B6E3E9834D55`; test DLL SHA-256 `CF0A303ADB18205A64F8F6B1259CFF0C3A61618AEF27F2B8CF2D90E67F39DA7C`; TRX SHA-256 `7AA315AE60B73247EE7A6AA4E94415E5678DD41CF1B4F21F40EF78D91D3D9586`. Local receipt: `E:\FileCat\.tmp\legacy-layout-r1.trx`.

The direct reader remains a legacy broad report. This receipt does not qualify its other collectors or broaden the metadata-only F3 route.
