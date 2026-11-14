#!/usr/bin/env python3
"""Check a sealed run's archive before it is cited as native-edit-storage evidence.

A route is listed in `NativeEditStorage.Qualified` only with its own evidence (`AWQ-<rid>-<fs>/edit`), and that evidence
means one thing: the pins of the listing ran on that machine, with the native store as the default, where a native edit
test fails instead of skipping. The release manifest checks that such an entry exists and that this report accepts it;
this is what reads what is inside it.

It reads a run's evidence archive (`<run>-evidence.zip` from the Windows runner, `<run>-evidence.tar.gz` from the Unix
one) and requires all of:

  * the machine record names `FILECAT_EDIT_NATIVE_REQUIRED=1`, so a native edit test could not skip;
  * the machine record says which system ran it, which is what binds an archive to a route rather than to a name;
  * every suite in the summary passed, with no failure anywhere in the run;
  * the pins of the listing are in its results, each with the outcome Passed - the application's composition pin, the
    route list's own pin, and the dual-root case, which is what a renamed, re-filtered or dropped pin would fail;
  * no test stepped aside because this process or folder could not hold native storage.

A lane's name is not evidence of anything, so nothing here reads one: an archive counts because it carries the pins,
whether it is a focused edit lane or a full regression. The report names an archive it accepts by its SHA-256 and the
system that ran it, which is what binds this reading to that archive and that archive to a route: the release manifest
refuses a listed edit-storage route unless the report it carries under the route's own `.../check` accepts the archive
it carries under the route's ID, on the system that route names. A name alone would let a report be moved to an archive
it never read, and a digest alone let one machine's lane stand for all three routes - both were accepted until the
twenty-ninth and thirtieth reviews tried them.

Usage: check_edit_storage_evidence.py ARCHIVE... [--out REPORT]
Exits non-zero, naming what failed, if any archive does not meet all of it.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import tarfile
import zipfile
from pathlib import Path

# What each runner's own machine record calls the system it ran on: `Os` in the Windows runner's `machine.json`,
# the first line of `uname -a` in the Unix runner's `machine.txt`. The architecture is in that line and not in the
# Windows record, so this names the system and the manifest refuses to bind two routes that share one.
SYSTEMS = (('Microsoft Windows', 'windows'), ('Windows', 'windows'), ('Linux', 'linux'), ('Darwin', 'macos'))
GATE_NAME = 'FILECAT_EDIT_NATIVE_REQUIRED'
GATE_VALUE = '1'
GATE = GATE_NAME + '=' + GATE_VALUE

# The pins a listing stands on, by their method names (each lives in the suite its platform runs: the application's
# composition pin, the Core route list, and the dual-root case in the platform's own storage tests).
PINS = (
    'Ordinary_startup_composes_native_storage_where_listed_and_the_legacy_store_elsewhere',
    'This_build_lists_native_edit_storage_on_the_owner_s_routes_only',
    'Legacy_and_native_roots_keep_their_own_sessions_and_revisions',
)

# What a native edit test says when it steps aside because this process or folder cannot hold native storage. With the
# gate in force these become failures, so in sealed edit-storage evidence none of them may appear at all.
STEP_ASIDE = (
    'Native edit storage cannot be held for this test folder',
    'Native edit storage refuses an elevated or root process',
    'Native edit storage requires an unelevated process.',
    'Native edit storage refuses root; run as an ordinary user.',
    'Edit sessions refuse an elevated token; run unelevated.',
    "The fixture's filesystem cannot be established here.",
    'is not a listed native edit storage route for',
)

RESULT = re.compile(r'<UnitTestResult\b[^>]*?\btestName="([^"]+)"[^>]*?\boutcome="([^"]+)"', re.S)
RESULT_REVERSED = re.compile(r'<UnitTestResult\b[^>]*?\boutcome="([^"]+)"[^>]*?\btestName="([^"]+)"', re.S)


class Archive:
    """A sealed run's files, by name, from either runner's archive format."""

    def __init__(self, path: Path) -> None:
        self.path = path
        if path.name.endswith('.zip'):
            self._zip: zipfile.ZipFile | None = zipfile.ZipFile(path)
            self._names = self._zip.namelist()
            self._tar: tarfile.TarFile | None = None
        else:
            self._tar = tarfile.open(path)
            self._names = self._tar.getnames()
            self._zip = None

    def __enter__(self) -> 'Archive':
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def names(self) -> list[str]:
        return list(self._names)

    def leaves(self, suffix: str = '', prefix: str = '') -> list[str]:
        result = []
        for name in self._names:
            leaf = name.replace('\\', '/').split('/')[-1]
            if leaf.startswith(prefix) and leaf.endswith(suffix):
                result.append(name)
        return result

    def find(self, leaf: str) -> list[str]:
        return [name for name in self._names if name.replace('\\', '/').split('/')[-1] == leaf]

    def read(self, name: str) -> str:
        if self._zip is not None:
            return self._zip.read(name).decode('utf-8', 'replace')
        handle = self._tar.extractfile(name) if self._tar is not None else None
        return '' if handle is None else handle.read().decode('utf-8', 'replace')

    def close(self) -> None:
        for handle in (self._zip, self._tar):
            if handle is not None:
                handle.close()


def machine_env(archive: Archive) -> str | None:
    """The environment the runner recorded for this run, however its machine record is shaped (None: no record at all)."""
    for leaf in ('machine.json', 'machine.txt'):
        for name in archive.find(leaf):
            text = archive.read(name)
            if leaf == 'machine.json':
                try:
                    return str(json.loads(text).get('Env', ''))
                except json.JSONDecodeError:
                    return text
            return text
    return None


def machine_system(archive: Archive) -> str | None:
    """Which system ran this, out of its own machine record (None: it does not say).

    An archive bound only by its digest is bound to no route: the thirtieth review carried the Windows lane's own
    archive, with its own true report, under all three listed routes, and the manifest took it for all three.
    """
    for leaf in ('machine.json', 'machine.txt'):
        for name in archive.find(leaf):
            text = archive.read(name)
            if leaf == 'machine.json':
                try:
                    text = str(json.loads(text).get('Os', ''))
                except json.JSONDecodeError:
                    pass
            first = next((line for line in text.splitlines() if line.strip()), '')
            return next((system for named, system in SYSTEMS if first.strip().startswith(named)), None)
    return None


def gate_value(recorded: str) -> str | None:
    """What this run set the gate to, or None if it did not set it at all.

    A substring test is not enough. Every reader of this variable compares it to "1" exactly, and both runners pass a
    value through verbatim, so a run recording `FILECAT_EDIT_NATIVE_REQUIRED=10` had no gate at all: its native edit
    tests skipped where they were meant to fail, and its composition pin was never armed.
    """
    # The Unix runner writes one `env=NAME=VALUE` line per variable; the Windows one records them in one
    # semicolon-separated string. So each item is read with its own `env=` prefix removed before its first `=`.
    for item in re.split(r'[;\s]+', recorded):
        name, separator, value = item.strip().removeprefix('env=').removeprefix('env:').partition('=')
        if separator and name.strip() == GATE_NAME:
            return value.strip()
    return None


def outcomes(archive: Archive) -> dict[str, set[str]]:
    """Every test result in the run's TRX files: its name to the outcomes it has (a test may be a theory's rows)."""
    found: dict[str, set[str]] = {}
    for name in archive.leaves(suffix='.trx'):
        text = archive.read(name)
        for pattern, order in ((RESULT, 'name-first'), (RESULT_REVERSED, 'outcome-first')):
            for match in pattern.finditer(text):
                test, outcome = match.groups() if order == 'name-first' else match.groups()[::-1]
                found.setdefault(test, set()).add(outcome)
    return found


def check(path: Path) -> tuple[list[str], dict[str, object]]:
    """Everything wrong with this archive as edit-storage evidence (empty when it stands), and what it holds."""
    problems: list[str] = []
    facts: dict[str, object] = {}
    with Archive(path) as archive:
        recorded = machine_env(archive)
        if recorded is None:
            problems.append('it carries no machine record, so nothing in it says the gate was in force')
        elif (value := gate_value(recorded)) is None:
            problems.append('its machine record does not name ' + GATE_NAME)
        elif value != GATE_VALUE:
            problems.append('its machine record sets %s to %r, not %r, so a native edit test could step aside'
                            % (GATE_NAME, value, GATE_VALUE))
        facts['system'] = system = machine_system(archive)
        if recorded is not None and system is None:
            problems.append('its machine record does not say which system ran it, so nothing in it binds it to a route')
        summaries = archive.find('summary.json')
        if not summaries:
            return problems + ['it carries no summary.json'], facts
        # Every summary in the archive, not the first one by name: a run that sealed more than one has to pass on all
        # of them, exactly as each status.txt below has to say PASS.
        suites: dict[str, object] = {}
        for name in sorted(summaries):
            summary = json.loads(archive.read(name))
            for suite, counters in (summary.get('suites') or {}).items():
                suites[suite] = counters
            if not summary.get('suites'):
                problems.append('%s names no suite' % name)
            if summary.get('failed'):
                problems.append('it failed: ' + ', '.join(map(str, summary['failed'])))
            for suite, counters in (summary.get('suites') or {}).items():
                if int(counters.get('failed', 0)) != 0:
                    problems.append('suite %s has %s failures' % (suite, counters['failed']))
            if summary.get('ok') is not True:
                problems.append('the runner did not call this run ok')
            codes = summary.get('exit_codes')
            if not isinstance(codes, dict) or not codes:
                problems.append('its summary carries no exit code')
            else:
                for suite, code in sorted(codes.items()):
                    if code != 0:
                        problems.append('suite %s exited %s' % (suite, code))
        facts['suites'] = suites
        if not suites:
            problems.append('its summary names no suite')
        # A file that is not there is not a check that passed: each of these carries one of the requirements above.
        statuses = archive.find('status.txt')
        if not statuses:
            problems.append('it carries no status.txt')
        for name in statuses:
            if archive.read(name).strip() != 'PASS':
                problems.append('its status is not PASS')
        # The step-aside scan reads these: an archive with none, or with nothing in them, has not been scanned.
        logs = archive.leaves(prefix='test-')
        if not logs:
            problems.append('it carries no test output to read')
        elif not any(archive.read(log).strip() for log in logs):
            problems.append('its test output is empty')
        results = outcomes(archive)
        if not results:
            problems.append('it carries no test results to read')
        pins = {}
        for pin in PINS:
            # The whole method name, so a test merely ending in the pin's text is not one of its cases.
            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith('.' + pin)}
            if not matching:
                problems.append('it does not hold the pin ' + pin)
            else:
                # The dual-root pin is the same method name in the Core suite's Unix class and in the Windows platform
                # suite's own class, and a Windows run carries both: there the Unix one is NotExecuted, with "Run the
                # Windows native storage lane." for its reason. So a pin stands when it passed somewhere in this run and
                # failed nowhere - a renamed, re-filtered or dropped pin still has no Passed row anywhere.
                wrong = {test: sorted(outcome) for test, outcome in matching.items()
                         if not outcome <= {'Passed', 'NotExecuted'}}
                if wrong:
                    problems.append('the pin %s did not pass: %s' % (pin, sorted(wrong.items())))
                elif not any('Passed' in outcome for outcome in matching.values()):
                    problems.append('no case of the pin %s ran: %s' % (pin, sorted(matching)))
            pins[pin] = sorted('%s %s' % (test, '+'.join(sorted(outcome))) for test, outcome in matching.items())
        facts['pins'] = pins
        for log in archive.leaves(prefix='test-'):
            text = archive.read(log)
            for reason in STEP_ASIDE:
                if reason in text:
                    problems.append('a test stepped aside in %s: %r' % (log, reason))
    return problems, facts


def accepted_digest(path: Path) -> str:
    """The archive this report accepts, named as the release manifest names every file it carries: by its bytes."""
    hashed = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1 << 20), b''):
            hashed.update(block)
    return hashed.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archives', nargs='+', type=Path)
    parser.add_argument('--out', type=Path, help='write the report here as well as to the output')
    arguments = parser.parse_args()
    lines: list[str] = []
    failed = False
    for path in arguments.archives:
        if not path.is_file():
            lines.append('%s: missing' % path.name)
            failed = True
            continue
        try:
            problems, facts = check(path)
        except (OSError, zipfile.BadZipFile, tarfile.TarError, json.JSONDecodeError, ValueError, TypeError,
                AttributeError, KeyError) as error:
            failed = True
            lines.append('%s: NOT edit-storage evidence' % path.name)
            lines.append('    it cannot be read as a sealed run: %s: %s' % (type(error).__name__, error))
            continue
        if problems:
            failed = True
            lines.append('%s: NOT edit-storage evidence' % path.name)
            lines.extend('    ' + problem for problem in problems)
            continue
        suites = facts['suites']
        assert isinstance(suites, dict)
        passed = sum(int(counters.get('passed', 0)) for counters in suites.values())
        skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values())
        lines.append('%s: edit-storage evidence, %d passed, %d skipped, no failure, %s in force, %s, sha256 %s'
                     % (path.name, passed, skipped, GATE, facts['system'], accepted_digest(path)))
        for name, counters in sorted(suites.items()):
            lines.append('    %s: %s of %s passed, %s skipped' % (name, counters.get('passed'),
                                                                  counters.get('total'), counters.get('skipped')))
        pins = facts['pins']
        assert isinstance(pins, dict)
        for pin, tests in sorted(pins.items()):
            lines.append('    pin %s: %s' % (pin, ', '.join(tests)))
    report = '\n'.join(lines) + '\n'
    sys.stdout.write(report)
    if arguments.out is not None:
        arguments.out.write_text(report, encoding='utf-8', newline='\n')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
