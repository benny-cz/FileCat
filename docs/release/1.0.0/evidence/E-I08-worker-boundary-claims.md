# E-I08-CLAIMS — reconcile current worker documentation

2026-10-10 CEST. Source review against **4f9d50047db06efd0d91096a95a5389137d16329** finds ADR-06 still calls the Shell host the only worker, although PictureDecoder starts a separate native-Skia FileCat picture worker on all platforms. XML comments also describe low integrity or stream-only operations as stronger protection than the implemented launch route provides.

[ADR-06](../../../adr/ADR-06-worker-isolation-shell-host.md) now describes the actual Windows token/job/fallback states, picture admission/deadline/field bounds, and inherited Unix process permissions without a filesystem/network sandbox or Windows-style job memory/child limits. Stream protocol, process separation and crash handling are distinguished from worker authority. Four C# files receive XML-comment corrections only; an independent raw Git comparison verifies every executable source line unchanged. Original and corrected bytes remain pinned. No new runtime observation, sandbox certification or collective archive/inspector/recovery acceptance is invented.

[Subsequent I332](E-I332-linux-picture-process-policy.md) restricts direct Linux x64 worker process creation before input at its separate overlay producer; healthy decoding, all-thread state and installation-failure refusal are independently observed. Mac/ARM64 and broader authority remain. The following conclusion retains the earlier wording-only scope.

This closes the specific obsolete wording within I08/I10; **I08 stays open** for Unix remediation or an approved threat-model/scope decision, broader permissions/lifetime/parser integration and installed-candidate qualification. Existing Windows/Unix/I312 observations retain their own exact producers. No owner risk acceptance, contract freeze or publication decision changes.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records preserve exact producers, raw commands/results, original failures/skips and restoration. The E: compatibility junction preserves historical paths; new artifact records use physical V: paths.

| File | SHA256 |
|---|---|
| `i08-boundary-claims-20261010-v1/independent-claims-v2.json` | `9ed129d29c7846f0302840a9d55e8b17f4a155a2572593b7a27022a48f7d6b2e` |
| `artifacts-relocation-20261010-v1/audit-boundary-claims-v2.py` | `dad75f1cda92fab1e12d9b48aeefeb75dccc017e500993265382a8e572f77a39` |
