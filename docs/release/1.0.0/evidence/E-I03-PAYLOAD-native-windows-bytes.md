# E-I03-PAYLOAD — actual Windows native payload byte provenance

2026-10-06. Exact source `850298359fc16cf28503cea42f3ba9c0b1599240`.
Preliminary I03/V20 evidence; no candidate or native execution qualification.

Independent PE-header inspection reads all four actual SC/FDD Windows payloads
from the authoritative production packager. It finds 62 PE files without a CLR
directory. Fifty third-party/native-runtime files exactly match members of seven
owned original NuGet archives by filename, length and SHA-256. Twelve FileCat
application/shell apphosts or native administrator bootstraps are generated
outputs, recorded separately rather than asserted to be unchanged package files.
Every full payload pin and each read archive remains unchanged after inspection.

Matches include ANGLE 2.1.27548.20260419, HarfBuzz native assets 8.3.1.3,
Skia native assets 3.119.4, WebView2 SDK 1.0.3179.45 and Windows .NET runtime
10.0.12. Some identical SDK/runtime file bytes also occur in the crossgen2
archive. Multiple matches are retained; identical content alone does not prove
which source path the producer read or that crossgen2 itself ships.

This maps actual native file bytes to archive members. It does not establish
archive-signature authenticity, complete embedded static-library/source/license
composition, managed ReadyToRun transformations, OS libraries loaded at runtime,
installer/AppImage inputs, legal eligibility or final artifact SBOM completeness.
No binary executes and no package or source file is modified. I03 stays Open.

Private `FileCatReleaseEvidence/native-payload-provenance-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-windows-native-payloads-v1.json | e24f3be6bb5b4f11d0f926ff1fbe5f12d22ac783a6d54ef26b500871a95b3a2f |
