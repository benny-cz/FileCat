# E-V09-MAC-APFS — actual APFS system-store topology and zero-open refusal

**Preliminary V09/I106 child accepted at source `144d73f864b00e90269259ce27d40cd89fd25b34`, whose product implementation is `5fe5e8a5c42bb6201164b1d15dd5ab9014ea63b7`; other media/topologies, installed consent and frozen-candidate qualification remain open.**

## Exact boundary

The physical Apple-silicon Mac runs macOS 27.0.1 build 26A434. `diskutil list physical` identifies one 1.0 TB internal physical disk, `disk0`. The mounted root is APFS volume `disk3s1s1` in container `disk3`, and its `APFSPhysicalStore` is `disk0s2`. This is the plan's actual APFS shared-physical-store boundary, not a synthesized property-list reply or an attached image.

An ordinary-user Core control classifies the raw recovery source `/dev/rdisk0` as `disk0`, the owned destination folder as `disk0`, and every one of FileCat's 15 write folders as that same disk with zero unknown answers. The actual App recovery flow then selects `/dev/rdisk0`, inventories its current write roots and runtime temporary/instance locations, explains the unsafe topology and refuses before opening the source. Its recording wrapper observes **zero device opens**.

## Exact validation

Raw evidence is retained at `V:\FileCat\artifacts\release-evidence\v09-mac-apfs-system-refusal-20261028-v1`. The exact Git archive SHA-256 is `3c123d8678428229f6e45428ab1ff3b23527f64cb77b45178dfbeb000a3ae5d8`. The copied result manifest contains 31 entries, independently rehashed with zero mismatches; `SHA256SUMS.txt` has SHA-256 `e9829c42a498ba748d5e6c4521a3c43ec3ab81d542b4271034ff81f2232f8895`.

- The pinned .NET 10.0.101 MSBuild builds the Core and App test projects successfully through the valid Microsoft-signed system host. Core-build log SHA-256: `d071cad295600f0fdc020599efcd362459b8e968ef2c30d601f656ce018f0727`; App-build log SHA-256: `1dffd024b66c15326134c7f2b47db3ed6880083a2f2622dc17573e6e76d2b6c9`.
- `UnixDeviceTests.Where_writing_goes_on_this_system_is_what_the_tester_expects` passes **1/1** in 6 seconds. Its output records source `disk0`, destination `disk0`, `shares: True`, and **15/15** FileCat write folders on that disk with **0 unknown**. TRX SHA-256: `7eb8df78e9ea665b62c15876c79a3628bec6678544b4d24d5caf490909b80afb`.
- The fully isolated `RecoveryWriteTraceTests.A_recovery_session_writes_only_where_it_should` passes **1/1** in 8 seconds. All FileCat state and runtime temporary data are under the owned stage on the same APFS store. The prompt names settings/history, local logs/journals/cache/temp, listing scratch, hex originals, runtime temp and instance connection, supplies the separate-disk `--data` remedy, and reports `refused; nothing opened` plus `devices opened: 0`. TRX SHA-256: `cb029e23813cfa4388e67c77e1597dc43ac7801556d6b9afed2c85a4783f11a0`.
- Exact relevant source hashes are retained with the compiled Recovery/Core/App test assemblies. The source archive, property lists, OS inventory, raw logs, XML results and restoration records all remain in the private package.

## Setup failures and restoration

Copying the valid signed system dotnet host into a new isolated path passed `codesign --verify` but macOS taskgated later killed two restore processes as `Taskgated Invalid Signature`. No product test ran and no device opened. Both raw `.ips` reports and the incomplete setup log remain failed setup evidence. The successful build explicitly ran the pinned 10.0.101 `MSBuild.dll` and VSTest through the original signed system host; it did not change `global.json`.

The first App invocation omitted `FILECAT_V09_DATA`, loaded the user's existing FileCat state and rewrote it on close. Settings and history remained byte-identical to their generated backups. The only workspace difference was the test window geometry; both forms are retained, and the active workspace was restored to the exact backup hash `1335471154340737c02c4f291eca5450af731d95bb50fd754632f42096bbd51c`. That run is setup evidence, not the acceptance producer. The authoritative repeat uses an owned isolated data root. The user's original local dotnet host was restored to SHA-256 `0504fefd0d7e79b889702eb041ac62bae4381909e9479b645502588328eb1c9f`; settings/history active hashes were also rechecked.

This accepts only actual Mac APFS same-physical-store attribution and refusal before raw-device open. It does not prove a successful scan to another physical store, authopen approval/refusal, device removal/replacement, other media, final installed consent, a frozen candidate, or publication.
