#!/usr/bin/env python3
"""Check a sealed run's archive before it is cited as native-edit-storage evidence.

A route is listed in `NativeEditStorage.Qualified` only with its own evidence (`AWQ-<rid>-<fs>/edit`), and that evidence
means one thing: the pins of the listing ran on that machine, with the native store as the default, where a native edit
test fails instead of skipping. The release manifest checks that such an entry exists and that this report accepts it;
this is what reads what is inside it.

It reads a run's evidence archive (`<run>-evidence.zip` from the Windows runner, `<run>-evidence.tar.gz` from the Unix
one) and requires all of:

  * the machine record names `FILECAT_EDIT_NATIVE_REQUIRED=1`, so a native edit test could not skip;
  * its own listing accounts for the whole file, each member as long as its own local header says - a zip is read
    from its end and sized from its directory, so an appended zip is read alone and a directory that overstates a
    size can leave the sealed half inside a member's span; a gzipped tar holds one member and nothing after it,
    since a tar prepended to another is read as the other one;
  * it carries one machine record, whose members do not repeat, and the system that record names agrees with the
    class that passed this run's own dual-root pin - which has to be one of the two real ones, so a record rewritten
    to another system, a second record put before the real one, and a pin whose class was renamed or nested are each
    refused rather than read (Linux and macOS are told apart by the record's own `uname -a` line, and nothing else);
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

What this reading is for: a lane that did not run the pins, a run whose gate was off or whose tests stepped aside, a
report moved to another archive or another route, a stale report, an archive renamed or swapped after it was read, and
an archive whose own parts disagree - the mistakes a release makes. What it cannot do is prove that an archive is a
machine's own sealed run: it reads text and bytes, so whoever can write the evidence folder can write something this
accepts. That rests on the runner that sealed the archive on its machine and on whoever carried it here. Each of the
rules above exists because a review got past the ones before it, so a new way past a rule is worth a case; a way past
that needs the evidence folder rewritten is the boundary, not a defect.

Usage: check_edit_storage_evidence.py ARCHIVE... [--out REPORT]
Exits non-zero, naming what failed, if any archive does not meet all of it.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import zlib
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
        # Only the files, each kept as its own member: a folder entry whose name ends in a file's name would be
        # counted as that file (a tar `notes/machine.json/` was counted as a second machine record), and - because
        # both formats resolve a name to its LAST member and give nothing for a folder - leaving it out of the list
        # while still reading by name let one appended 512-byte folder header empty any file in a sealed tar.
        if path.name.endswith('.zip'):
            self._zip: zipfile.ZipFile | None = zipfile.ZipFile(path)
            self._tar: tarfile.TarFile | None = None
            items: list[object] = [item for item in self._zip.infolist() if not item.is_dir()]
            self._names = [item.filename for item in items]  # type: ignore[attr-defined]
        else:
            self._tar = tarfile.open(path)
            self._zip = None
            items = [item for item in self._tar.getmembers() if not item.isdir()]
            self._names = [item.name for item in items]  # type: ignore[attr-defined]
        # First wins, which is the one a person listing the archive sees; a name held twice is refused anyway.
        self._members: dict[str, object] = {}
        for name, item in zip(self._names, items):
            self._members.setdefault(name, item)

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

    def listing_gap(self) -> str | None:
        """Why this archive's own listing does not account for the file it is in; None when it accounts for all of it.

        A zip is read from its end: zipfile takes the last end-of-directory record, and where that directory says it
        sits elsewhere it passes over everything before as a self-extractor stub. So a sealed zip with another zip
        appended is read as the appended one alone - no member of the sealed run listed, so no repeated name, no
        summary and no status of it ever read, with every sealed byte still in the file. Asking only whether some
        listed member starts at byte 0 was not enough: an appended directory that lists the sealed zip's own first
        entry satisfied that while covering none of the rest, and one listing the sealed entries with a size zeroed
        made a member read as empty. What a sealed run's own listing does is cover its file exactly, once.

        A gzipped tar needs none of this: tarfile stops at the sealed tar's own end-of-archive blocks, so members
        appended after them are never listed.
        """
        if self._zip is None:
            return self._gzip_gap()
        spans: list[tuple[int, int]] = []
        with open(self.path, 'rb') as handle:
            for item in self._zip.infolist():
                handle.seek(item.header_offset)
                header = handle.read(30)
                if len(header) < 30 or header[:4] != b'PK\x03\x04':
                    return 'the entry %r does not point at a member of this file' % item.filename
                # The local header's own name and extra lengths: an entry's extra field may differ between the local
                # header and the directory's copy, and this is measuring the file's layout, not the directory's.
                name, extra = struct.unpack('<HH', header[26:30])
                flag, = struct.unpack('<H', header[6:8])
                stated, = struct.unpack('<I', header[18:22])
                length = item.compress_size
                if flag & 0x08:
                    # The sizes travel after the data in a trailing descriptor: signature, CRC and two sizes, the
                    # sizes eight bytes each where they are recorded wide. No runner writes one.
                    length += 24 if item.compress_size > 0xffffffff else 16
                elif stated == 0xffffffff:
                    pass  # a size recorded in the zip64 extra field, which the directory's copy carries
                elif stated != item.compress_size:
                    # The size the file itself states for that member, against the one the directory states. A
                    # directory that overstates it leaves the member's bytes alone while its span swallows what
                    # follows - which is how an appended directory covered a sealed archive exactly, on paper.
                    return ('the entry %r is %d bytes long where the file says %d'
                            % (item.filename, item.compress_size, stated))
                spans.append((item.header_offset, item.header_offset + 30 + name + extra + length))
        directory = getattr(self._zip, 'start_dir', None)
        if directory is None:  # an older reader that does not say where the directory begins
            return None
        covered = 0
        reach = 0
        for start, end in sorted(spans):
            # Another guard with no case of its own: a span now runs as far as the member's own local header says,
            # so two listed members overlap only where the file overlaps them, which the reader refuses to read.
            if start < reach:
                return 'its members %d and %d overlap' % (start, reach)
            covered += end - start
            reach = max(reach, end)
        first = min((start for start, _ in spans), default=0)
        if first != 0:
            return 'its first member begins %d bytes into the file' % first
        # A guard with no case, and none to be had: zipfile derives where the directory is from the end record, so a
        # member listed after it makes the archive unreadable rather than overreaching. It stays because the two
        # sums below would otherwise have to speak for a layout they were not written for.
        if reach > directory:
            return 'its last member runs %d bytes past the directory that lists it' % (reach - directory)
        if reach != directory or covered != directory:
            return ('its listing covers %d of the %d bytes before the directory, so %d are in the file and in no '
                    'member of it' % (covered, directory, directory - covered))
        return None

    def _gzip_gap(self) -> str | None:
        """Why this gzipped tar is not the whole of its file; None when it is.

        gzip reads concatenated members as one stream and tarfile stops at the first tar's own end-of-archive blocks,
        so appending to a sealed `.tar.gz` adds nothing a reader sees - but a sealed archive *prepended* to a forged
        one is passed over in favour of the forgery, which is how a mac lane's bytes carried a five-member forgery.
        Every archive either runner seals is one gzip member with nothing after it.
        """
        with open(self.path, 'rb') as handle:
            if handle.read(2) != b'\x1f\x8b':
                return None  # not gzipped at all; what it is, if anything, the rules below decide
            handle.seek(0)
            stream = zlib.decompressobj(31)
            try:
                while not stream.eof:
                    block = handle.read(1 << 20)
                    if not block:
                        return 'its compressed stream does not end'
                    stream.decompress(block)
            except zlib.error as error:
                return 'its compressed stream cannot be read: %s' % error
            after = len(stream.unused_data) + len(handle.read())
        return None if after == 0 else 'the file holds %d bytes after this archive\'s own end' % after

    def repeated(self) -> list[str]:
        """Names this archive holds more than once.

        Both formats resolve a repeated name to the last member of that name, while every "read them all" loop below
        lists each occurrence - so an appended copy answers for the original, once per occurrence. Appending clean
        copies of the summary, the status, the results and the log under the names already there, with no original byte
        rewritten, turned a run that had failed into accepted evidence. Which member a name means is not a thing to
        guess, so a repeated name is refused.
        """
        counted: dict[str, int] = {}
        for name in self._names:
            counted[name] = counted.get(name, 0) + 1
        return sorted(name for name, count in counted.items() if count > 1)

    def find(self, leaf: str) -> list[str]:
        return [name for name in self._names if name.replace('\\', '/').split('/')[-1] == leaf]

    def read(self, name: str) -> str:
        """The bytes of the member this archive listed under that name, not of whichever member answers to it."""
        item = self._members[name]
        if self._zip is not None:
            return self._zip.read(item).decode('utf-8', 'replace')  # type: ignore[arg-type]
        handle = self._tar.extractfile(item) if self._tar is not None else None  # type: ignore[arg-type]
        return '' if handle is None else handle.read().decode('utf-8', 'replace')

    def close(self) -> None:
        for handle in (self._zip, self._tar):
            if handle is not None:
                handle.close()


def machine_records(archive: Archive) -> list[str]:
    """Every machine record in the archive. A run seals one; which of several speaks is not a thing to guess."""
    return [name for leaf in ('machine.json', 'machine.txt') for name in archive.find(leaf)]


def repeated_members(text: str) -> list[str]:
    """Members a JSON object holds more than once: a parser keeps the last, a reader sees the first.

    The same hazard as a repeated name in the archive, one level down - a machine record saying `Os` twice showed a
    reader Windows and this check Linux.
    """
    found: list[str] = []

    def hook(pairs: list[tuple[str, object]]) -> dict[str, object]:
        kept: dict[str, object] = {}
        for key, value in pairs:
            if key in kept:
                found.append(key)
            kept[key] = value
        return kept

    try:
        json.loads(text, object_pairs_hook=hook)
    except ValueError:
        return []
    return sorted(set(found))


def machine_field(archive: Archive, name: str, field: str) -> str:
    """One field of a machine record, however that runner shapes it: a JSON member, or the whole text to read on."""
    text = archive.read(name)
    if name.replace('\\', '/').endswith('machine.json'):
        try:
            return str(json.loads(text).get(field, ''))
        except json.JSONDecodeError:
            return text
    return text


def machine_system(archive: Archive, name: str) -> str | None:
    """Which system ran this, out of its own machine record (None: it does not say).

    An archive bound only by its digest is bound to no route: the thirtieth review carried the Windows lane's own
    archive, with its own true report, under all three listed routes, and the manifest took it for all three. This word
    is what the release manifest compares with the route's runtime, so `pinned_family` below reads the run's own
    results for the same fact and the two have to agree.
    """
    text = machine_field(archive, name, 'Os')
    first = next((line for line in text.splitlines() if line.strip()), '')
    return next((system for named, system in SYSTEMS if first.strip().startswith(named)), None)


def pinned_family(results: dict[str, set[str]]) -> str | None:
    """Which family of systems this run's own results say produced it, or None where they do not say.

    The dual-root pin is one method in two classes - the Windows platform suite's `WindowsEditStorageTests` and the
    Core suite's `UnixEditStorageTests` - and a Windows run passes the first while recording the second as not
    executed. So the results witness the family, and a machine record that disagrees with them is not read as evidence
    of either: the thirty-first review rewrote one `Os` field and had this check call a Windows run Linux, while the
    same report listed the Windows platform suite and that pin passing in the Windows class.
    """
    passed = {test.split('(')[0] for test, outcome in results.items()
              if test.split('(')[0].endswith('.' + PINS[2]) and 'Passed' in outcome}
    # Each class is asked for on its own. Read as a set of labels, any third class that also passed this pin - a
    # shared base, a helper, a platform class added later - left the set with two members and silenced the whole rule,
    # which is how a Windows lane was accepted as Linux evidence with one added row.
    windows = any(name.endswith('.WindowsEditStorageTests.' + PINS[2]) for name in passed)
    unix = any(name.endswith('.UnixEditStorageTests.' + PINS[2]) for name in passed)
    return 'both' if windows and unix else 'windows' if windows else 'unix' if unix else None


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
        if (gap := archive.listing_gap()) is not None:
            problems.append('the file does not begin and end with this archive: %s, so one zip is appended to '
                            'another and only one of them is read' % gap)
        if (repeated := archive.repeated()):
            problems.append('it holds %s more than once, and the last member of a name is what a reader gets: which '
                            'of them speaks for this run is not a thing to guess' % ', '.join(repeated))
        records = machine_records(archive)
        facts['system'] = system = None
        if not records:
            problems.append('it carries no machine record, so nothing in it says the gate was in force')
        elif len(records) > 1:
            problems.append('it carries %d machine records (%s): which of them speaks for this run is not a thing to '
                            'guess' % (len(records), ', '.join(sorted(records))))
        else:
            if records[0].replace('\\', '/').endswith('machine.json') and \
                    (twice := repeated_members(archive.read(records[0]))):
                problems.append('its machine record holds %s more than once, so what it says depends on who reads it'
                                % ', '.join(twice))
            recorded = machine_field(archive, records[0], 'Env')
            if (value := gate_value(recorded)) is None:
                problems.append('its machine record does not name ' + GATE_NAME)
            elif value != GATE_VALUE:
                problems.append('its machine record sets %s to %r, not %r, so a native edit test could step aside'
                                % (GATE_NAME, value, GATE_VALUE))
            facts['system'] = system = machine_system(archive, records[0])
            if system is None:
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
        # The record and the run have to agree about the family that produced this archive; where the run does not say
        # (a pin in neither class), the record is all there is, as it is between Linux and macOS.
        family = pinned_family(results)
        if family == 'both':
            problems.append('its own dual-root pin passed in the Windows platform suite\'s class and in the Core '
                            'suite\'s Unix class, which no one run does, so its results do not say what ran it')
        elif family is None:
            # None left the machine record unchallenged, and a class that is nested, renamed or merely longer than
            # the two known ones returned None: one renamed class had the Windows lane accepted as Linux evidence.
            problems.append('its results hold no passing dual-root pin in either platform class '
                            '(WindowsEditStorageTests or UnixEditStorageTests), so nothing in the run itself says '
                            'what produced it')
        elif system is not None and family != ('windows' if system == 'windows' else 'unix'):
            problems.append('its machine record says %s while its own dual-root pin passed in the %s class, so the '
                            'record and the run disagree about which system ran it'
                            % (system, 'Windows platform suite\'s' if family == 'windows' else 'Core suite\'s Unix'))
        # Both the console logs and the results: a test's own reason is in the results file, and the log carries
        # its name and `[SKIP]` without one, so reading only the logs read the one place a reason never appears.
        for log in sorted(set(archive.leaves(prefix='test-')) | set(archive.leaves(suffix='.trx'))):
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
                AttributeError, KeyError, NotImplementedError) as error:
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
