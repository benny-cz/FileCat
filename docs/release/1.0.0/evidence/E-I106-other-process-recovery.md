# E-I106 — Recovery discovery misses a separate portable installation

Discovered 2026-10-02 after the I105 remedy, while continuing V09/I09's broader discovery audit.
Potential Critical (deleted-data safety), must fix. Status Open at discovery; no unsafe device scan performed.

SDK-free Ubuntu 26.04.1/GNOME 50.1 in the identity-bound VM of E-ENV-07. A second owned portable installation
uses the I105 working overlay: App `4e74ce23704de182115fd6f879ceded8b689aa2f4a8efe7b7b3afd53bb0ff4f7`,
Core `5b66ee2f63dc4a0f210efb64c769507693ed2bd42a7b6c3b07e0c274b3d0dbd0`, unchanged smoke probe
`f9e43055ed6649a49e21abde49a425e7c425793885d473c44e040fe16f8648df`. This matches I105 production source
at `1cd803c5b2a764572d806313228b0331c19159d0`, but is not a rebuilt CI artifact.

Actual second-installation GUI PID 12176, profile cross-2ead864b8f45489e, has a busy Data/profiles/... lease
(independent Python fcntl oracle). Its own-base probe returns true; the first installation's probe returns false.
Both return false after graceful close. The production lookup only inventories its own portable base and per-user
roots. A separate installation or another --data root is outside that inventory; absence there cannot prove that
another FileCat process is not writing to the source. Required behavior: refuse device recovery when such another
process remains live or its presence cannot be established safely. The device guard must apply regardless of the
recovering window's own state mode; image-file parsing remains separate.

Private root: artifacts/release-evidence/portable-fallback-20261002.

| Before evidence | SHA-256 |
|---|---|
| cross-installation-before-results.json | `6793d71590f95cc4d42757835be77ec53b3493c494592ac801f9a5ffd7e8292c` |
| cross-installation-before.tar.gz, 65,466,898 bytes | `4652cd9171e37cc707bfefdc2572a53d91d9930e701d82521a2f2df0dcd76998` |

Archive guest/host hashes agree. Owned fixture token 2ead864b8f45489e923055eb050db960; full payload, owned
portable/per-user scratch state, native PID/executable/window records, probe logs and driver script retained.
## Preliminary remediation and Windows verification

Device recovery now takes a read-only process census before admitting a source, regardless of the current
state mode. Another deployed FileCat process, or an unavailable census, causes refusal and asks for other
windows/helpers to finish. The usual-root checks remain as an additional control. The census does not make
a global registry or write outside the selected state roots. Ordinary election/forwarding is unchanged.

The deployed FileCat process name and, for an actual FileCat entry assembly, its own apphost/runtime name are
checked; the current PID is excluded. A shared runtime name can be ambiguous, so the message makes no claim
that another process's state is known. Arbitrarily renamed other copies, visibility across accounts and a
process starting after the snapshot are not proved by this preliminary correction; wider re-audit remains open.
Image-file parsing does not use this device gate.

Baseline guard regression: true/unknown presence both fail; absent-process control passes (1/3). After the
fix, Windows recovery subset passes 14/17 with three Unix-only skips. A controlled real child process proves
liveness detection; current PID and an empty inventory are ignored; an access-denied inventory returns unknown.
Storage-oracle tests provide a separate empty process inventory, so an owner's interactive window cannot
contaminate their storage assertions. Final Windows App suite passes 229/244, zero failures, 15 explicit skips.
Smoke tool builds with zero warnings/errors and has an other-processes diagnostic role for native validation.

| Windows result/input | SHA-256 |
|---|---|
| before/before.trx | `b46ca221a63d3da0a2c8f5d01870e7532412aa8496e3f0cace79978e80c5ea74` |
| after/after.trx, before storage-fixture isolation | `954998ea4ceb47f8c8e4c5aa6882e27bd5cda7434b618c1aefc29f20aa446820` |
| full-app-windows/app.trx, final fixture inputs | `85c01e4b2e765b1e1f0d23dfc75c38c21db134ecbc45bde700a628daa5079beb` |
| Final Windows FileCat.dll | `ed12ed6e6b28996bf750c1a40ce7b4a5e4a93254ff7a6072d2e18a56a75a986d` |
| Final Windows Core DLL | `d4c734e184cdf070c15c4ac3507fc6b93d5126342a02529c243f5ad3ac8eba15` |
| Final Windows test DLL | `628589d4b823d41b5e6d20e27d8110986981794a1869d55ea21f138d8a744ee4` |
| Final LF SingleInstance.cs | `00be298f1848c58962d9af52d488b3a4d0a083aaf32322b0a26c71eb10667353` |
| Final LF MainViewModel.Recovery.cs | `e617ec90453ff29cd211cf001463bcfcee20756df9b4d90c1dfb693e4c97f942` |
| Final LF RecoverySafetyTests.cs | `995e9320d8d7133b7e237bcb893c6251d970505b43038af622aee2d25a5b51c6` |
| Final LF Unix harness | `f915a97c55a9f05a7fee6edc69d339103c4697446202fcadfdcf9d43f60617a2` |
| Final LF smoke Program.cs | `4d03c2d28455662acd56209090a28fccd5ab6023f6821cdbd3960200590263e2` |
| final-inputs/manifest.json | `cf2ab026acc1608630cd41b3975f1212f7c6b068572842e9ac93086edc45a304` |

Windows raw root: artifacts/release-evidence/i106-other-process-20261002. Before/final source and binary snapshots
retained. Earlier targeted test inputs are not relabelled as final. Updated strict Unix inventory requires 51
results across three scenarios (47 pass/four explicit skips); this count has not yet executed for I106.

Status: remediated and Windows-verified preliminarily, **not Closed**. Native process/GUI after cases, all four
affected CI lanes, rebuilt packages, broader census audit and exact-candidate tracing remain pending. I105 CI
passes at its own source identity; it cannot qualify the new guard. No source-device scan, tag, signing or publication.

## Native after and successor CI

SDK-free Ubuntu 26.04.1/GNOME Wayland, same verified VMware UUID, tests the corrected census against actual GUIs
from a second installation. Clean source ea4a2acad34bf0641fbc15bc64a64013e8600322, cross-published self-contained
App/smoke bundle `d3ce2050cd88e3f69a5988bb2402e48b4a1536437247d6ec24e362339f21da6f`, 53,502,159 bytes.
Owned fixture fedd910d17674086a06a4710d0c947be. Both separate portable state and explicit --data state pass:
before absent, actual window/process and independent fcntl lease busy, production census true, graceful exit 0,
then absent. Native PIDs 14599/14674, both executable paths independently resolve to the owned second installation.
No device recovery scan is performed; this is process discovery plus the earlier device-admission regression.

| Native after input/evidence | SHA-256 |
|---|---|
| FileCat apphost | `f21bb97cff44f1450a01bab717f89e447b9740a274d4dfd3a5b97ebcc94d5593` |
| FileCat.dll | `d04c5dd945e9de3f7ad5d1102ae6ed7125c7f05a4660401f84762d7d56c7de34` |
| Core DLL | `45728b847eaedf53a88ed2b1dd263a5c163c0a0793ecb011ac7d85407ca57842` |
| Smoke DLL | `ba3188eec8c56112d40784f39d95ea7d0ed63a05164b91c6c61e2324aca02891` |
| native-after-results.json | `0c964381c3a70f45c6eee192a1d2dc27f56d3cd802146c8b168ee1d76a071eff` |
| native-after-full.tar.gz, 103,649,736 bytes, 563 members | `9283f7cbaccd0f4cc9c93502d3d4d94553f2277163387382c8450e6be4e3e66c` |
| native-after-full.tar.gz.verified.json | `55bcf34b4dd50d83717f5b6677eabada3c0b7e9b7f423eb4b2ecf4bf5db16979` |

Full native before/after archives independently stream-verified, including every regular member's size/hash.
Before archive retains 289 members and its previously recorded hash. Guest/host after archive hashes also agree.

All four CI lanes pass at exact ecd61f3663f4c0b4c3249b34add5b4e726da0d62, run 37065695677. Independent strict
Unix inventories now establish the previously pending 51 outcomes per lane: 47 pass/four explicit skips.
Linux results JSON `0e3b144fc7548399b0e18c70a421b61eb47171d63b48456bdca9f8ef373473b2`; macOS
`fd4e46d325df0449781fa41a5f83fba5f4adec729f9e2f1294fc926937b55508`.

Successor ea4a2ac, which includes I107's palette guard, passes all four push lanes (37068514790) and all four
manual lanes plus Linux/macOS development packaging (37068909015, dev.549). Strict Unix inventory independently
again confirms 47 pass/four skips each: Linux JSON `e38aa7af797f7e65ebe5fbf8251081e4250743c4d62ed550ad907607a9466c7d`,
macOS `085b24f59144aa36e3a45cab15dfbab4d3d8ec89480b7651775cad54ce2e8293`. Raw run records, result archives and
dev.549 packages retained under artifacts/release-evidence/ci-37068909015. No tag or release was created.

Native after and CI are complete preliminarily. Rebuilt package native checks, wider alias/visibility/race
audit and exact-candidate source-device tracing remain pending; I106 is not Closed.

## Renamed-apphost audit, 2026-10-03 local

The wider audit establishes a real remaining gap at exact ea4a2ac. The same hash-bound native bundle is copied
into another fresh owned installation; its unmodified 78,256-byte apphost is copied to CatAlias. SDK-free Ubuntu
26.04.1 launches it with a unique portable profile. Actual GUI PID 16935, /proc executable path and process name
CatAlias are independently observed, and Python fcntl proves its instance lease busy. The production census
returns false despite the live FileCat. Graceful close returns 0. No source-device scan is performed.

Fixture `27d915b69efc4b86b375a1b1ff43ecaa`, private root artifacts/release-evidence/i106-other-process-20261002.
Full before archive includes both payloads, renamed apphost, driver, state and logs; guest/host hashes agree and
all regular members are independently stream-verified. This invalidates any broader absence conclusion from
the name-only census, while the earlier ordinary-name positive discovery cases remain valid at their identities.

| Renamed native before | SHA-256 |
|---|---|
| native-alias-before.py | `87d9b793715c88a5189ad64b359cec1417711e6b40e20ca59f99d3ee6bba57aa` |
| native-alias-before-results.json | `c5e2865ea041f6dd8193f8e806f9bbd964f991e6ac07579a253ee421e0ae7c0a` |
| native-alias-before-full.tar.gz, 103,686,338 bytes, 540 members | `889af2325e9b1728b54248aabc52969fabbb7ccefcac63788947c5de9c7f8035` |
| native-alias-before-full.tar.gz.verified.json | `1cdf90d7f7df57f0d255f1f40bddebfd4cee76b8a05f27e07c7ff61376956bb3` |

Working correction inventories processes and checks executable bindings rather than relying only on names.
The known FileCat/current runtime names remain conservative positives; dotnet is conservatively ambiguous even
when this window uses an apphost. Other executables are read only, up to 4 MiB, for the NUL-terminated FileCat.dll
apphost binding; incomplete or inaccessible inspection is unknown. A known positive takes precedence over an
unknown identity. The existing device-admission rule refuses unknown; no new state registry or external write
folder is introduced. .NET's [host design](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-components.md)
and [HostWriter](https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs)
describe the embedded managed application path; local deployed Windows/Linux bindings are also directly observed.

Targeted Windows guards: 16/19 pass, three explicit Unix-only skips. New regressions cover the actual built
apphost, a binding crossing the read-buffer boundary, a nonmatching assembly, bounded/incomplete reads,
unreadable identity and positive precedence. Strict Unix inventory now requires 19 outcomes per scenario,
57 total. Native after, affected App remainder, CI, visibility/availability/race audit and candidate tracing
remain pending at this working correction; I106 remains open.

### Working identity correction: native after and affected App

Hash-bound working inputs at a318c907abb263d5c04b0ba557264bbd09472f53; raw source copies and manifest retained.
Correction subsequently committed and pushed as `06c5791fc9a5fc2a51c3daf4749e9fd246dd435c`; the native working
payload remains identified by its original build/input hashes, not relabelled as a clean-commit build.
Self-contained Linux bundle `902b07038a10c8bf4b690266bbbb80d97d709bf03eb03238c5068a9a198129e0`, 54,182,314 bytes.
Native fixture `346699603e14470eae2953933deee938`, same SDK-free Ubuntu 26.04.1. Actual portable/--data/renamed
GUI PIDs 17535/17653/17750 and independent fcntl leases are observed; all three live census results are true,
and graceful close returns 0. Renamed executable is CatAlias; the apphost bytes remain unchanged.

**Before launch and after close, the ordinary account's census returns null, not false.** The complete process
inventory includes identities it cannot inspect. Device admission consequently refuses; no absence pass or
successful device-recovery availability is claimed. This improves safety over the false-negative census but
leaves an important availability/visibility audit open. Read-bound exclusions, cross-account visibility and
startup races need further work before closure. No source-device scan or final qualification.

Disjoint Windows guard/remainder runs cover all 250 App cases: 235 pass, zero failures, 15 explicit platform
skips. Guards 16/19 (three Unix skips); remainder 219/231 (12 skips). Earlier counts remain tied to earlier inputs.
Full native after archive independently stream-verified and guest/host hashes agree.

| Identity working input/result | SHA-256 |
|---|---|
| raw SingleInstance.cs | `5ee555ad2da198212eb710adcfcca38af7acbf3b3a89d13d43beb3d01cb44c51` |
| raw RecoverySafetyTests.cs | `c2f9d970e5626034fee665f18b6de4c9809b6d6ff65f725237cda5ba02e36a7a` |
| raw Unix inventory harness | `e0ef8ce78c6845afbe71375ab142f0ea26d55631b08bf04d7972e4ca9f33db37` |
| native-identity-after-inputs.json | `35f5059cdb0ad960f214e2f8927910267597f262257427b17306742c582f4c58` |
| Native App DLL | `c14bc006500fb41ceb7767d99553faaaf5afa2ea1069f504267f56eafc9781ae` |
| Native Core DLL | `25dbbe2a4d54117d0f914e8944ed7242acf3764dc96645d6f4263a6a5e959e96` |
| Native smoke DLL | `6f090a57c4b89ca78dcb38796138806206b9d29f8479e146b9273b91c9e0e0b6` |
| native-identity-after-results.json | `440f93f0cc77f8f32f199a3d86f467a0aa8028eda9b75b2fc434cc15ea5b2242` |
| native-identity-after-full.tar.gz, 105,061,836 bytes, 598 members | `7c20d9b41b0828bdca404a2b56643f6b9bf3ffc4b3e1604733f25c55e779429a` |
| native-identity-after-full.tar.gz.verified.json | `f9feae3c0c36b6155046d60a4aef535080f3d9c68795c37bd8980be4e5ce3684` |
| alias-after-windows/guards.trx | `8cc87d8176d03c1af91462e1bc21e7537dc189be1f1bd073e88b2859e881e9ed` |
| alias-after-windows-remainder/app-remainder.trx | `56ed78ccfb02f69156249340bd7796e9e64adc439f0416377c656e6ee41520e0` |

## Visibility/read-bound follow-up

Independent procfs audit at the earlier working identity inputs records 285 permission errors/73 readable
identities for uid 1000; root records 120 readable/240 missing executable links, including kernel tasks, and
three executables above the four-MiB bound (Python 7,477,160 bytes twice; snapd 33,654,424 bytes). Production
census returns null for both callers. Raw audits retained: uid-1000 JSON
`43acf068ecfb3bf752662a6f131a64e1b2f1386d6bae2b4f42f41192651ca213`; uid-0 JSON
`98bdea34f46f49b40abd12140372ccdde9cad507aeb6c0e7950dda98634513ee`.

The follow-up excludes only Linux tasks whose OS flags explicitly identify PF_KTHREAD; unparseable task
metadata stays unknown. The [kernel's flag definition](https://github.com/torvalds/linux/blob/master/include/linux/sched.h)
confirms this is a kernel thread. Executable reads remain bounded, now at 64 MiB, with vectorized binding search;
an incomplete or inaccessible read remains unknown. No names of ordinary system services are allowlisted.
An exact-bound EOF can be established rather than falsely treated as incomplete. Regression covers valid
kernel/user flags, a command with spaces/parenthesis, malformed flags and a larger nonmatching executable.

Follow-up committed/pushed as `d8c6f3baa4c427199c5e128a04a750a616bdb16e`. Native working payload is separately
bound to 1a9f1ba plus retained working input hashes, not relabelled as a clean d8c6f3b build. Same SDK-free Ubuntu
26.04.1, fixture `c27f63cc52044651beb79652435aa6e2`: actual GUI PIDs 18713/18859/18989, independent busy leases,
ordinary and root census true for portable/--data/renamed cases; all graceful closes return 0. Root census
returns false before and after each case. Ordinary-account absent cases still return null due to permissions,
as required for safe refusal. This establishes root process-discovery availability on this guest; it does not
qualify ordinary-account device recovery, Windows/macOS availability, process-start races or source-device writes.

Disjoint guard/remainder App runs: 236/251 pass, zero failures, 15 platform skips. Guards 17/20 with three Unix
skips; remainder 219/231 with 12 skips. Strict Unix inventory now requires 60 outcomes (56 pass/four skips).
Full native archive independently stream-verified, guest/host SHA-256 agrees. No source-device scan.

| Availability working input/result | SHA-256 |
|---|---|
| self-contained Linux bundle, 54,182,839 bytes | `1d7072f42a4e8587a69313d8169dc8dc84fa8598b85146aee54bfccfc6233453` |
| raw SingleInstance.cs | `d8958f13c4f6cf67d7c828d28f9669727256b0afdd8a73ee5298a1a728b2f41a` |
| raw RecoverySafetyTests.cs | `c3bbc435e5e1d90d9acdf194c1ca945c73cef048dd9d77e16e1744dd5440ecea` |
| raw strict Unix harness | `0049ea4df38f71d6d18df0ce321eacf9f9b923ab3ee07ede92ebb5eff88964b3` |
| native-availability-after-inputs.json | `1d03937e30543102cae61f373d66037c2403dd336b81d2eae61521fab5057a3f` |
| native App DLL | `a0601feceaefbc1f66a9576856a16b105b7a53766aca76e14aa3c8375ea5cad1` |
| native Core DLL | `9f56ee2b1aa274c10e0ea9560f20101e24e7f2cbe1ffc1e182faaad5ecb6d2c6` |
| native smoke DLL | `52e3d5d8d72685d3c3224c642ba4216de7a4857c3d382165687d8a7c9f172b35` |
| native-availability-after-results.json | `43dc81c2417ee5e337a66bcea00714075adf2da02e352b703d1a5dc237dcf314` |
| native-availability-after-full.tar.gz, 105,077,230 bytes, 607 members | `80f63acda95abd2b73d91be1d56ee342ffce615d6a6a11bb9c52aa8105627d38` |
| native-availability-after-full.tar.gz.verified.json | `e4dee7a51dd847b233b3e7fbedd9d9352a862d46d19a482b6ff419105745a813` |
| availability-after-windows/guards.trx | `2a506e469197b0ab5bf52d8688b654d6ae27a35713f4f900ffcb3a537470eb5a` |
| availability-after-windows-remainder/app-remainder.trx | `7e25aef5cac786131bd10637504f2e48a0d010a82d774c892a97a591bc43acbd` |

Earlier identity correction 06c5791 passes all four CI lanes, run 37073405252. Manual development run
[37073593358](https://github.com/benny-cz/FileCat/actions/runs/37073593358), exact 1a9f1ba, passes all four lanes
and Linux/macOS packaging (dev.553); Windows tag-only package correctly skipped. Independently verified strict
Unix inventory: 53 pass/four skips each, 57 outcomes. Linux result JSON
`04ca0ce10d2a8e7a66c1420130523004e3b026937208ef6b2c6957799f836fc3`; macOS
`b80d854863b619befe98ef40bb238d7095384a094ab2e7a0c75b3a9be782686d`. Raw results/packages retained under
artifacts/release-evidence/ci-37073593358; verified inventory JSON
`62aeda5589db4253564b3b90995aeda363c604d64645a3fad1a9952bf8d871ce`.

This earlier CI/package success cannot qualify d8c6f3b's later availability correction. Its CI/native rebuilt
packages, broader visibility/runtime aliases/race audit and exact-candidate tracing remain open; I106 not Closed.

Latest exact d8c6f3b CI [37075680108](https://github.com/benny-cz/FileCat/actions/runs/37075680108) now passes all
four lanes. Independently verified strict Unix inventory: 56 pass/four explicit skips per lane, 60 outcomes.
Linux JSON `27e5e1ff2deb750b701406aa6142a46c1128353edd4ee2c845517be7ee5774ba`; macOS
`240bed0e2ef5b2781b21c90b4f0896dac27c008db8de2faa954700a36f9d2473`. Raw archives/run retained under
artifacts/release-evidence/ci-37075680108; verified inventory JSON
`94b3532c36452ee95285aba4daf9d65c52a903b71a8ec6da4925ffbb2ff8be18`. No tag/package/release job runs on this
push. Affected CI complete; broader privilege/runtime-alias/race and candidate source-device tracing remain open.
