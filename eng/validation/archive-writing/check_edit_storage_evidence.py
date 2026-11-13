#!/usr/bin/env python3
"""Check a sealed run's archive before it is cited as native-edit-storage evidence.

A route is listed in `NativeEditStorage.Qualified` only with its own evidence (`AWQ-<rid>-<fs>/edit`), and that evidence
means one thing: the pins of the listing ran on that machine, with the native store as the default, where a native edit
test fails instead of skipping. The release manifest can only check that such an entry exists; this checks what is in it.

It reads a run's evidence archive (`<run>-evidence.zip` from the Windows runner, `<run>-evidence.tar.gz` from the Unix
one) and requires all of:

  * the machine record names `FILECAT_EDIT_NATIVE_REQUIRED=1`, so a native edit test could not skip;
  * every suite in the summary passed, with no failure anywhere in the run;
  * the pins of the listing are in its results, each with the outcome Passed - the application's composition pin, the
    route list's own pin, and the dual-root case, which is what a renamed, re-filtered or dropped pin would fail;
  * no test stepped aside because this process or folder could not hold native storage.

A lane's name is not evidence of anything, so nothing here reads one: an archive counts because it carries the pins,
whether it is a focused edit lane or a full regression.

Usage: check_edit_storage_evidence.py ARCHIVE... [--out REPORT]
Exits non-zero, naming what failed, if any archive does not meet all of it.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import tarfile
import zipfile
from pathlib import Path

GATE = 'FILECAT_EDIT_NATIVE_REQUIRED=1'

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


def machine_env(archive: Archive) -> str:
    """The environment the runner recorded for this run, however its machine record is shaped."""
    for leaf in ('machine.json', 'machine.txt'):
        for name in archive.find(leaf):
            text = archive.read(name)
            if leaf == 'machine.json':
                try:
                    return str(json.loads(text).get('Env', ''))
                except json.JSONDecodeError:
                    return text
            return text
    return ''


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
        if GATE not in machine_env(archive):
            problems.append('its machine record does not name ' + GATE)
        summaries = archive.find('summary.json')
        if not summaries:
            return problems + ['it carries no summary.json'], facts
        summary = json.loads(archive.read(summaries[0]))
        suites = summary.get('suites', {})
        facts['suites'] = suites
        if not suites:
            problems.append('its summary names no suite')
        if summary.get('failed'):
            problems.append('it failed: ' + ', '.join(map(str, summary['failed'])))
        for name, counters in suites.items():
            if int(counters.get('failed', 0)) != 0:
                problems.append('suite %s has %s failures' % (name, counters['failed']))
        for name in archive.find('status.txt'):
            if archive.read(name).strip() != 'PASS':
                problems.append('its status is not PASS')
        results = outcomes(archive)
        if not results:
            problems.append('it carries no test results to read')
        pins = {}
        for pin in PINS:
            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith(pin)}
            if not matching:
                problems.append('it does not hold the pin ' + pin)
            elif any(outcome != {'Passed'} for outcome in matching.values()):
                problems.append('the pin %s is not Passed: %s' % (pin, sorted(
                    sorted(outcome) for outcome in matching.values())))
            pins[pin] = sorted(matching)
        facts['pins'] = pins
        for log in archive.leaves(prefix='test-'):
            text = archive.read(log)
            for reason in STEP_ASIDE:
                if reason in text:
                    problems.append('a test stepped aside in %s: %r' % (log, reason))
    return problems, facts


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
        problems, facts = check(path)
        if problems:
            failed = True
            lines.append('%s: NOT edit-storage evidence' % path.name)
            lines.extend('    ' + problem for problem in problems)
            continue
        suites = facts['suites']
        assert isinstance(suites, dict)
        passed = sum(int(counters.get('passed', 0)) for counters in suites.values())
        skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values())
        lines.append('%s: edit-storage evidence, %d passed, %d skipped, no failure, %s in force'
                     % (path.name, passed, skipped, GATE))
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
