# E-ENV-MAC-1 — Temporary Mac native-session support

Verified preliminary environment controls, 2026-10-05. The owner authorizes temporary
remote-control setup and sleep inhibition, with restoration when Mac testing ends.
These controls do not supply native FileCat GUI or candidate qualification.

## Awake support and restoration

Owned UID-501 process 14103 runs `/usr/bin/caffeinate -i -s -t 14400` from
`/Users/benny/FileCatReleaseValidation/awake-9c64838bcad94c15993b6f4e46a24d9d`.
Its exact PID/UID/command and active native power assertion verify. Saved `pmset -g custom`
bytes are unchanged before/after, SHA-256
`3042d13ac43edef6c522d07e290dc997e9ee78550c401d867fbfbda830857c65`.
No saved sleep/lock/security preference is modified. Stop the exact owned helper when
Mac validation ends, after checking PID/UID/command, then verify absence and original
saved settings. Assertions also expire after four hours. Active infrastructure process
is excluded from completed test-process absence counts; restoration is pending.

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
  Single-use native refusal launcher remains unexecuted; GUI-route instrumentation next.

No candidate or human GO. **NO-GO** remains.
