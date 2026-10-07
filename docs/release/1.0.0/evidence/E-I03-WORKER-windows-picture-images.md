# E-I03-WORKER — actual Windows picture-worker modules

**Finite native I03/V20 observation at f502fd169478d0631ea4280adbb5b3f6457cda68.** The exact clean App payload starts the real picture worker through the production Windows restricted launcher. Its token reports low integrity, it decodes 65,536 exact pixel bytes, and it exits naturally with zero. No product rebuild or persistent machine change is needed.

## Actual workload and file origins

A private observer calls `SandboxedWorker.Start` with the actual FileCat apphost, picture-worker arguments and the production 1,536 MiB job-memory parameter. One owned 128 × 128 BMP is sent through standard input. The output pipe holds the decoder during the module snapshot; the observer then drains and checks every BGRA pixel. Read-only reflection obtains the launcher's actual child PID; a native token query independently reports `S-1-16-4096`, agreeing with the launcher's low-integrity flag.

The host is Windows Insider 10.0.26220, x64, .NET 10.0.12; SDK 10.0.401 builds only the private observers. This is a real Windows worker/process observation without desktop input.

All **46 module files** reported by the actual process are copied and hashed while it is live. Current origin files and retained copies agree before/after inspection:

| Origin | PE without CLR directory | PE with CLR directory | Total |
|---|---|---|---|
| Exact pinned development App payload | 2 | 5 | 7 |
| Installed .NET 10.0.12 host/runtime | 4 | 11 | 15 |
| Installed Windows libraries | 24 | 0 | 24 |
| Total | 30 | 16 | 46 |

The seven payload files are FileCat apphost/App/Core, Avalonia Base/Controls, managed SkiaSharp and native x64 SkiaSharp. Four dependency modules match exact members of three retained NuGet archives: Avalonia 12.1.1, SkiaSharp 3.119.4 and SkiaSharp.NativeAssets.Win32 3.119.4. The actual restored graph selects the matching members; NuGet metadata content hashes agree with the canonical source lock and actual assets graph. Raw archive SHA-512 agrees separately with its cache sidecar. These are distinct hash domains and all actual values remain recorded.

Installed .NET and Windows inputs retain their full file bytes, hashes and observed file/product versions. They are distinguished from shipped components; no signer, update-support or advisory disposition follows from an installed version.

## Independent verification and limits

A Python section/RVA reader and an independent .NET PEReader agree on every file's machine/magic, CLR-directory presence and ordinary/delay import names. The parsers use the [Microsoft PE specification](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format). Imports describe declared file dependencies, not every reachable load or static component.

The seal rechecks all 141 original App payload files, fourteen actual observer payload files, five independent-reader payload files, 81 retained files, every complete module snapshot, both parser results and exact NuGet members. The original source receipt/1,065-blob consumer seal remains linked. The header, complete uniform pixel output, low-integrity token and natural exit agree; the owned child PID is absent afterwards.

The package reader first stopped because it conflated the raw archive/cache SHA-512 with NuGet's metadata/lock content hash. Both actual values are now kept and checked in their own domains. The first seal also stopped on PowerShell's nonzero inherited status when the exited PID was absent; the corrected query retains its command/output/status. Neither correction reruns FileCat or rewrites the original worker capture.

This proves current-file provenance for a finite native picture-worker path. It does not establish mapping-time memory-byte authenticity, complete reachable/static composition, every format/helper, signatures, legal eligibility, full containment, native desktop/participant behavior or candidate qualification. Wider I03/I08 remains open. No physical source, persistent setting, frozen contract, candidate or publication changed.

Private `FileCatReleaseEvidence/windows-worker-images-20261007-v1`:

| Path | SHA-256 |
|---|---|
| native-worker-v2/command.json | faf79046a695300c822a40a83f77f9d3d1b3d9cb335e7a6dc361949139c2d346 |
| native-worker-v2/outputs/actual-worker-observations.json | fd5b789577805e77fbecf99142a14b32a7c40aa9b01a750e6700b289e6b19a0f |
| reader-v3/independent-pe-reader-v3.json | 1e030c0c2d376394ef67bfa40a4da8faf5264f492833988cdb7a445da1091ea5 |
| reader-v3/host-verification-v3.json | 5534a7f79469d18e7bc43912eae32438c83ead20c003a9c2f2d09bc112a7d10a |
| owned-package-matches-v5.json | 72dfc82b889d474645481c15ae44617916c13868e8ef1c13ddf8a2d475dd349d |
| independent-windows-worker-v7.json | 1f83ee9bf0f2dddb8ab9b6635ea01f6de31df76556d8478ff9244a8a73d03e87 |
