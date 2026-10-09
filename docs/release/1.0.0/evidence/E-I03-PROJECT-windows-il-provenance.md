# E-I03-PROJECT — method IL of FileCat's own assemblies in earlier Windows publications

2026-10-09. Bounded preliminary I03/V20 evidence for original **850298359fc16cf28503cea42f3ba9c0b1599240**, version 0.0.0-i03resourcecheck. This is not a current runtime or candidate. The four previously sealed Windows publications are win-x64/win-x64-fdd/win-arm64/win-arm64-fdd. I03 remains open.

The actual original payload manifests select eight project assemblies: FileCat, Archives, Core, Platform.Windows, PrivilegedHost, Recovery, Remote and ShellHost. Sixteen retained pre-ReadyToRun intermediates, one per project/RID, are compared with their 32 originally pinned published files. **The intermediates are newly pinned now, not sealed before the original build.** This does not establish the original pre-build chain, reproducibility or complete source composition. Every published byte pin is rechecked against its original production manifest.

The unchanged five-file metadata observer reads all 48 PE files as data in 16 bounded commands. Every published comparison preserves the complete captured logical method/signature/body vector, excluding only physical body RVA/file offsets. Each publication contains **19,204 method definitions, 18,486 IL bodies and 1,695,473 IL bytes** across the eight assemblies. All 32 logical comparisons and MVID comparisons match. Every corresponding whole-DLL hash and whole-metadata hash differs; the published native headers are present, while the retained IL intermediates have no native header. No whole-file or native-machine-code equivalence is inferred.

A separate bounded Python reader independently maps raw PE/CLI sections, verifies the complete metadata hash, decodes tiny/fat method headers and checks every complete IL hash, length, stack, initialization and local-signature value from the original raw offsets. It also checks complete sequential MethodDef token coverage, all input/command/stream pins and actual counts. The full signature/exception metadata vector remains the .NET observer's observation: Python does not implement every metadata table or instruction semantics. The existing separate Avalonia.Base one-byte detector is reused as a qualified control, not represented as 32 new detector runs.

Sixteen command/input/complete stdout/empty stderr receipts remain on E. The same observer files and all 48 input pins are rechecked after reading; its exact empty child TEMP directory is absent. No target assembly/native code is loaded or executed, and no product test, CI, GUI, setting or source-device change occurs.

Separate selected NuGet and runtime-pack evidence retains its own original producers. Native compilation/static links/resources/PDB source composition, apphosts/loader origins, complete source chain, excluded assets/platforms, licenses/provider acceptance, complete SBOM and later/current/candidate provenance remain open. The original native bootstrap's empty embedded revision retains its qualification. All 24 final-candidate campaigns, physical-source HOLD and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts retain the complete source, command, payload, stream and raw-result inventories.

| File | SHA256 |
|---|---|
| `project-il-followup-v1/compare-retained-project-il-v1.py` | `d467c797f31e643d8ad2bfcf11b304c991aa3d4746731acd7b913a612078588c` |
| `project-il-followup-v1/independent-retained-project-il-v1.json` | `46a8e039a5ea93b0cd42464ddadd03341d7fc717e0e52cc4a1e9a3b045e9b7f6` |
| `project-il-followup-v1/seal-project-il-final-v1.py` | `8718ceb69fdc445ef10695cc1e2a2b6af0013f6e49550180e3edef3f543035dc` |
| `project-il-followup-v1/independent-project-il-final-v1.json` | `c85860dd51d740defb871d8c69c9b218e3f880d900418d956ce8e68dd082ec62` |
