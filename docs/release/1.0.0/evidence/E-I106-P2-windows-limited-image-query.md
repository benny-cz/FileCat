# E-I106-P2 — Windows census uses a limited image query

I106/V09/V23. **The Windows image lookup correction passes controlled native and affected host tests.**
Unavailable identities still produce unknown; I106's complete absence, lifetime and device qualification remain
open. No USB access, process exemption, privilege enabling or recovery bypass is introduced.

Before this correction, the census reads Process.MainModule for every executable with an unrecognized name.
G1/P1 show that module paths can be unavailable although limited image queries succeed. Windows census now
uses PROCESS_QUERY_LIMITED_INFORMATION and QueryFullProcessImageNameW, then applies the unchanged bounded
FileCat apphost binding inspection. Microsoft documents the required rights and Win32 result format in
[QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew).
The buffer grows only for insufficient space and is bounded at 32,768 characters. Failed/empty/oversized queries
remain unknown; Unix behavior and conservative FileCat/dotnet name detection are unchanged.

Private root `artifacts/release-evidence/i106-limited-image-20261003`. Source is base
`c1624817edb674ea00a4bd6cdf5d47ad1c4d017d` plus the exact three-file working overlay in retained manifests.
This is not a clean candidate build. Production App/platform DLLs and both test fixture versions are retained.

An owned waiting cmd.exe is given a DACL denying module/memory-query rights. A fresh Process.MainModule query
fails with error 5, while the production limited image query returns its exact executable path. The negative
control then denies the limited query itself and requires null. Current-executable, Idle and invalid-ID controls
also pass. Only the owned child DACL changes; cleanup uses its already-held handle and ends the child.

The first elevated run passes the current-image case but fails the DACL control because MainModule still succeeds.
Its complete inputs/results remain retained. The corrected test restricts privileges only within an impersonation
scope using [CreateRestrictedToken](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-createrestrictedtoken)
and [RunImpersonated](https://learn.microsoft.com/en-us/dotnet/api/system.security.principal.windowsidentity.runimpersonated?view=net-10.0).
Both permission controls must then actually enter their denial/success paths. It changes no production token.
Corrected standard and elevated runs each pass both cases, zero skips/failures; positive and negative output is
independently verified. This fixture repair preserves the original assertions.

Full affected host App suite passes **242/263, 21 explicit prerequisite/platform skips**. Corrected full Windows
platform suite passes **161/198, 37 explicit skips**; the physical USB branches are unavailable deliberately and
not counted as device evidence. App compilation reports its existing obsolete Bitmap.Save warning. Independent
verification checks direct XML inventories, skip reasons, exact source/retained bytes, both permission paths,
elevated identity and 623 retained files. A separate worker check confirms owned controllers/test children absent.

| Retained private evidence | SHA-256 |
|---|---|
| Corrected working source manifest | `8751a53045f5e198fff847ffb7d0aa9ccdd09bb6e498e00db9870ed52dea33d5` |
| Retained working App DLL | `ac44868655f32407c28841dc511449e637f331a4caf59713258ca256625d5c67` |
| Retained working Windows platform DLL | `1fde72def351fda108657f35c28ef258b8a9c65a452f0d5406ff906b91c06752` |
| Corrected standard permission TRX | `fc64e231ff8086a36bbfbc11bdde611f8f4e67a07707507f36c16554e46306ea` |
| Corrected elevated permission TRX | `acd06fd8e22ad7b1a66ab30882196576111a439eea0b1effff1b5df037f36fd8` |
| Full host App TRX | `f7c275eb8850b9c8f9d019aa40f0da6f049313f7f129c391b2eea344a385f2f6` |
| Corrected full host Windows platform TRX | `074e567d64c3416c3c080ce0ae78565cbb3b69051c9a04d67fb264279fc2f923` |
| Initial elevated failed permission TRX | `297c662200dc88cb84b78be4918e55481f0a33c9dc75b226a4f87882e56bf9d3` |
| Independent source/native/XML/inventory verification | `dd125e206158084d4db9a3f210acf6096e384b0e1bc9a6b76e9df4c347112485` |
| Independent owned-worker cleanup | `6621924064962c9f85d8eb7e06898701cc728e96d8e7c3ab30e39730548f8a86` |

Successor CI and clean native guest verification remain pending. The preceding documentation-only c162481 CI
[37151556209](https://github.com/benny-cz/FileCat/actions/runs/37151556209) passes Windows x64/Ubuntu, fails the ARM64
thumbnail helper-start assertion and macOS verified-copy totals checkpoint, with three package jobs skipped.
These results predate this correction and do not qualify it. Complete run/log retained as hashes
`ed5f0e9f517c1c5a0dec68c09eca5e80c6286772dc80093f08cb6586a904545e` /
`b941fe85e49b69027b6c515539e070b9683be41a82a3dd8c4e743dab745f93a5`.
I108 is reopened for the two observer cases. I106/G6 remain open, FileCat USB validation held, no candidate, **NO-GO**.
