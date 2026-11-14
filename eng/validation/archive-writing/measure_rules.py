#!/usr/bin/env python3
"""Which case fails without each rule of the edit-storage evidence check? Run this instead of reasoning about it.

Review rounds hardened `check_edit_storage_evidence.py`, and three times over they found not a defect in it but a false
claim written beside a rule: "this guard cannot be reached", "two rules are left with no case", and - of this file's
own first version - "every rule of the check is listed below". All three were arguments, and all three were wrong.

So: the rules listed below are weakened one at a time, on a copy, with the check's own suite run against that copy, and
the cases that notice are printed. **This list is not the check.** A rule that is not an entry here is invisible to
this, which is how the line in `main()` that turns findings into a refusal sat unmeasured for a round - so a rule added
to the check wants an entry here, and `--audit` prints the check's own conditions that no entry names, to make a
missing one easier to see.

Each entry carries what is expected of it: nothing, for a rule a case must hold, or a note saying it is redundant with
another rule, or that it has no case and why. An entry expected to be held and held by nothing is the finding this
exists to make.

    python -I -X utf8 eng/validation/archive-writing/measure_rules.py [--rule <text to match>] [--audit]

It copies the check and its suite into a temporary folder and runs them there, so the repository is never modified, and
it runs the suite with `-B` and no `__pycache__`, because the suite loads the check through importlib and a same-size
mutant written within the second would otherwise run the previous one's bytecode (which labelled four measurements one
mutant behind when this was first done by hand).

Each entry is (what the rule is, the committed text, the weakened text, a note or None). The committed text must appear
exactly once in the check.
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

RULES: list[tuple[str, str, str, str | None]] = [
    ('the gate recorded as exactly 1',
     '        elif value != GATE_VALUE:',
     '        elif False and value != GATE_VALUE:', None),
    ('the gate named at all',
     "                problems.append('its machine record does not name ' + GATE_NAME)",
     "                problems.append('its machine record is quiet about ' + GATE_NAME)", 'redundant: with this branch gone the value comparison refuses a record with no gate anyway'),
    ("the gate's name read whole, not as a part",
     '        if separator and name.strip() == GATE_NAME:',
     '        if separator and GATE_NAME in name:', None),
    ('one machine record, not several',
     '        elif len(records) > 1:',
     '        elif len(records) > 1 and False:', None),
    ('a machine record whose members do not repeat',
     "            if records[0].replace('\\\\', '/').endswith('machine.json') and \\",
     "            if False and records[0].replace('\\\\', '/').endswith('machine.json') and \\", None),
    ('the system read from how the record line begins',
     '    return next((system for named, system in SYSTEMS if first.strip().startswith(named)), None)',
     '    return next((system for named, system in SYSTEMS if named in first), None)', None),
    ('the system named at all',
     '            if system is None:',
     '            if False and system is None:', None),
    ('the family read from each platform class on its own',
     "    windows = any(name.endswith('.WindowsEditStorageTests.' + PINS[2]) for name in passed)",
     "    windows = any('WindowsEditStorageTests' in name for name in passed)", None),
    ('the Unix platform class asked for by its whole name',
     "    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in passed)",
     "    unix = any('UnixEditStorageTests' in name for name in passed)", None),
    ('the record and the run agreeing about the system',
     "        elif system is not None and family != ('windows' if system == 'windows' else 'unix'):",
     "        elif False and system is not None and family != ('windows' if system == 'windows' else 'unix'):", None),
    ('a run that names neither platform class refused',
     '        elif family is None:',
     '        elif False and family is None:', None),
    ('every summary read, not the first by name',
     '        for name in sorted(summaries):',
     '        for name in sorted(summaries)[:1]:', None),
    ('every status file read, not the first',
     '        for name in statuses:',
     '        for name in statuses[:1]:', None),
    ('a summary that names no suite',
     "            if not summary.get('suites'):",
     "            if not summary.get('suites') and False:", None),
    ('the pins each passing somewhere and failing nowhere',
     '                if wrong:',
     '                if False and wrong:', None),
    ('a pin whose every case stepped aside',
     "                elif not any('Passed' in outcome for outcome in matching.values()):",
     "                elif False and not any('Passed' in outcome for outcome in matching.values()):", None),
    ('a step-aside reason anywhere in the logs or the results',
     '                if reason in text:',
     '                if False and reason in text:', None),
    ('a log and a results file found by how their names begin and end',
     '            if leaf.startswith(prefix) and leaf.endswith(suffix):',
     '            if prefix in leaf and suffix in leaf:', None),
    ('a name matched as the whole leaf',
     "        return [name for name in self._names if name.replace('\\\\', '/').split('/')[-1] == leaf]",
     "        return [name for name in self._names if leaf in name.replace('\\\\', '/').split('/')[-1]]", None),
    ('a name the archive holds more than once',
     '        if (repeated := archive.repeated()):',
     '        if (repeated := archive.repeated()) and False:', None),
    ('folder entries left out of the members',
     '            items = [item for item in self._tar.getmembers() if not item.isdir()]',
     '            items = list(self._tar.getmembers())', None),
    ('a member read through the member it listed',
     '        item = self._members[name]',
     '        item = name', None),
    ('the listing accounting for the file at all',
     '        if (gap := archive.listing_gap()) is not None:',
     '        if (gap := archive.listing_gap()) is not None and False:', None),
    ('an entry pointing at a member of this file',
     "                if len(header) < 30 or header[:4] != b'PK\\x03\\x04':",
     "                if False and (len(header) < 30 or header[:4] != b'PK\\x03\\x04'):", 'redundant since the name rule: an entry pointing elsewhere is refused for the name it does not carry'),
    ('the name the file gives a member',
     '                if written != item.orig_filename:',
     '                if False and written != item.orig_filename:', None),
    ('the size the file states for a member',
     '                elif stated != item.compress_size:',
     '                elif False and stated != item.compress_size:', None),
    ("a descriptor's width from both of its size fields",
     '                        if compressed == item.compress_size and plain == item.file_size:',
     '                        if compressed == item.compress_size:', None),
    ("the local header's own name and extra lengths",
     "                name, extra = struct.unpack('<HH', header[26:30])",
     "                name, extra = len(item.filename.encode('utf-8')), len(item.extra)", None),
    ('members that do not overlap',
     '            if start < reach:',
     '            if False and start < reach:', None),
    ('the first member at the start of the file',
     '        if first != 0:',
     '        if False and first != 0:', 'no case: the covering sums refuse what it would, for a listing that covers its file'),
    ('the last member not past its own directory',
     '        if reach > directory:',
     '        if False and reach > directory:', 'no case: redundant with the reach half of the covering sums'),
    ('the listing covering the bytes before the directory',
     '        if reach != directory or covered != directory:',
     '        if reach != directory:', None),
    ('the listing reaching the directory',
     '        if reach != directory or covered != directory:',
     '        if covered != directory:', 'no case: the guard above read as a sum'),
    ('a gzipped tar read as the whole of its file',
     '            return self._gzip_gap()',
     '            return None', None),
    ('a tar that is gzipped at all',
     "                return 'it is not gzipped, as a sealed run is'",
     '                return None', None),
    ("what follows a tar's last member being its own padding",
     "                    if tail.strip(b'\\x00'):",
     "                    if False and tail.strip(b'\\x00'):", None),
    ('a sparse member measured by what it holds',
     "            stored = sum(length for _, length in item.sparse) if getattr(item, 'sparse', None) else item.size",
     '            stored = item.size', None),
    ('a compressed stream that cannot be read',
     '            except zlib.error as error:',
     '            except KeyError as error:', 'the weakening raises where the rule refuses, so its case ends in an error rather than in a verdict'),
    ('a tar whose last member claims more than was compressed',
     "        return None if at >= end else 'its last member runs %d bytes past what was compressed' % (end - at)",
     '        return None', 'no case: reaching it needs a tar whose last member claims more than was compressed'),
    ('the verdict naming the system it read',
     "                     % (path.name, passed, skipped, GATE, facts['system'], accepted_digest(path)))",
     "                     % (path.name, passed, skipped, GATE, 'windows', accepted_digest(path)))", None),
    ('the verdict naming the digest of what it read',
     "                     % (path.name, passed, skipped, GATE, facts['system'], accepted_digest(path)))",
     "                     % (path.name, passed, skipped, GATE, facts['system'], 'f' * 64))", None),
    ('an archive it cannot read named and the rest still reported',
     '                AttributeError, KeyError, NotImplementedError) as error:',
     '                AttributeError, KeyError) as error:', None),
    ('an archive with problems refused, not given a verdict',
     '        if problems:',
     '        if problems and False:', None),
    ('every outcome a test name has, not the last file to name it',
     '                found.setdefault(test, set()).add(outcome)',
     '                found[test] = {outcome}', None),
    ("the system read from the record's first non-blank line",
     "    first = next((line for line in text.splitlines() if line.strip()), '')",
     "    first = next((line for line in reversed(text.splitlines()) if line.strip()), '')", None),
    ('a compressed stream that stops without ending',
     "                        return 'its compressed stream does not end'",
     '                        break', None),
    ('a summary whose exit codes are an empty map',
     '            if not isinstance(codes, dict) or not codes:',
     '            if not isinstance(codes, dict):', None),
    ('each reason a test gives for stepping aside',
     "    'Native edit storage refuses an elevated or root process',\n",
     '', None),
    ('each pin of the listing, by name',
     "    'This_build_lists_native_edit_storage_on_the_owner_s_routes_only',",
     "    'This_build_lists_native_edit_storage_on_the_owner_s_routes_only_v2',", None),
    # Surfaced by --audit after the thirty-ninth review: conditions no entry named.
    ('a member whose sizes travel in a trailing descriptor',
     "                if flag & 0x08:",
     "                if False and flag & 0x08:", None),
    ('a size the directory records as too wide to hold',
     "                elif stated == 0xffffffff:",
     "                elif False and stated == 0xffffffff:",
     None),  # held after all: a wide shape among the ordinary writers' reaches it
    ('bytes in the file after a tar archive of its own',
     "        if after:",
     "        if False and after:", None),
    ('a member of a machine record named twice inside it',
     "            if key in kept:",
     "            if False and key in kept:", None),
    ('results that name both platform classes as passing',
     "    return 'both' if windows and unix else 'windows' if windows else 'unix' if unix else None",
     "    return 'windows' if windows else 'unix' if unix else None", None),
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


def audit() -> int:
    """The check's own conditions and refusals that no entry here names: a missing rule hides among these."""
    named = {old.strip() for _, old, _, _ in RULES}
    loose = []
    for number, line in enumerate(CHECK.read_text(encoding='utf-8').splitlines(), 1):
        stripped = line.strip()
        if not stripped.startswith(('if ', 'elif ', 'problems.append(', 'return ')) or stripped in named:
            continue
        if any(stripped in old or old.strip() in stripped for _, old, _, _ in RULES):
            continue
        loose.append('%4d  %s' % (number, stripped[:112]))
    print('%d conditions and refusals no entry names (each is a rule to list, a message, or a line of plumbing):'
          % len(loose))
    print('\n'.join(loose))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rule', help='measure only the rules whose description holds this text')
    parser.add_argument('--audit', action='store_true', help='print the check\'s conditions no entry names')
    arguments = parser.parse_args()
    if arguments.audit:
        return audit()
    committed = CHECK.read_text(encoding='utf-8')
    held, unmeasured, noted, wrong = [], [], [], []
    with tempfile.TemporaryDirectory() as directory:
        folder = Path(directory)
        shutil.copy(SUITE, folder / SUITE.name)
        shutil.copy(CHECK, folder / CHECK.name)
        ran, failed = run(folder)
        print('as committed: %s cases, %s' % (ran, 'all passing' if not failed else 'FAILING: ' + ', '.join(failed)))
        if failed or ran == '?':
            return 2
        for what, old, new, note in RULES:
            if arguments.rule and arguments.rule not in what:
                continue
            if committed.count(old) != 1:
                wrong.append(what)
                print('%-62s THE RULE IS NOT THERE ONCE (%d)' % (what, committed.count(old)))
                continue
            (folder / CHECK.name).write_text(committed.replace(old, new), encoding='utf-8', newline='')
            ran, failed = run(folder)
            if ran == '?':
                wrong.append(what)
                print('%-62s the suite did not run' % what)
            elif failed:
                (held if note is None else noted).append(what)
                print('%-62s %s%s' % (what, ', '.join(failed), '' if note is None else '   [%s]' % note))
            elif note is None:
                unmeasured.append(what)
                print('%-62s NO CASE, AND ONE IS EXPECTED' % what)
            else:
                noted.append(what)
                print('%-62s no case   [%s]' % (what, note))
            (folder / CHECK.name).write_text(committed, encoding='utf-8', newline='')
    print()
    print('%d rules listed: %d held by a case, %d noted as redundant or caseless, %d expected to be held and are not%s'
          % (len(RULES), len(held), len(noted), len(unmeasured),
             ': ' + '; '.join(unmeasured) if unmeasured else ''))
    if wrong:
        print('%d entries no longer match the check and have to be rewritten: %s' % (len(wrong), '; '.join(wrong)))
    return 1 if wrong or unmeasured else 0


if __name__ == '__main__':
    raise SystemExit(main())
