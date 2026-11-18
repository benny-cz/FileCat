# Development package qualification

The package sources were pinned to commit 764f878d. The Mac and Ubuntu builds ran on the test machines using the checked-in packaging scripts and a verified sparse source checkout. Windows used the same package recipe with serial MSBuild. The Windows artifacts are unsigned development packages. The trusted signed installer remains a separate release gate.

| Platform | Package startup checks | Standard native desktop workflow |
|---|---|---|
| Mac | App ZIP extracted, strict ad-hoc signature verified, packaged executable returned the expected version | Passed using the verified app-bundle assemblies |
| Ubuntu VM | Self-contained tar, extracted Debian package and AppImage each returned the expected version | Passed using the verified Debian package assemblies |
| Windows VM | Portable executable returned the expected version from the original ZIP | Staged for retry after the first run yielded at the process guard |

The native harness keeps the original archives and extracted package files available for verification. It loads a separate copy of the packaged assemblies, verifies the application hash, runs Standard in both memory and recorded-result modes, opens the capacity and resource dialogs, and checks the captured folder exclusion and save workflow. The completed Mac and Ubuntu workflows preserved their source fixtures and exited normally. The Mac also captured its own window through the native Retina compositor.

The merged renderer suite completed on the Mac and Ubuntu VM with 982 passes, seven expected skips and zero failures or errors on each machine. The skips cover platform-specific and separately scheduled opt-in cases. On the Mac the first result collection encountered an unmatched shell glob. A new collection from the same finished guest run retrieved all 16 validated batch receipts. The Windows renderer suite continues from its verified batch receipts.

[Build, package and receipt hashes](source-manifest-r1582-development-packages.json) identify the checked results. Full native package workflows on Windows, the signed installer and the full independent review remain open.
