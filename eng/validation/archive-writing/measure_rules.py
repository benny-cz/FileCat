#!/usr/bin/env python3
"""Which case fails without each rule of the edit-storage evidence check? Run this instead of reasoning about it.

Review rounds hardened `check_edit_storage_evidence.py`, and three times over they found not a defect in it but a false
claim written beside a rule: "this guard cannot be reached", "two rules are left with no case", and - of this file's
own first version - "every rule of the check is listed below". All three were arguments, and all three were wrong.

So: the rules listed below are weakened one at a time, on a copy, with the check's own suite run against that copy, and
the cases that notice are printed. **This list is not the check, and the exit code speaks for this list alone.** A rule
that is not an entry here is invisible to the measurement, which is how the line in `main()` that turns findings into
a refusal sat unmeasured for a round - so a rule added to the check wants an entry here.

`--audit` is what points at a rule this list lacks, and it is a pointer, not a proof. It walks the check's syntax tree
and prints every line that holds a condition (an `if`, a comparison, a boolean, a comprehension's filter), a parsing
call (split, strip, partition, a prefix or suffix test, a regular expression, `json.loads`, `struct.unpack`, a decode)
or the iterable of a loop, which no entry names. It cannot see a rule held in a constant's contents - a list of reasons,
pins or descriptor shapes - which is why those constants have cases asserting their own words; and it found nothing of
the rule the fortieth review did find, because the line-prefix filter it replaced could not see a `for` over a split.

Each entry carries what is expected of it: nothing, for a rule a case must hold, or a note saying it is redundant with
another rule, or that it has no case and why. An entry expected to be held and held by nothing is the finding this
exists to make, and the exit code reports it.

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
import ast
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
     '            elif (values[0] if values else None) != GATE_VALUE:',
     '            elif False and (values[0] if values else None) != GATE_VALUE:', None),
    ('the gate named at all',
     '            elif not values:',
     '            elif not values and False:', 'redundant: without it the value comparison refuses a record with no gate, as a value of None'),
    ("the gate's name read whole, not as a part",
     '        if separator and (key.isascii() and key.upper() == GATE_NAME if windows else key == GATE_NAME):',
     '        if separator and (GATE_NAME in key.upper() if windows else GATE_NAME in key):', None),
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
     "                if False and (len(header) < 30 or header[:4] != b'PK\\x03\\x04'):", 'redundant: an entry pointing elsewhere is refused for the name it does not carry, or - named after what lies there - by the overlap rule; reaching either needs a rewritten directory'),
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
    ('a member whose sizes travel in a trailing descriptor',
     '                if flag & 0x08:',
     '                if False and flag & 0x08:', None),
    ('a size the directory records as too wide to hold',
     '                elif stated == 0xffffffff:',
     '                elif False and stated == 0xffffffff:', None),
    ('bytes in the file after a tar archive of its own',
     '        if after:',
     '        if False and after:', None),
    ('a member of a machine record named twice inside it',
     '            if key in kept:',
     '            if False and key in kept:', None),
    ('results that name both platform classes as passing',
     "    return 'both' if windows and unix else 'windows' if windows else 'unix' if unix else None",
     "    return 'windows' if windows else 'unix' if unix else None", None),
    ("the Windows runner's -Env read on ';' alone",
     "        pairs = [item for item in recorded.split(';') if item]",
     "        pairs = [item for item in re.split(r'[;\\s]+', recorded) if item]", None),
    ("the Unix runner's own env= lines, each one entry",
     "        pairs = [line[len('env='):] for line in text.split('\\n') if line.startswith('env=')]",
     "        pairs = [part for line in text.split('\\n') for part in re.split(r'[;\\s]+', line.removeprefix('env='))]", None),
    ('a value taken as the runner sets it, untrimmed',
     '            values.append(value)',
     '            values.append(value.strip())', None),
    ('a gate given more than once',
     '            elif len(values) > 1:',
     '            elif False and len(values) > 1:', None),
    ('a descriptor written without its signature',
     "((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4))",
     "((16, '<II', 8), (24, '<QQ', 8))", None),
    ("an empty member's descriptor told by what follows it",
     '                        fits = followed[:1] or fits[:1]',
     '                        fits = fits[:1]', None),
    ('any error reading one archive is its refusal, and the rest are reported',
     '        except Exception as error:  # noqa: BLE001 - a sealed run that does not read is a finding, not a crash',
     '        except (OSError, zipfile.BadZipFile) as error:', None),
    ("a descriptor's signature read as there or not",
     "                    signed = trailer[:4] == b'PK\\x07\\x08'",
     '                    signed = True', None),
    ('an empty member whose descriptor matches both widths',
     '                    if len(fits) > 1:',
     '                    if False and len(fits) > 1:', None),
    ('what follows a descriptor: the next header, or the directory',
     "                            if data_end + size_of == directory or after in (b'PK' + bytes((3, 4)), b'PK' + bytes((1, 2))):",
     '                            if data_end + size_of == directory:', None),
    ('a descriptor that states neither width',
     '                    if not fits:',
     '                    if False:', None),
    ('an archive with no machine record',
     '        if not records:',
     '        if False:', None),
    ('an archive with no summary',
     '        if not summaries:',
     '        if False:', None),
    ("a summary's own list of failures",
     "            if summary.get('failed'):",
     "            if False and summary.get('failed'):", None),
    ("a suite's own failure counter",
     "                if int(counters.get('failed', 0)) != 0:",
     "                if False and int(counters.get('failed', 0)) != 0:", None),
    ("the runner's own verdict on the run",
     "            if summary.get('ok') is not True:",
     "            if False and summary.get('ok') is not True:", None),
    ('a suite that exited other than 0',
     '                    if code != 0:',
     '                    if False and code != 0:', None),
    ('a summary that names no suite at all',
     '        if not suites:',
     '        if False:', None),
    ('an archive with no status file',
     '        if not statuses:',
     '        if False:', None),
    ('a status that is not PASS',
     "            if archive.read(name).strip() != 'PASS':",
     "            if False and archive.read(name).strip() != 'PASS':", None),
    ('an archive with no console log',
     '        if not logs:',
     '        if False:', None),
    ('console logs that say nothing',
     '        elif not any(archive.read(log).strip() for log in logs):',
     '        elif False:', None),
    ('an archive with no results to read',
     '        if not results:',
     '        if False:', None),
    ('a pin the results do not hold at all',
     '            if not matching:',
     '            if False:', None),
    ("the outcomes a pin's case may have",
     "                         if not outcome <= {'Passed', 'NotExecuted'}}",
     "                         if not outcome <= {'Passed', 'NotExecuted', 'Failed'}}", None),
    ('results that name both platform classes, refused',
     "        if family == 'both':",
     "        if False and family == 'both':", None),
    ('a Windows gate name matched without case, as the runner sets it',
     '        if separator and (key.isascii() and key.upper() == GATE_NAME if windows else key == GATE_NAME):',
     '        if separator and key == GATE_NAME:', None),
    ('a Windows Env trusted only as printable ASCII without %',
     '        if strange:',
     '        if False and strange:', None),
    ('a Windows Env trusted only as child.cmd passed it',
     '        if given != [recorded]:',
     '        if False and given != [recorded]:', None),
    ('a Windows Env that is not a string',
     '        if not isinstance(recorded, str):',
     '        if False and not isinstance(recorded, str):', 'redundant: a list in its place has no child.cmd that passed it, which that rule refuses'),
    ('a Unix record split at newlines alone, as bash writes it',
     "        pairs = [line[len('env='):] for line in text.split('\\n') if line.startswith('env=')]",
     "        pairs = [line[len('env='):] for line in text.splitlines() if line.startswith('env=')]", None),
    ("a gate's value taken after its first equals sign",
     "        key, separator, value = pair.partition('=')",
     "        key, separator, value = pair.rpartition('=')", None),
    ('a status read whole, not as a word in it',
     "            if archive.read(name).strip() != 'PASS':",
     "            if 'PASS' not in archive.read(name):", None),
    ('members measured in the order they lie in',
     '        for start, end in sorted(spans):',
     '        for start, end in spans:', None),
    ('a result read whichever order its attributes come in',
     "        test, outcome = match.groups() if order == 'name-first' else match.groups()[::-1]",
     '        test, outcome = match.groups()', None),
    ('a record that cannot say what the tests were given, refused',
     '            if unread is not None:',
     '            if False and unread is not None:', None),
    ('a pin found by its whole method name',
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith('.' + pin)}",
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith(pin)}", None),
    ("the dual-root pin's class found by its whole method name",
     "              if test.split('(')[0].endswith('.' + PINS[2]) and 'Passed' in outcome}",
     "              if test.split('(')[0].endswith(PINS[2]) and 'Passed' in outcome}", "redundant: the class test right after it asks for the whole suffix, the method's own dot included"),
    ('a percent sign in a Windows Env, which cmd.exe expands',
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character == '%']",
     "        strange = [character for character in recorded if not ' ' <= character <= '~']", None),
    ('a descriptor shape tried only with its signature as it is',
     '                        if signed != (size_of in (16, 24)) or len(trailer) < at + struct.calcsize(kind):',
     '                        if len(trailer) < at + struct.calcsize(kind):', 'no case: without it the other shapes are tried too, and they match only where a CRC equals a size or a size equals the bytes after it, which no writer arranges'),
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


PARSING = {'split', 'rsplit', 'partition', 'rpartition', 'strip', 'lstrip', 'rstrip', 'startswith', 'endswith',
           'removeprefix', 'removesuffix', 'match', 'search', 'fullmatch', 'findall', 'finditer', 'loads', 'unpack',
           'decode', 'splitlines'}


def audit() -> int:
    """Every line of the check that holds a condition, a parsing call or a loop's iterable and that no entry names.

    A pointer, not a proof: a rule held in a constant's contents is out of its reach, and a line it prints may be a
    message or plumbing. The exit code is 0 whatever it prints; reading the list is the point.
    """
    source = CHECK.read_text(encoding='utf-8')
    lines = source.splitlines()
    found: dict[int, set[str]] = {}

    def mark(node: ast.AST, why: str) -> None:
        found.setdefault(node.lineno, set()).add(why)

    for node in ast.walk(ast.parse(source)):
        if isinstance(node, (ast.If, ast.While, ast.IfExp, ast.Assert)):
            mark(node.test, 'condition')
        elif isinstance(node, (ast.Compare, ast.BoolOp)):
            mark(node, 'comparison')
        elif isinstance(node, ast.comprehension):
            for test in node.ifs:
                mark(test, 'filter')
            mark(node.iter, 'iterable')
        elif isinstance(node, ast.For):
            mark(node.iter, 'iterable')
        elif isinstance(node, ast.Call):
            name = node.func.attr if isinstance(node.func, ast.Attribute) else getattr(node.func, 'id', '')
            if name in PARSING:
                mark(node, 'parse:' + name)
    named = [old for _, old, _, _ in RULES]
    loose = []
    for number in sorted(found):
        text = lines[number - 1].strip()
        if not text or text.startswith('#') or any(text in old or old.strip() in text for old in named):
            continue
        loose.append('%4d  %-34s %s' % (number, ','.join(sorted(found[number]))[:34], text[:96]))
    print('%d lines hold a condition, a parsing call or an iterable that no entry names - each is a rule to list, a '
          'message, or plumbing:' % len(loose))
    print('\n'.join(loose))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rule', help='measure only the rules whose description holds this text')
    parser.add_argument('--audit', action='store_true',
                        help='print the lines of the check holding a condition, a parse or an iterable no entry names')
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
    measured = len(held) + len(noted) + len(unmeasured)
    print('%s: %d held by a case, %d noted as redundant or caseless, %d expected to be held and are not%s'
          % ('%d rules listed' % len(RULES) if measured + len(wrong) == len(RULES)
             else '%d of the %d rules listed measured' % (measured, len(RULES)),
             len(held), len(noted), len(unmeasured), ': ' + '; '.join(unmeasured) if unmeasured else ''))
    if wrong:
        print('%d entries no longer match the check and have to be rewritten: %s' % (len(wrong), '; '.join(wrong)))
    return 1 if wrong or unmeasured else 0


if __name__ == '__main__':
    raise SystemExit(main())
