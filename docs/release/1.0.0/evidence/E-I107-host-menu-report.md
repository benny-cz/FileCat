# E-I107 — Windows host menus appear and immediately disappear

Owner report 2026-10-02: no menu item can be clicked in the latest build on the Windows 11 host. Menus appear
and disappear again. The symptom persists after the automated host App test run finished. Owner explicitly
authorizes a live host test. The owner subsequently declares this a blocker, restarts Codex and reports that
the symptom persists from every launch location, including the now-running Windows 11 VMware guest. Guest
testing is authorized; vmrun confirms both guests running. High, must fix, current execution priority: reported
core mouse command access is lost. No independent UI reproduction or root-cause conclusion yet.

Owner launch directory: C:\Program Files\FileCat, copied from E:\FileCat\src\FileCat.App\bin\Release\net10.0.
Read-only file inspection confirms that the copied App DLL matches the current build byte for byte.
Product stamp 0.1.0-preview+1cd803c5b2a764572d806313228b0331c19159d0; these bytes include the uncommitted-at-build
I106 working inputs, so the stamp alone is not proof of a clean main build. Menu/input source is unchanged by I105/I106.

| Copied input | SHA-256 |
|---|---|
| FileCat.exe, 278,016 bytes | `2189f916952c6f8a247b3bb012e80d5451f5c17b41e6cf125f63425a0db8e9db` |
| FileCat.dll, 2,142,720 bytes | `ed12ed6e6b28996bf750c1a40ce7b4a5e4a93254ff7a6072d2e18a56a75a986d` |
| FileCat.Core.dll, 1,801,216 bytes | `d4c734e184cdf070c15c4ac3507fc6b93d5126342a02529c243f5ad3ac8eba15` |
| runtime-inputs.json | `482128f1784877e5fbd13e416b80eb0b0d03d169452b154ecce48e3fc070172e` |

Raw root artifacts/release-evidence/i107-host-menu-20261002. A point-in-time process inventory returned no
FileCat-named process or FileCat dotnet command, so a specific running PID is not established. This does not
disprove the owner's observation. Nothing was changed under Program Files or in the owner's settings.

Source inspection covers MainWindow's menu construction, click/open handlers, drag release handlers, focus
restoration, command search and Alt routing. No causal conclusion from that inspection. The App suite is
headless, so its passing result does not prove Win32 menu behavior. The owner suggests testing-tool/focus
interference; that remains a hypothesis.

The Windows Computer Use entry point crashes on import with `trusted Node process exited unexpectedly; kernel
reset, rerun your request`. Two initialization attempts and an explicit kernel reset/retry fail. Window listing
and selection are never reached, and no mouse/keyboard input is sent. Computer Use guidance requires its JS APIs
for Windows app automation; no alternate host input helper was used. A working connection is needed for the
requested direct click, followed by menu-open lifetime/focus observation and an isolated-build comparison if needed.

After the owner's Codex restart, initialization returns the same error. A basic JavaScript health check without
importing the UI helper also fails. This identifies an automation-runtime availability problem, not a proved
FileCat defect or a proved helper-specific failure. Repeating the Codex restart is not requested.

Initial investigative disposition: Open; no speculative menu correction and no claim that the helper crash
caused it. Native trace and remediation results are recorded below; exact-candidate interaction remains pending.

## Diagnostic work in progress

Owner reports compatibility-renderer launches still fail on both host and guest, then describes the menu as
possibly closing too quickly to see. The native close path is still unknown. A new headless mouse test clicks
every main-menu header and proves each remains open after release (1/1, no skips): TRX
`9f5a2f783534cd8d54da6e68511e000040f29b2ac5cd933692fefc304a79f020`. The same test with opt-in tracing
also passes (1/1): `f97067e54e6ef355dd73e3a1f4fba4dc33fa3746ec9ad6478615b21cebf64eea`.
Neither result proves the native Windows interaction.

An opt-in, bounded menu/focus/native-activation trace is being prepared; it uses the existing diagnostics folder
and records no typed text or file contents. Initial guest fixture token 1cfbbfaafee04485a49193f92d2f339f, machine
DESKTOP-A60F1NE, VMware UUID 9D224D56-1161-A849-ABA7-2581A980895C. App DLL
`6420e250c4f27396a7939024f1635916240294874171011b3f4e899e6fe5a2be` and bundle
`065e5fc40a2463d20f862327053ee8a753f4bd082f84bd0803637119c96e8052`; stamp 1cd803c includes working
I106/diagnostic inputs. Owner reports a runtime prompt and the menu symptom, but the isolated state folder/log
is absent, so execution of this diagnostic GUI is not established. Both its apphost and explicit dotnet DLL
--version probes return 0/0 with empty stderr. Startup-error capture and a Windows-specific payload are being
prepared. Initial raw inputs/probe records remain retained and are not relabelled as a successful UI trace.

Owner requests earlier-commit comparisons as a fallback. Isolated source archives/builds of exact fa3a02a and
08c2e2d have published successfully; native menu behavior has not yet been tested. Current source/menu remedy is
not declared verified or fixed. I107 remains the current must-fix blocker.

## Verified second diagnostic staging

The owner's latest observation is that no menu is visible, possibly because it closes too fast. Do not treat
this as evidence that the initial diagnostic started. A second payload adds early managed entry/error capture
and uses a Windows x64 publish output, excluding debug-symbol files. All diagnostic changes are opt-in through
FILECAT_MENU_TRACE=1; the existing diagnostics folder, 250-message budget, numeric menu-item identifiers and
bounded close stacks avoid recording typed text or file contents. This instrumentation does not remedy I107.

Fixture token f33ba859631d49c3a413e1250398ac9b on the same identity-bound Windows VMware guest. Three independently
verified bundles are extracted into separate owned directories with separate --data roots. Public desktop
launchers are `FileCat Menu Check - current.cmd`, `FileCat Menu Check - fa3a02a.cmd` and
`FileCat Menu Check - 08c2e2d.cmd`. They invoke the explicit installed x64 dotnet host, write an invocation marker,
capture stdout/stderr and record the exit code after FileCat closes. No agent-driven native click is claimed.
The guest now reports Microsoft.NETCore.App and Microsoft.WindowsDesktop.App 10.0.12. Prior 10.0.5 probe evidence
is retained as an earlier observation, not relabelled as the current environment.

| Payload | Source identity | App DLL SHA-256 | Bundle SHA-256 |
|---|---|---|---|
| Current diagnostic | 1cd803c stamp plus I106 and opt-in I107 working inputs, built before ecd61f3; not a clean candidate | `f6238313bf0a604812af3862e498621c132f3a838faf9d9d1929e6a59f389f94` | `2c87b1cacf232da2a95f6614c0766d57f7626b85a867ad59d88bb82085d63930` |
| Earlier source archive | fa3a02ad4a0d9b5323f5316504efa842c120d099 | `fb2915d74d26df1c51162d2944ebd44c56211064904013c5ced60875b565aa85` | `2f8489c2dc06de92e6e5c32ebfed57182207d44a7e6bcd58b62d4007d8309320` |
| Earlier source archive | 08c2e2dee4e04c936a34cd867770d05d758af686 | `8e698d090b203c4caa9769dd6f7c328263b740a3c13530bdccfce5aeb711fe5e` | `de96a7f57e493aed5b01dbb0d395cc19f9d2e1ce3d6b5ef32beee51865718fbd` |

Private manifest `checks-f33ba859631d49c3a413e1250398ac9b/manifest.json`
SHA-256 `f6e6eabdd84ae01f2a06d5f26842f584a1950d0b37bab78814954684103b0f13` retains payload sizes, dependencies,
four exact working-source hashes and source snapshots. Guest staging result
`55570390a65bd985b35b3e816b196438753455fab176db78db344e54acca93ea` independently checks each extracted input.
At the 21:27:52 UTC collection, none of the three invocation markers or state folders exists; owner click
execution is pending. This is not a failed menu test. Collection is retained without overwriting earlier probes.

Affected automated regression after instrumentation: `dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj
-c Release --no-restore --logger "trx;LogFileName=i107-app.trx" --results-directory
artifacts/release-evidence/i107-host-menu-20261002/app-after-trace --nologo` passes 230/245 with 15 platform skips,
zero failures. Independently parsed UnitTestResult outcomes confirm these counts and the new menu test passes.
TRX `b7d409e67bed96f049921dee85fa7a6f34b85a77759e3f8bae648a2a742f772e`; test DLL
`5ac982dc1f43b9dacf3b7ec4407aa0557256458e56852bef4b3363d74849470a`.
This test build's App DLL `61f090cb3ec5be00c07f25925f4eb155d9e0e59295bab5a7430371dd1c680c8e` differs from the
staged diagnostic build and is not substituted for it. No native menu result or qualified remedy yet.

## Native failure path and preliminary correction

Owner reports all three comparison builds fail. The next collection establishes execution of each isolated
GUI: all three state folders, startup logs and invocation markers exist; each exits 0 with empty stderr and
no crash log. Current diagnostic PID 12892 and module c23b9fd7-4f49-4b28-a0db-3208235b7b8b match its staged
payload. Native input was supplied by the owner, not by the unavailable agent automation runtime.

The current trace records menu item 3 opening at 21:29:33.7584173 UTC and closing at 21:29:34.1227310 UTC;
the second attempt opens at 21:29:34.6261089 and closes at 21:29:34.7959121. Repeated ThemeChanged events with
the same Classic palette precede both closes, followed by menu-items-changed. The close stack includes
Popup.OnDetachedFromLogicalTree, MenuItem.OnDetachedFromLogicalTree and ItemsControl.RemoveLogicalChild.
The main window remains active during both failures; native capture loss follows the detach. Together with
the source and reproduced regression, this identifies unchanged theme application rebuilding/removing the
open menu. A separate focus-stealing application is not needed to explain this failure path.

Raw collection `collection-20261002-213222-434.json`
SHA-256 `56ff555dae47d3f9b6642e2e5cd7386790f378e97d77930fc94d6bf89550f8bf`. Log exports independently match
guest hashes: current `7ee1f1647f7f76632bf25f43068609e9c92b3c034fa28e8a5277d9513e7a2525`, earlier fa3a02a
`e13e0604414db0c916fef81aebedc10af201b85d23fe9753ee6d489b36f4dd91`, earlier 08c2e2d
`3fbcea0dd88b7417b4a0395c408f433d024611b6a07b3888bb22dcb23a637950`. The owner observation covers the older
menus; their startup logs contain no menu trace, so no older-build close path is inferred from them.

Baseline at 459294c plus two new regression cases: a repeated System or Psychedelic theme application closes
the menu in both cases; ordinary mouse-click control passes. Baseline 1 pass/2 fail/no skips, TRX
`0f49add692918bd58c97734b8e1c227d3dd5eae57e738dc826604b1786954df9`. ThemeManager.Apply now remembers the
requested preference and returns when the resolved palette is unchanged and its resource dictionary is still
installed. Initial application and real palette changes continue to rebuild resources and notify views.

Targeted menu/theme/tooltip regression passes 14/14, no skips, including both failing cases and a control
that switches between System/its resolved explicit palette while retaining the requested preference, then
verifies a genuinely different palette still notifies/repaints. TRX
`d4354a7ccbf60a88cce44d44a5b57540fa8770859553f2ad0edda0f8ac36229c`. ThemeManager raw source
`5c9fb7c16d5a5ada1475f40e2b3cbd9ca06b493370d7296772e81bc8aa8b5c32`, targeted test DLL
`5c697bb8ae3d429ff422964f20e632b3da7b72a769a2baa74d8f661c446ffb76`; full input manifest retained privately.
Remaining App regression passes 219/234 with 15 platform skips, no failures; TRX
`629027d4e45598112fea05ffb07d088e3b4505041c6dafbde951b75a699a5364`. The disjoint targeted/remainder runs cover
the complete App inventory: 233/248 pass, 15 skips, zero failures. Status: Remediated preliminarily, not Closed.

Owner subsequently reports the host test works and authorizes proceeding with remaining release tasks.
Read-only Program Files inspection at 21:40 UTC confirms its App DLL matches the targeted working fix byte for
byte: `e8bc09e390253a9608c6d8ed7931b980f47bcbffba94793cb45292da7a7aa23a`, stamp 459294c plus working I107
inputs. Full host runtime inventory is retained in `app-after-theme-fix/verified-host-and-remainder.json`.
No running PID is established at that later inspection. This is an owner-operated host success bound to the
matching copied input; it is not clean-candidate qualification. Corrected guest interaction and affected CI
remain pending; the owner's blocking host symptom is cleared preliminarily.

After the owner closes Claude, Computer Use reset/import still fails. The no-import JavaScript health check
now supplies the more specific runtime error: `windows sandbox failed: helper_unknown_error: setup refresh
had errors`, kernel exit code 1. No window selection or input occurs. This is an independent automation gate;
it does not explain away the observed FileCat menu removal.

Affected CI at exact correction ea4a2ac passes all four push lanes (37068514790) and all four manual lanes plus
Linux/macOS development packaging (37068909015, dev.549). Raw records/results/packages retained; no tag or release
created. Native corrected guest click remains pending. After an owner-authorized idle hard stop completed, the
owner asked to keep the Windows VM running; the same VM was restarted without restoring a snapshot. The owner
then reverted their snapshot and confirmed availability; guest access succeeds again. Earlier diagnostics and
native evidence are safely retained on the host. Keep the Windows VM running per the latest instruction.
