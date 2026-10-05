# E-ENV-MAC-1 — Temporary Mac native-session support

Verified preliminary environment controls, 2026-10-05. The owner authorizes temporary
remote-control setup and sleep inhibition, with restoration when Mac testing ends.
These controls do not supply native FileCat GUI or candidate qualification.

## Awake support, closed-lid verification and restoration

The original owned UID-501 process 14103 runs `/usr/bin/caffeinate -i -s -t 14400`
from `awake-9c64838bcad94c15993b6f4e46a24d9d`. Its exact identity and assertion
verified again after reconnection. Its setup did not modify saved power preferences;
original `pmset -g custom` SHA-256 is
`3042d13ac43edef6c522d07e290dc997e9ee78550c401d867fbfbda830857c65`.
That assertion alone does not establish closed-lid wakefulness.

At the owner's explicit request, a separate temporary native sleep-disable control
uses `/usr/bin/pmset disablesleep 1`. Apple's [pmset implementation](https://github.com/apple-oss-distributions/PowerManagement/blob/main/pmset/pmset.m)
defines this system-wide setting. The root restorer follows the matching
[power-preference implementation](https://github.com/apple-oss-distributions/IOKitUser/blob/main/pwr_mgt.subproj/IOPMEnergyPrefs.c)
and changes only `SleepDisabled`. Native root is
`/Users/benny/FileCatReleaseValidation/lid-awake-afd4b8f111bc44039fc1a4edbc1fdd43`.
The original key is absent; existing `Update DarkWakeBG Setting` and both power-source
profiles remain unchanged. Original preferences, pinned controller and active result
are retained. The saved flag and IOPMrootDomain runtime flag independently report true.

The root watchdog is armed before the change, PID 14706/UID 0, exact owned
`power-control.py watchdog` identity. It restores on an explicit completion marker or
SIGTERM, AC disconnection, or its 43,200-second deadline (approximately
2026-10-06 08:05:52 UTC). It first re-enables the native sleep path, then removes only
the originally absent key through CFPreferences and notifies power management.
Unrelated current system settings are preserved; restoration checks the original
power-source profile bytes. **Restoration execution is still pending** while Mac
validation continues. The watchdog is a live process; reboot/failure recovery and
battery operation are not qualified. No persistent startup or remote-access service
is installed.

The owner physically closes the lid with AC connected. Four successful independent
SSH samples over 62.868 seconds verify `AppleClamshellState = true`, runtime and saved
`SleepDisabled = true`, AC supply, the live root restorer, and unchanged boot time and
SleepWakeUUID. Thus closed-lid SSH is actually verified for this finite interval.
The owner subsequently reopens the Mac. Longer-duration behavior is not inferred.

When Mac validation ends, verify the exact root watchdog PID/UID/command and pinned
controller before signalling it, await its `restoration.json`/exit, and independently
check the formerly absent key, original profiles and runtime sleep flag. Then stop
the exact owned caffeinate helper and verify absence. The active infrastructure
processes are excluded from completed test-process absence counts.

## Authentication and native desktop launch control

The owner updates benny's sudo credential. One hidden-input read-only sudo UID check
returns zero with SSH/sudo exits zero; no credential is retained. It does not attest
native authorization or supply UI consent.

A native C `SessionGetInfo` control, compiled with the installed macOS 15.2 SDK, runs
first as ordinary SSH UID 501, then from an owned temporary interactive launch agent
in `gui/501`. SSH security session 100633 has attributes 20512 and no graphic access;
the desktop agent session 100014 has attributes 8240 and graphic access. The actual
C API supplies the flag; no flag or privacy setting is changed. All 23 retained pins,
two process absences and exact launch-agent bootout verify independently. The agent
is removed and no persistent Library/LaunchAgents entry or remote-access setting is
added. A future GUI-context consent driver still needs actual validation; metadata
access is not proof that a dialog can be observed or controlled remotely.

## Local UI runtime gate

Supported Computer Use initialization reports `trusted Node process exited unexpectedly`.
After kernel reset/retry it exits code 1 with `windows sandbox failed: helper_unknown_error:
setup refresh had errors`. Browser automation initialization reports `failed to write kernel
assets: The system cannot find the path specified. (os error 3)`. No app input occurs.
Remote mouse-and-keyboard control is unavailable through those runtimes; no Mac remote
access service is enabled. Native CLI and desktop-agent launch controls remain usable.

## Reconnection and prepared desktop-context driver

The earlier two eight-second SSH timeouts and local-only staging failure remain
retained at their recorded identity. After owner wake/network input, the Mac is again
reachable at 192.168.0.199. The corrected staging retry reaches a fresh owned native
root `authopen-f9deb49d2a9c4839bef1a16c7ff027f1`.

The fresh ordinary-user desktop driver preparation now independently verifies 201
input pins (all 197 original inputs preserved), 21 retained pins and byte-identical
clean 8f75856 Recovery/Core DLLs. A temporary gui/501 agent supplies actual native
SessionGetInfo metadata before launching the probe: UID 501, normal groups, session
100014/attributes 24624/graphic access. The same seven independently computed ranges,
read-only descriptor and closure pass on the owned regular image. Bootstrap/bootout
exit zero; wrapper, metadata control and actual probe are absent; the agent is removed
and source bytes remain unchanged/detached. No native authorization or root-controller
capture occurs in this preparation. Actual refusal is launched later with owner
cancellation and independently passes at its precise component/trace/source/cleanup scope
in [E-V09-M9](E-V09-M9-macos-guard-desktop-refusal.md). Remote mouse/keyboard and drawn
workflow qualification remain unavailable through the failed UI runtimes.

## Private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005.

- Awake start `mac-awake-v1/start-stdout.json` SHA-256 `dc3a3a13fff17d641efe3a6bd36cbf5007983234d45b211fb5d80a0154379059`.
- Hidden sudo result `benny-sudo-probe-v1.json` SHA-256 `18a08a8821b74b8ac950d6f94f31399d95ec970a716e8d7b8a1cea0c2282b173`.
- Native session proof `mac-gui-session-probe-v1/independent-session-v1.json` SHA-256
  `f00f73a645b5c8d82d3b521fe4e13bb1218783bbd765307ee32d66343e01f62f`; native proof `4bc744757f68f2a25461a0ae744e27a53108bb78406355355da55e8daf6a4ee3`;
  transport `b3d3def9baad68ad458173628db5133201ff332f1fade6f2b693fc8c6bd6c22f`.
- Fresh refusal preparation `i140-refusal-prepared-v3/independent-prepared-v3.json`
  SHA-256 `8a690491c45d850e61f1ddf2fcaa6902025a2c64bacf2c9b4fe2d6042e6515d3`;
  native root `authopen-2e38bfeff30741b8932b2e57f77069a4`, 197 input/20 retained pins,
  two direct controls/seven independent ranges each, unchanged detached image/four absences.
  That single-use native refusal launcher remains unexecuted; fresh GUI-route preparation supersedes it.

- Connectivity gate private `mac-gui-refusal-prepared-v4/connectivity-gate-v1.json` SHA-256
  `e7199d015457e306d38518ef1547cf0523cf28473340922129b59f3e85f4d841`.

- Closed-lid setup `mac-lid-awake-v1/independent-setup-v1.json` SHA-256
  `99783cf70713c82e10f902c03eb3b514f348d503a705af95be8d097efee5bcec`;
  four-sample proof `independent-closed-lid-v1.json`
  `c81eeacd231174c56161c9b3153b5af177689f801cd2abb8a7291c27adf14397`.
  Controller `12230699540f5ed370feb28db7210883ee3ba055238c53b437d4e7435cc73cb0`;
  baseline `1d3b57272dd6bd4fb70a27219270fef2cf642f6304196964853594cbff8b6dad`;
  active result `77eb92c2d10ee0e214f8f4200e659747e79a3b39d3438d2bbc9c8db95ff00473`;
  watchdog ready `bc70514c74cfe7f19ff672fca28dfceacc2f0f3df24b70c6e6601b08a7001e23`.
- GUI-driver preparation `mac-gui-refusal-prepared-v4/independent-prepared-v4.json`
  SHA-256 `05db5db2685a9b232692542e2f50642cc5b3df1de26299ff8d6578ac08ec8d52`;
  transport `1eaf48a4b0d63357b8b0e0781c41f5d6eb9ca6e52917eed5c0caf0e1647a81e1`;
  collection `f27477abba0db6b8d2e23f3878d3ca7c983f40f366bd1ac19acb42e542b98e40`.

No candidate or human GO. **NO-GO** remains.

Actual owner-approved owned-device removal now verifies no source/no timeout, helper
ENOENT after the missing-path cue, source/cleanup/agent removal/nine absences and
201 input/57 retained pins (E-V09-M10). Reporting failure is I142; no further human
Mac dialog test is queued for the current slice. Power restoration remains due when
Mac validation ends.

Final committed 348cbc7 removal now independently verifies safety, truthful missing-
device IOException, post-helper missing-entry stat and complete owned cleanup
(E-V09-M10/E-I142). Clean physical Mac regression controls pass 23/4 declared skips.
No further human Mac interaction is queued. Temporary awake/restorer support remains
active while autonomous validation continues; restoration is still required.
