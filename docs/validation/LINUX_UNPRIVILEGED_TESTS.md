# Running Linux tests from a root container

FileCat's default Linux unit/UI test run is intended for an ordinary user. The
picture worker refuses an elevated process that cannot become an ordinary user,
and POSIX permission fixtures depend on the kernel enforcing file permissions.
Running those tests directly as root changes the contract they exercise: root can
write an owner-readable journal, while the picture worker correctly refuses to
decode. Supplying invented `SUDO_UID`/`SUDO_GID` values or disabling a production
guard is not a test-environment fix.

`eng/validation/run-linux-unprivileged-tests.py` provides an isolated ordinary-user
execution path for an otherwise root-only Linux build container. Use normal
`dotnet test` when already running as an ordinary user.

## Prerequisites and use

The helper requires Linux, Python 3.11 or newer, util-linux `setpriv`, an existing
ordinary account (`nobody` by default), and the SDK version pinned in `global.json`.
Build the trusted checkout with its pinned dependencies first:

```sh
dotnet restore FileCat.slnx --locked-mode --disable-parallel
dotnet build FileCat.slnx -c Release --no-restore --disable-build-servers -m:1
```

Then run the already-built assembly. The results directory must be new; results
from previous runs are never overwritten. Supply an absolute path to the pinned
SDK executable and to the compiled assembly. For example:

```sh
python3 eng/validation/run-linux-unprivileged-tests.py \
  --dotnet /opt/filecat-dotnet/dotnet \
  --assembly /work/FileCat/tests/FileCat.Core.Tests/bin/Release/net10.0/FileCat.Core.Tests.dll \
  --settings docs/validation/ux-resource-bounded.runsettings \
  --results /work/evidence/core-unprivileged

python3 eng/validation/run-linux-unprivileged-tests.py \
  --dotnet /opt/filecat-dotnet/dotnet \
  --assembly /work/FileCat/tests/FileCat.App.Tests/bin/Release/net10.0/FileCat.App.Tests.dll \
  --settings docs/validation/ux-resource-bounded.runsettings \
  --results /work/evidence/app-unprivileged
```

`--filter` accepts the usual VSTest filter, such as
`'FullyQualifiedName~ChecksumManifestBoundedInputTests'`. `--user` selects another
existing account with a nonzero user ID and primary group ID. The helper creates
no system accounts and changes no account configuration. `--timeout` bounds the
test process tree (1,800 seconds by default). The default runsettings limits xUnit
parallel work to four threads; `--settings` can supply another explicit file.
For a complete suite under the helper's 1.5 GiB per-process managed-heap cap, use
`--settings docs/validation/ux-resource-bounded.runsettings`. This schedules one
xUnit collection at a time. Tests retain their original inputs, assertions,
discovery, deadlines, and any concurrent work they start internally. The original
four-thread configuration remains available in `docs/validation/ux-s1.runsettings`.

The helper performs no restore or build, so it does not need package credentials,
network access, or permission to traverse the original checkout. Run large test
assemblies sequentially on memory-constrained hosts.

## Identity, isolation, and evidence

Every invocation creates a fresh private `/tmp/filecat-unprivileged-tests-*`
directory. It copies the SDK and complete compiled output, including fixture
data and apphosts. The SDK remains root-owned and read-only to the test identity;
the copied test output and temporary state belong to the selected account.
Writable copied output preserves tests that intentionally rename a sibling
apphost. Copies are independent files, so those tests cannot modify the checkout
through hard links. Staged symbolic links must remain inside their copied tree.
The stage stays inaccessible to the test account until setup has completed.

`setpriv` drops all real, effective, saved, and filesystem user/group IDs, removes
supplementary groups, clears all capability sets including the bounding set, and
sets `no_new_privs`. A child process checks the actual `/proc/self/status` values
before it executes .NET. It also verifies that each of these operations fails
with a kernel permission denial:

- Regaining root user or group IDs.
- Adding the root supplementary group.
- Reading a root-only file in the stage.
- Opening an owner-readable file for writing when its write permission is absent.

The child inherits an explicit small environment. The runner supplies private
.NET CLI, temporary, and XDG state directories. It does not fabricate a sudo
identity, inherit injection variables, or carry live-device/benchmark opt-ins
into the test run. This intentionally covers the ordinary automated suite;
native desktop, hardware, service, and opt-in benchmark gates still need their
documented environments.

The result directory retains:

| File | Evidence |
| --- | --- |
| `identity.json` | Actual kernel IDs/capability sets and successful permission-denial probes, written before .NET starts through a launcher-owned descriptor closed before exec. |
| `results.trx` | The complete VSTest case results, copied only from a bounded regular file without following a result symlink. |
| `tests.log` | Runner/test standard output and errors. |
| `tests.runsettings` | The exact root-owned configuration copy passed to the test process, retained before execution. |
| `run.json` | Source assembly hash, pinned SDK, requested account/filter, configuration path/hash, timing, exit code, reconciled per-case TRX counters, and the original reported counters. |

TRX accounting uses every `UnitTestResult`, with a distinct nonempty execution ID
and a completed `Passed`, `Failed`, or `NotExecuted` outcome. The number of records
must equal the nonzero reported total, and their passed/failed counts must agree
with the summary. Some xUnit/VSTest reports retain each native skip as a
`NotExecuted` record while leaving the aggregate `notExecuted` counter at zero.
The helper counts those records in `result.counters.notExecuted` and preserves the
unchanged aggregate values in `result.reported_counters`. It rejects duplicate,
missing, unfinished, or inconsistent results. Complete runs that contain failures
retain their counters in `run.json` and still return a nonzero exit code.

An empty selection, incomplete TRX, missing identity evidence, failed privilege
probe, nonzero test exit, or failed test result cannot be reported as success.
A timeout kills the helper's own process group and retains the log and receipt.
Temporary copies are removed after execution. The original SDK, checkout,
workspace permissions, production guards, and system configuration are untouched.

This is a privilege-correct test runner for a trusted build, not a general-purpose
sandbox for arbitrary code: tests can use their ordinary account's host access.

## Runner regression tests

```sh
python3 -m unittest discover -s eng/validation \
  -p test_run_linux_unprivileged_tests.py -v
```

The Python tests cover complete identity validation, every capability set, saved
and filesystem IDs, refusal of partial/missing permission evidence, environment
isolation, independent copies and link containment, bounded regular result
collection, and nonempty complete TRX accounting, including omitted aggregate
skips, duplicate execution IDs, unfinished results, and complete failing runs.
Every real test invocation also
executes the kernel-level privilege and file-permission probes before the test
assembly is allowed to start.

## Checksum input regression contract

`ChecksumManifestBoundedInputTests` tests two separate protections. An accessible
`/dev/zero` device must be refused as a non-regular input before parsing or content
budgeting. A regular UTF-8 manifest must accept an exact byte budget, retain the
complete bytes actually parsed, and reject a one-byte-short budget. The latter
cases include multibyte filenames and inputs crossing the reader's 64 KiB chunk
boundary. Neither test requires the device guard to pretend that a special file
is an oversized regular manifest.

## Verified ordinary-user findings

The original 31 App failures were rerun with the same test assertions under the
verified ordinary identity: 30 picture-worker cases and the interrupted-journal
permission case all passed. The worker's production privilege refusal and the
journal's permission checks remain enabled. Each run retains proof that all four
user/group IDs were dropped, all five capability sets were empty, and the
permission-denial probes succeeded.

The first complete ordinary-user Core run exposed a separate trash-location
defect: an account without a usable profile directory could not use a valid
absolute `XDG_DATA_HOME`. Linux now resolves the configured data directory before
consulting the profile. A valid profile is required only for the Linux
`.local/share` fallback or the macOS trash location. Relative or malformed XDG
paths are ignored; they are never resolved against the working directory. This
follows the [XDG Base Directory specification](https://specifications.freedesktop.org/basedir/0.8/)
and the [Trash specification](https://specifications.freedesktop.org/trash/1.0/).
Existing ownership, symbolic-link, volume, and move/restore checks are unchanged.

`UnixTrashEnvironmentTests` adds 25 cases for configured-directory priority,
missing profiles, relative paths, NUL-containing paths, Linux fallback, and macOS
behavior. Its resolver inputs are explicit: the cases do not mutate process-wide
environment variables or create trash directories. The existing `UnixTrashTests`
also exercise the public resolver in the actual ordinary-user environment. The
combined focused gate passed all 32 cases under the verified ordinary identity,
including all seven existing behavior tests and the 25 new resolver cases.

The diagnostic Core run retained 4,511 complete case records: 4,400 passed, one
failed at the trash-location defect, and 110 were skipped. The preceding root run
contained 4,508 cases. The increase is exactly three: one checksum budget fact was
replaced by four boundary-test rows. The renamed device-admission case preserves
its one-case count. Two former root-only skips executed and passed with the real
ordinary identity, reducing skips from 112 to 110. Three PE inspector cases use
the copied assembly paths in their displayed arguments but retain the same case
counts and outcomes. This discovery comparison distinguishes added coverage and
environment-dependent execution from missing tests. The corrected production
boundary was rerun with its 25 additional resolver cases, keeping every diagnostic
result separate from the complete passing run below.

### Full-suite resource scheduling

The corrected Core boundary was first run with the existing four-thread
configuration. It retained all 4,536 case records: 4,425 passed, one failed with
`OutOfMemoryException`, and 110 were skipped. The corrected trash behavior and all
25 new resolver cases passed. The unchanged binary-navigation test that failed
had passed in both preceding complete runs. It allocates two 1 MiB buffers for
each navigation call while walking hundreds of difference ranges.

The completed TRX records show that the failure overlapped the FAT32 recovery
fuzz case, the six-worker page-cache stress case, and network/metadata fixtures.
The fuzz case copies a 40 MiB image for each of its 200 rounds; it passed its own
per-round allocation assertion. The test apphost's observed peak resident memory
was 1,681,988 KiB under the unchanged 1.5 GiB managed-heap cap. This is evidence of
aggregate test pressure; it does not establish a new comparison algorithm defect.
The failed run, its exact source-assembly hash, kernel identity, original and
reconciled counters, memory observation, and per-case overlap analysis remain
separate from the subsequent isolated and complete reruns.

The isolated `FileCompareTests` recheck passed all 19 cases under the same heap
cap and original four-thread configuration, using the identical Core assembly.
The previously failing navigation case completed successfully in 0.686 seconds.

The resource-bounded configuration reduces overlap between test collections. It
does not raise the heap cap, exclude tests, alter fuzz rounds, or remove the
page-cache test's six concurrent workers. Retain the configuration path and
SHA-256 alongside each final project result so its scheduling is reproducible.

The complete serial Core rerun passed: 4,426 passed, zero failed, and 110 explicit
skips across all 4,536 distinct execution records, with a runner elapsed time of
554.416 seconds. Its case-discovery multiset matches the complete parallel run;
only the three PE inspector cases' temporary stage paths need normalization.
The executed case intervals have a maximum overlap of one, confirming that the
configuration took effect. Both runs used the same compiled Core assembly and
heap cap, and the final evidence retains the exact executed configuration bytes,
path, and SHA-256.
