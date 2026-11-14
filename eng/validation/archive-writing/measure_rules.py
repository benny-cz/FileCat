#!/usr/bin/env python3
"""Which case fails without each rule of the edit-storage evidence check? Run this instead of reasoning about it.

Nine review rounds hardened `check_edit_storage_evidence.py`, and twice over they found not a defect in it but a false
reason written beside a rule: "this guard cannot be reached", "two rules are left with no case". Both were arguments,
and both were wrong. So the arguments are replaced by this: every rule of the check is listed below with one weakening
of it, this applies them one at a time to a copy of the check, runs the check's own suite against that copy, and prints
the cases that notice. A rule no case notices is named as such - not explained.

    python -I -X utf8 eng/validation/archive-writing/measure_rules.py [--rule <text to match>]

It copies the check and its suite into a temporary folder and runs them there, so the repository is never modified, and
it runs the suite with `-B` and no `__pycache__`, because the suite loads the check through importlib and a same-size
mutant written within the second would otherwise run the previous one's bytecode (which labelled four measurements one
mutant behind when this was first done by hand).

Each entry is (what the rule is, the committed text, the weakened text). The committed text must appear exactly once.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
CHECK = HERE / 'check_edit_storage_evidence.py'
SUITE = HERE / 'test_check_edit_storage_evidence.py'

RULES: list[tuple[str, str, str]] = [
    ('the gate recorded as exactly 1',
     "        elif value != GATE_VALUE:",
     "        elif False and value != GATE_VALUE:"),
    ('the gate named at all',
     "                problems.append('its machine record does not name ' + GATE_NAME)",
     "                problems.append('its machine record is quiet about ' + GATE_NAME)"),
    ("the gate's name read whole, not as a part",
     "        if separator and name.strip() == GATE_NAME:",
     "        if separator and GATE_NAME in name:"),
    ('one machine record, not several',
     "        elif len(records) > 1:",
     "        elif len(records) > 1 and False:"),
    ('a machine record whose members do not repeat',
     "            if records[0].replace('\\\\', '/').endswith('machine.json') and \\",
     "            if False and records[0].replace('\\\\', '/').endswith('machine.json') and \\"),
    ('the system read from how the record line begins',
     "    return next((system for named, system in SYSTEMS if first.strip().startswith(named)), None)",
     "    return next((system for named, system in SYSTEMS if named in first), None)"),
    ('the system named at all',
     "            if system is None:",
     "            if False and system is None:"),
    ('the family read from each platform class on its own',
     "    windows = any(name.endswith('.WindowsEditStorageTests.' + PINS[2]) for name in passed)",
     "    windows = any('WindowsEditStorageTests' in name for name in passed)"),
    ('the Unix platform class asked for by its whole name',
     "    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in passed)",
     "    unix = any('UnixEditStorageTests' in name for name in passed)"),
    ('the record and the run agreeing about the system',
     "        elif system is not None and family != ('windows' if system == 'windows' else 'unix'):",
     "        elif False and system is not None and family != ('windows' if system == 'windows' else 'unix'):"),
    ('a run that names neither platform class refused',
     "        elif family is None:",
     "        elif False and family is None:"),
    ('every summary read, not the first by name',
     "        for name in sorted(summaries):",
     "        for name in sorted(summaries)[:1]:"),
    ('every status file read, not the first',
     "        for name in statuses:",
     "        for name in statuses[:1]:"),
    ('a summary that names no suite',
     "            if not summary.get('suites'):",
     "            if not summary.get('suites') and False:"),
    ('the pins each passing somewhere and failing nowhere',
     "                if wrong:",
     "                if False and wrong:"),
    ('a pin whose every case stepped aside',
     "                elif not any('Passed' in outcome for outcome in matching.values()):",
     "                elif False and not any('Passed' in outcome for outcome in matching.values()):"),
    ('a step-aside reason anywhere in the logs or the results',
     "                if reason in text:",
     "                if False and reason in text:"),
    ('a log and a results file found by how their names begin and end',
     "            if leaf.startswith(prefix) and leaf.endswith(suffix):",
     "            if prefix in leaf and suffix in leaf:"),
    ('a name matched as the whole leaf',
     "        return [name for name in self._names if name.replace('\\\\', '/').split('/')[-1] == leaf]",
     "        return [name for name in self._names if leaf in name.replace('\\\\', '/').split('/')[-1]]"),
    ('a name the archive holds more than once',
     "        if (repeated := archive.repeated()):",
     "        if (repeated := archive.repeated()) and False:"),
    ('folder entries left out of the members',
     "            items = [item for item in self._tar.getmembers() if not item.isdir()]",
     "            items = list(self._tar.getmembers())"),
    ('a member read through the member it listed',
     "        item = self._members[name]",
     "        item = name"),
    ("the listing accounting for the file at all",
     "        if (gap := archive.listing_gap()) is not None:",
     "        if (gap := archive.listing_gap()) is not None and False:"),
    ('an entry pointing at a member of this file',
     "                if len(header) < 30 or header[:4] != b'PK\\x03\\x04':",
     "                if False and (len(header) < 30 or header[:4] != b'PK\\x03\\x04'):"),
    ("the name the file gives a member",
     "                if written != item.orig_filename:",
     "                if False and written != item.orig_filename:"),
    ("the size the file states for a member",
     "                elif stated != item.compress_size:",
     "                elif False and stated != item.compress_size:"),
    ("a descriptor's width from both of its size fields",
     "                        if compressed == item.compress_size and plain == item.file_size:",
     "                        if compressed == item.compress_size:"),
    ("the local header's own name and extra lengths",
     "                name, extra = struct.unpack('<HH', header[26:30])",
     "                name, extra = len(item.filename.encode('utf-8')), len(item.extra)"),
    ('members that do not overlap',
     "            if start < reach:",
     "            if False and start < reach:"),
    ('the first member at the start of the file',
     "        if first != 0:",
     "        if False and first != 0:"),
    ('the last member not past its own directory',
     "        if reach > directory:",
     "        if False and reach > directory:"),
    ('the listing covering the bytes before the directory',
     "        if reach != directory or covered != directory:",
     "        if reach != directory:"),
    ('the listing reaching the directory',
     "        if reach != directory or covered != directory:",
     "        if covered != directory:"),
    ('a gzipped tar read as the whole of its file',
     "            return self._gzip_gap()",
     "            return None"),
    ('a tar that is gzipped at all',
     "                return 'it is not gzipped, as a sealed run is'",
     "                return None"),
    ("what follows a tar's last member being its own padding",
     "                    if tail.strip(b'\\x00'):",
     "                    if False and tail.strip(b'\\x00'):"),
    ('a sparse member measured by what it holds',
     "            stored = sum(length for _, length in item.sparse) if getattr(item, 'sparse', None) else item.size",
     "            stored = item.size"),
    ('a compressed stream that cannot be read',
     "            except zlib.error as error:",
     "            except KeyError as error:"),
    ("a tar whose last member claims more than was compressed",
     "        return None if at >= end else 'its last member runs %d bytes past what was compressed' % (end - at)",
     "        return None"),
    ('the verdict naming the system it read',
     "                     % (path.name, passed, skipped, GATE, facts['system'], accepted_digest(path)))",
     "                     % (path.name, passed, skipped, GATE, 'windows', accepted_digest(path)))"),
    ('the verdict naming the digest of what it read',
     "                     % (path.name, passed, skipped, GATE, facts['system'], accepted_digest(path)))",
     "                     % (path.name, passed, skipped, GATE, facts['system'], 'f' * 64))"),
    ('an archive it cannot read named and the rest still reported',
     "                AttributeError, KeyError, NotImplementedError) as error:",
     "                AttributeError, KeyError) as error:"),
]


def run(folder: Path) -> tuple[str, list[str]]:
    """The suite in `folder`, and the cases that failed."""
    shutil.rmtree(folder / '__pycache__', ignore_errors=True)
    done = subprocess.run([sys.executable, '-I', '-B', '-X', 'utf8', '-m', 'unittest', 'discover',
                           '-s', str(folder), '-p', SUITE.name, '-v'], capture_output=True, text=True, cwd=folder)
    text = done.stdout + done.stderr
    ran = re.search(r'Ran (\d+) tests', text)
    return (ran.group(1) if ran else '?'), sorted({name[len('test_'):] for name in
                                                   re.findall(r'(?:FAIL|ERROR): (test_\w+)', text)})


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rule', help='measure only the rules whose description holds this text')
    arguments = parser.parse_args()
    committed = CHECK.read_text(encoding='utf-8')
    unmeasured, wrong = [], []
    with tempfile.TemporaryDirectory() as directory:
        folder = Path(directory)
        shutil.copy(SUITE, folder / SUITE.name)
        shutil.copy(CHECK, folder / CHECK.name)
        ran, failed = run(folder)
        print('as committed: %s cases, %s' % (ran, 'all passing' if not failed else 'FAILING: ' + ', '.join(failed)))
        if failed or ran == '?':
            return 2
        for what, old, new in RULES:
            if arguments.rule and arguments.rule not in what:
                continue
            if committed.count(old) != 1:
                wrong.append(what)
                print('%-62s the rule is not there once (%d)' % (what, committed.count(old)))
                continue
            (folder / CHECK.name).write_text(committed.replace(old, new), encoding='utf-8', newline='')
            ran, failed = run(folder)
            if ran == '?':
                wrong.append(what)
                print('%-62s the suite did not run' % what)
            elif failed:
                print('%-62s %s' % (what, ', '.join(failed)))
            else:
                unmeasured.append(what)
                print('%-62s NO CASE' % what)
            (folder / CHECK.name).write_text(committed, encoding='utf-8', newline='')
    print()
    print('%d rules measured; %d with no case%s' % (len(RULES) - len(wrong), len(unmeasured),
                                                    ': ' + '; '.join(unmeasured) if unmeasured else ''))
    if wrong:
        print('%d entries no longer match the check and have to be rewritten: %s' % (len(wrong), '; '.join(wrong)))
    return 1 if wrong else 0


if __name__ == '__main__':
    raise SystemExit(main())
