#!/usr/bin/env python3
"""Which verdict changes without each rule of the edit-storage evidence check? Run this instead of reasoning about it.

Review rounds hardened `check_edit_storage_evidence.py`, and three times over they found not a defect in it but a false
claim written beside a rule: "this guard cannot be reached", "two rules are left with no case", and - of this file's
own first version - "every rule of the check is listed below". All three were arguments, and all three were wrong.

So: the rules listed below are weakened one at a time, on a copy, with the check's own suite run against that copy, and
what changes is printed. **This list is not the check, and the exit code speaks for this list alone.** A rule
that is not an entry here is invisible to the measurement, which is how the line in `main()` that turns findings into
a refusal sat unmeasured for a round - so a rule added to the check wants an entry here.

`--audit` is what points at a rule this list lacks, and it is a pointer, not a proof. It walks the check's syntax tree
and prints every line that holds a condition (an `if`, a comparison, a boolean, a comprehension's filter), a parsing
call (split, strip, partition, a prefix or suffix test, a regular expression, `json.loads`, `struct.unpack`, a decode)
or the iterable of a loop, which no entry names. It cannot see a rule held in a constant's contents - a list of reasons,
pins, descriptor shapes, byte-order marks or system words - which is why the reasons and the pins have cases asserting
their own words, and each descriptor shape, mark and system word an entry of its own (the forty-eighth review found a
mark and a system word that none measured). The launcher's pattern is built from its text escaped, so a character
changed there refuses the runner's own launcher, and a case drops and changes its words, line ends, path, case, spaces
and redirection, with entries weakening each part a review named; a result is read as XML, not by a pattern (the
fifty-eighth and fifty-ninth reviews found parts of both patterns held by no case). Nor can it see a rule held in a
plain expression - a bound, a sum, a slice:
the fiftieth review found two held by no case (the tar's floor taken over every member, and every byte of a later
block read past it), each an entry with a case now. Nor can it see how far a loop or a read reaches: a loop read to its
first item alone has an entry, and the sixtieth and sixty-first reviews found loops and reads reaching only as far as
every case did - past each case's counts and sizes, short of r35's lanes' - passing every case while refusing every
archive r35 accepted. So the cases built as r35's lanes reach past the counts and sizes those reviews measured r35's
lanes reaching, each named where its case is built, and an entry reads each such loop or read only as far as r35's
lanes reach (this said "each count they reach" until the sixty-first review found the sizes of members and a stream
past every case). Nor does it print a line an entry already names - an entry naming the lines its
committed text lies on, and no other (matched as text, the fifty-second review found two lines hidden as parts of others'
entries) - though a line may hold more than one rule: it prints such a line where the checks on it outnumber the entries naming it, and that, too, is
a pointer - the forty-seventh review found three rules sharing lines with others, held by no case. And it found nothing of
the rule the fortieth review did find, because the line-prefix filter it replaced could not see a `for` over a split.

What counts is a verdict, not a message. The suite runs with every case going on to its end - an assertion that does not
hold is written down rather than raised - and with every call a case makes into the check written down by its verdict:
an archive accepted (on the system it names) or refused, a listing whole or not, main()'s exit code with its report's
verdict lines - for the functions the cases call for a verdict, check(), main() and listing_gap() (repeated() is wrapped
too, though no case calls it on its own). A call that raises counts as that call's own refusal, since main() refuses an
archive whose reading raises, and the case goes on. A rule is held when, weakened, some case's verdicts change - by its
own absence: a weakening takes its rule away and nothing after it, where one that returned early held its entry by
skipping what followed (the forty-eighth review found the gzip header's entry so). Until the forty-third review a case
that noticed only a changed message counted as holding its rule, and thirty-two entries were "held" that way.

Each entry carries what is expected of it, and the measurement checks it: nothing, for a rule some case holds by a
verdict; a note beginning `redundant` for a rule whose weakening changes no verdict in the suite (another rule refuses
the same archives - measured over the suite's archives, which is evidence and not proof); `no case` for a rule no case
notices at all; or `the weakening raises` for a rule whose weakening can only crash, where it would refuse. An entry
held only by a message, a note the measurement contradicts, and an entry expected to be held and held by nothing are
the findings this exists to make, and the exit code reports each.

    python -I -X utf8 eng/validation/archive-writing/measure_rules.py [--rule <text to match>] [--audit]

It copies the check and its suite into a temporary folder and runs them there, so the repository is never modified, and
it runs the suite with `-B` and no `__pycache__`, because the suite loads the check through importlib and a same-size
mutant written within the second would otherwise run the previous one's bytecode (which labelled four measurements one
mutant behind when this was first done by hand).

It runs the suite with `-I`, which ignores PYTHONHASHSEED, so every run draws a string-hash seed of its own: a rule read
in a set's own order would be measured one way in one run and another in the next, so the check reads its sets sorted
(the fifty-fourth review found two loops held or not by the seed). A suite run that does not end within `TIMEOUT` seconds
is reported, not waited for.

Each entry is (what the rule is, the committed text, the weakened text, a note or None). The committed text must appear
exactly once in the check.
"""
from __future__ import annotations

import argparse
import ast
import os
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
     '        if (key.isascii() and key.upper() == GATE_NAME) if windows else key == GATE_NAME:',
     '        if (GATE_NAME in key.upper()) if windows else GATE_NAME in key:', None),
    ('one machine record, not several',
     '        elif len(records) > 1:',
     '        elif len(records) > 1 and False:', None),
    ('a machine record whose members do not repeat',
     "            if records[0].replace('\\\\', '/').endswith('machine.json') and \\",
     "            if False and records[0].replace('\\\\', '/').endswith('machine.json') and \\", None),
    ('the system read from how the record line begins',
     '    return next((system for named, system in SYSTEMS if first.startswith(named)), None)',
     '    return next((system for named, system in SYSTEMS if named in first), None)', None),
    ('the system named at all',
     '            if system is None:',
     '            if False and system is None:', None),
    ('the family read from each platform class on its own',
     "    windows = any(name.endswith('.WindowsEditStorageTests.' + PINS[2]) for name in sorted(passed))",
     "    windows = any('WindowsEditStorageTests' in name for name in sorted(passed))", None),
    ('the Unix platform class asked for by its whole name',
     "    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in sorted(passed))",
     "    unix = any('UnixEditStorageTests' in name for name in sorted(passed))", None),
    ('the record and the run agreeing about the system',
     "        elif system is not None and family != ('windows' if system == 'windows' else 'unix'):",
     "        elif False and system is not None and family != ('windows' if system == 'windows' else 'unix'):", None),
    ('a run that names neither platform class refused',
     '        elif family is None:',
     '        elif False and family is None:', 'redundant: with no family, the agreement test after it refuses a record that names a system (None is neither family), and a record that names none is refused for that'),
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
     '        return [name for name in self._names if self._leaf(name) == leaf]',
     '        return [name for name in self._names if leaf in self._leaf(name)]', None),
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
     "                if False and (len(header) < 30 or header[:4] != b'PK\\x03\\x04'):", None),
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
     "            if handle.read(2) != b'\\x1f\\x8b':",
     "            if False and handle.read(2) != b'\\x1f\\x8b':", 'redundant: without it the gzip reader refuses a stream with no gzip header'),
    ("what follows a tar's last member being its own padding",
     "                    if tail.strip(b'\\x00'):",
     "                    if False and tail.strip(b'\\x00'):", None),
    ('a sparse member measured by what it holds',
     "            stored = sum(length for _, length in item.sparse) if getattr(item, 'sparse', None) else item.size",
     '            stored = item.size', None),
    ('a compressed stream that cannot be read',
     '            except zlib.error as error:',
     '            except KeyError as error:', "the weakening raises where the rule refuses, which main() refuses as it would the rule's own finding"),
    ('a tar a member of which claims more than was compressed',
     "        return None if at >= end else 'a member of it reaches %d bytes past what was compressed' % (end - at)",
     '        return None', None),
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
     "                found.setdefault(element.get('testName', ''), set()).add(element.get('outcome', ''))",
     "                found[element.get('testName', '')] = {element.get('outcome', '')}", None),
    ("the system read from the record's first line, not its last",
     "    first = text.split('\\n', 1)[0]",
     "    first = text.rstrip('\\n').rsplit('\\n', 1)[-1]", None),
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
     "        pairs = [line[len('env='):] for line in lines if line.startswith('env=')]",
     "        pairs = [part for line in lines if line.startswith('env=') for part in re.split(r'[;\\s]+', line[len('env='):]) if part]", None),
    ('a value taken as the runner sets it, untrimmed',
     '            values.append(value)',
     '            values.append(value.strip())', None),
    ('a gate given more than once',
     '            elif len(values) > 1:',
     '            elif False and len(values) > 1:', None),
    ("an empty member's descriptor told by what follows it",
     '                        fits = [size_of for size_of in fits if data_end + size_of in starts][:1] or fits[:1]',
     '                        fits = fits[:1]', None),
    ('any error reading one archive is its refusal, and the rest are reported',
     '        except Exception as error:  # noqa: BLE001 - a sealed run that does not read is a finding, not a crash',
     '        except (OSError, zipfile.BadZipFile) as error:', None),
    ('an empty member whose descriptor matches both widths',
     '                    if len(fits) > 1:',
     '                    if False and len(fits) > 1:', None),
    ('what follows a descriptor: where a listed member begins, or the directory',
     '        starts = {entry.header_offset for entry in self._zip.infolist()} | {directory}',
     '        starts = {directory}', None),
    ('a descriptor that states neither width',
     '                    if not fits:',
     '                    if False:', "the weakening raises where the rule refuses (no width is chosen to read), which main() refuses as it would the rule's own finding"),
    ('an archive with no machine record',
     '        if not records:',
     '        if False:', "the weakening raises where the rule refuses (there is no record to read), which main() refuses as it would the rule's own finding"),
    ('an archive with no summary',
     '        if not summaries:',
     '        if False:', 'redundant: with no summary no suite is named, which is refused'),
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
     '        if False:', 'redundant: there is a summary by then, and a summary that names no suite is refused by name'),
    ('an archive with no status file',
     '        if not statuses:',
     '        if False:', None),
    ('a status that is not PASS',
     "            if archive.read(name).strip() != 'PASS':",
     "            if False and archive.read(name).strip() != 'PASS':", None),
    ('an archive with no console log',
     '        if not logs:',
     '        if False:', 'redundant: with no log the test output reads as empty, which is refused'),
    ('console logs that say nothing',
     '        elif not any(archive.read(log).strip() for log in logs):',
     '        elif False:', None),
    ('an archive with no results to read',
     '        if not results:',
     '        if False:', 'redundant: with no results no pin is held, which is refused'),
    ('a pin the results do not hold at all',
     '            if not matching:',
     '            if False:', 'redundant: a pin with no cases has none that ran, which is refused'),
    ("the outcomes a pin's case may have",
     "                         if not outcome <= {'Passed', 'NotExecuted'}}",
     "                         if not outcome <= {'Passed', 'NotExecuted', 'Failed'}}", None),
    ('results that name both platform classes, refused',
     "        if family == 'both':",
     "        if False and family == 'both':", "redundant: 'both' is neither family, so the agreement test after it refuses the record whichever system it names"),
    ('a Windows gate name matched without case, as the runner sets it',
     '        if (key.isascii() and key.upper() == GATE_NAME) if windows else key == GATE_NAME:',
     '        if key == GATE_NAME:', None),
    ('a Windows Env trusted only as printable ASCII without %',
     '        if strange:',
     '        if False and strange:', "redundant: child.cmd has to pass the record's Env, and its arguments are printable ASCII without % or ^, with no ! anywhere in the file"),
    ('a Windows Env trusted only as child.cmd passed it',
     '        if launched.group(7) != recorded:',
     '        if False and launched.group(7) != recorded:', None),
    ('a Windows Env that is not a string',
     '        if not isinstance(recorded, str):',
     '        if False and not isinstance(recorded, str):', 'redundant: a list in its place is not the string its child.cmd passes, which that rule refuses'),
    ('a Unix record split at newlines alone, as bash writes it',
     "        lines = own_lines(text.split('\\n'))",
     '        lines = own_lines(text.splitlines())', None),
    ("a gate's value taken after its first equals sign",
     "        key, separator, value = pair.partition('=')",
     "        key, separator, value = pair.rpartition('=')", None),
    ('a status read whole, not as a word in it',
     "            if archive.read(name).strip() != 'PASS':",
     "            if 'PASS' not in archive.read(name):", None),
    ('members measured in the order they lie in',
     '        for start, end in sorted(spans):',
     '        for start, end in spans:', None),
    ('a record that cannot say what the tests were given, refused',
     '            if unread is not None:',
     '            if False and unread is not None:', 'redundant: every reason a record cannot be read comes with no values, and a record that names no gate is refused'),
    ('a pin found by its whole method name',
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith('.' + pin)}",
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith(pin)}", None),
    ("the dual-root pin's class found by its whole method name",
     "              if test.split('(')[0].endswith('.' + PINS[2]) and 'Passed' in outcome}",
     "              if test.split('(')[0].endswith(PINS[2]) and 'Passed' in outcome}", "redundant: the class test right after it asks for the whole suffix, the method's own dot included"),
    ('a percent sign in a Windows Env, which cmd.exe expands',
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '%!^']",
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '!^']", "redundant: a launcher argument holds no %, and child.cmd has to pass the record's Env"),
    ('a ! or ^ in a Windows Env, which delayed expansion changes',
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '%!^']",
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '%']", "redundant: child.cmd holds no ! and its arguments no ^, and it has to pass the record's Env"),
    ('one child.cmd, not several',
     '        if len(scripts) != 1:',
     '        if not scripts:', None),
    ('a child.cmd at all',
     '        if len(scripts) != 1:',
     '        if len(scripts) > 1:', "the weakening raises where the rule refuses (there is no child.cmd to read), which main() refuses as it would the rule's own finding"),
    ('child.cmd read whole, not just its start',
     '        launched = LAUNCHER.fullmatch(script)',
     '        launched = LAUNCHER.match(script)', None),
    ("a launcher that is not the runner's own, refused",
     '        if launched is None:',
     '        if False and launched is None:', "the weakening raises where the rule refuses (such a file has no arguments to read - or, where a line of it runs past 8191 characters, the length rule refuses it first), which main() refuses as it would the rule's own finding"),
    ('a launcher argument without a quote of its own',
     'ARGUMENT = r\'"([ !#$&-\\]_-~]*)"\'',
     'ARGUMENT = r\'"(.*?)"\'', None),
    ('a launcher argument of printable ASCII without % or ^',
     'ARGUMENT = r\'"([ !#$&-\\]_-~]*)"\'',
     'ARGUMENT = r\'"([^"]*)"\'', None),
    ('a launcher argument PowerShell reads as written, refused where it would not',
     '        if odd:',
     '        if False and odd:', None),
    ('a launcher argument starting with -, which PowerShell reads as a parameter name',
     "        odd = [argument for argument in launched.groups() if argument.startswith('-') or argument.endswith('\\\\')]",
     "        odd = [argument for argument in launched.groups() if argument.endswith('\\\\')]", None),
    ('a launcher argument ending in a backslash, which escapes its closing quote',
     "        odd = [argument for argument in launched.groups() if argument.startswith('-') or argument.endswith('\\\\')]",
     "        odd = [argument for argument in launched.groups() if argument.startswith('-')]", None),
    ('a name outside FILECAT_, which the test platform may read',
     "        if not (key.upper() if windows else key).startswith('FILECAT_'):",
     "        if False and not (key.upper() if windows else key).startswith('FILECAT_'):", None),
    ('a Windows name inside FILECAT_ matched without case',
     "        if not (key.upper() if windows else key).startswith('FILECAT_'):",
     "        if not key.startswith('FILECAT_'):", None),
    ("a child.cmd holding '!', which delayed expansion changes",
     "        if '!' in script:",
     "        if False and '!' in script:", None),
    ('a name counted as repeated from its second copy',
     '        return sorted(name for name, count in counted.items() if count > 1)',
     '        return sorted(name for name, count in counted.items() if count > 2)', None),
    ("the dual-root pin's theory rows read by their method name",
     "    passed = {test.split('(')[0] for test, outcome in results.items()",
     '    passed = {test for test, outcome in results.items()', None),
    ('a step-aside reason read in the results as well as the logs',
     "        for log in sorted(set(archive.leaves(prefix='test-')) | set(archive.leaves(suffix='.trx'))):",
     "        for log in sorted(set(archive.leaves(prefix='test-'))):", None),
    ('the exit code of a run with a refused archive',
     '    return 1 if failed else 0',
     '    return 0', None),
    ('a launcher line no longer than cmd.exe reads as written',
     '        if len(line) > CMD_LINE:',
     '        if False and len(line) > CMD_LINE:', None),
    ("a member's name read as its header's flag says",
     "                written = handle.read(name).decode('utf-8' if flag & 0x800 else 'cp437', 'replace')",
     "                written = handle.read(name).decode('cp437', 'replace')", None),
    ('a record entry with no value, refused',
     '        if not separator:',
     '        if False and not separator:', None),
    ('the dual-root pin counted only where it passed',
     "              if test.split('(')[0].endswith('.' + PINS[2]) and 'Passed' in outcome}",
     "              if test.split('(')[0].endswith('.' + PINS[2])}", None),
    ('a narrow descriptor with its signature',
     "((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4))",
     "((24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4))", None),
    ('a wide descriptor with its signature',
     "((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4))",
     "((16, '<II', 8), (12, '<II', 4), (20, '<QQ', 4))", None),
    ('a narrow descriptor written without its signature',
     "((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4))",
     "((16, '<II', 8), (24, '<QQ', 8), (20, '<QQ', 4))", None),
    ('a wide descriptor written without its signature',
     "((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4))",
     "((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4))", None),
    ("a zip64 member's size as its own local header states it",
     '                    if local is None or local[1] != item.compress_size:',
     '                    if False and (local is None or local[1] != item.compress_size):', None),
    ("a Windows filter holding '@', refused",
     "        if '@' in launched.group(4) + launched.group(5):",
     "        if False and '@' in launched.group(4) + launched.group(5):", None),
    ('a Unix filter beginning with -, refused',
     "            if line.startswith(('filter=-', 'filter=/')):",
     "            if line.startswith('filter=/'):", None),
    ('a status read past its line end',
     "            if archive.read(name).strip() != 'PASS':",
     "            if archive.read(name) != 'PASS':", None),
    ('a quote in a Unix filter or project name, refused',
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     "            held = [character for character in '@%\\\\' if character in line]", None),
    ("an '@' in a Unix filter or project name, refused",
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     '            held = [character for character in \'"%\\\\\' if character in line]', None),
    ("a '%' in a Unix filter or project name, refused",
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     '            held = [character for character in \'"@\\\\\' if character in line]', None),
    ('a Unix test line read with the filter',
     "            if line.startswith(('filter=', 'test ', 'entry=')) and held:",
     "            if line.startswith(('filter=', 'entry=')) and held:", None),
    ("a Windows project entry holding '@', refused",
     "        if '@' in launched.group(4) + launched.group(5):",
     "        if '@' in launched.group(4):", None),
    ('a zip64 header carrying no zip64 field, refused',
     '                    if local is None or local[1] != item.compress_size:',
     '                    if local is not None and local[1] != item.compress_size:', None),
    ('a zip64 field found past the fields before it',
     '    while at + 4 <= len(extra):',
     '    if at + 4 <= len(extra):', None),
    ('a member read by its byte-order mark',
     "        for mark, encoding in ((b'\\xff\\xfe', 'utf-16-le'), (b'\\xfe\\xff', 'utf-16-be'), (b'\\xef\\xbb\\xbf', 'utf-8')):",
     '        for mark, encoding in ():', None),
    ('a console log of nothing but line ends, read as empty',
     '        elif not any(archive.read(log).strip() for log in logs):',
     '        elif not any(archive.read(log) for log in logs):', None),
    ("a pin's theory rows read by their method name",
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith('.' + pin)}",
     "            matching = {test: outcome for test, outcome in results.items() if test.endswith('.' + pin)}", None),
    ("a descriptor's compressed size read against its directory's",
     '                        if compressed == item.compress_size and plain == item.file_size:',
     '                        if plain == item.file_size:', None),
    ('a descriptor followed by the directory itself',
     '        starts = {entry.header_offset for entry in self._zip.infolist()} | {directory}',
     '        starts = {entry.header_offset for entry in self._zip.infolist()}', None),
    ('a local header cut short, refused',
     "                if len(header) < 30 or header[:4] != b'PK\\x03\\x04':",
     "                if header[:4] != b'PK\\x03\\x04':", 'redundant: a header cut short of its 30 bytes either does not begin with the signature, which the other half refuses, or raises reading its lengths, which main() refuses'),
    ('a member found by how its name begins',
     '            if leaf.startswith(prefix) and leaf.endswith(suffix):',
     '            if leaf.endswith(suffix):', None),
    ('a member found by how its name ends',
     '            if leaf.startswith(prefix) and leaf.endswith(suffix):',
     '            if leaf.startswith(prefix):', None),
    ("a record's system read from its first line, not the first that holds something",
     "    first = text.split('\\n', 1)[0]",
     "    first = next((line for line in text.splitlines() if line.strip()), '')", None),
    ('a Unix filter beginning with /, refused',
     "            if line.startswith(('filter=-', 'filter=/')):",
     "            if line.startswith('filter=-'):", None),
    ('a backslash in a Unix filter or project name, refused',
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     '            held = [character for character in \'"@%\' if character in line]', None),
    ('a Windows filter beginning with /, refused',
     "        if launched.group(4).startswith('/'):",
     "        if False and launched.group(4).startswith('/'):", None),
    ('a zip name split at its backslashes, and a tar name not',
     "        return (name.replace('\\\\', '/') if self._zip is not None else name).split('/')[-1]",
     "        return name.replace('\\\\', '/').split('/')[-1]", None),
    ("a member's leaf read after its last folder",
     "        return (name.replace('\\\\', '/') if self._zip is not None else name).split('/')[-1]",
     "        return (name.replace('\\\\', '/') if self._zip is not None else name)", None),
    ('the Windows record read apart from the Unix one',
     "    if name.endswith('machine.json'):",
     "    if False and name.endswith('machine.json'):", None),
    ('a Windows caption read wherever it names Windows',
     "        return 'windows' if 'Windows' in first else None",
     "        return 'windows' if first.strip().startswith(('Microsoft Windows', 'Windows')) else None", None),
    ('a machine.json naming another system, read as naming none',
     "        return 'windows' if 'Windows' in first else None",
     "        return 'windows' if 'Windows' in first else next((system for named, system in SYSTEMS if first.strip().startswith(named)), None)", None),
    ('a machine.txt naming only a system its runner runs on',
     "SYSTEMS = (('Linux', 'linux'), ('Darwin', 'macos'))",
     "SYSTEMS = (('Windows', 'windows'), ('Linux', 'linux'), ('Darwin', 'macos'))", None),
    ('a Linux uname line read as linux',
     "SYSTEMS = (('Linux', 'linux'), ('Darwin', 'macos'))",
     "SYSTEMS = (('Darwin', 'macos'),)", None),
    ('a Darwin uname line read as macos',
     "SYSTEMS = (('Linux', 'linux'), ('Darwin', 'macos'))",
     "SYSTEMS = (('Linux', 'linux'),)", None),
    ("a member's name read in the OEM code page where its header names no other",
     "                written = handle.read(name).decode('utf-8' if flag & 0x800 else 'cp437', 'replace')",
     "                written = handle.read(name).decode('utf-8' if flag & 0x800 else 'latin-1', 'replace')", None),
    ('what is not UTF-8 in a member without a mark, read as replaced',
     "        return data.decode('utf-8', 'replace')",
     "        return data.decode('utf-8', 'strict')", None),
    ('a zip64 field told by its id, not its width',
     '        if kind == 1:',
     '        if size >= 16:', None),
    ('a zip64 field too narrow for both sizes, read as stating none',
     "            return struct.unpack('<QQ', extra[at + 4:at + 20]) if size >= 16 else None  # type: ignore[return-value]",
     "            return struct.unpack('<QQ', extra[at + 4:at + 20])  # type: ignore[return-value]", None),
    ("a zip64 field's sizes read eight bytes wide",
     "            return struct.unpack('<QQ', extra[at + 4:at + 20]) if size >= 16 else None  # type: ignore[return-value]",
     "            return struct.unpack('<II', extra[at + 4:at + 12]) if size >= 16 else None  # type: ignore[return-value]", None),
    ("an empty entry in the Windows runner's -Env passed over, as its child passes it",
     "        pairs = [item for item in recorded.split(';') if item]",
     "        pairs = recorded.split(';')", None),
    ('a passed count that is not a number, refused',
     "                passed = sum(int(counters.get('passed', 0)) for counters in suites.values())",
     "                passed = sum(int(counters.get('passed', 0)) for counters in suites.values() if str(counters.get('passed', 0)).isdigit())", None),
    ('a skipped count that is not a number, refused',
     "                skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values())",
     "                skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values() if str(counters.get('skipped', 0)).isdigit())", None),
    ('a UTF-16 little-endian member read by its mark',
     "        for mark, encoding in ((b'\\xff\\xfe', 'utf-16-le'), (b'\\xfe\\xff', 'utf-16-be'), (b'\\xef\\xbb\\xbf', 'utf-8')):",
     "        for mark, encoding in ((b'\\xfe\\xff', 'utf-16-be'), (b'\\xef\\xbb\\xbf', 'utf-8')):", None),
    ('a UTF-16 big-endian member read by its mark',
     "        for mark, encoding in ((b'\\xff\\xfe', 'utf-16-le'), (b'\\xfe\\xff', 'utf-16-be'), (b'\\xef\\xbb\\xbf', 'utf-8')):",
     "        for mark, encoding in ((b'\\xff\\xfe', 'utf-16-le'), (b'\\xef\\xbb\\xbf', 'utf-8')):", None),
    ('a UTF-8 member read past its mark',
     "        for mark, encoding in ((b'\\xff\\xfe', 'utf-16-le'), (b'\\xfe\\xff', 'utf-16-be'), (b'\\xef\\xbb\\xbf', 'utf-8')):",
     "        for mark, encoding in ((b'\\xff\\xfe', 'utf-16-le'), (b'\\xfe\\xff', 'utf-16-be')):", None),
    ('a Windows record read by its Os member, not as text',
     "    if name.replace('\\\\', '/').endswith('machine.json'):",
     "    if False and name.replace('\\\\', '/').endswith('machine.json'):", None),
    ('a member with a mark read with what does not decode replaced',
     "                return data[len(mark):].decode(encoding, 'replace')",
     "                return data[len(mark):].decode(encoding, 'strict')", None),
    ('a record line read as an entry only where it begins env=',
     "        pairs = [line[len('env='):] for line in lines if line.startswith('env=')]",
     "        pairs = [line[len('env='):] for line in lines if 'env=' in line]", None),
    ("the dual-root pin's family read past its theory rows' arguments",
     "              if test.split('(')[0].endswith('.' + PINS[2]) and 'Passed' in outcome}",
     "              if test.endswith('.' + PINS[2]) and 'Passed' in outcome}", None),
    ('as many test logs as listed test processes',
     '            if len(listed) != len(written):',
     '            if False and len(listed) != len(written):', None),
    ('test logs counted without case, as NTFS and APFS name them',
     "            written = {archive._leaf(log).lower() for log in archive.leaves(prefix='test-', suffix='.log')}",
     "            written = {archive._leaf(log) for log in archive.leaves(prefix='test-', suffix='.log')}", None),
    ("the Windows runner's list read from tests.txt",
     "    if record.replace('\\\\', '/').endswith('machine.json'):",
     "    if False and record.replace('\\\\', '/').endswith('machine.json'):", None),
    ('a listed test process read only from a test line',
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if line.startswith('test ')]",
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if line]", None),
    ('a Windows project path holding .., refused',
     '        if outside:',
     '        if False and outside:', None),
    ('a Unix entry line holding .., refused',
     "            if line.startswith('entry=') and leaves_packet(line[len('entry='):]):",
     "            if False and line.startswith('entry=') and leaves_packet(line[len('entry='):]):", None),
    ('a project entry read as a path only where it names a project',
     "    if entry.startswith(('env:', 'expand:')):",
     "    if False and entry.startswith(('env:', 'expand:')):", None),
    ('a project path read before its shard',
     "    return '..' in entry.split('#', 1)[0]",
     "    return '..' in entry", None),
    ('an entry line read with the filter for what dotnet changes',
     "            if line.startswith(('filter=', 'test ', 'entry=')) and held:",
     "            if line.startswith(('filter=', 'test ')) and held:", None),
    ('every results file read',
     "    for name in archive.leaves(suffix='.trx'):",
     "    for name in archive.leaves(suffix='.trx')[:1]:", None),
    ('every suite of every summary counted',
     "            for suite, counters in (summary.get('suites') or {}).items():\n                suites[suite] = counters",
     "            for suite, counters in list((summary.get('suites') or {}).items())[:1]:\n                suites[suite] = counters", None),
    ("every suite's failure counter read",
     "            for suite, counters in (summary.get('suites') or {}).items():\n                if int(counters.get('failed', 0)) != 0:",
     "            for suite, counters in list((summary.get('suites') or {}).items())[:1]:\n                if int(counters.get('failed', 0)) != 0:", None),
    ("every suite's exit code read",
     '                for suite, code in sorted(codes.items()):',
     '                for suite, code in sorted(codes.items())[:1]:', None),
    ('every pin read',
     '        for pin in PINS:',
     '        for pin in PINS[:2]:', None),
    ('the digest of every block of the archive',
     "        for block in iter(lambda: handle.read(1 << 20), b''):",
     '        for block in [handle.read(1 << 20)]:', None),
    ('a compressed stream read to its end, block after block',
     '                while not stream.eof:',
     '                if not stream.eof:', None),
    ('every tar member measured for the floor, folders and links too',
     '        for item in self._tar.getmembers():',
     '        for item in [member for member in self._tar.getmembers() if member.isreg()]:', None),
    ('every piece of a sparse member summed',
     "            stored = sum(length for _, length in item.sparse) if getattr(item, 'sparse', None) else item.size",
     "            stored = sum(length for _, length in item.sparse[:1]) if getattr(item, 'sparse', None) else item.size", None),
    ('a pin counted as run only where a case Passed',
     "                elif not any('Passed' in outcome for outcome in matching.values()):",
     '                elif not any(outcome for outcome in matching.values()):', None),
    ('every step-aside reason read',
     '            for reason in STEP_ASIDE:',
     '            for reason in STEP_ASIDE[:1]:', None),
    ('both kinds of machine record found',
     "    return [name for leaf in ('machine.json', 'machine.txt') for name in archive.find(leaf)]",
     "    return [name for leaf in ('machine.json',) for name in archive.find(leaf)]", None),
    ("a line's carriage return not counted in its length",
     "        line = max((part.rstrip('\\r') for part in script.split('\\n')), key=len)",
     "        line = max(script.split('\\n'), key=len)", None),
    ('every record line scanned for the filter rules',
     '        for line in lines:',
     '        for line in lines[:1]:', None),
    ('every record entry read',
     '    for pair in pairs:',
     '    for pair in pairs[:1]:', None),
    ('every archive given reported',
     '    for path in arguments.archives:',
     '    for path in arguments.archives[:1]:', None),
    ('the Windows record told from the Unix one in its gate reading',
     "    windows = name.replace('\\\\', '/').endswith('machine.json')",
     '    windows = False', None),
    ('an archive read as a zip by its name',
     "        if path.name.endswith('.zip'):",
     "        if True or path.name.endswith('.zip'):", None),
    ('a tar measured by its own floor',
     '        if self._zip is None:',
     '        if False and self._zip is None:', None),
    ('every zip member measured',
     '            for item in self._zip.infolist():',
     '            for item in self._zip.infolist()[:1]:', None),
    ('the size the local header states',
     "                stated, = struct.unpack('<I', header[18:22])",
     '                stated = item.compress_size', None),
    ('every name counted for repeats',
     '        for name in self._names:\n            counted[name] = counted.get(name, 0) + 1',
     '        for name in self._names[:1]:\n            counted[name] = counted.get(name, 0) + 1', None),
    ('every record member checked for repeats',
     '        for key, value in pairs:',
     '        for key, value in pairs[:1]:', None),
    ("a Windows launcher's projects read one entry at a time",
     "        outside = [entry for entry in launched.group(5).split(',') if leaves_packet(entry)]",
     '        outside = [entry for entry in [launched.group(5)] if leaves_packet(entry)]', None),
    ('every case of a pin read for how it ended',
     '                wrong = {test: sorted(outcome) for test, outcome in matching.items()',
     '                wrong = {test: sorted(outcome) for test, outcome in list(matching.items())[:1]', None),
    ("the record's system read as its family",
     "        elif system is not None and family != ('windows' if system == 'windows' else 'unix'):",
     "        elif system is not None and family != ('windows' if system else 'unix'):", None),
    ('the agreement asked only of a record that names a system',
     "        elif system is not None and family != ('windows' if system == 'windows' else 'unix'):",
     "        elif family != ('windows' if system == 'windows' else 'unix'):", 'redundant: a record that names no system is refused for that, whatever family its run names'),
    ('a mark read only where a member begins',
     '            if data.startswith(mark):',
     '            if mark in data:', None),
    ('a zip folder entry left out of the members',
     '            items: list[object] = [item for item in self._zip.infolist() if not item.is_dir()]',
     '            items: list[object] = list(self._zip.infolist())', None),
    ('a listed process read line by line',
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if line.startswith('test ')]",
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\r\\n')) if line.startswith('test ')]", None),
    ('a built or published entry read as a path to its end',
     "    if entry.startswith(('build:', 'publish:')):",
     "    if False and entry.startswith(('build:', 'publish:')):", None),
    ("the tar's floor taken over every member",
     '            end = max(end, item.offset_data + (stored + 511) // 512 * 512)',
     '            end = (item.offset_data + (stored + 511) // 512 * 512)', None),
    ('every byte of a later block past the floor read',
     '                    tail = plain[max(0, end - at):]',
     '                    tail = plain[end - at:]', None),
    ('the reach of every member before',
     '            reach = max(reach, end)',
     '            reach = end', 'redundant: members read in order whose spans do not overlap each end at or past the reach before them, and one that overlaps is refused before the reach is taken'),
    ('a member spanning its header, name, extra and data',
     '                spans.append((item.header_offset, item.header_offset + 30 + name + extra + length))',
     '                spans.append((item.header_offset, item.header_offset + 30 + name + length))', None),
    ('what follows the stream, read and unread',
     '            after = len(stream.unused_data) + len(handle.read())',
     '            after = len(handle.read())', None),
    ("a descriptor's flag read from the member's own header",
     "                flag, = struct.unpack('<H', header[6:8])",
     '                flag = item.flag_bits', None),
    ('every test list read',
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if line.startswith('test ')]",
     "    return [line for name in lists[:1] for line in own_lines(archive.read(name).split('\\n')) if line.startswith('test ')]", None),
    ('a Unix name matched with case',
     "        if not (key.upper() if windows else key).startswith('FILECAT_'):",
     "        if not key.upper().startswith('FILECAT_'):", None),
    ('every width a descriptor may have',
     '                        fits = [size_of for size_of in fits if data_end + size_of in starts][:1] or fits[:1]',
     '                        fits = [size_of for size_of in fits[:1] if data_end + size_of in starts][:1] or fits[:1]', None),
    ('every launcher argument read for what PowerShell changes',
     "        odd = [argument for argument in launched.groups() if argument.startswith('-') or argument.endswith('\\\\')]",
     "        odd = [argument for argument in launched.groups()[:1] if argument.startswith('-') or argument.endswith('\\\\')]", None),
    ('every element of a results file read, a row nested in InnerResults too',
     '        for element in root.iter():',
     '        for element in (row for results in root for row in results):', None),
    ('every result read for a pin',
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith('.' + pin)}",
     "            matching = {test: outcome for test, outcome in list(results.items())[:1] if test.split('(')[0].endswith('.' + pin)}", None),
    ('every result read for the family',
     "    passed = {test.split('(')[0] for test, outcome in results.items()",
     "    passed = {test.split('(')[0] for test, outcome in list(results.items())[:1]", None),
    ("a zip64 member's compressed size read from its own field",
     '                    if local is None or local[1] != item.compress_size:',
     '                    if local is None or local[0] != item.compress_size:', None),
    ('a variable read as naming no path',
     "    if entry.startswith(('env:', 'expand:')):",
     "    if entry.startswith(('expand:',)):", None),
    ('a package to expand read as naming no project path',
     "    if entry.startswith(('env:', 'expand:')):",
     "    if entry.startswith(('env:',)):", None),
    ('every case of a pin read for whether one passed',
     "                elif not any('Passed' in outcome for outcome in matching.values()):",
     "                elif not any('Passed' in outcome for outcome in list(matching.values())[:1]):", None),
    ("a run's test processes' logs told by their prefix from its other logs",
     "            written = {archive._leaf(log).lower() for log in archive.leaves(prefix='test-', suffix='.log')}",
     "            written = {archive._leaf(log).lower() for log in archive.leaves(suffix='.log')}", None),
    ("a tar's stream measured by every block it read",
     '                    at += len(plain)',
     '                    at = len(plain)', None),
    ('a record scanned for what dotnet changes only where its lines hand the tests something',
     "            if line.startswith(('filter=', 'test ', 'entry=')) and held:",
     '            if held:', None),
    ('every member a leaf is looked for in',
     '        for name in self._names:\n            leaf = self._leaf(name)',
     '        for name in self._names[:1]:\n            leaf = self._leaf(name)', None),
    ("a built or published entry's path read for ..",
     "        return '..' in entry",
     '        return False', None),
    ("a test entry's path read for ..",
     "    return '..' in entry.split('#', 1)[0]",
     '    return False', None),
    ('a built or published entry refused for .. alone',
     "        return '..' in entry",
     '        return True', None),
    ('a launcher argument holding +, as a shard of two classes does',
     'ARGUMENT = r\'"([ !#$&-\\]_-~]*)"\'',
     'ARGUMENT = r\'"([ !#$&-*,-\\]_-~]*)"\'', None),
    ("a member's name compared as its directory wrote it",
     '                if written != item.orig_filename:',
     '                if written != item.filename:', None),
    ("a Unix test line's ! passed, as a negated shard's is",
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     '            held = [character for character in \'"@%\\\\!\' if character in line]', None),
    ('more test logs than its runner listed processes, refused',
     '            if len(listed) != len(written):',
     '            if len(listed) > len(written):', None),
    ('an exit code below zero, refused',
     '                    if code != 0:',
     '                    if code > 0:', None),
    ('a failure counter below zero, refused',
     "                if int(counters.get('failed', 0)) != 0:",
     "                if int(counters.get('failed', 0)) > 0:", None),
    ('the Unix class told by its own leading dot',
     "    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in sorted(passed))",
     "    unix = any(name.endswith('UnixEditStorageTests.' + PINS[2]) for name in sorted(passed))", None),
    ('every passing name read for the Windows class',
     "    windows = any(name.endswith('.WindowsEditStorageTests.' + PINS[2]) for name in sorted(passed))",
     "    windows = any(name.endswith('.WindowsEditStorageTests.' + PINS[2]) for name in sorted(passed)[:1])", None),
    ('every passing name read for the Unix class',
     "    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in sorted(passed))",
     "    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in sorted(passed)[:1])", None),
    ("a gzip stream read with gzip's own 32 KiB window, as every accepted r35 Unix lane's needs",
     '            stream = zlib.decompressobj(31)',
     '            stream = zlib.decompressobj(30)', None),
    ("every suite's passed tests counted in the report",
     "                passed = sum(int(counters.get('passed', 0)) for counters in suites.values())",
     "                passed = sum(int(counters.get('passed', 0)) for counters in list(suites.values())[:1])", None),
    ("every suite's skipped tests counted in the report",
     "                skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values())",
     "                skipped = sum(int(counters.get('skipped', 0)) for counters in list(suites.values())[:1])", None),
    ('a machine.json read as Windows only where its caption names Windows',
     "        return 'windows' if 'Windows' in first else None",
     "        return 'windows' if True else None", None),
    ("a launcher's Env sorting after the record's, refused",
     '        if launched.group(7) != recorded:',
     '        if launched.group(7) < recorded:', None),
    ("a launcher's Env sorting before the record's, refused",
     '        if launched.group(7) != recorded:',
     '        if launched.group(7) > recorded:', None),
    ("a directory's name sorting after the member's own, refused",
     '                if written != item.orig_filename:',
     '                if written > item.orig_filename:', None),
    ("a directory's name sorting before the member's own, refused",
     '                if written != item.orig_filename:',
     '                if written < item.orig_filename:', None),
    ("a member's last block counted whole where its data fills it in part",
     '            end = max(end, item.offset_data + (stored + 511) // 512 * 512)',
     '            end = max(end, item.offset_data + (stored + 510) // 512 * 512)', None),
    ("a launcher argument holding ', ( ) or |, as a filter in dotnet's own grammar does",
     'ARGUMENT = r\'"([ !#$&-\\]_-~]*)"\'',
     'ARGUMENT = r\'"([ !#$&*-\\]_-{}~]*)"\'', None),
    ("a launcher argument holding a 6, as the next candidate's run folder edit-r36-win does",
     'ARGUMENT = r\'"([ !#$&-\\]_-~]*)"\'',
     'ARGUMENT = r\'"([ !#$&-57-\\]_-~]*)"\'', None),
    ('a Windows Env value holding :, as a drive path does',
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '%!^']",
     "        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '%!^:']", None),
    ("a Unix line's & passed, as a filter beside a shard writes it",
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     '            held = [character for character in \'"@%\\\\&\' if character in line]', None),
    ('a Unix entry, filter or test line holding a 3, as hang:30 does',
     '            held = [character for character in \'"@%\\\\\' if character in line]',
     '            held = [character for character in \'"@%\\\\3\' if character in line]', None),
    ("the Unix gate's name read whole, not as a part",
     '        if (key.isascii() and key.upper() == GATE_NAME) if windows else key == GATE_NAME:',
     '        if (key.isascii() and key.upper() == GATE_NAME) if windows else GATE_NAME in key:', None),
    ('a Windows gate name read only where it is ASCII',
     '        if (key.isascii() and key.upper() == GATE_NAME) if windows else key == GATE_NAME:',
     '        if (key.upper() == GATE_NAME) if windows else key == GATE_NAME:',
     'redundant: an Env holding anything but printable ASCII is refused before any of its names is read, so every name '
     'read here is ASCII'),
    ("a directory understating its member's size, refused",
     '                elif stated != item.compress_size:',
     '                elif stated < item.compress_size:', None),
    ("a directory understating its zip64 member's size, refused",
     '                    if local is None or local[1] != item.compress_size:',
     '                    if local is None or local[1] < item.compress_size:', None),
    ("a summary's ok missing or null, refused",
     "            if summary.get('ok') is not True:",
     "            if summary.get('ok') is False:", None),
    ("a summary's ok read as true only where it is JSON's true",
     "            if summary.get('ok') is not True:",
     "            if not summary.get('ok'):", None),
    ('every log and results file of a run read for a step-aside reason, the last too',
     "        for log in sorted(set(archive.leaves(prefix='test-')) | set(archive.leaves(suffix='.trx'))):",
     "        for log in sorted(set(archive.leaves(prefix='test-')) | set(archive.leaves(suffix='.trx')))[:-1]:", None),
    ("a Unix record's own lines found at its packet= line",
     "        if line.startswith('packet='):",
     '        if False:', None),
    ("a Unix record's every line looked at for its packet= line",
     '    for index, line in enumerate(lines):',
     '    for index, line in enumerate(lines[:1]):', None),
    ("a Unix record's listed processes read from its own lines",
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if line.startswith('test ')]",
     "    return [line for name in lists for line in archive.read(name).split('\\n') if line.startswith('test ')]", None),
    ("a Unix record's filter, entry, env and test lines read from its own lines",
     "        lines = own_lines(text.split('\\n'))",
     "        lines = text.split('\\n')", None),
    ("a listed process read from a line that begins with test, not one holding it",
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if line.startswith('test ')]",
     "    return [line for name in lists for line in own_lines(archive.read(name).split('\\n')) if 'test ' in line]", None),
    ("a Unix record's own lines found at the line that begins with packet=, not one holding it",
     "        if line.startswith('packet='):",
     "        if 'packet=' in line:", None),
    ('an empty value read as a value, as both runners apply one',
     '        if not separator:',
     '        if not value:', None),
    ('each report line written as one line',
     "    report = '\\n'.join(one_line(line) for line in lines) + '\\n'",
     "    report = '\\n'.join(lines) + '\\n'", None),
    ('every character a report line cannot show written as its escape, not the line feed alone',
     "    return character if character.isprintable() else character.encode('unicode_escape').decode('ascii')",
     "    return character if character != chr(10) else character.encode('unicode_escape').decode('ascii')", None),
    ("a report's escape read back as ASCII",
     "    return character if character.isprintable() else character.encode('unicode_escape').decode('ascii')",
     "    return character if character.isprintable() else character.encode('unicode_escape').decode('latin-1')",
     'redundant: unicode_escape writes ASCII alone, which every single-byte decoding reads alike'),
    ("a Unix record's path rule read on its entry lines alone, not on its env, filter and test lines",
     "            if line.startswith('entry=') and leaves_packet(line[len('entry='):]):",
     "            if leaves_packet(line[len('entry='):]):", None),
    ("a launcher's hang minutes of three digits, as the runner allows up to 600",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]{1,2})'))", None),
    ("a launcher's hang minutes holding any digit",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-5]+)'))", None),
    ("a launcher's hang minutes holding a digit",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]*)'))", None),
    ("a launcher's hang minutes written bare, not quoted",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{1}'), ARGUMENT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ('a launcher beginning @echo off',
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('@echo off\\r\\n'), '(?:' + re.escape('@echo off\\r\\n') + ')?').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's first line ended CRLF, as its runner ends it",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('@echo off\\r\\n'), re.escape('@echo off') + r'\\r?\\n').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's last line ended CRLF, as its runner ends it",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('2>&1\\r\\n'), re.escape('2>&1') + r'\\r?\\n').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's last line ended",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('2>&1\\r\\n'), re.escape('2>&1') + r'(?:\\r\\n)?').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher running the system's own powershell.exe, not one elsewhere",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\'), r'\\S*').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ('a launcher running powershell.exe, not another program',
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe'), r'\\S+').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's program on drive C",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('C:\\\\Windows'), '[A-Z]:' + re.escape('\\\\Windows')).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's program path's v1.0 read with its dot",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('v1.0'), 'v1.0').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's program read with the dot of powershell.exe",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('powershell.exe'), 'powershell.exe').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's program path's separators read as written",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('\\\\'), '.').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ('a launcher read in its own case',
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'), re.I)", None),
    ("a launcher's spaces read one each",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(' '), ' +').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's output written over, not appended to",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('> '), '>+' + re.escape(' ')).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ('a launcher passing -NoProfile',
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(' -NoProfile'), '(?:' + re.escape(' -NoProfile') + ')?').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ('a launcher passing -ExecutionPolicy Bypass',
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(' -ExecutionPolicy Bypass'), '(?:' + re.escape(' -ExecutionPolicy Bypass') + ')?').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ('a launcher passing -WindowStyle Hidden',
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(' -WindowStyle Hidden'), '(?:' + re.escape(' -WindowStyle Hidden') + ')?').replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))", None),
    ("a launcher's -File argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-File {0}\'), re.escape(\'-File \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', "redundant: the run-folder rule holds -File, -Run and the output to the one folder its record names, and the other two of them are read as arguments are, so this one holds no character they refuse"),
    ("a launcher's -Run argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-Run {0}\'), re.escape(\'-Run \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', "redundant: the run-folder rule holds -File, -Run and the output to the one folder its record names, and the other two of them are read as arguments are, so this one holds no character they refuse"),
    ("a launcher's -Packet argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-Packet {0}\'), re.escape(\'-Packet \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', None),
    ("a launcher's -Filter argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-Filter {0}\'), re.escape(\'-Filter \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', None),
    ("a launcher's -Projects argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-Projects {0}\'), re.escape(\'-Projects \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', None),
    ("a launcher's -Dotnet argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-Dotnet {0}\'), re.escape(\'-Dotnet \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', None),
    ("a launcher's -Env argument read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'-Env {0}\'), re.escape(\'-Env \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', "redundant: a launcher's -Env must be its record's Env, whose own rule refuses what this one does there - %, ^ and what is not printable ASCII - and a quote ends the argument either way"),
    ("a launcher's output read as the launcher's other arguments are",
     "LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape('{0}'), ARGUMENT).replace(re.escape('{1}'), '([0-9]+)'))",
     'LAUNCHER = re.compile(re.escape(LAUNCHER_TEXT).replace(re.escape(\'> {0}\'), re.escape(\'> \') + \'"([^"]*)"\').replace(re.escape(\'{0}\'), ARGUMENT).replace(re.escape(\'{1}\'), \'([0-9]+)\'))', "redundant: the run-folder rule holds -File, -Run and the output to the one folder its record names, and the other two of them are read as arguments are, so this one holds no character they refuse"),
    ("a child.cmd's longest line measured, not its second alone",
     "        line = max((part.rstrip('\\r') for part in script.split('\\n')), key=len)",
     "        line = script.split('\\n')[1].rstrip('\\r')", 'redundant: LAUNCHER admits a file of two lines alone, whose second is the longer'),
    ("a result read by its element's own name, its namespace aside",
     "            if element.tag.rpartition('}')[2] == 'UnitTestResult':",
     "            if element.tag == 'UnitTestResult':", None),
    ('a result read from a UnitTestResult element, not one whose name ends so',
     "            if element.tag.rpartition('}')[2] == 'UnitTestResult':",
     "            if element.tag.rpartition('}')[2].endswith('UnitTestResult'):", None),
    ('a result read from a UnitTestResult element, not one whose name begins so',
     "            if element.tag.rpartition('}')[2] == 'UnitTestResult':",
     "            if element.tag.rpartition('}')[2].startswith('UnitTestResult'):", None),
    ("a result naming no test read as no test's",
     "                found.setdefault(element.get('testName', ''), set()).add(element.get('outcome', ''))",
     "                found.setdefault(element.get('testName'), set()).add(element.get('outcome', ''))", None),
    ('a results file that is not XML, refused',
     '        if unread:',
     '        if False and unread:', None),
    ('a result read in any namespace, not in the logger\'s or none alone',
     "            if element.tag.rpartition('}')[2] == 'UnitTestResult':",
     "            if element.tag in ('UnitTestResult', '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}UnitTestResult'):", None),
    ('a results file read whatever its root',
     '            root = ElementTree.fromstring(archive.read(name))',
     "            root = ElementTree.fromstring(archive.read(name))\n            if root.tag.rpartition('}')[2] != 'TestRun':\n                continue", None),
    ("a launcher's hang minutes without a leading zero, as PowerShell writes the runner's [int]",
     "        if hang[:1] == '0' or (len(hang), hang) > (3, '600'):",
     "        if (len(hang), hang) > (3, '600'):", None),
    ("a launcher's hang minutes no more than 600",
     "        if hang[:1] == '0' or (len(hang), hang) > (3, '600'):",
     "        if hang[:1] == '0':", None),
    ("a launcher's hang minutes compared by their length first",
     "        if hang[:1] == '0' or (len(hang), hang) > (3, '600'):",
     "        if hang[:1] == '0' or hang > '600':", None),
    ("a launcher's -File in the run folder its record names",
     "        if (launched.group(1), launched.group(2), launched.group(9)) != (run + '\\\\child.ps1', run, run + '\\\\child.out'):",
     "        if (launched.group(2), launched.group(9)) != (run, run + '\\\\child.out'):", None),
    ("a launcher's -Run in the run folder its record names",
     "        if (launched.group(1), launched.group(2), launched.group(9)) != (run + '\\\\child.ps1', run, run + '\\\\child.out'):",
     "        if (launched.group(1), launched.group(9)) != (run + '\\\\child.ps1', run + '\\\\child.out'):", None),
    ("a launcher's output in the run folder its record names",
     "        if (launched.group(1), launched.group(2), launched.group(9)) != (run + '\\\\child.ps1', run, run + '\\\\child.out'):",
     "        if (launched.group(1), launched.group(2)) != (run + '\\\\child.ps1', run):", None),
    ("a launcher's run folder read from its record's RunId, not from the launcher",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId'))",
     '        run = launched.group(2)', None),
    ("a launcher's run folder under its runner's own root",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId'))",
     "        run = launched.group(2).rpartition('\\\\')[0] + '\\\\' + str(record.get('RunId'))", None),
    ('every results file read, past the five of a full r35 lane',
     "    for name in archive.leaves(suffix='.trx'):",
     "    for name in archive.leaves(suffix='.trx')[:5]:", None),
    ('every test log counted, past the five of a full r35 lane',
     "            written = {archive._leaf(log).lower() for log in archive.leaves(prefix='test-', suffix='.log')}",
     "            written = {archive._leaf(log).lower() for log in archive.leaves(prefix='test-', suffix='.log')[:5]}", None),
    ("every element of a results file read, past where r35's furthest pin stands (element 16,779 of its file)",
     '        for element in root.iter():',
     '        for element in list(root.iter())[:17000]:', None),
    ("every result read for a pin, past where r35's furthest pin stands (result 14,957 of its run)",
     "            matching = {test: outcome for test, outcome in results.items() if test.split('(')[0].endswith('.' + pin)}",
     "            matching = {test: outcome for test, outcome in list(results.items())[:15000] if test.split('(')[0].endswith('.' + pin)}", None),
    ("every result read for the family, past where r35's furthest dual-root pin stands (result 12,714 of its run)",
     "    passed = {test.split('(')[0] for test, outcome in results.items()",
     "    passed = {test.split('(')[0] for test, outcome in list(results.items())[:15000]", None),
    ('every suite of every summary kept, past the five of a full r35 lane',
     "            for suite, counters in (summary.get('suites') or {}).items():\n                suites[suite] = counters",
     "            for suite, counters in list((summary.get('suites') or {}).items())[:5]:\n                suites[suite] = counters", None),
    ("every suite's passed tests counted in the report, past the five of a full r35 lane",
     "                passed = sum(int(counters.get('passed', 0)) for counters in suites.values())",
     "                passed = sum(int(counters.get('passed', 0)) for counters in list(suites.values())[:5])", None),
    ("every suite's skipped tests counted in the report, past the five of a full r35 lane",
     "                skipped = sum(int(counters.get('skipped', 0)) for counters in suites.values())",
     "                skipped = sum(int(counters.get('skipped', 0)) for counters in list(suites.values())[:5])", None),
    ('the digest of every block of the archive, past the seven of a full r35 lane',
     "        for block in iter(lambda: handle.read(1 << 20), b''):",
     "        for block in list(iter(lambda: handle.read(1 << 20), b''))[:7]:", None),
    ('every archive given reported, past the fourteen the records read in one run',
     '    for path in arguments.archives:',
     '    for path in arguments.archives[:14]:', None),
    ("every report line written, past the ninety-three of the records' report on r35",
     "    report = '\\n'.join(one_line(line) for line in lines) + '\\n'",
     "    report = '\\n'.join(one_line(line) for line in lines[:93]) + '\\n'", None),
    ('every tar member listed, past the thirty-seven of a full r35 lane',
     '            items = [item for item in self._tar.getmembers() if not item.isdir()]',
     '            items = [item for item in self._tar.getmembers()[:37] if not item.isdir()]', None),
    ('every tar member named, past the thirty files of a full r35 lane',
     '            self._names = [item.name for item in items]  # type: ignore[attr-defined]',
     '            self._names = [item.name for item in items[:30]]  # type: ignore[attr-defined]', None),
    ('every member kept under its name, past the thirty files of a full r35 lane',
     '        for name, item in zip(self._names, items):',
     '        for name, item in list(zip(self._names, items))[:30]:', None),
    ('every member a leaf is looked for in, past the thirty files of a full r35 lane',
     '        for name in self._names:\n            leaf = self._leaf(name)',
     '        for name in self._names[:30]:\n            leaf = self._leaf(name)', None),
    ('every tar member measured for the floor, past the thirty-seven of a full r35 lane',
     '        for item in self._tar.getmembers():',
     '        for item in self._tar.getmembers()[:37]:', None),
    ('every member a name is found in, past the thirty files of a full r35 lane',
     '        return [name for name in self._names if self._leaf(name) == leaf]',
     '        return [name for name in self._names[:30] if self._leaf(name) == leaf]', None),
    ("every env= line of a Unix record read, past where the runner-shaped lanes' gates stand (line 15 of their own)",
     "        pairs = [line[len('env='):] for line in lines if line.startswith('env=')]",
     "        pairs = [line[len('env='):] for line in lines[:16] if line.startswith('env=')]", None),
    ('every log and results file read for a step-aside reason, past the ten of a full r35 lane',
     "        for log in sorted(set(archive.leaves(prefix='test-')) | set(archive.leaves(suffix='.trx'))):",
     "        for log in sorted(set(archive.leaves(prefix='test-')) | set(archive.leaves(suffix='.trx')))[:10]:", None),
    ("a result's testName and outcome read as attributes in no namespace, as the logger writes them",
     "                found.setdefault(element.get('testName', ''), set()).add(element.get('outcome', ''))",
     "                found.setdefault(next((v for k, v in element.attrib.items() if k.rpartition('}')[2] == 'testName'), ''), set()).add(next((v for k, v in element.attrib.items() if k.rpartition('}')[2] == 'outcome'), ''))", None),
    ("every suite's exit code read, past the five of a full r35 lane",
     '                for suite, code in sorted(codes.items()):',
     '                for suite, code in sorted(codes.items())[:5]:', None),
    ("every suite's failure counter read, past the five of a full r35 lane",
     "            for suite, counters in (summary.get('suites') or {}).items():\n                if int(counters.get('failed', 0)) != 0:",
     "            for suite, counters in list((summary.get('suites') or {}).items())[:5]:\n                if int(counters.get('failed', 0)) != 0:", None),
    ("a zip member read whole, past the 35.9 MB of r35's largest",
     '            data = self._zip.read(item)  # type: ignore[arg-type]',
     '            data = self._zip.open(item).read(35 << 20)  # type: ignore[arg-type]', None),
    ("a tar member read whole, past the 34.9 MB of r35's largest",
     "            data = b'' if handle is None else handle.read()",
     "            data = b'' if handle is None else handle.read(34 << 20)", None),
    ('a compressed stream read to its end, past the seven blocks of a full r35 lane',
     '                while not stream.eof:',
     '                while not stream.eof and handle.tell() < 7 << 20:', None),
    ("every item of a Windows Env read, past the four of r35's own",
     "        pairs = [item for item in recorded.split(';') if item]",
     "        pairs = [item for item in recorded.split(';')[:4] if item]", None),
    ("a launcher's run folder read from its record's RunId in the RunId's own case",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId'))",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId')).lower()", None),
    ("a launcher's run folder read from its record's whole RunId, past fifty-nine characters",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId'))",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId'))[:59]", None),
    ("a launcher's run folder read from its record's RunId with its dots and underscores",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId'))",
     "        run = 'C:\\\\FileCatValidation\\\\aw-claude\\\\' + str(record.get('RunId')).replace('.', '').replace('_', '')", None),
    ("every member's start known where a descriptor's width is decided, past thirty members",
     '        starts = {entry.header_offset for entry in self._zip.infolist()} | {directory}',
     '        starts = {entry.header_offset for entry in self._zip.infolist()[:30]} | {directory}', None),
    ("a Unix record's packet= line looked for past the seven lines of macOS's head",
     '    for index, line in enumerate(lines):',
     '    for index, line in enumerate(lines[:7]):', None),
    ("every zip entry listed, past the thirty-six of r35's full Windows lane",
     '            items: list[object] = [item for item in self._zip.infolist() if not item.is_dir()]',
     '            items: list[object] = [item for item in self._zip.infolist()[:36] if not item.is_dir()]', None),
    ("every zip member named, past the thirty files of r35's full Windows lane",
     '            self._names = [item.filename for item in items]  # type: ignore[attr-defined]',
     '            self._names = [item.filename for item in items[:30]]  # type: ignore[attr-defined]', None),
    ("every zip entry measured for the listing, past the thirty-six of r35's full Windows lane",
     '            for item in self._zip.infolist():',
     '            for item in self._zip.infolist()[:36]:', None),
    ("every span of a zip's listing summed, past the thirty-six of r35's full Windows lane",
     '        for start, end in sorted(spans):',
     '        for start, end in sorted(spans)[:36]:', None),
]

# A suite run that has not ended in this many seconds is reported rather than waited for: a weakening can loop for
# ever - a reader's offset that stops advancing - and the measurement would have hung on it without a word (the
# fifty-fourth review). The whole suite runs in seconds.
TIMEOUT = 600


def run(folder: Path) -> tuple[str, int, set[str], dict[str, list[str]], dict[str, list[str]]]:
    """The suite in `folder`, run to the end of every case: how many cases ran and how many were skipped, the cases
    that noticed (an assertion that did not hold, or an error), each case's verdicts from the check in order, and its
    refusals' first reasons - or 'timeout' for how many ran, and nothing else, where the suite did not end."""
    shutil.rmtree(folder / '__pycache__', ignore_errors=True)
    log = folder / 'verdicts.log'
    if log.exists():
        log.unlink()
    try:
        done = subprocess.run([sys.executable, '-I', '-B', '-X', 'utf8', '-m', 'unittest', 'discover',
                               '-s', str(folder), '-p', SUITE.name, '-v'], capture_output=True, text=True, cwd=folder,
                              env=dict(os.environ, MEASURE_VERDICTS=str(log)), timeout=TIMEOUT)
    except subprocess.TimeoutExpired:
        return 'timeout', 0, set(), {}, {}
    text = done.stdout + done.stderr
    ran = re.search(r'Ran (\d+) tests', text)
    skipped = re.search(r'skipped=(\d+)', text)
    noticed = {name[len('test_'):] for name in re.findall(r'(?:FAIL|ERROR): (test_\w+)', text)}
    verdicts: dict[str, list[str]] = {}
    reasons: dict[str, list[str]] = {}
    for line in (log.read_text(encoding='utf-8').split('\n') if log.exists() else []):
        if not line:
            continue
        test, kind, value = line.split('\t', 2)
        test = test[len('test_'):]
        if kind == 'assert':
            noticed.add(test)
        elif kind == 'verdict':
            verdicts.setdefault(test, []).append(value)
        else:
            reasons.setdefault(test, []).append(value)
    return (ran.group(1) if ran else '?'), int(skipped.group(1)) if skipped else 0, noticed, verdicts, reasons


def settled(values: list[str]) -> list[str]:
    """A call that raised is that call's own refusal - written down as such by the suite, with what it raised beside it
    - since main() refuses an archive whose reading raises, as it refuses any other."""
    return [value.split(' (raised ')[0] for value in values]


def compare(before: list[str], after: list[str]) -> str:
    """'same' when a case got the same verdicts in the same order, 'changed' when one of them differs, and 'unknown'
    when the case stopped early - an error in the case itself - before any differed."""
    for one, other in zip(before, after):
        if one != other:
            return 'changed'
    return 'same' if len(before) == len(after) else 'unknown'


# What a note says of its rule, by how it begins - and what the measurement must then find.
KINDS = (('redundant', 'redundant'), ('no case', 'caseless'), ('the weakening raises', 'raises'))


def expected(note: str | None) -> str:
    if note is None:
        return 'held'
    for prefix, kind in KINDS:
        if note.startswith(prefix):
            return kind
    raise SystemExit('a note begins with one of %s: %r' % (', '.join(prefix for prefix, _ in KINDS), note))


PARSING = {'split', 'rsplit', 'partition', 'rpartition', 'strip', 'lstrip', 'rstrip', 'startswith', 'endswith',
           'removeprefix', 'removesuffix', 'match', 'search', 'fullmatch', 'findall', 'finditer', 'loads', 'unpack',
           'decode', 'splitlines'}


def audit() -> int:
    """Every line of the check that holds a condition, a parsing call or a loop's iterable and that no entry names.

    A pointer, not a proof: a rule held in a constant's contents or in a plain expression is out of its reach, and a
    line it prints may be a message or plumbing. The exit code is 0 whatever it prints; reading the list is the point.
    """
    source = CHECK.read_text(encoding='utf-8')
    lines = source.splitlines()
    found: dict[int, set[str]] = {}
    checks: dict[int, set[int]] = {}  # each line's checks, an `and` or `or` counted by its parts alone

    def mark(node: ast.AST, why: str) -> None:
        found.setdefault(node.lineno, set()).add(why)
        if not isinstance(node, ast.BoolOp):
            checks.setdefault(node.lineno, set()).add(id(node))

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
    # An entry names the lines its committed text lies on, and no other. Matched as text, a line was taken for named
    # where it was a part of another line's entry - a loop over the same names, a return the start of a longer one -
    # and two rules were hidden so (the fifty-second review).
    spans = []
    for _, old, _, _ in RULES:
        at = source.find(old)
        if at >= 0:
            first = source.count('\n', 0, at) + 1
            spans.append((first, first + old.rstrip('\n').count('\n')))
    loose = []
    for number in sorted(found):
        text = lines[number - 1].strip()
        if not text or text.startswith('#'):
            continue
        naming = sum(1 for first, last in spans if first <= number <= last)
        held = len(checks.get(number, ()))
        if naming and held <= naming:
            continue
        loose.append('%4d  %-34s %s' % (number, ','.join(sorted(found[number]))[:34], text[:96] if not naming else
                                        '[%d checks, %d entries] %s' % (held, naming, text[:78])))
    print('%d lines hold a condition, a parsing call or an iterable that no entry names, or more of them than the '
          'entries naming the line - each is a rule to list, a message, or plumbing:' % len(loose))
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
    for _, _, _, note in RULES:
        expected(note)
    committed = CHECK.read_text(encoding='utf-8')
    held, noted, unmeasured, contradicted, stale, hung = [], [], [], [], [], []
    # Two entries that weaken the check alike measure one rule twice and count it twice, and leave whatever each
    # was meant to measure apart unmeasured (one shape of four went so, the forty-sixth review found).
    mutants: dict[str, str] = {}
    for what, old, new, _ in RULES:
        if committed.count(old) == 1:
            twin = mutants.setdefault(committed.replace(old, new), what)
            if twin != what:
                stale.append(what)
                print('%-62s THE SAME WEAKENING AS %r' % (what, twin))
    with tempfile.TemporaryDirectory() as directory:
        folder = Path(directory)
        shutil.copy(SUITE, folder / SUITE.name)
        shutil.copy(CHECK, folder / CHECK.name)
        ran, skipped, failed, baseline, base_reasons = run(folder)
        if ran == 'timeout':
            print('as committed: the suite did not end within %d seconds' % TIMEOUT)
            return 2
        print('as committed: %s cases, %s; %d verdicts' % (
            ran, 'all passing' if not failed else 'FAILING: ' + ', '.join(sorted(failed)),
            sum(len(values) for values in baseline.values())))
        if failed or ran == '?':
            return 2
        if skipped:
            # A case that does not run holds nothing, and would show its rules as caseless: the GNU tar case skips
            # where the first tar on PATH is bsdtar (Windows' own tar.exe ahead of Git's, from PowerShell).
            print('%d of them skipped here, so this measurement would not be the suite\'s: run it where every case '
                  'runs - GNU tar and bsdtar both on PATH, as Git Bash on Windows has them' % skipped)
            return 2
        for what, old, new, note in RULES:
            if arguments.rule and arguments.rule not in what or what in stale:
                continue
            if committed.count(old) != 1:
                stale.append(what)
                print('%-62s THE RULE IS NOT THERE ONCE (%d)' % (what, committed.count(old)))
                continue
            (folder / CHECK.name).write_text(committed.replace(old, new), encoding='utf-8', newline='')
            ran, _, noticed, verdicts, reasons = run(folder)
            (folder / CHECK.name).write_text(committed, encoding='utf-8', newline='')
            if ran == 'timeout':
                hung.append(what)
                print('%-62s THE SUITE DID NOT END within %d seconds' % (what, TIMEOUT))
                continue
            if ran == '?':
                stale.append(what)
                print('%-62s the suite did not run' % what)
                continue
            tests = sorted(set(baseline) | set(verdicts))
            seen = {test: compare(settled(baseline.get(test, [])), settled(verdicts.get(test, []))) for test in tests}
            changed = [test for test in tests if seen[test] == 'changed']
            unknown = [test for test in tests if seen[test] == 'unknown']
            raised = [test for test in tests if any(' (raised ' in value for value in verdicts.get(test, []))]
            kind = expected(note)
            if kind == 'held' and changed:
                held.append(what)
                said = 'a verdict changes in ' + ', '.join(changed)
            elif kind == 'held' and (unknown or noticed):
                contradicted.append(what)
                example = next((test for test in sorted(noticed) if reasons.get(test) != base_reasons.get(test)), None)
                said = ('HELD BY A MESSAGE ONLY: no verdict changes; noticed by %s%s' % (
                    ', '.join(sorted(noticed)), '' if example is None else '; %s now says %r' % (
                        example, (reasons.get(example) or ['?'])[0])) if not unknown else
                        'UNCONFIRMED: no verdict changes before %s stopped early' % ', '.join(unknown))
            elif kind == 'held':
                unmeasured.append(what)
                said = 'NO CASE, AND ONE IS EXPECTED'
            elif kind == 'redundant' and not changed and not unknown:
                noted.append(what)
                said = 'no verdict changes%s   [%s]' % (
                    '; a message does in ' + ', '.join(sorted(noticed)) if noticed else '', note)
            elif kind == 'caseless' and not changed and not noticed:
                noted.append(what)
                said = 'no case   [%s]' % note
            elif kind == 'raises' and raised and not changed:
                noted.append(what)
                said = 'raises in %s   [%s]' % (', '.join(raised), note)
            else:
                contradicted.append(what)
                said = 'THE NOTE IS FALSE: %s   [%s]' % (
                    'a verdict changes in ' + ', '.join(changed) if changed else
                    'cases stopped early: ' + ', '.join(unknown) if unknown and kind == 'redundant' else
                    'noticed by ' + ', '.join(sorted(noticed)) if noticed and kind == 'caseless' else
                    'nothing raises', note)
            print('%-62s %s' % (what, said))
    print()
    measured = len(held) + len(noted) + len(unmeasured) + len(contradicted)
    print('%s: %d held by a verdict, %d noted as redundant, caseless or raising, %d expected to be held and are not, '
          '%d whose note or expectation the measurement contradicts%s'
          % ('%d rules listed' % len(RULES) if measured + len(stale) + len(hung) == len(RULES)
             else '%d of the %d rules listed measured' % (measured, len(RULES)),
             len(held), len(noted), len(unmeasured), len(contradicted),
             ': ' + '; '.join(unmeasured + contradicted) if unmeasured or contradicted else ''))
    if stale:
        print('%d entries no longer match the check and have to be rewritten: %s' % (len(stale), '; '.join(stale)))
    if hung:
        print('%d entries whose weakening kept the suite from ending, each to be weakened another way: %s'
              % (len(hung), '; '.join(hung)))
    return 1 if stale or unmeasured or contradicted or hung else 0


if __name__ == '__main__':
    raise SystemExit(main())
