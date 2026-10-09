# E-I03-SHELLHOST — bounded Windows helper executable provenance

2026-10-09. Preliminary I03/V20 evidence for the four original Windows publications at **850298359fc16cf28503cea42f3ba9c0b1599240**, version 0.0.0-i03resourcecheck: win-x64/win-x64-fdd/win-arm64/win-arm64-fdd. Every selected original published pin is checked against its earlier production manifest. This is not a current artifact or candidate; I03 remains open.

The four FileCat.ShellHost.exe payloads match **20 complete raw PE sections** from the corresponding x64/ARM64 .NET 10.0.12 apphost templates: .text, .rdata, .data, .pdata and .reloc in each publication. The only preparation before comparing these complete sections replaces the unique 64-byte apphost placeholder with FileCat.ShellHost.dll and zero padding. Section offsets are independently mapped; no whole-executable/header/resource-directory equality is claimed.

Two newly downloaded official Microsoft.NETCore.App.Host reference archives, win-x64 and win-arm64 10.0.12, retain official registration/catalog metadata and complete SHA512 matches. Each selected apphost member is byte-identical to the installed SDK template used in the comparison. The existing SDK 10.0.401 native NuGet verifier reports author and repository signature success for both archives; its actual tool/config/command/streams and existing-trust-store observation are retained. **These reference archives/templates are newly pinned now, not original pre-build input seals.** License/notice contents are retained without making provider/legal eligibility or complete-source conclusions.

The four original executables also preserve **40 complete resource leaf contents** from their retained managed intermediates, including exact resource paths, languages, lengths and hashes. The original full comparisons are unequal: all forty code pages change from intermediate 0 to published 1252. These differences are retained explicitly; matching resource content does not establish matching resource directory bytes. The intermediates are newly pinned rather than original build seals. A version-tagged primary [HostWriter reference](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs) supports the binding/resource-copy interpretation; it is not claimed as the exact source of the executed SDK tool.

The four FileCat.PrivilegedHost.exe files **do not match** those apphost template sections. All four actual nonmatches remain. Their original, pinned project selects the custom C++ native bootstrap via Build/PublishPrivilegedNativeBootstrap, which explains why an apphost-template origin is not established for them. No bootstrap provenance or product defect is inferred from these nonmatches. [Project IL](E-I03-PROJECT-windows-il-provenance.md) independently retains the managed helper/project-body subset and its limits.

The final independent raw PE reader rehashes all official metadata/archive/member/template/tool/command inputs, all eight original published helper pins, the twenty complete sections, forty resource contents/code-page differences and original project source. The exact empty verifier TEMP namespace is archived and absent, with zero retained locks. No target assembly/native executable is loaded or run; no product test/CI replay, installation, host UI, device, account or persistent setting changes.

Original pre-build source/input chain, native bootstrap/static links, full PE headers/resource directories, loader origins, other assets/platforms, complete licenses/SBOM, current artifacts and exact-candidate qualification remain open. Physical-source HOLD, all final-candidate campaigns and explicit human stable GO remain.

## Selected immutable receipts

Paths are relative to the private FileCatReleaseEvidence root unless absolute. Nested receipts retain complete inputs, commands, raw streams and inventories.

| File | SHA256 |
|---|---|
| `apphost-payload-followup-v1/inspect-original-helper-apphosts-v1.py` | `3f96d0cb061534bc8bcd59f9574f844660da1f0490e93ab53bb74e7cab4000b0` |
| `apphost-payload-followup-v1/original-helper-apphost-observations-v1.json` | `030229f89d9fb2e8cb9d5be7b500aeb653ea5c4f2a1751eeef225eda3d010d85` |
| `apphost-payload-followup-v1/inspect-apphost-resources-v1.py` | `7f80b7ab0a10a5ef0d8118a802ef3e2d17d5a81c19107f188d37577abc38da10` |
| `apphost-payload-followup-v1/original-shell-apphost-resource-observations-v1.json` | `2166072c2af1617751462116fee04359c29ca219b8b4ad685d8fc276c7466430` |
| `apphost-payload-followup-v1/verify-apphost-reference-packages-v1.py` | `3d53abb0fb56920a08dd9e1bc987216701c651f28b180e882f810815edb454c0` |
| `apphost-payload-followup-v1/independent-official-apphost-reference-packages-v1.json` | `4f2b9941dc8b027f02f5a5a3efe2aa8a5d8ec3190d747b41074391c28537f3c2` |
| `apphost-payload-followup-v1/independent-apphost-native-signature-observation-v1.json` | `a1aa35266750b2ed2b787a48d60c4edd6a73aaa5079b8e7c51819203a19376a0` |
| `apphost-payload-followup-v1/seal-shell-apphost-provenance-v1.py` | `3b9cd2d03cd2507fb2e68710750144f95514ca6de94da5a76eba27042e1e54ef` |
| `apphost-payload-followup-v1/independent-shell-apphost-provenance-final-v1.json` | `8e280c5c0d5a89f299ff181696a64230849e1b0ccb2ddf226543ace608b339db` |
