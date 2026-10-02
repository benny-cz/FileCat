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

Status Open. Do not change menu code speculatively or label the helper crash as its cause. Direct live test,
reproduction, remedy if needed, affected regression and candidate interaction evidence remain pending.

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
