#!/usr/bin/env python3
"""Check a sealed run's archive before it is cited as native-edit-storage evidence.

A route is listed in `NativeEditStorage.Qualified` only with its own evidence (`AWQ-<rid>-<fs>/edit`), and that evidence
means one thing: the pins of the listing ran on that machine, with the native store as the default, where a native edit
test fails instead of skipping. The release manifest checks that such an entry exists and that this report accepts it;
this is what reads what is inside it.

It reads a run's evidence archive (`<run>-evidence.zip` from the Windows runner, `<run>-evidence.tar.gz` from the Unix
one) and requires all of:

  * the machine record gives `FILECAT_EDIT_NATIVE_REQUIRED` exactly the value `1`, once, and sets nothing outside
    `FILECAT_`, read as its runner applies it as far as reviews have found how (see `gate_assignments`), and a record
    that cannot say what reached the tests is refused. That a native edit test could not skip rests on this reading
    and on the machine's own environment, which no record holds; the tests recording the gate as they read it would
    end both, and is queued for the next candidate;
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

What this reading is for: a lane that did not run the pins, a run whose record shows the gate off or whose tests stepped
aside, a report moved to another archive or another route, a stale report, an archive renamed or swapped after it was
read, and an archive whose own parts disagree - the mistakes a release makes. What it cannot do is prove that an archive
is a machine's own sealed run: it reads text and bytes, so whoever can write the evidence folder can write something
this accepts. That rests on the runner that sealed the archive on its machine and on whoever carried it here. Each of
the rules above exists because a review got past the ones before it, so a new way past a rule is worth a case; a way
past that needs the evidence folder rewritten is the boundary, not a defect.

Which case fails without a rule here is measured, not argued: `measure_rules.py` beside this file weakens the rules it
lists, one at a time, and names the cases that notice; the ones it reports with no case are marked as such where they
are, and `measure_rules.py --audit` points at lines here holding a condition, a parse or a loop that no entry
names - a pointer, not a proof, since a rule held in a constant's contents is beyond it. Three reviews found a false
claim written beside a rule - two of them beside rules here, one of them that harness's own "every rule" - which is why
the claims are gone and the command is cited instead.

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
# The one launcher the Windows runner writes into child.cmd, every argument quoted: cmd.exe reads it, and PowerShell
# splits it again. An argument is printable ASCII without `"` (which would re-cut the line - a filter could bind an
# -Env of its own), `%` (which cmd.exe expands) or `^` (which delayed expansion removes, turning a `\^"` into `\"`);
# and the file as a whole holds no `!` (see gate_assignments).
ARGUMENT = r'"([ !#$&-\]_-~]*)"'
LAUNCHER = re.compile(
    r'@echo off\r\nC:\\Windows\\System32\\WindowsPowerShell\\v1\.0\\powershell\.exe -NoProfile -ExecutionPolicy '
    r'Bypass -WindowStyle Hidden -File {0} -Run {0} -Packet {0} -Filter {0} -Projects {0} -Dotnet {0} -Env {0} '
    r'-HangMinutes [0-9]+ > {0} 2>&1\r\n'.replace('{0}', ARGUMENT))

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

        A gzipped tar is read by `_gzip_gap` instead, which asks the same question of its own format.
        """
        if self._zip is None:
            return self._gzip_gap()
        spans: list[tuple[int, int]] = []
        directory = getattr(self._zip, 'start_dir', None)
        if directory is None:
            # A reader that does not say where the directory begins cannot be asked this question, and the answer
            # then is not "nothing is wrong" - every rule of this layout reading rests on it.
            return 'this reader does not say where its directory begins, so the file cannot be accounted for'
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
                # The name the file gives this member, against the name its directory gives it. Every rule below finds
                # its files by the directory's names, and reading a renamed member is refused by the reader itself -
                # so a directory that renames one hides it instead: a log that said a test had stepped aside stopped
                # being a log, with every byte of the archive untouched.
                written = handle.read(name).decode('utf-8' if flag & 0x800 else 'cp437', 'replace')
                if written != item.orig_filename:
                    return ('the entry %r is the member %r where it points' % (item.orig_filename, written))
                extra_field = handle.read(extra)
                length = item.compress_size
                if flag & 0x08:
                    # The sizes travel after the data, in a descriptor whose own shape says how wide it is: four
                    # bytes each or eight, with or without its signature. Neither the entry's size nor its extra
                    # field answers that - libarchive writes wide descriptors with no zip64 extra field in the local
                    # header at all - so the pair that states this member's own size is the pair that was written.
                    handle.seek(item.header_offset + 30 + name + extra + item.compress_size)
                    trailer = handle.read(24)
                    signed = trailer[:4] == b'PK\x07\x08'
                    # Both sizes, not the compressed one alone: read as narrow, a wide descriptor's low half states
                    # the compressed size correctly for any member under 4 GiB, and only its second field tells them
                    # apart. An empty member states nought in both widths, so there what follows decides: the next
                    # member's header, the directory's, or the directory's own place - which two ordinary writers'
                    # archives (an empty file under bsdtar's zip64, and zipfile writing wide to a stream) needed.
                    data_end = item.header_offset + 30 + name + extra + item.compress_size
                    fits = []
                    for size_of, kind, at in ((16, '<II', 8), (24, '<QQ', 8), (12, '<II', 4), (20, '<QQ', 4)):
                        if signed != (size_of in (16, 24)) or len(trailer) < at + struct.calcsize(kind):
                            continue
                        compressed, plain = struct.unpack(kind, trailer[at:at + struct.calcsize(kind)])
                        if compressed == item.compress_size and plain == item.file_size:
                            fits.append(size_of)
                    if len(fits) > 1:
                        followed = []
                        for size_of in fits:
                            handle.seek(data_end + size_of)
                            after = handle.read(4)
                            if data_end + size_of == directory or after in (b'PK' + bytes((3, 4)), b'PK' + bytes((1, 2))):
                                followed.append(size_of)
                        fits = followed[:1] or fits[:1]
                    if not fits:
                        return ('the descriptor after %r does not state the %d bytes its directory does'
                                % (item.orig_filename, item.compress_size))
                    length += fits[0]
                elif stated == 0xffffffff:
                    pass  # a size recorded in the zip64 extra field, which the directory's copy carries
                elif stated != item.compress_size:
                    # The size the file itself states for that member, against the one the directory states. A
                    # directory that overstates it leaves the member's bytes alone while its span swallows what
                    # follows - which is how an appended directory covered a sealed archive exactly, on paper.
                    return ('the entry %r is %d bytes long where the file says %d'
                            % (item.filename, item.compress_size, stated))
                spans.append((item.header_offset, item.header_offset + 30 + name + extra + length))
        covered = 0
        reach = 0
        for start, end in sorted(spans):
            # The one rule that refuses a listing which drops a member and pads the gap with another that lies
            # inside a third - which a directory alone can do wherever a member's own bytes hold a local file header,
            # a member holding a stored zip among them: both names are then honest, no name repeats, and the sums
            # agree. The reader refuses the outer member of such a pair and reads the inner one happily, so nothing
            # else here sees it. It has a case.
            if start < reach:
                return 'its members %d and %d overlap' % (start, reach)
            covered += end - start
            reach = max(reach, end)
        # No case, as `measure_rules.py` says of it: the sums below refuse what this would, for a listing that
        # covers the file. It is kept because it says plainly what the sums only imply.
        first = min((start for start, _ in spans), default=0)
        if first != 0:
            return 'its first member begins %d bytes into the file' % first
        # No case, as `measure_rules.py` says of it, and redundant with the reach half of the rule below rather
        # than with the covered half: a listing whose last member runs past its own directory can cover exactly as
        # many bytes as lie before it, which the thirty-eighth review built. One of this pair would do; both stay
        # because each says a different thing about the layout, and the harness names which is unmeasured.
        if reach > directory:
            return 'its last member runs %d bytes past the directory that lists it' % (reach - directory)
        # The covered half has a case (a hole anywhere in the listing); the reach half has none of its own, as
        # `measure_rules.py` says of each half in turn.
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
        end = 0
        for item in self._tar.getmembers():
            # What this member holds in the archive: for a sparse one that is the sum of its own pieces, while its
            # size is what it expands to. GNU `tar -S` and macOS's own tar write them, and measuring a sparse member
            # by its expanded size refused the archive by however many zeros it left out.
            stored = sum(length for _, length in item.sparse) if getattr(item, 'sparse', None) else item.size
            end = max(end, item.offset_data + (stored + 511) // 512 * 512)
        with open(self.path, 'rb') as handle:
            if handle.read(2) != b'\x1f\x8b':
                # Both runners gzip what they seal, and a reader stops at the first tar's end-of-archive blocks, so
                # a plain tar would carry whatever follows it unread and unasked about.
                return 'it is not gzipped, as a sealed run is'
            handle.seek(0)
            stream = zlib.decompressobj(31)
            at = 0
            try:
                while not stream.eof:
                    block = handle.read(1 << 20)
                    if not block:
                        return 'its compressed stream does not end'
                    plain = stream.decompress(block)
                    # Past the last member: the two end-of-archive blocks and the padding after them, which are
                    # zeros. Anything else there is another tar, which the reader above would never reach.
                    tail = plain[max(0, end - at):]
                    if tail.strip(b'\x00'):
                        return 'what it holds after its own last member is not padding but %d bytes of something' \
                               % len(tail.strip(b'\x00'))
                    at += len(plain)
            except zlib.error as error:
                return 'its compressed stream cannot be read: %s' % error
            after = len(stream.unused_data) + len(handle.read())
        if after:
            return 'the file holds %d bytes after this archive\'s own end' % after
        # No case, as `measure_rules.py` says of it: reaching it needs a tar whose last member claims more than
        # was compressed, which nothing here builds. It is the floor the rule above stands on.
        return None if at >= end else 'its last member runs %d bytes past what was compressed' % (end - at)

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


def gate_assignments(archive: Archive, name: str) -> tuple[list[str], str | None]:
    """Every value this run's own runner gave the gate, as far as its record can say - in order.

    A substring test is not enough, and neither is a looser parse than the runner's. Every reader of this variable
    compares it to "1" exactly, and both runners pass the gate's value through verbatim (the Windows child replaces
    `{run}` in a value, which `1` does not hold), so a run recording
    `FILECAT_EDIT_NATIVE_REQUIRED=10` - or `=1 ` with a trailing space - had no gate at all: its native edit tests
    skipped where they were meant to fail, and its composition pin was never armed.

    The Windows runner records its `-Env` string in `Env` - in the ANSI code page with best-fit, so as given only
    where it is plain ASCII - then splits it on `;` alone and sets each name to the rest of its item, untrimmed; a
    value may hold spaces, so `FILECAT_X=1 FILECAT_EDIT_NATIVE_REQUIRED=1` is one variable `FILECAT_X` and no gate.
    The Unix runner writes one `env=NAME=VALUE` line per entry it exports; a value may hold `;`, so
    `env=FILECAT_X=1;FILECAT_EDIT_NATIVE_REQUIRED=1` is likewise one variable and no gate. This check once split both
    on `;` and whitespace, trimmed, and took the first - and read each of those, and a gate given twice (the runner
    applying the last), as the gate in force while the tests ran without it.

    The forty-first review found three more layers between the record and the tests. On Windows the names are matched
    without case - the runner's pattern then ignored case, and the environment matches names without it - so a lowercase gate
    after the real one was the value the tests saw; and the recorded Env is not what the child is given: the runner
    hands it to child.cmd, which cmd.exe reads again (expanding %...%, ending at a line break), and writes machine.json
    in the ANSI code page with best-fit, so a fullwidth semicolon in a value reads as a real one there. So a Windows Env
    is trusted only where it is printable ASCII without %, ! or ^, and only where the sealed child.cmd passes it. On
    Unix a line ends at a newline alone, as bash writes it - Python's splitlines() also ends one at 0x1c or 0x85, which
    bash keeps inside one value.

    The forty-second review re-cut child.cmd through its other arguments: a `"` in the filter let an `-e` of its own
    bind the child's Env while the real `-Env` text still stood in the file, which the check matched by pattern. So
    child.cmd is read whole, as the one launcher its runner writes - every argument printable ASCII without `"`, `%`
    or `^`, none starting with `-` or ending in a backslash, which PowerShell would read otherwise - and its `-Env` must
    be the record's. It also set the gate through the test platform: beside the gate,
    `VSTESTCLIRUNSETTINGS=RunConfiguration.EnvironmentVariables.FILECAT_EDIT_NATIVE_REQUIRED=0` is read by MSBuild as
    a property and by VSTest as settings, which set the gate to 0 inside the test host. A name outside `FILECAT_` is
    refused. And the Unix runner writes its filter into the same record as its `env=` lines: a filter holding a line
    break wrote a line of its own there - an `env=` line the runner never exported, while each test process took the
    filter's first line alone. Nothing in the record tells such a line from the runner's own, so the runner refuses a
    filter or project entry that is not printable ASCII now. A variable in the machine's own environment can set the
    gate with nothing in the record at all; that is beyond this reading, and ends only with the tests recording the
    gate as they read it.

    The forty-third review found delayed expansion's other form. Where it is on, cmd.exe reads `!NAME:` up to the next
    colon as an undefined variable and deletes it, no closing `!` needed - so a `!` in `-Dotnet` and a `:` in the Env
    deleted the `-Env` between them, while the rest of the Env value completed the Dotnet path, and the child ran with
    no gate. Whether delayed expansion is on is a setting no record holds, so a child.cmd holding `!` anywhere is
    refused; the Windows runner refuses `!` in every argument and takes `#~` for a negated shard.

    Returns the values in order, or a reason the record cannot say what the tests were given.
    """
    text = archive.read(name)
    windows = name.replace('\\', '/').endswith('machine.json')
    if windows:
        try:
            recorded = json.loads(text).get('Env', '')
        except (ValueError, AttributeError):
            return [], None
        if not isinstance(recorded, str):
            return [], 'its machine record gives Env as a %s, not the string its runner passes on' % type(recorded).__name__
        strange = [character for character in recorded if not ' ' <= character <= '~' or character in '%!^']
        if strange:
            return [], ('its machine record\'s Env holds %r, which cmd.exe or the ANSI code page changes on its way to '
                        'the tests, so the record cannot say what they were given' % strange[0])
        scripts = archive.find('child.cmd')
        if len(scripts) != 1:
            return [], ('it carries %d child.cmd where its runner seals one, so the record cannot say what the tests '
                        'were given' % len(scripts))
        script = archive.read(scripts[0])
        if '!' in script:
            return [], ("its child.cmd holds '!', which cmd.exe expands where delayed expansion is on - a setting no "
                        'record holds - so the record cannot say what the tests were given')
        launched = LAUNCHER.fullmatch(script)
        if launched is None:
            return [], ('its child.cmd is not the launcher its runner writes, so the record cannot say what the tests '
                        'were given')
        odd = [argument for argument in launched.groups() if argument.startswith('-') or argument.endswith('\\')]
        if odd:
            return [], ('its child.cmd passes %r, which PowerShell does not read as written - a leading - names a '
                        'parameter, a trailing backslash escapes the closing quote - so the record cannot say what the '
                        'tests were given' % odd[0])
        if launched.group(7) != recorded:
            return [], ('its child.cmd passed %r where its machine record holds Env %r, so the record cannot say what '
                        'the tests were given' % (launched.group(7), recorded))
        pairs = [item for item in recorded.split(';') if item]
    else:
        pairs = [line[len('env='):] for line in text.split('\n') if line.startswith('env=')]
    values = []
    for pair in pairs:
        key, separator, value = pair.partition('=')
        if not (key.upper() if windows else key).startswith('FILECAT_'):
            return [], ('its machine record sets %r, outside FILECAT_ - the test platform reads some such names '
                        '(VSTESTCLIRUNSETTINGS can set the gate inside the test host), so the record cannot say what the '
                        'tests were given' % key)
        if separator and (key.isascii() and key.upper() == GATE_NAME if windows else key == GATE_NAME):
            values.append(value)
    return values, None


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
            values, unread = gate_assignments(archive, records[0])
            if unread is not None:
                problems.append(unread)
            elif not values:
                problems.append('its machine record does not name ' + GATE_NAME)
            elif len(values) > 1:
                problems.append('its machine record gives %s %d values (%s), and its runner applies only the last'
                                % (GATE_NAME, len(values), ', '.join(repr(value) for value in values)))
            elif (values[0] if values else None) != GATE_VALUE:
                problems.append('its machine record sets %s to %r, not %r, so a native edit test could step aside'
                                % (GATE_NAME, values[0] if values else None, GATE_VALUE))
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
        # Any error at all, not a list of the ones met so far: a truncated tar raised EOFError and a bit-rotted
        # member zlib.error, which this list did not hold, so the run ended with a traceback - no verdict for the
        # archives after it and no report written. An archive that cannot be read is one refusal among others.
        except Exception as error:  # noqa: BLE001 - a sealed run that does not read is a finding, not a crash
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
