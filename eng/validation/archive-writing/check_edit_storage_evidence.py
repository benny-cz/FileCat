#!/usr/bin/env python3
"""Check a sealed run's archive before it is cited as native-edit-storage evidence.

A route is listed in `NativeEditStorage.Qualified` only with its own evidence (`AWQ-<rid>-<fs>/edit`), and that evidence
means one thing: the edit suites ran on that machine with the native store as the default, where a native edit test
fails instead of skipping. The release manifest can only check that such an entry exists; this checks what is inside it.

It reads a run's evidence archive (`<run>-evidence.zip` from the Windows runner, `<run>-evidence.tar.gz` from the Unix
one) and requires all of:

  * the machine record names `FILECAT_EDIT_NATIVE_REQUIRED=1`, so a native edit test could not skip;
  * every suite in the summary passed with no failure;
  * at least one suite is an edit suite (its lane name or its project names the edit classes), with passes;
  * the test log holds no skip whose reason is that native storage was unavailable.

Usage: check_edit_storage_evidence.py ARCHIVE... [--out REPORT]
Exits non-zero, naming what failed, if any archive does not meet all of it.
"""
from __future__ import annotations

import argparse
import json
import sys
import tarfile
import zipfile
from pathlib import Path

GATE = 'FILECAT_EDIT_NATIVE_REQUIRED=1'
# What a native edit test says when it steps aside because this process or folder cannot hold native storage. With the
# gate in force these become failures, so in sealed edit-storage evidence none of them may appear at all.
STEP_ASIDE = (
    'Native edit storage cannot be held for this test folder',
    'Native edit storage refuses an elevated or root process',
    'is not a listed native edit storage route for',
)


class Archive:
    """A sealed run's files, by name, from either runner's archive format."""

    def __init__(self, path: Path) -> None:
        self.path = path
        if path.name.endswith('.zip'):
            self._zip = zipfile.ZipFile(path)
            self._names = self._zip.namelist()
            self._tar = None
        else:
            self._tar = tarfile.open(path)
            self._names = self._tar.getnames()
            self._zip = None

    def names(self) -> list[str]:
        return list(self._names)

    def find(self, leaf: str) -> list[str]:
        return [name for name in self._names if name.replace('\\', '/').split('/')[-1] == leaf]

    def read(self, name: str) -> str:
        if self._zip is not None:
            return self._zip.read(name).decode('utf-8', 'replace')
        handle = self._tar.extractfile(name)
        return '' if handle is None else handle.read().decode('utf-8', 'replace')

    def close(self) -> None:
        (self._zip or self._tar).close()


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


def check(path: Path) -> list[str]:
    """Everything wrong with this archive as edit-storage evidence; empty when it stands."""
    problems: list[str] = []
    archive = Archive(path)
    try:
        if GATE not in machine_env(archive):
            problems.append('its machine record does not name ' + GATE)
        summaries = archive.find('summary.json')
        if not summaries:
            problems.append('it carries no summary.json')
            return problems
        summary = json.loads(archive.read(summaries[0]))
        suites = summary.get('suites', {})
        if not suites:
            problems.append('its summary names no suite')
        if summary.get('failed'):
            problems.append('it failed: ' + ', '.join(map(str, summary['failed'])))
        for name, counters in suites.items():
            if int(counters.get('failed', 0)) != 0:
                problems.append('suite %s has %s failures' % (name, counters['failed']))
        edit = {name: counters for name, counters in suites.items() if 'Edit' in name}
        if not edit:
            problems.append('no suite of it is an edit suite (a lane whose name holds "Edit")')
        for name, counters in edit.items():
            if int(counters.get('passed', 0)) <= 0:
                problems.append('edit suite %s passed nothing' % name)
        for name in archive.find('status.txt'):
            if archive.read(name).strip() != 'PASS':
                problems.append('its status is not PASS')
        for log in (name for name in archive.names() if name.replace('\\', '/').split('/')[-1].startswith('test-')):
            text = archive.read(log)
            for reason in STEP_ASIDE:
                if reason in text:
                    problems.append('a test stepped aside in %s: %r' % (log, reason))
    finally:
        archive.close()
    return problems


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
        problems = check(path)
        if problems:
            failed = True
            lines.append('%s: NOT edit-storage evidence' % path.name)
            lines.extend('    ' + problem for problem in problems)
        else:
            archive = Archive(path)
            try:
                summary = json.loads(archive.read(archive.find('summary.json')[0]))
            finally:
                archive.close()
            passed = sum(int(counters.get('passed', 0)) for counters in summary['suites'].values())
            skipped = sum(int(counters.get('skipped', 0)) for counters in summary['suites'].values())
            lines.append('%s: edit-storage evidence, %d passed, %d skipped, no failure, %s in force'
                         % (path.name, passed, skipped, GATE))
            for name, counters in sorted(summary['suites'].items()):
                lines.append('    %s: %s of %s passed, %s skipped' % (name, counters.get('passed'),
                                                                      counters.get('total'), counters.get('skipped')))
    report = '\n'.join(lines) + '\n'
    sys.stdout.write(report)
    if arguments.out is not None:
        arguments.out.write_text(report, encoding='utf-8', newline='\n')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
