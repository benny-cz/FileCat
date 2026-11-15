"""Unit checks for the native-edit-storage evidence check: what it accepts, and what it must refuse.

Run: python3 -m unittest discover -s eng/validation/archive-writing -p test_check_edit_storage_evidence.py -v
The real archives are produced by the three machines' edit lanes; these are the smallest archives of each shape.
"""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import random
import re
import sys
import gzip
import shutil
import struct
import subprocess
import tarfile
import tempfile
import unittest
import unittest.mock
import warnings
import zipfile
import zlib

SPEC = importlib.util.spec_from_file_location("checker", Path(__file__).with_name("check_edit_storage_evidence.py"))
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)


def measuring(log_path):
    """measure_rules.py runs this suite with MEASURE_VERDICTS naming a file, to tell a rule that changes a verdict from
    one that changes only a message. Every case then runs to its end with each assertion that does not hold written
    down instead of raised, and the verdict of every call a case makes into the check - an archive accepted (on the
    system it names) or refused, a listing whole or not, a repeated name or none, main()'s exit code with its report's
    verdict lines - is written down beside them, for the outermost call only (what check() asks of its own helpers is
    not a verdict). A call that raises is written down as its own refusal, with what it raised, and handed back as one,
    so the case goes on. Nothing here runs otherwise."""
    current = ['?']
    depth = [0]

    def note(kind, value):
        with open(log_path, 'a', encoding='utf-8') as log:
            log.write('%s\t%s\t%s\n' % (current[0], kind, value))

    def verdict(name, result):
        if name == 'check':
            return 'accepted, %s' % result[1].get('system') if not result[0] else 'refused'
        if name == 'listing_gap':
            return 'whole' if result is None else 'gap'
        if name == 'repeated':
            return 'none repeated' if not result else 'repeated'
        # main(): its exit code and its report's verdict lines - the system and digest a verdict names are what the
        # release manifest binds a route by, and the counts it reports what the records quote of a lane - with each
        # archive's own digest written as such, since a fixture's bytes differ from run to run.
        out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv[:-1] else None
        text = Path(out).read_text(encoding='utf-8') if out and Path(out).is_file() else ''
        for argument in sys.argv[1:]:
            if argument != out and Path(argument).is_file():
                text = text.replace(hashlib.sha256(Path(argument).read_bytes()).hexdigest(),
                                    '<the digest of %s>' % Path(argument).name)
        return 'exit %r: %s' % (result, ' | '.join(line for line in text.split('\n') if line and not line[0].isspace()))

    # What a call that raised stands for: its own refusal, as main() turns any error reading an archive into one - and
    # handed back so, the case goes on to its end and every verdict after it is written down too. A main() that raised
    # wrote no report and exits 1, which is every archive it was given refused, and is written down as that.
    refusals = {'check': (['raised'], {}), 'listing_gap': 'raised', 'repeated': ['raised'], 'main': 1}
    words = {'check': 'refused', 'listing_gap': 'gap', 'repeated': 'repeated'}

    def refused_by_main():
        out = sys.argv[sys.argv.index('--out') + 1] if '--out' in sys.argv[:-1] else None
        given = [argument for argument in sys.argv[1:] if argument != out and not argument.startswith('--')]
        return 'exit 1: ' + ' | '.join('%s: NOT edit-storage evidence' % Path(argument).name for argument in given)

    def outermost(name, function):
        def wrapper(*arguments, **named):
            depth[0] += 1
            try:
                result = function(*arguments, **named)
            except Exception as error:
                if depth[0] != 1:
                    raise
                note('verdict', '%s (raised %s)' % (refused_by_main() if name == 'main' else words[name],
                                                    type(error).__name__))
                return refusals[name]
            finally:
                depth[0] -= 1
            if depth[0] == 0:
                note('verdict', verdict(name, result))
                if name == 'check' and result[0]:
                    note('reason', result[0][0][:160])
            return result
        return wrapper

    checker.check = outermost('check', checker.check)
    checker.main = outermost('main', checker.main)
    checker.Archive.listing_gap = outermost('listing_gap', checker.Archive.listing_gap)
    checker.Archive.repeated = outermost('repeated', checker.Archive.repeated)

    run = unittest.TestCase.run

    def tracked(self, result=None):
        current[0] = self.id().rsplit('.', 1)[-1]
        return run(self, result)
    unittest.TestCase.run = tracked

    def soft(name, method):
        def wrapper(self, *arguments, **named):
            try:
                return method(self, *arguments, **named)
            except AssertionError:
                note('assert', name)
        return wrapper
    managers = {'assertRaises', 'assertRaisesRegex', 'assertWarns', 'assertWarnsRegex', 'assertLogs', 'assertNoLogs'}
    for name in dir(unittest.TestCase):
        if (name.startswith('assert') and name not in managers) or name == 'fail':
            setattr(unittest.TestCase, name, soft(name, getattr(unittest.TestCase, name)))


if os.environ.get('MEASURE_VERDICTS'):
    measuring(os.environ['MEASURE_VERDICTS'])

# The words a run is read against, written out here rather than taken from the check: a fixture built from the check's
# own constants follows them, so a reason dropped or a pin renamed there would change no verdict any case sees.
PIN_NAMES = (
    'Ordinary_startup_composes_native_storage_where_listed_and_the_legacy_store_elsewhere',
    'This_build_lists_native_edit_storage_on_the_owner_s_routes_only',
    'Legacy_and_native_roots_keep_their_own_sessions_and_revisions',
)
STEP_ASIDE_WORDS = (
    'Native edit storage cannot be held for this test folder',
    'Native edit storage refuses an elevated or root process',
    'Native edit storage requires an unelevated process.',
    'Native edit storage refuses root; run as an ordinary user.',
    'Edit sessions refuse an elevated token; run unelevated.',
    'Edit sessions refuse an elevated process.',
    'Run legacy admission unelevated.',
    "The fixture's filesystem cannot be established here.",
    'is not a listed native edit storage route for',
)
PASSING_TRX = ''.join(
    '<UnitTestResult executionId="%d" testName="FileCat.Tests.%s" outcome="Passed" />' % (index, pin)
    for index, pin in enumerate(PIN_NAMES))
SUMMARY = {"ok": True, "failed": [], "exit_codes": {"FileCat.App.Tests-Edit": 0},
           "suites": {"FileCat.App.Tests-Edit": {"total": 7, "passed": 7, "skipped": 0, "failed": 0,
                                                 "counters_total": 7}}}


# The test process each fixture runs, as both runners list it - the Unix runner in its record, the Windows runner in
# tests.txt - beside the log it writes: a run's archive holds a log for each process its runner listed.
TEST_LINE = 'test FileCat.App.Tests-Edit: (FullyQualifiedName~Edit)'


def logger_row(test, outcome, message=None, number=0):
    """A result row as the test platform's TRX logger writes it: its name and its outcome far apart, with the
    attributes the logger writes between them, and a skipped row's reason in its error message (as r35's rows hold
    them; the fifty-third review found the result pattern held by no case for want of them)."""
    ids = '%08x-6184-4683-a741-%012x' % (number, number)
    row = ('<UnitTestResult executionId="%s" testId="%s" testName="%s" computerName="FILECAT-VM" duration="00:00:00.0071731" '
           'startTime="2026-11-14T08:30:44.0704720+01:00" endTime="2026-11-14T08:30:44.0776835+01:00" '
           'testType="13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b" outcome="%s" testListId="8c84fa94-04c1-424b-9868-57a2d4851a1d" '
           'relativeResultsDirectory="%s"' % (ids, ids, test, outcome, ids))
    if message is None:
        return '    ' + row + ' />\r\n'
    return ('    ' + row + '>\r\n      <Output>\r\n        <ErrorInfo>\r\n          <Message>%s</Message>\r\n'
            '        </ErrorInfo>\r\n      </Output>\r\n    </UnitTestResult>\r\n' % message)


def logger_trx(rows):
    """A TRX file as the logger writes it, around its result rows."""
    return ('<?xml version="1.0" encoding="utf-8"?>\r\n<TestRun id="2e34b0aa-6184-4683-a741-373b45165ae0" '
            'name="filecat@FILECAT-VM 2026-11-14 08:29:20" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '\r\n  <Results>\r\n' + ''.join(rows) + '  </Results>\r\n</TestRun>\r\n')


def zip_with_backslashes(path, members):
    """A zip as Compress-Archive writes one: each file deflated, each folder stored, and every name's folders split
    with a backslash - the bytes of each name rewritten after zipfile wrote it, since zipfile turns a backslash into a
    slash on Windows (the fifty-third review found no case holding a name so written)."""
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as held:
        for name, blob in members:
            if name.endswith('/'):
                held.writestr(zipfile.ZipInfo(name), b'')
            else:
                held.writestr(name, blob)
    data = path.read_bytes()
    for name, _ in members:
        if '/' in name:
            raw, folded = name.encode('ascii'), name.replace('/', '\\').encode('ascii')
            assert data.count(raw) == 2, name  # its local header and its directory entry
            data = data.replace(raw, folded)
    path.write_bytes(data)


R35_PROJECTS = ('publish:src\\FileCat.App\\FileCat.App.csproj,tests\\FileCat.Core.Tests\\FileCat.Core.Tests.csproj'
                '#Edit+NativeEditStorage,tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit,'
                'tests\\FileCat.Platform.Windows.Tests\\FileCat.Platform.Windows.Tests.csproj#Edit')
R35_ENV = ('FILECAT_ARCHIVE_CONTAINMENT=1;FILECAT_ARCHIVE_TXF_NATIVE=1;FILECAT_EDIT_NATIVE_REQUIRED=1;'
           'FILECAT_ARCHIVE_APP_ASSETS={run}\\publish\\FileCat.App')


def admitted(bracket):
    """Every printable ASCII character a pattern's bracket admits, in order - read by Python's engine, which reads these
    brackets as the runners' own engines do (a `]` leading a bracket is itself, and so is a `[` inside one)."""
    with warnings.catch_warnings():
        warnings.simplefilter('ignore', FutureWarning)
        pattern = re.compile(bracket)
    return ''.join(chr(code) for code in range(0x20, 0x7f) if pattern.fullmatch(chr(code)))


# What each runner lets through, from the brackets of its own patterns as its source writes them: an accepted case
# carries all of it, so the check is held to accept every character a runner admits and not only the ones the other
# fixtures happen to hold (the fifty-fifth review found a launcher's `(`, `)`, `|` and four of the digits, a hang of
# three digits, an Env value's `:`, and a Unix line's `&` and five of the digits, in no accepted case). The Windows
# runner's -Filter (and -Projects), and each value of its -Env; the Unix runner's filter and each entry, and an env:
# entry's value, which its own pattern also holds to no space.
WINDOWS_FILTER = admitted(r'[ #$&-?A-\]_-~]')
WINDOWS_ENV_VALUE = admitted(r'[ #$&-:<-\]_-~]')
UNIX_ENTRY = admitted('[] !#&-?A-[^-~]')
UNIX_ENV_VALUE = ''.join(character for character in admitted('[!-#%-~]') if character in UNIX_ENTRY)


SECOND = 'FileCat.Core.Tests-Edit-NativeEditStorage'


def two_process_run(unix, counted=0, exited=0):
    """An edit lane as the runners seal one: two test processes, each with its own TRX, log, exit code and line in
    its runner's list, the pins split between them as r35's are - the composition and route pins in the first suite,
    the dual-root pin in the second, which skips a test as r35's suites skip some (the fifty-fourth review found the
    skipped count's sum held by no case, no fixture suite skipping one). `counted` and `exited` are the second suite's
    failure counter and exit code."""
    system = 'Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux' if unix else 'Microsoft Windows 11 Pro 10.0.26220.0'
    platform = ('FileCat.Core.Tests.UnixEditStorageTests.' if unix else
                'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.')
    first = ''.join('<UnitTestResult testName="FileCat.Tests.%s" outcome="Passed" />' % pin for pin in PIN_NAMES[:2])
    counters = {"total": 5, "passed": 4, "skipped": 1, "failed": counted, "counters_total": 5}
    summary = dict(SUMMARY, exit_codes={"FileCat.App.Tests-Edit": 0, SECOND: exited},
                   suites=dict(SUMMARY['suites'], **{SECOND: counters}))
    contents = files(trx=first, summary=summary, system=system)
    contents['results/%s.trx' % SECOND] = ('<?xml version="1.0"?><TestRun><Results><UnitTestResult testName="%s%s" '
                                           'outcome="Passed" /></Results></TestRun>' % (platform, PIN_NAMES[2]))
    contents['test-%s.log' % SECOND] = '[xUnit.net] everything ran\n'
    line = 'test %s: (FullyQualifiedName~Edit|FullyQualifiedName~NativeEditStorage)' % SECOND
    if unix:
        contents['machine.txt'] += line + '\n'
    else:
        contents['tests.txt'] += line + '\r\n'
    return contents


def unix_head(system):
    """What the Unix runner writes into machine.txt before its own lines: `uname -a`, the system's own description
    (`sw_vers` on macOS, `lsb_release -d` elsewhere), its CPUs, the SDK, and the `df -h` line of the run's folder, which
    holds `%` - as r35's records do, and the fifty-second review found a rule held by no fixture for want of it."""
    if system.startswith('Darwin'):
        return [system, 'ProductName:\t\tmacOS', 'ProductVersion:\t\t27.0.1', 'BuildVersion:\t\t26A434', '8', '10.0.101',
                '/dev/disk3s5   926Gi   297Gi   605Gi    33%    2.4M  6.3G    0%   /System/Volumes/Data']
    return [system, 'Description:\tUbuntu 24.04.5 LTS', '12', '10.0.101', '/dev/sda2       196G  103G   83G  56% /']


# The test platform's attachment folder in results/, empty, which both runners seal (r35's hold one per process).
ATTACHMENTS = 'results/2e34b0aa-6184-4683-a741-373b45165ae0'


def windows_json(record):
    """A record as the Windows runner writes it: Windows PowerShell 5.1's ConvertTo-Json - a member a line, four spaces
    in, two after the colon, what is not ASCII as written - through Set-Content, which ends every line with CRLF."""
    return '{\r\n' + ',\r\n'.join('    %s:  %s' % (json.dumps(key), json.dumps(value, ensure_ascii=False))
                               for key, value in record.items()) + '\r\n}\r\n'


def files(gate=True, trx=PASSING_TRX, summary=None, log='[xUnit.net] everything ran\n', status='PASS\r\n',
          gate_value=None, system='Microsoft Windows 11 Pro 10.0.26220.0'):
    """The files the runners seal, as a name-to-text map: each member r35's edit lanes hold, though not each in
    every shape r35's has - the result rows here are short, the launcher's projects plain, the Windows zip's members
    stored and its names split with `/` - shapes the cases built as r35's own lanes carry instead (the fifty-third
    review found five rules the sealed lanes rest on held by none of these fixtures); each argument stands for one way
    a run can differ. The machine record is the one
    the system's own runner writes: the Windows runner's `machine.json`, its caption in `Os` with what is not ASCII as
    written, and its tests.txt; the Unix runner's `machine.txt` - the lines `unix_head` gives, then `packet=`, `filter=`,
    an `entry=` line per entry, its `hang=`, `parallel=` and `env=` lines and a `test` line for its test process. Beside
    them each runner seals its build log, the Windows runner its scheduled task's log, its account and launch records and
    its child's script and output, the Unix runner its start, finish and exit files: the fifty-second review found three
    rules no case held because no fixture carried the members a runner seals besides the ones they read."""
    if gate_value is not None:
        gate_text = checker.GATE_NAME + '=' + gate_value
    else:
        gate_text = checker.GATE if gate else 'FILECAT_EDIT_NATIVE_ROOT=/tmp/x'
    windows = 'Windows' in system
    # The dual-root pin is one method in two real classes, and the check reads the class that passed it: a fixture
    # naming neither is not a run of these suites, so it carries the class this fixture's own system implies.
    platform = ('FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' if windows
                else 'FileCat.Core.Tests.UnixEditStorageTests.')
    trx = trx.replace('FileCat.Tests.' + PIN_NAMES[2], platform + PIN_NAMES[2])
    if windows:
        contents = {'machine.json': windows_json({"RunId": "edit-test", "Os": system,
                                                  "Env": 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate_text})}
    else:
        lines = unix_head(system) + [
            'packet=546d8caf28f54066cbb58e5d222a9e5aab464899ab35568b4108e01fc8c982d4', 'filter=*',
            'entry=hang:25', 'entry=parallel:on', 'entry=env:FILECAT_ARCHIVE_TXF_NATIVE=1', 'entry=env:' + gate_text,
            'entry=tests/FileCat.App.Tests/FileCat.App.Tests.csproj#Edit', 'hang=25m', 'parallel=on',
            'env=FILECAT_ARCHIVE_TXF_NATIVE=1', 'env=' + gate_text, TEST_LINE]
        contents = {'machine.txt': '\n'.join(lines) + '\n'}
    contents.update({
        'summary.json': json.dumps(SUMMARY if summary is None else summary),
        'status.txt': status,
        'results/FileCat.App.Tests-Edit.trx': '<?xml version="1.0"?><TestRun><Results>' + trx + '</Results></TestRun>',
        'test-FileCat.App.Tests-Edit.log': log,
    })
    if windows:
        contents.update({
            'tests.txt': TEST_LINE + '\r\n',
            'build-FileCat.App.Tests.log': ('  Determining projects to restore...\r\n  FileCat.App.Tests -> C:\\FileCatValidation'
                                            '\\aw-claude\\edit-test\\src\\tests\\FileCat.App.Tests\\bin\\Release\\'
                                            'FileCat.App.Tests.dll\r\n'),
            'task.log': ('SUCCESS: The scheduled task "FcRun-edit-test" has successfully been created.\r\n'
                         'SUCCESS: Attempted to run the scheduled task "FcRun-edit-test".\r\n'),
            'account.json': windows_json({"Name": "FILECAT-VM\\Admin", "Sid": "S-1-5-21-1-2-3-1001",
                                          "EnabledAdministrators": False, "ElevationType": 3, "Session": 1, "Cpus": 4}),
            'launch.json': windows_json({"Finished": "2026-11-14T07:34:17.0872071Z"}),
            'child.ps1': 'param([string]$Run, [string]$Packet, [string]$Filter, [string]$Projects)\r\n',
            'child.out': '',
        })
    else:
        contents.update({
            'build-FileCat.App.Tests.log': ('  Determining projects to restore...\n  FileCat.App.Tests -> /home/benny/'
                                            'FileCatValidation/aw-claude/edit-test/src/tests/FileCat.App.Tests/bin/'
                                            'Release/FileCat.App.Tests.dll\n'),
            'started.txt': '2026-11-14T08:48:57Z\n',
            'finished.txt': '2026-11-14T08:52:55Z\n',
            'exit-FileCat.App.Tests-Edit.txt': '0\n',
        })
    return contents


LAUNCHED = {'run': 'C:\\FileCatValidation\\aw-claude\\edit-test',
            'packet': 'C:\\FileCatValidation\\aw-claude\\packets\\src-048bf334.tar.gz', 'filter': '*',
            'projects': 'tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit',
            'dotnet': 'C:\\FileCatValidation\\dotnet\\dotnet.exe', 'hang': '20'}


def launcher(env, **changed):
    """The child.cmd the Windows runner writes, character for character, with whichever arguments a case changes."""
    given = dict(LAUNCHED, env=env, **changed)
    return ('@echo off\r\nC:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -ExecutionPolicy '
            'Bypass -WindowStyle Hidden -File "{run}\\child.ps1" -Run "{run}" -Packet "{packet}" -Filter "{filter}" '
            '-Projects "{projects}" -Dotnet "{dotnet}" -Env "{env}" -HangMinutes {hang} > "{run}\\child.out" 2>&1\r\n'
            ).format(**given)


def with_child_cmd(contents):
    """The Windows runner also seals the child.cmd that handed its `-Env` to the child, and the check reads the gate only
    where that file is the runner's own launcher passing the record's Env - so a fixture carries one, written as the
    runner writes it from its machine record's Env, unless the case wrote its own. A case drops it by setting it to
    None."""
    if contents.get('child.cmd', '') is None:
        return {name: text for name, text in contents.items() if name != 'child.cmd'}
    if 'machine.json' not in contents or 'child.cmd' in contents:
        return contents
    record = contents['machine.json']
    if isinstance(record, bytes):
        # A record a case gives as bytes: UTF-8 with a mark, as the runner writes it now, or an ANSI code page, in which
        # its Env is ASCII whichever page it is.
        mark = bytes((0xEF, 0xBB, 0xBF))
        record = record[3:].decode('utf-8') if record.startswith(mark) else record.decode('latin-1')
    try:
        env = json.loads(record).get('Env')
    except (ValueError, AttributeError):
        return contents
    if not isinstance(env, str):
        return contents
    return dict(contents, **{'child.cmd': launcher(env)})


def sealed(leaf, text, unix):
    """A member's bytes as its runner writes them now. The test platform writes each TRX file in UTF-8 with a mark, on
    both systems. The Unix runner's other members are UTF-8 with no mark, its status ending in a newline. The Windows
    runner's status ends in a carriage return and a newline; its logs - each test process's, each build's and publish's,
    and the scheduled task's, all through Windows PowerShell 5.1's `*>` - are UTF-16 with a mark; its JSON records are
    UTF-8 with a mark (`Set-Content -Encoding UTF8`: machine.json and summary.json since the forty-ninth review,
    account.json and launch.json since the fifty-second; the r35 lanes' were in the ANSI code page with no mark, which a
    case builds as bytes). Bytes a case gives stand."""
    if isinstance(text, bytes):
        return text
    name = leaf.split('/')[-1]
    if name == 'status.txt' and unix:
        text = text.replace('\r\n', '\n')
    if name.endswith('.trx'):
        return bytes((0xEF, 0xBB, 0xBF)) + text.encode('utf-8')
    if name.endswith('.log') and not unix:
        return bytes((0xFF, 0xFE)) + text.encode('utf-16-le')
    if name.endswith('.json') and not unix:
        return bytes((0xEF, 0xBB, 0xBF)) + text.encode('utf-8')
    return text.encode('utf-8')


def archive(directory, name, contents, unix=False):
    """The archive its runner seals of these contents, folders and all: the Unix runner tars its run folder - that
    folder, its results folder and the test platform's attachment folder are members of it - and the Windows runner
    zips the folder's contents, the empty attachment folder an entry of its own - the members as r35's archives hold
    them, though here stored where Compress-Archive deflates and named with `/` where it writes a backslash, which
    zipfile cannot write on Windows; `zip_with_backslashes` writes those."""
    contents = with_child_cmd(contents)
    path = Path(directory) / name
    if unix:
        with tarfile.open(path, 'w:gz') as tar:
            for folder in ('edit-test', 'edit-test/results', 'edit-test/' + ATTACHMENTS):
                item = tarfile.TarInfo(folder)
                item.type = tarfile.DIRTYPE
                item.mode = 0o755
                tar.addfile(item)
            for leaf, text in contents.items():
                blob = sealed(leaf, text, unix)
                item = tarfile.TarInfo('edit-test/' + leaf)
                item.size = len(blob)
                tar.addfile(item, io.BytesIO(blob))
    else:
        with zipfile.ZipFile(path, 'w') as zipped:
            zipped.writestr(ATTACHMENTS + '/', b'')
            for leaf, text in contents.items():
                zipped.writestr(leaf.replace('/', '\\'), sealed(leaf, text, unix))
    return path


class EvidenceChecks(unittest.TestCase):
    def check(self, contents, unix=False, name='edit-run-evidence'):
        with tempfile.TemporaryDirectory() as directory:
            leaf = name + ('.tar.gz' if unix else '.zip')
            problems, facts = checker.check(archive(directory, leaf, contents, unix))
        return problems, facts

    def test_accepts_a_run_that_carries_the_gate_the_pins_and_no_failure(self):
        for unix in (False, True):
            with self.subTest(unix=unix):
                problems, facts = self.check(files(), unix)
                self.assertEqual([], problems)
                self.assertEqual(set(checker.PINS), set(facts['pins']))

    def test_accepts_a_windows_run_whose_other_platform_s_class_stepped_aside(self):
        """The dual-root pin is the same method in two classes, and a Windows run holds both: the Core suite's Unix
        class says "Run the Windows native storage lane." while the Windows platform suite's own class passes - read in
        either order, and r35's reads the Unix class first, the Core suite's results file before the platform suite's
        (the fifty-first review: every fixture had the passing row first)."""
        pin = checker.PINS[2]
        passed = '<UnitTestResult testName="FileCat.Platform.Windows.Tests.WindowsEditStorageTests.%s" outcome="Passed" />' % pin
        aside = '<UnitTestResult testName="FileCat.Core.Tests.UnixEditStorageTests.%s" outcome="NotExecuted" />' % pin
        rest = PASSING_TRX.replace('<UnitTestResult executionId="2" testName="FileCat.Tests.%s" outcome="Passed" />' % pin, '')
        for first, trx in (('the class that stepped aside', rest + aside + passed), ('the class that passed', rest + passed + aside)):
            with self.subTest(first=first):
                problems, facts = self.check(files(trx=trx))
                self.assertEqual([], problems)
                self.assertEqual(2, len(facts['pins'][pin]))

    def test_refuses_a_run_in_which_every_case_of_a_pin_stepped_aside(self):
        """A pin stands only where it ran: every case of it NotExecuted is no evidence, whichever pin it is - the
        dual-root pin's family rule refuses that one too, so the first pin is what this rule alone refuses."""
        pin = PIN_NAMES[2]
        trx = PASSING_TRX.replace('testName="FileCat.Tests.%s" outcome="Passed"' % pin,
                                  'testName="FileCat.Tests.%s" outcome="NotExecuted"' % pin)
        problems, _ = self.check(files(trx=trx))
        self.assertIn('no case of the pin %s ran: %s'
                      % (pin, ['FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin]), problems)
        pin = PIN_NAMES[0]
        trx = PASSING_TRX.replace('testName="FileCat.Tests.%s" outcome="Passed"' % pin,
                                  'testName="FileCat.Tests.%s" outcome="NotExecuted"' % pin)
        self.assertEqual(['no case of the pin %s ran: %s' % (pin, ['FileCat.Tests.' + pin])],
                         self.check(files(trx=trx))[0])

    def test_refuses_a_run_whose_pin_failed(self):
        pin = checker.PINS[1]
        trx = PASSING_TRX.replace('testName="FileCat.Tests.%s" outcome="Passed"' % pin,
                                  'testName="FileCat.Tests.%s" outcome="Failed"' % pin)
        problems, _ = self.check(files(trx=trx))
        self.assertTrue(any(problem.startswith('the pin %s did not pass' % pin) for problem in problems), problems)

    def test_refuses_a_run_whose_machine_record_does_not_name_the_gate(self):
        problems, _ = self.check(files(gate=False))
        self.assertEqual(['its machine record does not name ' + checker.GATE_NAME], problems)

    def test_refuses_a_run_that_set_the_gate_to_another_value(self):
        """`=10` holds the gate's name as a substring and nothing else: every reader compares the value to "1"."""
        problems, _ = self.check(files(gate_value='10'))
        self.assertEqual(["its machine record sets %s to '10', not '1', so a native edit test could step aside"
                          % checker.GATE_NAME], problems)

    def test_refuses_a_run_with_no_machine_record_at_all(self):
        contents = files()
        del contents['machine.json']
        problems, _ = self.check(contents)
        self.assertIn('it carries no machine record, so nothing in it says the gate was in force', problems)

    def test_reads_the_gate_out_of_the_unix_runner_s_own_record(self):
        """The Unix runner writes one `env=NAME=VALUE` line per variable into machine.txt."""
        contents = files(system='Linux filecat-ubuntu2404 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        contents['machine.txt'] = ('Linux filecat-ubuntu2404 7.0.0-43-generic #43 SMP x86_64 GNU/Linux\n'
                                   'packet=abc\nfilter=*\nenv=FILECAT_ARCHIVE_TXF_NATIVE=1\nenv=%s\n%s\n'
                                   % (checker.GATE, TEST_LINE))
        # Its results name neither class, so the record is all that says the system - as it is between Linux and macOS.
        problems, _ = self.check(contents, unix=True)
        self.assertEqual([], problems)

    def test_refuses_a_run_that_is_missing_a_pin(self):
        trx = ''.join(part for part in PASSING_TRX.split('/>') if checker.PINS[0] not in part)
        problems, _ = self.check(files(trx=trx))
        self.assertIn('it does not hold the pin ' + checker.PINS[0], problems)

    def test_reads_a_result_whose_attributes_are_in_the_other_order(self):
        reversed_trx = ''.join(
            '<UnitTestResult outcome="Passed" testName="FileCat.Tests.%s" />' % pin for pin in checker.PINS)
        problems, _ = self.check(files(trx=reversed_trx))
        self.assertEqual([], problems)

    def test_reads_a_theory_row_as_its_pin(self):
        rows = ''.join('<UnitTestResult testName="FileCat.Tests.%s(store: 1)" outcome="Passed" />' % pin
                       for pin in checker.PINS)
        problems, _ = self.check(files(trx=rows))
        self.assertEqual([], problems)

    def test_refuses_a_run_with_a_failure_however_it_is_recorded(self):
        """A failure counts wherever the runner wrote it down - its summary's list, a suite's counter, its own ok, a
        suite's exit code - and each place alone is enough."""
        summary = {"ok": False, "failed": ["FileCat.Tests.Something"], "exit_codes": {"FileCat.App.Tests-Edit": 1},
                   "suites": {"FileCat.App.Tests-Edit": {"total": 7, "passed": 6, "skipped": 0, "failed": 1,
                                                         "counters_total": 7}}}
        problems, _ = self.check(files(summary=summary))
        self.assertIn('it failed: FileCat.Tests.Something', problems)
        self.assertIn('suite FileCat.App.Tests-Edit has 1 failures', problems)
        self.assertIn('the runner did not call this run ok', problems)
        self.assertIn('suite FileCat.App.Tests-Edit exited 1', problems)
        counters = dict(SUMMARY['suites']['FileCat.App.Tests-Edit'], failed=1)
        for change, said in (({'failed': ['FileCat.Tests.Something']}, 'it failed: FileCat.Tests.Something'),
                             ({'suites': {'FileCat.App.Tests-Edit': counters}},
                              'suite FileCat.App.Tests-Edit has 1 failures'),
                             ({'ok': False}, 'the runner did not call this run ok'),
                             ({'exit_codes': {'FileCat.App.Tests-Edit': 1}}, 'suite FileCat.App.Tests-Edit exited 1')):
            with self.subTest(said):
                self.assertEqual([said], self.check(files(summary=dict(SUMMARY, **change)))[0])

    def test_refuses_a_run_the_runner_did_not_call_ok_even_with_clean_counters(self):
        """A test host that dies, or a lane whose rows and counters disagree, is only visible in the runner's verdict."""
        summary = dict(SUMMARY, ok=False)
        problems, _ = self.check(files(summary=summary))
        self.assertEqual(['the runner did not call this run ok'], problems)

    def test_refuses_a_run_whose_summary_carries_no_exit_code(self):
        summary = {key: value for key, value in SUMMARY.items() if key != 'exit_codes'}
        problems, _ = self.check(files(summary=summary))
        self.assertEqual(['its summary carries no exit code'], problems)

    def test_refuses_a_run_whose_status_is_not_pass(self):
        problems, _ = self.check(files(status='FAIL'))
        self.assertIn('its status is not PASS', problems)

    def test_refuses_a_run_whose_status_file_is_absent(self):
        contents = files()
        del contents['status.txt']
        problems, _ = self.check(contents)
        self.assertEqual(['it carries no status.txt'], problems)

    def test_refuses_a_run_whose_test_output_says_nothing(self):
        problems, _ = self.check(files(log='   \n'))
        self.assertEqual(['its test output is empty'], problems)

    def test_refuses_a_second_summary_that_failed(self):
        """Every summary in the archive is read, not the first one by name."""
        contents = files()
        contents['zz-other/summary.json'] = json.dumps(dict(SUMMARY, failed=['FileCat.Tests.Other'], ok=False))
        problems, _ = self.check(contents)
        self.assertIn('it failed: FileCat.Tests.Other', problems)

    def test_does_not_take_a_test_merely_ending_in_a_pin_s_text_for_that_pin(self):
        pin = checker.PINS[0]
        trx = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Tests.Not_' + pin, 1)
        problems, _ = self.check(files(trx=trx))
        self.assertIn('it does not hold the pin ' + pin, problems)

    def test_refuses_a_run_whose_test_output_is_absent(self):
        """The step-aside scan reads those logs: without them it is not a check that passed - and a process its
        runner listed without its log is refused for that too."""
        contents = files()
        del contents['test-FileCat.App.Tests-Edit.log']
        problems, _ = self.check(contents)
        self.assertIn('it carries no test output to read', problems)
        self.assertTrue(any('its runner listed 1 test processes where the archive holds the logs of 0' in problem
                            for problem in problems), problems)
        del contents['tests.txt']
        self.assertEqual(['it carries no test output to read'], self.check(contents)[0])

    def test_refuses_a_run_where_a_test_stepped_aside_for_native_storage(self):
        """Each reason, written out here rather than taken from the check: a reason dropped there is then a run
        accepted here."""
        for reason in STEP_ASIDE_WORDS:
            with self.subTest(reason=reason):
                problems, _ = self.check(files(log='[xUnit.net] SKIP ' + reason + ' here\n'))
                self.assertTrue(any('stepped aside' in problem for problem in problems), problems)

    def test_refuses_a_run_with_no_results_to_read(self):
        contents = files()
        del contents['results/FileCat.App.Tests-Edit.trx']
        problems, _ = self.check(contents)
        self.assertIn('it carries no test results to read', problems)

    def test_refuses_an_archive_without_a_summary(self):
        contents = files()
        del contents['summary.json']
        problems, _ = self.check(contents)
        self.assertEqual(['it carries no summary.json'], problems)

    def test_reads_the_system_each_runner_records_in_its_own_way(self):
        """What binds an archive to a route: the Windows runner's `Os`, the Unix runner's first `uname -a` line."""
        self.assertEqual('windows', self.check(files())[1]['system'])
        for first, system in (('Linux filecat-ubuntu2404 7.0.0-43-generic #43 SMP x86_64 GNU/Linux', 'linux'),
                              ('Darwin Mareks-MacBook-Pro.local 27.0.0 Darwin Kernel Version 27.0.0 arm64', 'macos')):
            contents = files(system=first)
            contents['machine.txt'] = first + '\nenv=%s\n%s\n' % (checker.GATE, TEST_LINE)
            problems, facts = self.check(contents, unix=True)
            self.assertEqual([], problems)
            self.assertEqual(system, facts['system'])

    def test_refuses_a_run_that_carries_a_second_machine_record(self):
        """Put an extra record before the real one and the system was read from the extra one: nothing was removed, and
        the archive said whatever that record said. A run seals one record."""
        contents = files()
        contents['aa-notes/machine.json'] = json.dumps({"Os": "Linux something 6.0.0 x86_64 GNU/Linux",
                                                        "Env": checker.GATE})
        problems, _ = self.check(contents)
        self.assertTrue(any('it carries 2 machine records' in problem for problem in problems), problems)

    def test_refuses_a_run_whose_record_and_results_disagree_about_its_system(self):
        """One rewritten `Os` field had this check call a real Windows run Linux. The dual-root pin's own class is in
        the run's results, so the run says the family too, and the two have to agree - here a Unix record with a
        Windows run's results, since a Windows record names no other system (the forty-eighth review)."""
        pin = checker.PINS[2]
        windows = (PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin) +
                   '<UnitTestResult testName="FileCat.Core.Tests.UnixEditStorageTests.%s" outcome="NotExecuted" />' % pin)
        unix = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Core.Tests.UnixEditStorageTests.' + pin)
        self.assertEqual([], self.check(files(trx=windows))[0])  # its record says Windows, as that run's does
        problems, _ = self.check(files(trx=windows, system='Linux host 6.0.0 x86_64 GNU/Linux'), unix=True)
        self.assertIn('its machine record says linux while its own dual-root pin passed in the Windows platform '
                      "suite's class, so the record and the run disagree about which system ran it", problems)
        problems, _ = self.check(files(trx=unix, system='Darwin host 27.0.0 arm64'), unix=True)
        self.assertEqual([], problems)  # a Unix run and a Unix record agree, whichever Unix it is
        problems, _ = self.check(files(trx=unix))
        self.assertIn("its machine record says windows while its own dual-root pin passed in the Core suite's Unix "
                      'class, so the record and the run disagree about which system ran it', problems)

    def test_reads_the_family_past_a_third_class_that_also_passed_the_pin(self):
        """Read as a set of labels, a third class passing the dual-root pin silenced the agreement rule altogether:
        one added row had this check accept the real Windows archive as Linux evidence. Each class is asked for on its
        own now, so a third one is ignored and the two known ones are read as they were - on either system, the third
        class sorting before the platform class, as a shared one in the Core suite does (the fifty-fourth review found
        both classes' reading of every name held or not by the string-hash seed each run drew)."""
        pin = checker.PINS[2]
        windows = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin)
        unix = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Core.Tests.UnixEditStorageTests.' + pin)
        shared = '<UnitTestResult testName="FileCat.Core.Tests.EditStorageSharedTests.%s" outcome="Passed" />' % pin
        self.assertEqual([], self.check(files(trx=windows + shared))[0])
        problems, _ = self.check(files(trx=windows + shared, system='Linux host 6.0.0 x86_64 GNU/Linux'), unix=True)
        self.assertIn('its machine record says linux while its own dual-root pin passed in the Windows platform '
                      "suite's class, so the record and the run disagree about which system ran it", problems)
        problems, facts = self.check(files(trx=unix + shared, system='Linux host 6.0.0 x86_64 GNU/Linux'), unix=True)
        self.assertEqual(([], 'linux'), (problems, facts.get('system')))
        problems, _ = self.check(files(trx=unix + shared))
        self.assertIn("its machine record says windows while its own dual-root pin passed in the Core suite's Unix "
                      'class, so the record and the run disagree about which system ran it', problems)

    def test_refuses_a_run_whose_pin_passed_in_both_platforms_classes(self):
        """No one run passes both: the Unix class is not executed on Windows and the Windows class does not exist
        elsewhere. Results saying both say nothing about what ran it."""
        pin = checker.PINS[2]
        both = (PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin) +
                '<UnitTestResult testName="FileCat.Core.Tests.UnixEditStorageTests.%s" outcome="Passed" />' % pin)
        problems, _ = self.check(files(trx=both))
        self.assertIn("its own dual-root pin passed in the Windows platform suite's class and in the Core suite's Unix "
                      'class, which no one run does, so its results do not say what ran it', problems)

    # The file's own layout, which decides whether an archive is the whole of its file. Each shape below is built by
    # hand because no writer makes it: a directory that lies about a size, a member that overlaps another, a listing
    # that stops short of the directory or runs past it, a tar with something before it. `CENTRAL` is a central
    # directory entry: signature, versions, flag, method, time, date, CRC, the two sizes, the three lengths, the disk,
    # the two attribute words and the local header's offset.
    CENTRAL = '<4sHHHHHHIIIHHHHHII'

    def rewritten_directory(self, path, entries, at=None, names=None):
        """The archive at `path` with a directory listing only `entries`, as (ZipInfo, compressed size).

        `at` writes it where the archive's own directory was, so not one byte of any member changes; `names` gives an
        entry a name of its own, which is how one member is listed twice under two names.
        """
        blob = path.read_bytes() if at is None else path.read_bytes()[:at]
        body = bytearray()
        for index, (item, size) in enumerate(entries):
            name = ((names or {}).get(index) or item.filename).encode('utf-8')
            body += struct.pack(self.CENTRAL, b'PK\x01\x02', 20, 20, item.flag_bits, item.compress_type, 0, 0,
                                item.CRC, size, item.file_size, len(name), 0, 0, 0, 0, 0, item.header_offset) + name
        path.write_bytes(blob + bytes(body) + struct.pack(
            '<4sHHHHIIH', b'PK\x05\x06', 0, 0, len(entries), len(entries), len(body), len(blob), 0))
        return path

    def laid_out(self, directory, contents=None, leaf='edit-layout-evidence.zip'):
        """A fixture zip, its members in file order with each one's own compressed size from its local header."""
        path = archive(directory, leaf, contents or files())
        order = []
        with zipfile.ZipFile(path) as held, open(path, 'rb') as handle:
            start = held.start_dir
            for item in sorted(held.infolist(), key=lambda entry: entry.header_offset):
                handle.seek(item.header_offset)
                head = handle.read(30)
                name, extra = struct.unpack('<HH', head[26:30])
                order.append((item, 30 + name + extra + item.compress_size))
        return path, order, start

    def test_refuses_a_directory_that_states_a_size_the_file_does_not(self):
        """A zip is sized from its directory, and a deflated member stops at its own stream end - so a directory that
        overstates a size leaves the member's bytes alone while its span swallows what follows, which is how an
        appended directory covered a whole sealed archive on paper. The local header states the size too. Stretched
        over bytes put after the last member, with every member still listed, it is the only rule that refuses."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, start = self.laid_out(directory)
            first = order[0][0]
            stretched = start - first.header_offset - (order[0][1] - first.compress_size)
            self.rewritten_directory(path, [(first, stretched)])
            problems, _ = checker.check(path)
            self.assertTrue(any('where the file says' in problem for problem in problems), problems)
        with tempfile.TemporaryDirectory() as directory:
            contents = with_child_cmd(files())
            contents['zz-unread.bin'] = 'bytes no rule reads ' * 8
            path, order, start = self.laid_out(directory, contents)
            last = order[-1][0]
            self.assertEqual('zz-unread.bin', last.filename)
            junk = bytes(64)
            path.write_bytes(path.read_bytes()[:start] + junk)
            entries = [(item, item.compress_size) for item, _ in order[:-1]] + [(last, last.compress_size + len(junk))]
            self.rewritten_directory(path, entries)
            problems, _ = checker.check(path)
            self.assertEqual(1, len(problems), problems)
            self.assertIn('where the file says', problems[0])

    def test_refuses_a_listing_that_stops_short_of_its_directory(self):
        """Once the sizes have to agree, a forged directory can still leave an entry out - and those bytes are then in
        the file and in no member of it."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, _ = self.laid_out(directory)
            self.rewritten_directory(path, [(item, item.compress_size) for item, _ in order[:-1]])
            problems, _ = checker.check(path)
            self.assertTrue(any('in no member of it' in problem for problem in problems), problems)

    def test_refuses_a_gap_padded_with_a_member_lying_inside_another(self):
        """Where a member's own bytes hold a local file header - a member holding a stored zip, say - a directory
        alone can drop an entry and pad the gap with the member inside: both names honest, no name repeated, the
        coverage exact, and a log that said a test had stepped aside listed by nothing. The overlap check is the only
        rule that refuses it, and the reader reads that inner member happily."""
        reason = checker.STEP_ASIDE[0]
        with tempfile.TemporaryDirectory() as directory:
            held = io.BytesIO()
            body = 'nested member bytes, with room for the log it will stand for' * 4
            with zipfile.ZipFile(held, 'w') as inner:  # stored, so its member's own header is in these bytes
                inner.writestr('nested.txt', body)
            blob = held.getvalue()
            with zipfile.ZipFile(io.BytesIO(blob)) as reading:
                nested = reading.infolist()[0]
                span = 30 + len(nested.filename) + len(nested.extra) + nested.compress_size
            text = '[xUnit.net] %s\n' % reason
            padding = span - 30 - len('test-h.log') - len(text)
            self.assertGreaterEqual(padding, 0, 'the log has to fit the span it will be swapped for')
            path = Path(directory) / 'edit-nested-evidence.zip'
            with zipfile.ZipFile(path, 'w') as writing:  # the log before the outer member, so the reach still lands
                for name, member in with_child_cmd(files()).items():  # not `body`, which is the inner member's own text
                    writing.writestr(name.replace('/', '\\'), member)
                writing.writestr('test-h.log', text + 'x' * padding)
                writing.writestr('outer.bin', blob)
            self.assertTrue(any(reason in problem for problem in checker.check(path)[0]))
            with zipfile.ZipFile(path) as reading:
                start = reading.start_dir
                entries = sorted(reading.infolist(), key=lambda item: item.header_offset)
                outer = next(item for item in entries if item.filename == 'outer.bin')
                inside = zipfile.ZipInfo('nested.txt')
            with open(path, 'rb') as handle:  # where that member's bytes start, from its own header
                handle.seek(outer.header_offset)
                head = handle.read(30)
                name_length, extra_length = struct.unpack('<HH', head[26:30])
                inside.header_offset = outer.header_offset + 30 + name_length + extra_length
            if True:
                inside.CRC, inside.compress_type, inside.flag_bits = nested.CRC, nested.compress_type, 0
                inside.file_size = nested.file_size
            kept = [(item, item.compress_size) for item in entries if item.filename != 'test-h.log']
            self.rewritten_directory(path, kept + [(inside, nested.compress_size)], at=start)
            with checker.Archive(path) as reading:
                self.assertEqual(body, reading.read('nested.txt'))  # the reader reads it happily
            problems, _ = checker.check(path)
            self.assertFalse(any(reason in problem for problem in problems), 'the log is hidden, as the forgery wants')
            self.assertTrue(any('overlap' in problem for problem in problems), problems)

    @staticmethod
    def tar_called(kind):
        """A tar on this machine whose own version line names it: 'GNU' writes the sparse member, 'bsdtar' the zip."""
        for name in ('tar', 'bsdtar', 'C:/Windows/System32/tar.exe'):
            found = shutil.which(name) or (name if Path(name).is_file() else None)
            if found is None:
                continue
            said = subprocess.run([found, '--version'], capture_output=True, text=True)
            if kind in (said.stdout or '') + (said.stderr or ''):
                return found
        return None

    @staticmethod
    def with_a_hole(directory):
        """A tree holding a file with a megabyte of hole in it, for a writer that stores holes as holes."""
        tree = Path(directory) / 'run'
        tree.mkdir()
        with open(tree / 'hole.bin', 'wb') as handle:
            handle.write(b'x' * 1024)
            handle.seek(1024 * 1024)
            handle.write(b'y' * 1024)
        (tree / 'a.txt').write_text('x', encoding='utf-8')
        return tree

    def test_accepts_a_tar_whose_member_holds_less_than_it_expands_to(self):
        """A sparse member's size is what it expands to, not what the archive holds, so measuring it by its size
        refused the archive by however many zeros the writer left out - a latent refusal of a runner's own tar."""
        found = self.tar_called('GNU')
        if found is None:
            self.skipTest('no GNU tar on this machine')
        with tempfile.TemporaryDirectory() as directory:
            self.with_a_hole(directory)
            for leaf, flags in (('sparse.tar.gz', ['-S']), ('plain.tar.gz', [])):
                done = subprocess.run([found] + flags + ['-czf', leaf, 'run'], cwd=directory, capture_output=True)
                self.assertEqual(0, done.returncode, done.stderr[-300:])
                with self.subTest(leaf):
                    with checker.Archive(Path(directory) / leaf) as held:
                        self.assertIsNone(held.listing_gap())
            with tarfile.open(Path(directory) / 'sparse.tar.gz') as held:
                self.assertTrue(any(getattr(item, 'sparse', None) for item in held.getmembers()),
                                'this tar did not write the hole as a hole, so the case proves nothing')

    def test_accepts_a_wide_descriptor_whose_header_carries_no_zip64_field(self):
        """libarchive decides zip64 for the whole archive and then writes eight-byte sizes in every descriptor while
        its local headers carry no zip64 field at all - so the descriptor itself says how wide it is."""
        found = self.tar_called('bsdtar')
        if found is None:
            self.skipTest('no libarchive tar on this machine')
        with tempfile.TemporaryDirectory() as directory:
            self.with_a_hole(directory)
            done = subprocess.run([found, '--format', 'zip', '--options', 'zip:zip64', '-cf', 'wide.zip', 'run'],
                                  cwd=directory, capture_output=True)
            self.assertEqual(0, done.returncode, done.stderr[-300:])
            wide = Path(directory) / 'wide.zip'
            with zipfile.ZipFile(wide) as held:
                self.assertTrue(any(item.flag_bits & 0x08 for item in held.infolist()),
                                'this writer did not write trailing descriptors, so the case proves nothing')
            with checker.Archive(wide) as held:
                self.assertIsNone(held.listing_gap())

    def test_refuses_a_gap_padded_with_a_second_name_for_another_member(self):
        """A directory written where the archive's own was - so no member byte changes - that drops one entry and
        pads the gap by listing another member a second time under a name of its own. Every size agrees, the
        coverage is exact and no name repeats, and the dropped file is in the file and listed by nothing: what
        refuses it is that the member it points at carries its own name, which this entry is not."""
        reason = checker.STEP_ASIDE[0]
        contents = files()
        contents['test-FileCat.Core.Tests-Edit.log'] = '[xUnit.net] %s\n' % reason
        with tempfile.TemporaryDirectory() as directory:
            path, order, start = self.laid_out(directory, contents)
            self.assertTrue(any(reason in problem for problem in checker.check(path)[0]))
            dropped = next(index for index, (item, _) in enumerate(order)
                           if item.filename.endswith('Core.Tests-Edit.log'))
            padding = next(index for index, (item, _) in enumerate(order) if index != dropped)
            kept = [(item, item.compress_size) for index, (item, _) in enumerate(order) if index != dropped]
            # The dropped member's bytes are covered by listing another member a second time, under a name of its own.
            twin = order[padding][0]
            kept.append((twin, twin.compress_size))
            spare = {len(kept) - 1: 'padding.bin'}
            self.rewritten_directory(path, kept, at=start, names=spare)
            problems, _ = checker.check(path)
            self.assertFalse(any(reason in problem for problem in problems), 'the log is hidden, as the forgery wants')
            self.assertTrue(any('where it points' in problem for problem in problems), problems)

    def test_refuses_a_listing_with_a_hole_in_the_middle(self):
        """The half of the sums that catches a hole anywhere but at the end: drop an entry in the middle and the
        reach still lands on the directory, so only the covered bytes say anything. The member dropped is one no
        rule needs, so nothing else refuses."""
        with tempfile.TemporaryDirectory() as directory:
            items = list(with_child_cmd(files()).items())
            contents = dict(items[:2] + [('extra.bin', 'bytes no rule reads ' * 8)] + items[2:])
            path, order, start = self.laid_out(directory, contents)
            kept = [(item, item.compress_size) for item, _ in order if item.filename != 'extra.bin']
            self.assertEqual(len(order) - 1, len(kept))
            self.rewritten_directory(path, kept, at=start)
            problems, _ = checker.check(path)
            self.assertTrue(any('in no member of it' in problem for problem in problems), problems)

    def test_refuses_a_member_renamed_by_its_directory(self):
        """Every rule here finds its files by the directory's names, and a renamed member cannot be read - so a
        directory that renames one hides it: a log that said a test had stepped aside stopped being a log, with every
        byte of the archive untouched. The file gives each member its own name too."""
        reason = checker.STEP_ASIDE[0]
        contents = files()
        contents['test-FileCat.Core.Tests-Edit.log'] = '[xUnit.net] %s\n' % reason
        # A name sorting before the member's own and one sorting after it: the two are compared for being the same, not
        # for an order (the fifty-fourth review found a name sorting after held by no case).
        for name in ('notes.bin', 'zzzz-hidden.log'):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                path, order, start = self.laid_out(directory, contents)
                renamed = {index: name for index, (item, _) in enumerate(order)
                           if item.filename.endswith('Core.Tests-Edit.log')}
                self.rewritten_directory(path, [(item, item.compress_size) for item, _ in order], at=start,
                                         names=renamed)
                problems, _ = checker.check(path)
                self.assertTrue(any('where it points' in problem for problem in problems), problems)

    def test_refuses_a_gzip_stream_that_does_not_read(self):
        """A reader stops at the first tar's end-of-archive blocks and never reaches the trailer, so a second member
        whose own trailer is broken is read as if it were not there at all."""
        with tempfile.TemporaryDirectory() as directory:
            clean = archive(directory, 'edit-clean-evidence.tar.gz', files(), unix=True)
            failed = archive(directory, 'edit-failed-evidence.tar.gz', files(status='FAIL'), unix=True)
            broken = bytearray(clean.read_bytes())
            broken[-8:-4] = b'\x00\x00\x00\x00'  # its CRC32 trailer
            joined = Path(directory) / 'edit-joined-evidence.tar.gz'
            joined.write_bytes(bytes(broken) + failed.read_bytes())
            problems, _ = checker.check(joined)
            self.assertTrue(any('cannot be read' in problem for problem in problems), problems)

    def test_refuses_a_tar_that_is_not_the_whole_of_what_was_compressed(self):
        """The rule is about the tar, not its wrapper: one gzip member can hold a clean tar and then a failed one,
        which a reader never reaches - and a tar that is not gzipped at all was asked nothing."""
        with tempfile.TemporaryDirectory() as directory:
            clean = Path(directory) / 'clean.tar'
            failed = Path(directory) / 'failed.tar'
            for path, contents in ((clean, with_child_cmd(files())), (failed, with_child_cmd(files(status='FAIL')))):
                with tarfile.open(path, 'w') as held:
                    for leaf, text in contents.items():
                        blob = text.encode('utf-8')
                        item = tarfile.TarInfo('run/' + leaf)
                        item.size = len(blob)
                        held.addfile(item, io.BytesIO(blob))
            both = clean.read_bytes() + failed.read_bytes()
            wrapped = Path(directory) / 'edit-wrapped-evidence.tar.gz'
            wrapped.write_bytes(gzip.compress(both))
            problems, _ = checker.check(wrapped)
            self.assertTrue(any('not padding' in problem for problem in problems), problems)
            bare = Path(directory) / 'edit-bare-evidence.tar.gz'
            bare.write_bytes(both)
            problems, _ = checker.check(bare)
            self.assertTrue(any('not gzipped' in problem for problem in problems), problems)

    def test_refuses_a_member_listed_twice_in_the_directory(self):
        """One member listed at its own offset twice: the bytes are covered twice and a hole of the same size goes
        unseen, which is what the overlap guard is for - though it is the repeated name that refuses this, since a
        span now runs as far as the member's own header says and two real members cannot overlap."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, _ = self.laid_out(directory)
            doubled = [(item, size - 30 - len(item.filename.encode('utf-8'))) for item, size in order]
            doubled.append(doubled[0])  # the first member listed twice, at its own offset
            self.rewritten_directory(path, doubled)
            problems, _ = checker.check(path)
            self.assertTrue(any('overlap' in problem or 'more than once' in problem for problem in problems), problems)

    def test_refuses_an_entry_that_does_not_point_at_a_member(self):
        """Only this rule looks at a folder entry's offset, since a folder is never read: pointed inside another
        member's bytes, it is a listing of something that is not a member of this file."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, _ = self.laid_out(directory)
            folder = zipfile.ZipInfo('shadow/')
            folder.header_offset = order[0][0].header_offset + 16  # inside the first member's own header
            folder.CRC = folder.compress_type = folder.file_size = folder.flag_bits = 0
            self.rewritten_directory(path, [(item, item.compress_size) for item, _ in order] + [(folder, 0)])
            with checker.Archive(path) as held:
                self.assertIn('does not point at a member', held.listing_gap() or '')

    def test_accepts_the_zip_shapes_ordinary_writers_make(self):
        """What the layout rules must never refuse: a wide (zip64) entry whose local extra the directory does not
        repeat, a member written to an unseekable stream so its sizes travel in a trailing descriptor, and folders."""
        with tempfile.TemporaryDirectory() as directory:
            wide = Path(directory) / 'wide.zip'
            with zipfile.ZipFile(wide, 'w', allowZip64=True) as held:
                # Written wide on purpose: the local header then carries a 20-byte zip64 extra that the directory
                # does not repeat, so a span measured from the directory's lengths falls 20 bytes short.
                with held.open(zipfile.ZipInfo('summary.json'), 'w', force_zip64=True) as member:
                    member.write(b'{}' * 64)
            folders = Path(directory) / 'folders.zip'
            with zipfile.ZipFile(folders, 'w') as held:
                held.writestr('results/', '')
                held.writestr('results/a.trx', '<TestRun />')
            class Unseekable(io.RawIOBase):  # a stream zipfile cannot rewind, so it writes a data descriptor
                def __init__(self, handle): self.handle = handle
                def writable(self): return True
                def seekable(self): return False
                def write(self, data): return self.handle.write(data)
            streamed = Path(directory) / 'streamed.zip'
            with open(streamed, 'wb') as raw:
                with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                    held.writestr('summary.json', '{}' * 64)
            wide_streamed = Path(directory) / 'wide-streamed.zip'
            with open(wide_streamed, 'wb') as raw:  # a descriptor is 24 bytes where the entry is written wide
                with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                    with held.open(zipfile.ZipInfo('summary.json'), 'w', force_zip64=True) as member:
                        member.write(b'{}' * 64)
            for path in (wide, folders, streamed, wide_streamed):
                with self.subTest(path.name):
                    with checker.Archive(path) as held:
                        self.assertIsNone(held.listing_gap())

    def test_refuses_a_tar_with_another_archive_before_it(self):
        """gzip reads joined members as one stream while tarfile stops at the first tar's end-of-archive blocks: an
        append is not read, but a sealed archive put after another one is passed over in favour of the first. The
        first here passes too, so its place in front is all that is refused."""
        with tempfile.TemporaryDirectory() as directory:
            sealed = archive(directory, 'edit-sealed-evidence.tar.gz', files(), unix=True)
            first = archive(directory, 'edit-first-evidence.tar.gz', files(log='[xUnit.net] another run\n'), unix=True)
            joined = Path(directory) / 'edit-joined-evidence.tar.gz'
            joined.write_bytes(first.read_bytes() + sealed.read_bytes())
            self.assertIn(sealed.read_bytes(), joined.read_bytes())  # the sealed archive is there, unread
            problems, _ = checker.check(joined)
            self.assertTrue(any("after this archive's own end" in problem for problem in problems), problems)

    def test_refuses_a_run_whose_pin_passed_only_in_a_unix_class_of_its_own(self):
        """The mirror of the Windows-side case: a class renamed on the Unix side left the family unsaid too, and the
        record went unchallenged - one nested in another as well, whose name the class's own leading dot keeps from
        being read as the Unix class (the fifty-third review)."""
        pin = checker.PINS[2]
        for renamed in ('FileCat.Core.Tests.UnixEditStorageTestsV2.', 'FileCat.Core.Tests.Outer+UnixEditStorageTests.'):
            with self.subTest(renamed=renamed):
                trx = PASSING_TRX.replace('FileCat.Tests.' + pin, renamed + pin)
                problems, _ = self.check(files(trx=trx, system='Linux host 6.0.0 x86_64 GNU/Linux'), unix=True)
                self.assertIn('its results hold no passing dual-root pin in either platform class '
                              '(WindowsEditStorageTests or UnixEditStorageTests), so nothing in the run itself says '
                              'what produced it', problems)

    def test_gives_no_verdict_to_an_archive_it_has_found_fault_with(self):
        """The line that turns findings into a refusal, and the one the release manifest matches: without it a lane
        that failed - a failed case, a suite that exited 1, a status of FAIL - is written an accepting verdict with
        its own digest in it, which is what a route would then be listed on. A passing archive goes first, as in a run
        of several: alone, the failed one ended main() with an error instead, which said nothing of this line."""
        contents = files(status='FAIL', summary={"ok": False, "failed": ["FileCat.Tests.ARealFailure"],
                                                 "exit_codes": {"S": 1},
                                                 "suites": {"S": {"total": 1, "passed": 0, "skipped": 0,
                                                                  "failed": 1}}})
        with tempfile.TemporaryDirectory() as directory:
            good = archive(directory, 'edit-good-evidence.zip', files())
            path = archive(directory, 'edit-failed-evidence.zip', contents)
            report = Path(directory) / 'report.txt'
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(good), str(path), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            said = output.getvalue()
            self.assertIn('edit-failed-evidence.zip: NOT edit-storage evidence', said)
            self.assertNotIn('edit-failed-evidence.zip: edit-storage evidence,', said)
            self.assertEqual(said, report.read_text(encoding='utf-8'))

    def test_reads_every_outcome_a_test_name_has_across_the_results(self):
        """A member added to a zip is a shape ordinary writers make, and the layout rules allow it. So a second
        results file naming a pin as passed must not replace the first one's Failed: every outcome a name has is
        read, and a pin that failed anywhere fails."""
        pin = checker.PINS[1]
        failed = PASSING_TRX.replace('testName="FileCat.Tests.%s" outcome="Passed"' % pin,
                                     'testName="FileCat.Tests.%s" outcome="Failed"' % pin)
        contents = files(trx=failed)
        contents['results/zz-added.trx'] = ('<?xml version="1.0"?><TestRun><Results>'
                                            '<UnitTestResult testName="FileCat.Tests.%s" outcome="Passed" />'
                                            '</Results></TestRun>' % pin)
        problems, _ = self.check(contents)
        self.assertTrue(any(('the pin %s did not pass' % pin) in problem for problem in problems), problems)

    def test_reads_the_system_from_the_first_line_and_not_from_all_of_them(self):
        """The record's own first line is what names the system; a later line that names another - `Linux-compat`
        under a Darwin kernel - must not decide the route this binds to."""
        contents = files(system='Darwin host 27.0.0 arm64')
        contents['machine.txt'] = ('Darwin host 27.0.0 Darwin Kernel Version 27.0.0 arm64\n'
                                   'Linux-compat layer: off\nenv=%s\n%s\n' % (checker.GATE, TEST_LINE))
        problems, facts = self.check(contents, unix=True)
        self.assertEqual([], problems)
        self.assertEqual('macos', facts['system'])

    def test_refuses_a_gzip_stream_that_stops_without_ending(self):
        """Bytes taken off the end of a sealed archive leave its members listable and its stream unfinished."""
        with tempfile.TemporaryDirectory() as directory:
            whole = archive(directory, 'edit-cut-evidence.tar.gz', files(), unix=True)
            blob = whole.read_bytes()
            for cut in (4, 16):
                with self.subTest(cut=cut):
                    whole.write_bytes(blob[:-cut])
                    problems, _ = checker.check(whole)
                    self.assertTrue(any('does not end' in problem for problem in problems), problems)

    def test_refuses_a_second_summary_that_names_no_suite(self):
        """Each summary has to name the suites it speaks for: one that names none, beside one that does, says
        nothing about whatever ran under it - and the counters of the other would answer for both."""
        contents = files()
        contents['zz-second/summary.json'] = json.dumps(dict(SUMMARY, suites={}))
        self.assertIn('zz-second/summary.json names no suite', self.check(contents)[0])

    def test_refuses_a_summary_whose_exit_codes_are_an_empty_map(self):
        """A summary carrying no exit code at all says nothing about how its suites ended, whether the key is
        missing or the map behind it is empty."""
        problems, _ = self.check(files(summary=dict(SUMMARY, exit_codes={})))
        self.assertIn('its summary carries no exit code', problems)

    def test_knows_the_reasons_and_the_pins_by_their_own_words(self):
        """Every fixture here is built from these words, written out in this file, so the check's own constants have
        to say the same: a reason dropped or a pin renamed there is a verdict changed here."""
        self.assertEqual(STEP_ASIDE_WORDS, checker.STEP_ASIDE)
        self.assertEqual(('FILECAT_EDIT_NATIVE_REQUIRED', '1'), (checker.GATE_NAME, checker.GATE_VALUE))
        self.assertEqual(PIN_NAMES, checker.PINS)

    def windows_record(self, env):
        """A machine record as the Windows runner seals it: its `-Env` string in `Env` (as given, where that is plain
        ASCII); archive() adds the launcher that passed it, as child.cmd."""
        contents = files()
        contents['machine.json'] = json.dumps({"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro", "Env": env})
        return contents

    def unix_record(self, *lines):
        """A machine record as the Unix runner seals it: uname first, then its own lines, one `env=` per entry."""
        contents = files(system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        if not any(line.startswith('test ') for line in lines):
            lines += (TEST_LINE,)
        head = unix_head('Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        contents['machine.txt'] = '\n'.join(head + list(lines)) + '\n'
        return contents

    def test_reads_the_gate_the_way_the_windows_runner_applies_it(self):
        """The Windows runner splits its `-Env` on `;` alone, sets each value untrimmed and applies a repeated name
        in order. So a trailing space is a value of `1 `, which every reader compares to `1` and finds off; a pair
        joined by a space is one variable holding the gate's text; and the gate given twice ends as its last value.
        This check once read all three as the gate in force while the tests ran without it."""
        gate = checker.GATE_NAME
        for env, said in ((gate + '=1 ', "sets %s to '1 ', not '1'" % gate),
                          ('FILECAT_ARCHIVE_TXF_NATIVE=1 ' + gate + '=1', 'does not name ' + gate),
                          (gate + '=1;FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=0', "gives %s 2 values ('1', '0')" % gate)):
            with self.subTest(env):
                problems, _ = self.check(self.windows_record(env))
                self.assertTrue(any(said in problem for problem in problems), problems)
        self.assertEqual([], self.check(self.windows_record('FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'))[0])

    def test_reads_the_gate_the_way_the_unix_runner_applies_it(self):
        """The Unix runner exports one entry per `env=` line, and a value may hold `;` - so `A=1;GATE=1` is one
        variable holding the gate's text. The gate given twice ends as its last value, and the gate's text inside
        another line (a filter, say) sets nothing. A filter holding a line break once wrote a line of its own there, an
        `env=` line the runner never exported: nothing in the record tells it from the runner's own, and the runner
        refuses such a filter now."""
        gate = checker.GATE_NAME
        for lines, said in ((('env=FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1',), 'does not name ' + gate),
                            (('env=' + gate + '=1', 'env=' + gate + '=0'), "gives %s 2 values ('1', '0')" % gate),
                            (('filter=' + gate + '=1',), 'does not name ' + gate)):
            with self.subTest(lines):
                problems, _ = self.check(self.unix_record(*lines), unix=True)
                self.assertTrue(any(said in problem for problem in problems), problems)
        self.assertEqual([], self.check(self.unix_record('packet=abc', 'env=' + gate + '=1'), unix=True)[0])

    def test_accepts_a_descriptor_written_without_its_signature(self):
        """A trailing descriptor's signature is optional, and a writer may leave it out: twelve bytes, or twenty where
        the entry is wide. Measured as if it were there, such a member is refused four bytes short."""
        class Unseekable(io.RawIOBase):  # zipfile cannot rewind it, so it writes a trailing descriptor
            def __init__(self, handle): self.handle = handle
            def writable(self): return True
            def seekable(self): return False
            def write(self, data): return self.handle.write(data)
        for wide in (False, True):
            with self.subTest(wide=wide), tempfile.TemporaryDirectory() as directory:
                raw = io.BytesIO()
                with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                    with held.open(zipfile.ZipInfo('summary.json'), 'w', force_zip64=wide) as member:
                        member.write(('{}' * 48).encode('ascii'))
                blob = raw.getvalue()
                at = blob.find(b'PK' + bytes((7, 8)))
                self.assertNotEqual(-1, at, 'this zipfile wrote no descriptor, so the case proves nothing')
                self.assertEqual(24 if wide else 16, blob.find(b'PK' + bytes((1, 2)), at) - at)
                blob = blob[:at] + blob[at + 4:]                 # the signature left out, the rest unmoved
                end = blob.rfind(b'PK' + bytes((5, 6)))
                start, = struct.unpack('<I', blob[end + 16:end + 20])
                blob = blob[:end + 16] + struct.pack('<I', start - 4) + blob[end + 20:]  # the directory, four bytes on
                path = Path(directory) / 'unsigned.zip'
                path.write_bytes(blob)
                with zipfile.ZipFile(path) as held:
                    self.assertIsNone(held.testzip())            # an archive any reader reads
                with checker.Archive(path) as held:
                    self.assertIsNone(held.listing_gap())

    def test_refuses_a_descriptor_that_states_sizes_its_directory_does_not(self):
        """A descriptor is where a streamed member's sizes are, so one stating other sizes than the directory's - in
        either width, with or without its signature - is a member the file does not account for."""
        with tempfile.TemporaryDirectory() as directory:
            class Unseekable(io.RawIOBase):
                def __init__(self, handle): self.handle = handle
                def writable(self): return True
                def seekable(self): return False
                def write(self, data): return self.handle.write(data)
            raw = io.BytesIO()
            with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                held.writestr('summary.json', '{}' * 48)
            blob = bytearray(raw.getvalue())
            at = blob.find(b'PK' + bytes((7, 8)))
            self.assertNotEqual(-1, at, 'this zipfile wrote no descriptor, so the case proves nothing')
            blob[at + 12:at + 16] = struct.pack('<I', 7)  # the plain size it states, now neither width's
            path = Path(directory) / 'misstated.zip'
            path.write_bytes(bytes(blob))
            with checker.Archive(path) as held:
                self.assertIn('does not state', held.listing_gap() or '')

    def test_accepts_an_empty_member_written_wide_to_a_stream(self):
        """An empty member states nought in both descriptor widths, so the narrow one was taken and the archive refused
        eight bytes short - for zipfile writing wide to a stream, and for libarchive's zip64 over an empty file."""
        with tempfile.TemporaryDirectory() as directory:
            class Unseekable(io.RawIOBase):
                def __init__(self, handle): self.handle = handle
                def writable(self): return True
                def seekable(self): return False
                def write(self, data): return self.handle.write(data)
            path = Path(directory) / 'empty-wide.zip'
            with open(path, 'wb') as raw:
                with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                    with held.open(zipfile.ZipInfo('empty.txt'), 'w', force_zip64=True):
                        pass
                    with held.open(zipfile.ZipInfo('summary.json'), 'w', force_zip64=True) as member:
                        member.write(b'{}' * 48)
            with checker.Archive(path) as held:
                self.assertIsNone(held.listing_gap())
            # The empty member last, its descriptor followed by the directory itself, which decides its width there
            # (the fifty-sixth review dropped a directory header read wherever one stands).
            path = Path(directory) / 'empty-wide-last.zip'
            with open(path, 'wb') as raw:
                with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                    with held.open(zipfile.ZipInfo('summary.json'), 'w', force_zip64=True) as member:
                        member.write(b'{}' * 48)
                    with held.open(zipfile.ZipInfo('empty.txt'), 'w', force_zip64=True):
                        pass
            with checker.Archive(path) as held:
                self.assertIsNone(held.listing_gap())
            # bsdtar's half: where it is not found the case says so as a skip, which measure_rules.py stops on, rather
            # than passing with half of it unrun (the fifty-third review).
            found = self.tar_called('bsdtar')
            if found is None:
                self.skipTest('no bsdtar here: the half of this case it writes cannot run')
            tree = Path(directory) / 'run'
            tree.mkdir()
            (tree / 'empty.txt').write_bytes(b'')
            (tree / 'a.txt').write_text('x', encoding='utf-8')
            done = subprocess.run([found, '--format', 'zip', '--options', 'zip:zip64', '-cf', 'wide.zip', 'run'],
                                  cwd=directory, capture_output=True)
            self.assertEqual(0, done.returncode, done.stderr)
            with checker.Archive(Path(directory) / 'wide.zip') as held:
                self.assertIsNone(held.listing_gap())

    def test_names_an_archive_cut_short_and_still_reports_the_rest(self):
        """A tar cut off inside a member raises from the reader itself, which once ended the whole run with a traceback:
        no verdict for the archives after it and no report written. It is one refusal among others."""
        with tempfile.TemporaryDirectory() as directory:
            whole = archive(directory, 'edit-cut-evidence.tar.gz', files(log='x' * 20000), unix=True)
            blob = gzip.decompress(whole.read_bytes())
            whole.write_bytes(gzip.compress(blob[:len(blob) // 3]))   # cut inside a member, re-wrapped whole
            good = archive(directory, 'edit-good-evidence.zip', files())
            report = Path(directory) / 'report.txt'
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(whole), str(good), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            said = output.getvalue()
            self.assertIn('edit-cut-evidence.tar.gz: NOT edit-storage evidence', said)
            self.assertIn('edit-good-evidence.zip: edit-storage evidence,', said)
            self.assertEqual(said, report.read_text(encoding='utf-8'))

    def test_reads_a_windows_gate_name_without_case_as_the_runner_sets_it(self):
        """The Windows runner's pattern ignored case and the environment matches names without it, so a lowercase gate
        after the real one is the value the tests saw - and a lowercase gate alone is the gate."""
        gate = checker.GATE_NAME
        problems, _ = self.check(self.windows_record(gate + '=1;FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate.lower() + '=0'))
        self.assertTrue(any("gives %s 2 values ('1', '0')" % gate in problem for problem in problems), problems)
        self.assertEqual([], self.check(self.windows_record('FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate.lower() + '=1'))[0])

    def test_reads_the_windows_env_only_as_child_cmd_passed_it(self):
        """The recorded Env reaches the child through child.cmd, which cmd.exe reads again - expanding %...%, ending at
        a line break - and machine.json was in the ANSI code page with best-fit (r35's), so a fullwidth semicolon in
        a value read there as a real one while the child was given one variable. The record is trusted only as plain
        printable ASCII without %, ! or ^, and only where the sealed child.cmd - the runner's own launcher, read whole -
        passes exactly it."""
        gate = checker.GATE_NAME
        for env, said in (('FILECAT_ARCHIVE_TXF_NATIVE=100%;' + gate + '=1', "holds '%'"),
                          ('FILECAT_ARCHIVE_TXF_NATIVE=1\r\nX;' + gate + '=1', "holds '\\r'")):
            with self.subTest(env):
                problems, _ = self.check(self.windows_record(env))
                self.assertTrue(any(said in problem for problem in problems), problems)
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'
        # Less than the record's Env, or more: a gate set again after the record's ends is the child's last word on it
        # (the fifty-fourth review found the longer side held by no case).
        longer = recorded + ';' + gate + '=0'
        for script, said in ((launcher(recorded.replace(';', chr(0xFF1B))), 'is not the launcher its runner writes'),
                             (launcher('FILECAT_ARCHIVE_TXF_NATIVE=1'),
                              "its child.cmd passed 'FILECAT_ARCHIVE_TXF_NATIVE=1' where its machine record holds Env"),
                             (launcher(longer), 'its child.cmd passed %r where its machine record holds Env' % longer),
                             (None, 'it carries 0 child.cmd where its runner seals one')):
            with self.subTest(script):
                contents = self.windows_record(recorded)
                contents['child.cmd'] = script
                problems, _ = self.check(contents)
                self.assertTrue(any(said in problem for problem in problems), problems)

    def test_reads_child_cmd_whole_as_the_launcher_its_runner_writes(self):
        """The forty-second review re-cut child.cmd through its other arguments: a `"` in the filter let an `-e` of its
        own bind the child's Env (cmd.exe and PowerShell split the line differently), while the real `-Env` text still
        stood in the file for a pattern to find. So child.cmd is read whole, as the one launcher its runner writes:
        every argument printable ASCII without `"`, `%` or `^`, none starting with `-` (a parameter name to PowerShell)
        or ending in a backslash (which turns the closing quote into a literal one)."""
        gate = checker.GATE_NAME
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'
        smuggled = '*" -Projects "x" -Dotnet "y" -e "FILECAT_ARCHIVE_TXF_NATIVE=1" -HangMinutes 20 '
        for changed, said in (({'filter': smuggled}, 'is not the launcher its runner writes'),
                              ({'filter': 'FullyQualifiedName~100%'}, 'is not the launcher its runner writes'),
                              ({'projects': 'tests\\A.csproj^'}, 'is not the launcher its runner writes'),
                              ({'filter': '-e:FILECAT_ARCHIVE_TXF_NATIVE=1'}, "passes '-e:FILECAT_ARCHIVE_TXF_NATIVE=1'"),
                              ({'dotnet': 'C:\\FileCatValidation\\dotnet\\'},
                               "passes 'C:\\\\FileCatValidation\\\\dotnet\\\\'")):
            with self.subTest(changed):
                contents = self.windows_record(recorded)
                contents['child.cmd'] = launcher(recorded, **changed)
                problems, _ = self.check(contents)
                self.assertTrue(any(said in problem for problem in problems), problems)
        contents = self.windows_record(recorded)
        contents['child.cmd'] = launcher(recorded) + 'set ' + gate + '=0\r\n'
        problems, _ = self.check(contents)
        self.assertTrue(any('is not the launcher its runner writes' in problem for problem in problems), problems)
        contents = self.windows_record(recorded)
        contents['extra/child.cmd'] = launcher(recorded)
        problems, _ = self.check(contents)
        self.assertTrue(any('it carries 2 child.cmd where its runner seals one' in problem for problem in problems),
                        problems)
        # What real lanes pass: a negated shard (`#~` now), a path with a space, `{run}` in a value, an empty Env.
        env = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1;FILECAT_ARCHIVE_APP_ASSETS={run}\\publish\\FileCat.App'
        contents = self.windows_record(env)
        contents['child.cmd'] = launcher(env, projects='tests\\B.csproj#~ArchiveAppContainerTests,tests\\C D\\C.csproj')
        self.assertEqual([], self.check(contents)[0])
        problems, _ = self.check(self.windows_record(''))
        self.assertEqual(['its machine record does not name ' + gate], problems)

    def test_refuses_a_child_cmd_holding_an_exclamation_mark(self):
        """Where delayed expansion is on, cmd.exe reads `!NAME:` up to the next colon as an undefined variable and
        deletes it, no closing `!` needed: `-Dotnet "C:\\FileCatValidation\\dotnet\\!x\\..\\dotnet.exe"` and an Env
        ending `=:dotnet.exe` lost the `-Env` between them while the Dotnet path still resolved, and the child ran with
        no gate (the forty-third review, replayed here under `cmd /V:ON`). Whether delayed expansion is on is a setting
        no record holds, so a child.cmd holding `!` anywhere is refused - a negated shard is written `#~` now."""
        gate = checker.GATE_NAME
        env = gate + '=1;FILECAT_ARCHIVE_TXF_NATIVE=:dotnet.exe'
        for env, changed in ((env, {'dotnet': 'C:\\FileCatValidation\\dotnet\\!x\\..\\dotnet.exe'}),
                             ('FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1',
                              {'projects': 'tests\\B.csproj#!ArchiveAppContainerTests'})):
            with self.subTest(changed):
                contents = self.windows_record(env)
                contents['child.cmd'] = launcher(env, **changed)
                problems, _ = self.check(contents)
                self.assertTrue(any("its child.cmd holds '!'" in problem for problem in problems), problems)

    def test_reads_a_launcher_argument_only_as_printable_ascii(self):
        """cmd.exe reads child.cmd in the OEM code page, so a character outside printable ASCII does not reach the child
        as written; the runner's patterns once ignored case, which let U+0130 and U+212A by. Any argument, not only the
        Env, is refused for one."""
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + checker.GATE_NAME + '=1'
        for changed in ({'filter': 'FullyQualifiedName~Caf' + chr(0xE9)},
                        {'projects': 'tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#' + chr(0x212A) + 'elvin'}):
            with self.subTest(changed):
                contents = self.windows_record(recorded)
                contents['child.cmd'] = launcher(recorded, **changed)
                problems, _ = self.check(contents)
                self.assertTrue(any('is not the launcher its runner writes' in problem for problem in problems),
                                problems)

    def test_refuses_a_name_outside_filecat_which_the_test_platform_may_read(self):
        """MSBuild reads the environment as properties, and VSTest reads the VSTestCLIRunSettings property as settings:
        beside the gate, `VSTESTCLIRUNSETTINGS=RunConfiguration.EnvironmentVariables.FILECAT_EDIT_NATIVE_REQUIRED=0`
        set the gate to 0 inside the test host (the forty-second review, through the child's own Env loop and
        `dotnet test` arguments). A name outside FILECAT_ is refused - a Windows one matched without case."""
        gate = checker.GATE_NAME
        settings = 'VSTESTCLIRUNSETTINGS=RunConfiguration.EnvironmentVariables.' + gate + '=0'
        said = "sets 'VSTESTCLIRUNSETTINGS', outside FILECAT_"
        problems, _ = self.check(self.windows_record(gate + '=1;' + settings))
        self.assertTrue(any(said in problem for problem in problems), problems)
        problems, _ = self.check(self.unix_record('env=' + gate + '=1', 'env=' + settings), unix=True)
        self.assertTrue(any(said in problem for problem in problems), problems)
        self.assertEqual([], self.check(self.windows_record('filecat_archive_txf_native=1;' + gate + '=1'))[0])

    def test_refuses_a_windows_env_that_delayed_expansion_would_change(self):
        """Where delayed expansion is on, cmd.exe replaces `!...!` and removes `^` in child.cmd: `A=!x;GATE=1;B=!`
        reached the child as `A=`, with no gate (the forty-second review, under cmd /V:ON)."""
        gate = checker.GATE_NAME
        for env, said in (('FILECAT_ARCHIVE_TXF_NATIVE=!x;' + gate + '=1;FILECAT_ARCHIVE_CONTAINMENT=!', "holds '!'"),
                          ('FILECAT_ARCHIVE_TXF_NATIVE=1^;' + gate + '=1', "holds '^'")):
            with self.subTest(env):
                problems, _ = self.check(self.windows_record(env))
                self.assertTrue(any(said in problem for problem in problems), problems)

    def test_reads_a_unix_record_by_its_own_line_ends(self):
        """bash's echo ends a line at a newline alone; Python's splitlines() also ends one at 0x1c or 0x85, which the
        runner keeps inside one value - so a line could be split where the tests saw one variable."""
        gate = checker.GATE_NAME
        for lines, said in ((('env=' + gate + '=1' + chr(0x1C),), "sets %s to '1\\x1c'" % gate),
                            (('env=FILECAT_ARCHIVE_TXF_NATIVE=1' + chr(0x1C) + 'env=' + gate + '=1',), 'does not name ' + gate),
                            (('env=' + gate + '=1' + chr(0x85),), "sets %s to '1\\x85'" % gate)):
            with self.subTest(lines):
                problems, _ = self.check(self.unix_record(*lines), unix=True)
                self.assertTrue(any(said in problem for problem in problems), problems)

    def test_reads_a_gate_value_after_its_first_equals_sign_alone(self):
        """Both runners split an entry at its first `=` and apply the rest, so `GATE=0=1` gives the tests `0=1` - and
        `GATE=1;GATE=1=0` gives them `1=0`, last. Split at the last `=` instead, that second entry named no gate at all
        and the first read as the gate in force (the forty-fourth review)."""
        gate = 'FILECAT_EDIT_NATIVE_REQUIRED'
        problems, _ = self.check(self.windows_record(gate + '=0=1'))
        self.assertTrue(any("sets %s to '0=1'" % gate in problem for problem in problems), problems)
        problems, _ = self.check(self.unix_record('env=' + gate + '=0=1'), unix=True)
        self.assertTrue(any("sets %s to '0=1'" % gate in problem for problem in problems), problems)
        said = "its machine record gives %s 2 values ('1', '1=0'), and its runner applies only the last" % gate
        self.assertEqual([said], self.check(self.windows_record(gate + '=1;' + gate + '=1=0'))[0])
        self.assertEqual([said], self.check(self.unix_record('env=' + gate + '=1', 'env=' + gate + '=1=0'),
                                            unix=True)[0])

    def test_reads_a_status_whole_and_not_as_a_word_in_it(self):
        """Both runners append to the status after a run they had to tidy up after - the Windows rerun of this round
        sealed `PASS` and then `leftover: 2252` - and such a status is not a plain PASS."""
        problems, _ = self.check(files(status='PASS\r\nleftover: 2252\r\n'))
        self.assertIn('its status is not PASS', problems)

    def test_refuses_a_machine_record_whose_env_is_not_a_string(self):
        """The runner passes a string on; a list in its place would be read through its printed form."""
        contents = files()
        contents['machine.json'] = json.dumps({"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro",
                                               "Env": [checker.GATE]})
        problems, _ = self.check(contents)
        self.assertTrue(any('gives Env as a list' in problem for problem in problems), problems)

    def test_accepts_a_wide_descriptor_whose_size_field_spells_a_header(self):
        """A member of exactly 0x02014b50 bytes, written wide to a stream: the descriptor's plain size, read where a
        narrow descriptor would end, spells a directory header - so what follows cannot tell the widths apart there,
        and only both size fields together do."""
        class Unseekable(io.RawIOBase):
            def __init__(self, handle): self.handle = handle
            def writable(self): return True
            def seekable(self): return False
            def write(self, data): return self.handle.write(data)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'spelled.zip'
            with open(path, 'wb') as raw:
                with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_DEFLATED) as held:
                    with held.open(zipfile.ZipInfo('zeros.bin'), 'w', force_zip64=True) as member:
                        remaining = 0x02014B50
                        while remaining:
                            block = min(remaining, 1 << 22)
                            member.write(bytes(block))
                            remaining -= block
            with checker.Archive(path) as held:
                self.assertIsNone(held.listing_gap())

    def test_accepts_a_directory_listed_out_of_file_order(self):
        """A directory need not list its members in the order they lie in; the layout is read in file order."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, start = self.laid_out(directory)
            self.rewritten_directory(path, [(item, item.compress_size) for item, _ in reversed(order)], at=start)
            with checker.Archive(path) as held:
                self.assertIsNone(held.listing_gap())

    def test_refuses_a_launcher_line_longer_than_cmd_reads_as_written(self):
        """cmd.exe reads about 8191 characters of what follows a program's name as written; past that an `&` inside
        quotes can end the command and run the rest of the line as another. The forty-fourth review padded an Env so its
        `&` fell there: the gate after it never reached the child, and the check read it as in force. A line of 8191
        characters still stands, and one character more does not."""
        gate = 'FILECAT_EDIT_NATIVE_REQUIRED'
        head, tail = 'FILECAT_ARCHIVE_TXF_NATIVE=1;FILECAT_ARCHIVE_PAD=', '&;' + gate + '=1'
        base = len(launcher(head + tail).split('\r\n')[1])
        for length in (8191, 8192, 8191 + 60):
            with self.subTest(length):
                env = head + 'x' * (length - base) + tail
                self.assertEqual(length, len(launcher(env).split('\r\n')[1]))
                problems, _ = self.check(self.windows_record(env))
                self.assertEqual([] if length == 8191 else
                                 ["its child.cmd's launcher line is %d characters long, past the 8191 cmd.exe reads as "
                                  'written, so the record cannot say what the tests were given' % length], problems)

    def test_refuses_an_entry_whose_member_header_does_not_begin_as_one(self):
        """An entry has to point at a local file header. The note that called this redundant - an entry pointing
        elsewhere being refused for the name it does not carry - was false: with the signature changed and the name
        left, a member no rule reads is refused by this rule alone (the forty-fourth review)."""
        with tempfile.TemporaryDirectory() as directory:
            contents = with_child_cmd(files())
            contents['zz-unread.bin'] = 'bytes no rule reads ' * 8
            path, order, _ = self.laid_out(directory, contents)
            unread = next(item for item, _ in order if item.filename == 'zz-unread.bin')
            data = bytearray(path.read_bytes())
            data[unread.header_offset:unread.header_offset + 4] = b'PK\x03\x05'
            path.write_bytes(bytes(data))
            problems, _ = checker.check(path)
            self.assertEqual(1, len(problems), problems)
            self.assertIn("the entry 'zz-unread.bin' does not point at a member of this file", problems[0])

    def test_accepts_a_member_whose_name_is_not_ascii(self):
        """zipfile writes a name outside ASCII in UTF-8 and says so in the header's flag, as an ordinary writer may: the
        name is read as its flag says, or the member is not the one its entry names (the forty-fourth review)."""
        with tempfile.TemporaryDirectory() as directory:
            contents = with_child_cmd(files())
            contents['zz-caf' + chr(0xE9) + '.bin'] = 'bytes no rule reads'
            path = archive(directory, 'edit-named-evidence.zip', contents)
            self.assertEqual([], checker.check(path)[0])

    def test_reports_every_archive_when_one_states_counters_that_are_not_numbers(self):
        """main() once turned a suite's counters into numbers outside its guard, so a summary saying "seven" passed the
        check and then ended the run with a traceback: no verdict for the archives after it and no report."""
        counters = dict(SUMMARY['suites']['FileCat.App.Tests-Edit'], passed='seven')
        summary = dict(SUMMARY, suites={'FileCat.App.Tests-Edit': counters})
        with tempfile.TemporaryDirectory() as directory:
            odd = archive(directory, 'edit-odd-evidence.zip', files(summary=summary))
            good = archive(directory, 'edit-good-evidence.zip', files())
            report = Path(directory) / 'report.txt'
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(odd), str(good), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            lines = report.read_text(encoding='utf-8').splitlines()
            self.assertIn('edit-odd-evidence.zip: NOT edit-storage evidence', lines)
            self.assertTrue(any(line.startswith('edit-good-evidence.zip: edit-storage evidence,') for line in lines),
                            lines)

    def test_refuses_a_tar_whose_last_member_claims_more_than_was_compressed(self):
        """A last member cut short - its header kept, some of its bytes, no end-of-archive blocks - is refused: tarfile
        raises listing it, since it seeks past that member to read what follows, and an archive that cannot be read is
        a refusal. For a regular member the floor that says so in its own words lies beneath that; a folder or a
        link claiming data is listed by tarfile, and the floor alone refuses it (the next case)."""
        contents = with_child_cmd(files())
        with tempfile.TemporaryDirectory() as directory:
            for cut in (0, 100, 511):
                with self.subTest(cut=cut):
                    raw = io.BytesIO()
                    with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as held:
                        for leaf, text in contents.items():
                            blob = text.encode('utf-8')
                            item = tarfile.TarInfo('run/' + leaf)
                            item.size = len(blob)
                            held.addfile(item, io.BytesIO(blob))
                        item = tarfile.TarInfo('run/zz-unread.bin')
                        item.size = 4096
                        held.addfile(item, io.BytesIO(bytes(4096)))
                    data = raw.getvalue()
                    path = Path(directory) / ('edit-cut-%d-evidence.tar.gz' % cut)
                    path.write_bytes(gzip.compress(data[:data.rfind(b'run/zz-unread.bin') + 512 + cut]))
                    report = Path(directory) / 'report.txt'
                    with contextlib.redirect_stdout(io.StringIO()):
                        with unittest.mock.patch.object(sys, 'argv', ['check', str(path), '--out', str(report)]):
                            self.assertEqual(1, checker.main())
                    self.assertIn(path.name + ': NOT edit-storage evidence',
                                  report.read_text(encoding='utf-8').splitlines())

    def test_refuses_a_tar_whose_last_entry_claims_data_tarfile_does_not_skip(self):
        """tarfile skips a member's data only for a regular member: a folder or a link whose size field claims data is
        listed without it, and the bytes it claims are not there. The floor refuses such a tar, and alone (the
        forty-fifth review)."""
        contents = with_child_cmd(files())
        for kind in (tarfile.DIRTYPE, tarfile.SYMTYPE):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as directory:
                raw = io.BytesIO()
                with tarfile.open(fileobj=raw, mode='w', format=tarfile.GNU_FORMAT) as held:
                    for leaf, text in contents.items():
                        blob = text.encode('utf-8')
                        item = tarfile.TarInfo('run/' + leaf)
                        item.size = len(blob)
                        held.addfile(item, io.BytesIO(blob))
                    entry = tarfile.TarInfo('run/zz-entry')
                    entry.type = kind
                    entry.linkname = 'machine.json' if kind == tarfile.SYMTYPE else ''
                    entry.size = 100000
                    held.addfile(entry)
                path = Path(directory) / 'edit-claiming-evidence.tar.gz'
                path.write_bytes(gzip.compress(raw.getvalue()))
                problems, _ = checker.check(path)
                self.assertEqual(1, len(problems), problems)
                self.assertIn('past what was compressed', problems[0])

    def test_accepts_an_unsigned_descriptor_whose_crc_spells_the_signature(self):
        """A descriptor may leave its signature out, and then its CRC comes first - and a CRC of 0x08074b50 spells the
        signature. Read as signed alone, no shape fitted and a member every reader reads was refused (the forty-fifth
        review): every shape is tried now, and what follows decides between them."""
        body = b'a member whose CRC-32 spells the descriptor signature' + bytes((0xD8, 0xB9, 0x81, 0x3D))
        self.assertEqual(0x08074B50, zlib.crc32(body))

        class Unseekable(io.RawIOBase):
            def __init__(self, handle): self.handle = handle
            def writable(self): return True
            def seekable(self): return False
            def write(self, data): return self.handle.write(data)
        for method in (zipfile.ZIP_DEFLATED, zipfile.ZIP_STORED):
            with self.subTest(method=method), tempfile.TemporaryDirectory() as directory:
                raw = io.BytesIO()
                with zipfile.ZipFile(Unseekable(raw), 'w', method) as held:
                    held.writestr('zz-crc.bin', body)
                blob = raw.getvalue()
                signature = b'PK' + bytes((7, 8))
                at = blob.find(signature)
                self.assertEqual(signature, blob[at + 4:at + 8])  # the CRC after it spells the signature again
                blob = blob[:at] + blob[at + 4:]  # the descriptor's own signature left out, as a writer may
                end = blob.rfind(b'PK' + bytes((5, 6)))
                start, = struct.unpack('<I', blob[end + 16:end + 20])
                blob = blob[:end + 16] + struct.pack('<I', start - 4) + blob[end + 20:]
                path = Path(directory) / 'crc.zip'
                path.write_bytes(blob)
                with zipfile.ZipFile(path) as held:
                    self.assertIsNone(held.testzip())
                with checker.Archive(path) as held:
                    self.assertIsNone(held.listing_gap())

    def test_refuses_a_record_entry_with_no_value(self):
        """An entry with no `=` is one the Windows child throws on applying and the Unix runner never writes, so a record
        holding one cannot say what the tests were given (the forty-fifth review)."""
        gate = 'FILECAT_EDIT_NATIVE_REQUIRED'
        said = "its machine record gives 'FILECAT_ARCHIVE_TXF_NATIVE' with no value, which its runner cannot apply"
        problems, _ = self.check(self.windows_record(gate + '=1;FILECAT_ARCHIVE_TXF_NATIVE'))
        self.assertTrue(any(said in problem for problem in problems), problems)
        problems, _ = self.check(self.unix_record('env=' + gate + '=1', 'env=FILECAT_ARCHIVE_TXF_NATIVE'), unix=True)
        self.assertTrue(any(said in problem for problem in problems), problems)

    def test_refuses_a_zip64_member_its_directory_stretches(self):
        """A zip64 member's local header defers its sizes to its zip64 extra field, which has to state what the
        directory does: taken unchecked, a directory that dropped a log saying a test stepped aside and stretched the
        zip64 member over its bytes was accepted (the forty-sixth review). Without that log, the zip64 member stands."""
        reason = STEP_ASIDE_WORDS[0]
        with tempfile.TemporaryDirectory() as directory:
            for logged in (False, True):
                path = Path(directory) / ('edit-z64-%s-evidence.zip' % logged)
                with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as held:
                    for name, text in with_child_cmd(files()).items():
                        held.writestr(name.replace('/', '\\'), text)
                    with held.open(zipfile.ZipInfo('zz-wide.bin'), 'w', force_zip64=True) as member:
                        member.write(b'w' * 300)
                    if logged:
                        held.writestr('test-zz-Core.Tests-Edit.log', '[xUnit.net] ' + reason + '\n')
                problems, _ = checker.check(path)
                self.assertEqual([], problems) if not logged else self.assertTrue(
                    any(reason in problem for problem in problems), problems)
            with zipfile.ZipFile(path) as held:
                start = held.start_dir
                order = sorted(held.infolist(), key=lambda item: item.header_offset)
            wide = next(item for item in order if item.filename == 'zz-wide.bin')
            log = next(item for item in order if item.filename.startswith('test-zz'))
            self.assertIs(log, order[-1])
            kept = [(item, item.compress_size + (start - log.header_offset if item is wide else 0))
                    for item in order if item is not log]
            self.rewritten_directory(path, kept, at=start)
            problems, _ = checker.check(path)
            self.assertEqual(1, len(problems), problems)
            self.assertIn("the entry 'zz-wide.bin' is", problems[0])

    def test_refuses_a_filter_dotnet_does_not_pass_on_as_written(self):
        """dotnet hands --filter to MSBuild as VSTestTestCaseFilter="...", so a `"` ended that property and what
        followed a `;` set others - VSTestCLIRunSettings among them, which set the gate to 0 inside the test host while
        the record named it as 1 (the forty-sixth review, through SDK 10.0.101). An `@` reads as a response file where
        the filter begins and as an item list inside it, and a leading `-` as an option of dotnet's."""
        gate = 'FILECAT_EDIT_NATIVE_REQUIRED'
        injected = ('(FullyQualifiedName~Edit)";VSTestCLIRunSettings=RunConfiguration.EnvironmentVariables.' + gate +
                    '=0;Z="1')
        for value, said in ((injected, "holding '\"'"), ('@lane.rsp', "holding '@'"), ('X@(None)Y', "holding '@'"),
                            ('-p:Z=1', 'beginning with -')):
            with self.subTest(value):
                problems, _ = self.check(self.unix_record('filter=' + value, 'env=' + gate + '=1'), unix=True)
                self.assertTrue(any(said in problem for problem in problems), problems)
        self.assertEqual([], self.check(self.unix_record('filter=FullyQualifiedName~Edit|FullyQualifiedName~Native',
                                                         'env=' + gate + '=1'), unix=True)[0])
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'
        contents = self.windows_record(recorded)
        contents['child.cmd'] = launcher(recorded, filter='@lane.rsp')
        problems, _ = self.check(contents)
        self.assertTrue(any("a filter or a project entry holding '@'" in problem for problem in problems), problems)

    def test_reads_a_windows_console_log_in_the_encoding_its_runner_writes(self):
        """The Windows runner's console logs are UTF-16 with a mark (Windows PowerShell 5.1's `*>`), and read as UTF-8
        they could neither hold a reason nor be empty: the log half of the step-aside scan and the empty-output rule were
        inert on every Windows archive (the forty-seventh review). Read by their mark, both work."""
        for log, said in (('[xUnit.net] SKIP ' + STEP_ASIDE_WORDS[0] + ' here\r\n', 'stepped aside'),
                          ('', 'its test output is empty'), ('\r\n\r\n', 'its test output is empty')):
            with self.subTest(log=log):
                problems, _ = self.check(files(log=log))
                self.assertTrue(any(said in problem for problem in problems), problems)
        self.assertEqual([], self.check(files(log='[xUnit.net] ' + 'everything ran ' + chr(0x2713) + '\r\n'))[0])

    def test_refuses_a_status_that_is_not_pass_once_its_line_end_is_read(self):
        """Both runners end the status with a line end - a newline, or a carriage return and a newline - and that is
        still PASS; anything else on the line is not."""
        for status in ('PASS\r\n', 'PASS\n', 'PASS'):
            with self.subTest(status=status):
                self.assertEqual([], self.check(files(status=status))[0])
        self.assertIn('its status is not PASS', self.check(files(status='PASSED\r\n'))[0])

    def test_refuses_a_project_name_dotnet_does_not_pass_on_as_written(self):
        """A project entry's name reaches MSBuild in the logger's property, so it holds no `"`, `@` or `%` either - on a
        Unix test line, or in a Windows launcher's projects (the forty-seventh review)."""
        gate = 'FILECAT_EDIT_NATIVE_REQUIRED'
        for line, said in (('test FileCat.A@B: *', "holding '@'"), ('test FileCat.A%25B: *', "holding '%'"),
                           ('test FileCat.A"B: *', "holding '\"'"), ('filter=FullyQualifiedName~A%22B', "holding '%'")):
            with self.subTest(line=line):
                problems, _ = self.check(self.unix_record('filter=*', 'env=' + gate + '=1', line), unix=True)
                self.assertTrue(any(said in problem for problem in problems), problems)
        self.assertEqual([], self.check(self.unix_record('filter=*', 'env=' + gate + '=1', 'test FileCat.A-B: *'),
                                        unix=True)[0])
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'
        contents = self.windows_record(recorded)
        contents['child.cmd'] = launcher(recorded, projects='tests\\A@B.csproj')
        problems, _ = self.check(contents)
        self.assertTrue(any("a filter or a project entry holding '@'" in problem for problem in problems), problems)

    def test_refuses_a_zip64_header_that_carries_no_zip64_field(self):
        """A local header stating 0xFFFFFFFF for its size and carrying no zip64 extra field states no size at all, and
        is refused rather than sized from its directory."""
        with tempfile.TemporaryDirectory() as directory:
            contents = with_child_cmd(files())
            contents['zz-unread.bin'] = 'bytes no rule reads ' * 8
            path, order, _ = self.laid_out(directory, contents)
            unread = next(item for item, _ in order if item.filename == 'zz-unread.bin')
            data = bytearray(path.read_bytes())
            data[unread.header_offset + 18:unread.header_offset + 22] = bytes((0xFF, 0xFF, 0xFF, 0xFF))
            path.write_bytes(bytes(data))
            problems, _ = checker.check(path)
            self.assertEqual(1, len(problems), problems)
            self.assertIn("the entry 'zz-unread.bin' is", problems[0])
            self.assertIn('where the file says nothing', problems[0])

    def test_accepts_a_zip64_member_whose_header_carries_another_extra_field_first(self):
        """zipfile writes a member's own extra field first and the zip64 field after it, so the zip64 field is found by
        walking the fields, not by reading the first (the forty-seventh review)."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-z64-extra-evidence.zip'
            with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as held:
                for name, text in with_child_cmd(files()).items():
                    held.writestr(name.replace('/', '\\'), sealed(name, text, False))
                stamped = zipfile.ZipInfo('zz-stamped.bin')
                stamped.extra = struct.pack('<HHBI', 0x5455, 5, 1, 0)  # an extended timestamp, before the zip64 field
                with held.open(stamped, 'w', force_zip64=True) as member:
                    member.write(b's' * 300)
            with zipfile.ZipFile(path) as held:
                self.assertIsNone(held.testzip())
            self.assertEqual([], checker.check(path)[0])

    def test_refuses_a_descriptor_that_states_another_compressed_size(self):
        """A descriptor states both sizes, and its compressed size has to be the directory's as well as its plain one:
        a descriptor stating the plain size right and the compressed size wrong fits no width."""
        class Unseekable(io.RawIOBase):  # zipfile cannot rewind it, so it writes a trailing descriptor
            def __init__(self, handle): self.handle = handle
            def writable(self): return True
            def seekable(self): return False
            def write(self, data): return self.handle.write(data)
        with tempfile.TemporaryDirectory() as directory:
            raw = io.BytesIO()
            with zipfile.ZipFile(Unseekable(raw), 'w', zipfile.ZIP_STORED) as held:
                held.writestr('zz-unread.bin', 'bytes no rule reads ' * 8)
            blob = bytearray(raw.getvalue())
            at = blob.find(b'PK' + bytes((7, 8)))
            self.assertNotEqual(-1, at, 'this zipfile wrote no descriptor, so the case proves nothing')
            compressed, = struct.unpack('<I', blob[at + 8:at + 12])
            blob[at + 8:at + 12] = struct.pack('<I', compressed + 1)
            path = Path(directory) / 'stated.zip'
            path.write_bytes(bytes(blob))
            with checker.Archive(path) as held:
                self.assertIn("the descriptor after 'zz-unread.bin' does not state", held.listing_gap() or '')

    def test_reads_a_machine_record_s_system_from_its_first_line(self):
        """No runner writes a blank line before the record's first: a record that begins with one does not say which
        system ran it."""
        contents = self.unix_record('env=FILECAT_EDIT_NATIVE_REQUIRED=1')
        contents['machine.txt'] = '\n' + contents['machine.txt']
        problems, _ = self.check(contents, unix=True)
        self.assertIn('its machine record does not say which system ran it, so nothing in it binds it to a route',
                      problems)

    def test_names_an_archive_whose_compression_it_cannot_read(self):
        """A method this reader does not know is one more refusal, and the run's other archives are still reported:
        it ended the whole run with a traceback and wrote no report at all."""
        with tempfile.TemporaryDirectory() as directory:
            unreadable = Path(directory) / 'edit-method-evidence.zip'
            with zipfile.ZipFile(unreadable, 'w', zipfile.ZIP_DEFLATED) as held:
                held.writestr('summary.json', '{}' * 64)
            blob = bytearray(unreadable.read_bytes())
            for at in (8, blob.rfind(b'PK\x01\x02') + 10):  # the method, in the local header and the directory
                blob[at:at + 2] = struct.pack('<H', 9)       # Deflate64, which Python does not implement
            unreadable.write_bytes(bytes(blob))
            good = archive(directory, 'edit-good-evidence.zip', files())
            report = Path(directory) / 'report.txt'
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(unreadable), str(good), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            self.assertIn('edit-method-evidence.zip: NOT edit-storage evidence', output.getvalue())
            self.assertIn('edit-good-evidence.zip: edit-storage evidence', output.getvalue())
            self.assertEqual(output.getvalue(), report.read_text(encoding='utf-8'))

    def test_refuses_a_zip_appended_to_another_zip(self):
        """zipfile reads a zip from its end, so a sealed zip with another appended is read as the appended one alone:
        a lane whose status said FAIL, whose summary held a failure and whose log held a step-aside reason was
        accepted, with every sealed byte of it still in the file."""
        failed = files(summary={"ok": False, "failed": ["a real case"], "exit_codes": {"S": 1},
                                "suites": {"S": {"total": 1, "passed": 0, "skipped": 0, "failed": 1}}},
                       status='FAIL', log='[xUnit.net] %s\n' % checker.STEP_ASIDE[0])
        with tempfile.TemporaryDirectory() as directory:
            sealed = archive(directory, 'edit-sealed-evidence.zip', failed)
            clean = archive(directory, 'edit-clean-evidence.zip', files())
            both = Path(directory) / 'edit-appended-zip-evidence.zip'
            both.write_bytes(sealed.read_bytes() + clean.read_bytes())
            self.assertIn(sealed.read_bytes(), both.read_bytes())  # nothing of the sealed run was rewritten
            problems, _ = checker.check(both)
            self.assertTrue(any('does not begin and end with this archive' in problem for problem in problems),
                            problems)

    def test_refuses_a_zip_whose_own_listing_leaves_the_file_unaccounted_for(self):
        """Asking only whether some listed member starts at byte 0 was not enough: a directory appended to a sealed
        zip, listing the sealed entries at their own offsets, satisfies that while the end record puts the directory
        past them - so the bytes between are in the file and in no member of it, and one zeroed size in that appended
        copy makes a member read as empty. A sealed run's own listing covers its file exactly, once."""
        failed = files(status='FAIL')
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-forged-evidence.zip', failed)
            blob = path.read_bytes()
            with zipfile.ZipFile(path) as held:
                start, count = held.start_dir, len(held.infolist())
            copied = blob[start:blob.rfind(b'PK\x05\x06')]  # its own central directory, verbatim
            path.write_bytes(blob + copied + struct.pack(
                '<4sHHHHIIH', b'PK\x05\x06', 0, 0, count, count, len(copied), len(blob), 0))
            self.assertIn(blob, path.read_bytes())  # nothing of the first zip was rewritten
            problems, _ = checker.check(path)
            self.assertTrue(any('in no member of it' in problem for problem in problems), problems)

    def test_reads_a_log_and_a_results_file_by_how_their_names_begin_and_end(self):
        """The step-aside scan and the two presence rules find their files by a name's start and end, not by a part of
        it: a run carrying only `latest-run.log` carries no console log of this runner's, and only a `.trx.bak` carries
        no results. Matched loosely, both would be found where there are none."""
        contents = files()
        contents['latest-run.log'] = contents.pop('test-FileCat.App.Tests-Edit.log')
        self.assertIn('it carries no test output to read', self.check(contents)[0])
        contents = files()
        contents['results/FileCat.App.Tests-Edit.trx.bak'] = contents.pop('results/FileCat.App.Tests-Edit.trx')
        self.assertIn('it carries no test results to read', self.check(contents)[0])

    def test_reads_the_system_from_how_the_record_s_line_begins(self):
        """A macOS uname line can name Linux further along it (`Linux-compat`), and a system matched anywhere in the
        line would read that run as the Linux route's."""
        contents = files(system='Darwin mac.local 27.0.0 Darwin Kernel Version 27.0.0 arm64 Linux-compat')
        problems, facts = self.check(contents, unix=True)
        self.assertEqual([], problems)
        self.assertEqual('macos', facts['system'])

    def test_reads_the_gate_s_name_whole_and_not_as_a_prefix_of_another(self):
        """`FILECAT_EDIT_NATIVE_REQUIRED_LEGACY=1` is not this gate, and a name matched as a part would take it - in
        either runner's record (the fifty-fifth review found the Unix record's reading held by no case)."""
        contents = files()
        contents['machine.json'] = json.dumps({"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro",
                                               "Env": checker.GATE_NAME + '_LEGACY=1'})
        self.assertIn('its machine record does not name ' + checker.GATE_NAME, self.check(contents)[0])
        legacy = checker.GATE_NAME + '_LEGACY=1'
        record = self.unix_record('entry=env:' + legacy, 'env=' + legacy)
        self.assertIn('its machine record does not name ' + checker.GATE_NAME, self.check(record, unix=True)[0])

    def test_reads_every_status_file_and_not_the_first_of_them(self):
        """Each status.txt has to say PASS, as each summary has to pass - the code says so beside the summaries."""
        contents = files()
        contents['inner/status.txt'] = 'FAIL'
        self.assertIn('its status is not PASS', self.check(contents)[0])

    def test_reads_a_name_as_the_whole_leaf_and_not_a_part_of_one(self):
        """The runners drop `full-r35-win-status.txt` and its kin beside an archive, so a rule matching part of a
        leaf would take one of those for the run's own `status.txt` and never miss it."""
        contents = files()
        contents['full-r35-win-status.txt'] = contents.pop('status.txt')
        problems, _ = self.check(contents)
        self.assertIn('it carries no status.txt', problems)

    def test_reads_the_step_aside_reasons_out_of_the_results_too(self):
        """A test's own reason is in the results file; the console log prints its name and `[SKIP]` without one."""
        reason = checker.STEP_ASIDE[1]
        trx = PASSING_TRX + ('<UnitTestResult testName="FileCat.Tests.Something" outcome="NotExecuted">'
                             '<Output><StdOut>%s</StdOut></Output></UnitTestResult>' % reason)
        problems, _ = self.check(files(trx=trx))
        self.assertTrue(any(('stepped aside' in problem and reason in problem) for problem in problems), problems)

    def test_reads_the_member_it_listed_and_not_whichever_answers_to_the_name(self):
        """One appended 512-byte folder header with a file's own name made that file read as empty: a tar resolves a
        name to its last member and gives nothing for a folder, and the folder was kept out of the name list, so
        nothing saw it. It emptied a log that said a test had stepped aside, and the lane was accepted - here beside a
        second log, so the run still has test output to read."""
        step = STEP_ASIDE_WORDS[0]
        contents = files(log='[xUnit.net] ' + step + '\n')
        contents['test-FileCat.Core.Tests-Edit.log'] = '[xUnit.net] everything ran\n'
        contents['tests.txt'] += 'test FileCat.Core.Tests-Edit: (FullyQualifiedName~Edit)\r\n'  # its runner lists both
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-shadowed-evidence.tar.gz', contents, unix=True)
            self.assertTrue(any(step in problem for problem in checker.check(path)[0]))
            name = next(n for n in checker.Archive(path).names() if n.split('/')[-1].startswith('test-FileCat.App'))
            with tarfile.open(path, 'r:gz') as held:
                members = [(item, held.extractfile(item).read() if item.isfile() else None)
                           for item in held.getmembers()]
            with tarfile.open(path, 'w:gz') as rebuilt:
                for item, data in members:
                    rebuilt.addfile(item) if data is None else rebuilt.addfile(item, io.BytesIO(data))
                folder = tarfile.TarInfo(name)
                folder.type = tarfile.DIRTYPE
                rebuilt.addfile(folder)
            problems, _ = checker.check(path)
            self.assertTrue(any(step in problem for problem in problems), problems)

    def test_does_not_count_a_folder_entry_as_the_file_it_is_named_after(self):
        """A folder of a record's own name was counted as a second machine record, and read as nothing."""
        contents = files()
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-foldered-evidence.tar.gz', contents, unix=True)
            with tarfile.open(path, 'r:gz') as held:
                members = [(item, held.extractfile(item).read() if item.isfile() else None)
                           for item in held.getmembers()]
            with tarfile.open(path, 'w:gz') as rebuilt:
                for item, data in members:
                    rebuilt.addfile(item) if data is None else rebuilt.addfile(item, io.BytesIO(data))
                folder = tarfile.TarInfo('notes/machine.json')
                folder.type = tarfile.DIRTYPE
                rebuilt.addfile(folder)
            self.assertEqual([], checker.check(path)[0])

    def test_refuses_a_run_whose_pin_passed_only_in_a_class_of_its_own(self):
        """A class that is nested, renamed, or merely longer than the two real ones left the family unsaid, and an
        unsaid family left the machine record unchallenged: the Windows lane, relabelled, was accepted as Linux."""
        pin = checker.PINS[2]
        for other in ('FileCat.Platform.Windows.Tests.Outer+WindowsEditStorageTests.',
                      'FileCat.Platform.Windows.Tests.WindowsEditStorageTestsV2.',
                      'FileCat.Core.Tests.EditStorageSharedTests.'):
            with self.subTest(other=other):
                trx = PASSING_TRX.replace('FileCat.Tests.' + pin, other + pin)
                problems, _ = self.check(files(trx=trx))
                self.assertIn('its results hold no passing dual-root pin in either platform class '
                              '(WindowsEditStorageTests or UnixEditStorageTests), so nothing in the run itself says '
                              'what produced it', problems)

    def test_refuses_a_machine_record_that_holds_a_member_twice(self):
        """A parser keeps the last of a repeated member while a reader sees the first: one record showed a reader
        Windows and this check Linux. A member repeated with nothing else wrong is refused as well."""
        contents = files()
        contents['machine.json'] = ('{"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro", "Env": "%s", '
                                    '"Os": "Linux h 6.0 x86_64 GNU/Linux"}' % checker.GATE)
        problems, _ = self.check(contents)
        self.assertIn('its machine record holds Os more than once, so what it says depends on who reads it', problems)
        contents = files()
        contents['machine.json'] = ('{"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro", "Env": "%s", '
                                    '"RunId": "edit-test-again"}' % checker.GATE)
        self.assertEqual(['its machine record holds RunId more than once, so what it says depends on who reads it'],
                         self.check(contents)[0])

    def test_refuses_a_summary_that_names_no_suite(self):
        """Counters are what the report's numbers come from; a summary with none is not a run that passed."""
        problems, _ = self.check(files(summary=dict(SUMMARY, suites={})))
        self.assertIn('summary.json names no suite', problems)
        self.assertIn('its summary names no suite', problems)

    def test_refuses_an_archive_that_holds_a_name_more_than_once(self):
        """A reader gets the last member of a repeated name while every loop here lists each occurrence, so appending
        a clean copy - with no original byte rewritten - made a run that failed read as one that passed. A name held
        twice with the same text is refused as well: which copy speaks is not a thing to guess."""
        with tempfile.TemporaryDirectory() as directory:
            failed = files(summary={"ok": False, "failed": ["a case"], "exit_codes": {"S": 1},
                                    "suites": {"S": {"total": 1, "passed": 0, "skipped": 0, "failed": 1}}},
                           status='FAIL')
            path = archive(directory, 'edit-appended-evidence.zip', failed)
            self.assertNotEqual([], checker.check(path)[0])
            clean = files()
            with warnings.catch_warnings():  # the duplicate name is the point of this case
                warnings.simplefilter('ignore', UserWarning)
                with zipfile.ZipFile(path, 'a') as zipped:
                    for name in ('summary.json', 'status.txt'):
                        zipped.writestr(name, clean[name])
            problems, _ = checker.check(path)
            self.assertTrue(any('more than once' in problem for problem in problems), problems)
            path = archive(directory, 'edit-twice-evidence.zip', files())
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                with zipfile.ZipFile(path, 'a') as zipped:
                    zipped.writestr('status.txt', 'PASS')
            problems, _ = checker.check(path)
            self.assertEqual(1, len(problems), problems)
            self.assertIn('status.txt more than once', problems[0])

    def test_refuses_a_run_whose_machine_record_does_not_say_which_system_ran_it(self):
        problems, _ = self.check(files(system='a machine'))
        self.assertIn('its machine record does not say which system ran it, so nothing in it binds it to a route',
                      problems)

    def test_names_the_archive_it_accepts_by_its_own_digest(self):
        """What binds a report to an archive: the release manifest requires the report carried under a route's
        `.../check` to name, by digest, the archive carried under that route's ID. A name could be any file's, and a
        report that named only a name could be moved to an archive it never read."""
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-good-evidence.zip', files())
            report = Path(directory) / 'report.txt'
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(path), '--out', str(report)]):
                    self.assertEqual(0, checker.main())
            verdict = report.read_text(encoding='utf-8').splitlines()[0]
            self.assertTrue(verdict.startswith('edit-good-evidence.zip: edit-storage evidence,'), verdict)
            self.assertTrue(verdict.endswith(', no failure, %s in force, windows, sha256 %s'
                                             % (checker.GATE, hashlib.sha256(path.read_bytes()).hexdigest())), verdict)

    def test_names_the_system_it_read_and_not_always_one_of_them(self):
        """The word the release manifest binds a route by. A check that printed one system for every run would have
        every Unix lane's report bind to the Windows route, which is the hole two reviews closed."""
        for system, word in (('Microsoft Windows 11 Pro 10.0.26220.0', 'windows'),
                             ('Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux', 'linux'),
                             ('Darwin host 27.0.0 Darwin Kernel Version 27.0.0 arm64', 'macos')):
            with self.subTest(word):
                with tempfile.TemporaryDirectory() as directory:
                    unix = word != 'windows'
                    path = archive(directory, 'edit-system-evidence' + ('.tar.gz' if unix else '.zip'),
                                   files(system=system), unix=unix)
                    report = Path(directory) / 'report.txt'
                    with contextlib.redirect_stdout(io.StringIO()):
                        with unittest.mock.patch.object(sys, 'argv', ['check', str(path), '--out', str(report)]):
                            self.assertEqual(0, checker.main())
                    self.assertIn(', %s, sha256 ' % word, report.read_text(encoding='utf-8').splitlines()[0])

    def test_names_an_archive_it_cannot_read_and_still_writes_its_report(self):
        """A malformed archive is one more refusal: the run's other archives are still reported, and `--out` is written."""
        with tempfile.TemporaryDirectory() as directory:
            good = archive(directory, 'edit-good-evidence.zip', files())
            bad = Path(directory) / 'edit-bad-evidence.zip'
            bad.write_bytes(b'not a zip at all')
            report = Path(directory) / 'report.txt'
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                with unittest.mock.patch.object(
                        sys, 'argv', ['check', str(bad), str(good), '--out', str(report)]):
                    code = checker.main()
            text = output.getvalue()
            self.assertEqual(1, code)
            self.assertIn('edit-bad-evidence.zip: NOT edit-storage evidence', text)
            self.assertIn('it cannot be read as a sealed run: BadZipFile', text)
            self.assertIn('edit-good-evidence.zip: edit-storage evidence', text)
            self.assertEqual(text, report.read_text(encoding='utf-8'))

    def test_reads_windows_wherever_its_caption_says_the_word(self):
        """`Os` is the system's caption in its own language, written in its ANSI code page by the runner that sealed the
        r35 lanes, and read by a prefix the first of these named no system at all (the forty-eighth review): Windows is
        named wherever the word stands, and what is not UTF-8 is read as a replacement character."""
        for caption, page in (('Майкрософт Windows 10 Pro 10.0.19045.0', 'cp1251'),
                              ('Microsoft Windows 11 Pro für Workstations 10.0.26100.0', 'cp1252'),
                              ('Windows 11 Pro 10.0.26100.0', 'cp1252')):
            with self.subTest(caption=caption):
                contents = files(system=caption)
                contents['machine.json'] = contents['machine.json'].encode(page)
                problems, facts = self.check(contents)
                self.assertEqual([], problems)
                self.assertEqual('windows', facts['system'])

    def test_reads_a_system_only_from_the_record_its_runner_writes(self):
        """Only the Windows runner writes `machine.json`, and only the Unix runner - on Linux or macOS, nowhere else -
        writes `machine.txt`: a `machine.json` naming Linux and a `machine.txt` naming Windows name no system (the
        forty-eighth review)."""
        pin = checker.PINS[2]
        said = 'its machine record does not say which system ran it, so nothing in it binds it to a route'
        unix = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Core.Tests.UnixEditStorageTests.' + pin)
        contents = files(trx=unix)
        contents['machine.json'] = json.dumps({"RunId": "edit-test", "Os": 'Linux host 6.0.0 x86_64 GNU/Linux',
                                               "Env": 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + checker.GATE})
        self.assertIn(said, self.check(contents)[0])
        # Beside a Windows run's own results too: a caption that does not name Windows - Linux's, or none at all - names
        # no system, whatever the results say (the fifty-fourth review found the caption's own test held by no case,
        # every such record here standing beside the Unix class).
        windows = PASSING_TRX.replace('FileCat.Tests.' + pin,
                                      'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin)
        for caption in ('Linux host 6.0.0 x86_64 GNU/Linux', ''):
            with self.subTest(caption=caption):
                contents = files(trx=windows)
                contents['machine.json'] = windows_json(dict(json.loads(contents['machine.json']), Os=caption))
                self.assertIn(said, self.check(contents)[0])
        contents = files(trx=windows, system='Linux host 6.0.0 x86_64 GNU/Linux')
        contents['machine.txt'] = 'Windows host 10.0.26100 x86_64\nenv=%s\n%s\n' % (checker.GATE, TEST_LINE)
        self.assertIn(said, self.check(contents, unix=True)[0])

    def test_reads_a_tar_member_s_name_with_its_backslashes(self):
        """A tar's names are POSIX, where a backslash is part of a name: split there, a log named `test-A\\B.log`
        was read as `B.log`, never scanned, and its reason never read (the forty-eighth review). In a zip it separates
        folders."""
        contents = files(system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        contents['test-A\\B.log'] = '[xUnit.net] SKIP ' + STEP_ASIDE_WORDS[0] + ' here\n'
        problems, _ = self.check(contents, unix=True)
        self.assertTrue(any('stepped aside' in problem for problem in problems), problems)

    def test_refuses_a_filter_dotnet_halves_or_reads_as_a_switch(self):
        """dotnet halves a filter's trailing backslashes on their way (two arriving as one) and reads a filter beginning
        with `/` as a switch of its own (the forty-eighth review): a Unix filter or project name holds no backslash, and
        neither runner's filter begins with `/`."""
        gate = 'FILECAT_EDIT_NATIVE_REQUIRED'
        for line, said in (('filter=FullyQualifiedName~X\\\\', "holding '\\\\'"),
                           ('filter=FullyQualifiedName~A\\B', "holding '\\\\'"),
                           ('test FileCat.A\\B: *', "holding '\\\\'"),
                           ('filter=/p:X=1', 'a filter beginning with /'),
                           ('filter=-p:X=1', 'a filter beginning with -')):
            with self.subTest(line=line):
                problems, _ = self.check(self.unix_record('filter=*', 'env=' + gate + '=1', line), unix=True)
                self.assertTrue(any(said in problem for problem in problems), problems)
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'
        contents = self.windows_record(recorded)
        contents['child.cmd'] = launcher(recorded, filter='/p:X=1')
        problems, _ = self.check(contents)
        self.assertTrue(any('passes a filter beginning with /' in problem for problem in problems), problems)

    def test_reads_a_member_s_name_in_the_oem_code_page_where_its_header_names_no_other(self):
        """A zip whose names are not flagged UTF-8 stores them in the OEM code page - as Explorer writes them - and both
        the reader and this check read them so: read in another, an ordinary archive's own name would not be its own
        (the forty-eighth review)."""
        with tempfile.TemporaryDirectory() as directory:
            contents = files()
            contents['Pr?fung.txt'] = 'a member no rule reads\r\n'
            path = archive(directory, 'edit-oem-evidence.zip', contents)
            data = path.read_bytes()
            self.assertEqual(2, data.count(b'Pr?fung.txt'))
            path.write_bytes(data.replace(b'Pr?fung.txt', b'Pr' + bytes((0x81,)) + b'fung.txt'))
            with zipfile.ZipFile(path) as held:
                self.assertIn('Pr' + chr(0xFC) + 'fung.txt', held.namelist())
            self.assertEqual([], checker.check(path)[0])

    def test_accepts_a_zip64_member_whose_header_carries_a_field_as_wide_first(self):
        """An NTFS field (32 bytes) before the zip64 field is as wide as it, and the zip64 field is told by its id, not
        its width (the forty-eighth review)."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-z64-ntfs-evidence.zip'
            with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as held:
                for name, text in with_child_cmd(files()).items():
                    held.writestr(name.replace('/', '\\'), sealed(name, text, False))
                stamped = zipfile.ZipInfo('zz-ntfs.bin')
                # NTFS times (id 10): four reserved bytes, then attribute 1 of 24 bytes - three times of eight.
                stamped.extra = struct.pack('<HHIHHQQQ', 0x000A, 32, 0, 1, 24, 1, 2, 3)
                with held.open(stamped, 'w', force_zip64=True) as member:
                    member.write(b's' * 300)
            with zipfile.ZipFile(path) as held:
                self.assertIsNone(held.testzip())
            self.assertEqual([], checker.check(path)[0])

    def test_refuses_a_zip64_field_too_narrow_for_both_sizes(self):
        """A local header's zip64 field holds both sizes; one too narrow for them states no compressed size, and read
        past its end, the bytes after it - here the member's size itself - would be taken for one (bytes changed: no
        writer writes such a field)."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-z64-narrow-evidence.zip'
            with zipfile.ZipFile(path, 'w') as held:
                for name, text in with_child_cmd(files()).items():
                    held.writestr(name.replace('/', '\\'), sealed(name, text, False))
                narrow = zipfile.ZipInfo('zz-narrow.bin')
                narrow.extra = struct.pack('<HHQQ', 1, 8, 300, 300)  # a zip64 field of one size, and that size again
                held.writestr(narrow, b'n' * 300)
            with zipfile.ZipFile(path) as held:
                offset = held.getinfo('zz-narrow.bin').header_offset
            data = bytearray(path.read_bytes())
            data[offset + 18:offset + 22] = bytes((0xFF, 0xFF, 0xFF, 0xFF))
            path.write_bytes(bytes(data))
            problems, _ = checker.check(path)
            self.assertEqual(1, len(problems), problems)
            self.assertIn("the entry 'zz-narrow.bin' is 300 bytes long where the file says nothing", problems[0])

    def test_reads_a_windows_env_past_an_empty_entry(self):
        """`;FILECAT_EDIT_NATIVE_REQUIRED=1` passes the Windows runner's own pattern, and its child passes the empty
        entry over and sets the gate (the forty-eighth review): an empty entry is passed over here too, not read as one
        with no value."""
        self.assertEqual([], self.check(self.windows_record(';' + checker.GATE))[0])

    def test_refuses_a_run_whose_skipped_count_is_not_a_number(self):
        """As a passed count that is not a number is refused, so is a skipped one (the forty-eighth review)."""
        counters = dict(SUMMARY['suites']['FileCat.App.Tests-Edit'], skipped='none')
        summary = dict(SUMMARY, suites={'FileCat.App.Tests-Edit': counters})
        with tempfile.TemporaryDirectory() as directory:
            odd = archive(directory, 'edit-odd-evidence.zip', files(summary=summary))
            report = Path(directory) / 'report.txt'
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(odd), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            self.assertIn('edit-odd-evidence.zip: NOT edit-storage evidence',
                          report.read_text(encoding='utf-8').splitlines())

    def test_reads_each_byte_order_mark_a_member_may_carry(self):
        """Each mark on its own (the forty-eighth review): a console log in UTF-16 big-endian holding a reason, and a
        log of a UTF-8 mark and a line end alone, which says nothing."""
        reason = '[xUnit.net] SKIP ' + STEP_ASIDE_WORDS[0] + ' here\r\n'
        for log, said in ((bytes((0xFE, 0xFF)) + reason.encode('utf-16-be'), 'stepped aside'),
                          (bytes((0xEF, 0xBB, 0xBF)) + b'\r\n', 'its test output is empty')):
            with self.subTest(said=said):
                problems, _ = self.check(files(log=log))
                self.assertTrue(any(said in problem for problem in problems), problems)

    def test_reads_a_marked_member_with_what_does_not_decode_replaced(self):
        """A console log with a mark is read as its mark says, and a unit that does not decode - a lone surrogate - is
        read as a replacement character, as in a member without a mark, not taken for an archive that cannot be read."""
        log = (bytes((0xFF, 0xFE)) + '[xUnit.net] ran '.encode('utf-16-le') + bytes((0x00, 0xD8)) +
               ' here\r\n'.encode('utf-16-le'))
        self.assertEqual([], self.check(files(log=log))[0])

    def test_reads_a_record_line_as_an_entry_only_where_it_begins_env(self):
        """A Unix record's entries are its `env=` lines, and a filter holding `env=` further along is a filter."""
        record = self.unix_record('filter=FullyQualifiedName~Xenv=Y', 'env=FILECAT_EDIT_NATIVE_REQUIRED=1')
        self.assertEqual([], self.check(record, unix=True)[0])

    def test_refuses_a_run_whose_test_processes_are_named_alike(self):
        """Each test process writes its TRX, log and exit code under its name, and two named alike - an entry given
        twice, `#~X` beside `#not+X`, names that differ only in case, which NTFS and APFS do not tell apart - wrote over
        each other, so a failure in the first went unread (the forty-ninth review): its runner listed two processes
        where the archive holds one log."""
        said = 'its runner listed 2 test processes where the archive holds the logs of 1'
        contents = files()
        contents['tests.txt'] = (TEST_LINE + '\r\n') * 2
        problems, _ = self.check(contents)
        self.assertTrue(any(said in problem for problem in problems), problems)
        problems, _ = self.check(self.unix_record('env=FILECAT_EDIT_NATIVE_REQUIRED=1', TEST_LINE, TEST_LINE), unix=True)
        self.assertTrue(any(said in problem for problem in problems), problems)
        # Two logs in an archive made where case tells names apart, and one file where the runner wrote them.
        contents = self.unix_record('env=FILECAT_EDIT_NATIVE_REQUIRED=1', TEST_LINE, TEST_LINE.replace('Edit:', 'edit:'))
        contents['test-FileCat.App.Tests-edit.log'] = '[xUnit.net] everything ran\n'
        problems, _ = self.check(contents, unix=True)
        self.assertTrue(any(said in problem for problem in problems), problems)

    def test_reads_every_process_of_a_run_as_its_runners_seal_one(self):
        """An edit lane runs more than one test process - r35's split their pins over two or three - and every one of
        them is read: the results of each, the failure counter and exit code of each suite, and the counts of all in the
        report (the forty-ninth review found each of these loops held by no case, the fixtures carrying one process)."""
        for unix in (False, True):
            with self.subTest(unix=unix):
                problems, facts = self.check(two_process_run(unix), unix)
                self.assertEqual([], problems)
                self.assertEqual(set(checker.PINS), set(facts['pins']))
                self.assertIn('suite %s has 1 failures' % SECOND, self.check(two_process_run(unix, counted=1), unix)[0])
                self.assertIn('suite %s exited 1' % SECOND, self.check(two_process_run(unix, exited=1), unix)[0])
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-two-evidence.zip', two_process_run(False))
            report = Path(directory) / 'report.txt'
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(path), '--out', str(report)]):
                    self.assertEqual(0, checker.main())
            self.assertIn('edit-two-evidence.zip: edit-storage evidence, 11 passed, 1 skipped, no failure,',
                          report.read_text(encoding='utf-8').splitlines()[0])

    def test_refuses_a_run_whose_dual_root_pin_failed_in_one_row(self):
        """Every pin is read, the dual-root one too: a row of it that failed beside one that passed is a pin that failed
        somewhere, which the family read from the passing row does not refuse (the forty-ninth review)."""
        pin = PIN_NAMES[2]
        rows = (PASSING_TRX.replace('<UnitTestResult executionId="2" testName="FileCat.Tests.%s" outcome="Passed" />'
                                    % pin, '') +
                '<UnitTestResult testName="FileCat.Tests.%s(root: 1)" outcome="Passed" />' % pin +
                '<UnitTestResult testName="FileCat.Tests.%s(root: 2)" outcome="Failed" />' % pin)
        problems, _ = self.check(files(trx=rows))
        self.assertTrue(any(problem.startswith('the pin %s did not pass' % pin) for problem in problems), problems)

    def test_names_an_archive_larger_than_a_block_by_the_digest_of_all_of_it(self):
        """The digest is read a block of 1 MiB at a time, and the full lanes r35 accepted are six times that: it is the
        digest of every block, not of the first (the forty-ninth review)."""
        contents = files()
        contents['zz-ballast.bin'] = random.Random(49).randbytes(1536 * 1024)
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-large-evidence.zip', contents)
            self.assertGreater(path.stat().st_size, 1 << 20)
            report = Path(directory) / 'report.txt'
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(path), '--out', str(report)]):
                    self.assertEqual(0, checker.main())
            self.assertTrue(report.read_text(encoding='utf-8').splitlines()[0].endswith(
                ', windows, sha256 ' + hashlib.sha256(path.read_bytes()).hexdigest()))

    def test_refuses_a_project_path_that_leaves_the_packet(self):
        """A project path holding `..` reached another run's sources beside the packet, with the packet's hash still in
        the record (the forty-ninth review): refused in a Windows launcher's projects, and in the Unix record's entry
        lines, which its runner writes since then. A variable's value and a test entry's shard name no path; a built or
        published entry is a path to its end, `#` and all, as its runner takes it (the fiftieth review)."""
        said = 'reaches out of the hash-pinned packet'
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;FILECAT_EDIT_NATIVE_REQUIRED=1'
        for projects, refused in (('..\\..\\edit-r35-win\\src\\tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit', True),
                                  ('build:..\\other\\A.csproj,tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit', True),
                                  ('tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit,..\\x\\B.csproj', True),
                                  ('publish:#\\..\\..\\..\\edit-r34-win\\src\\src\\FileCat.App\\FileCat.App.csproj', True),
                                  ('build:x#\\..\\other\\A.csproj', True),
                                  ('expand:C:\\FileCatValidation\\aw-claude\\packets\\..\\packets\\X.zip,'
                                   'tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit', False),
                                  ('tests\\FileCat.App.Tests\\FileCat.App.Tests.csproj#Edit..Native', False)):
            with self.subTest(projects=projects):
                contents = self.windows_record(recorded)
                contents['child.cmd'] = launcher(recorded, projects=projects)
                problems, _ = self.check(contents)
                self.assertEqual(refused, any(said in problem for problem in problems), problems)
        for entry, refused in (('../../edit-r34-ubuntu/src/tests/FileCat.App.Tests/FileCat.App.Tests.csproj#Edit', True),
                               ('publish:../x/src/FileCat.App/FileCat.App.csproj', True),
                               ('publish:#/../../../edit-r34-ubuntu/src/src/FileCat.App/FileCat.App.csproj', True),
                               ('build:x#/../../../other/A.csproj', True),
                               ('tests/FileCat.App.Tests/FileCat.App.Tests.csproj#Edit..Native', False),
                               ('env:FILECAT_ARCHIVE_SMALL_VOLUME=/srv/../small', False)):
            with self.subTest(entry=entry):
                record = self.unix_record('entry=' + entry, 'env=FILECAT_EDIT_NATIVE_REQUIRED=1')
                problems, _ = self.check(record, unix=True)
                self.assertEqual(refused, any(said in problem for problem in problems), problems)

    def test_refuses_a_unix_entry_line_holding_what_dotnet_changes(self):
        """The Unix record names each entry since the forty-ninth review, and an entry holding `"`, `@`, `%` or a
        backslash is refused there as in a test line - its runner refuses one in every entry before a run."""
        for entry, said in (('tests/A@B.csproj#Edit', "holding '@'"), ('env:FILECAT_X=a%25b', "holding '%'")):
            with self.subTest(entry=entry):
                record = self.unix_record('entry=' + entry, 'env=FILECAT_EDIT_NATIVE_REQUIRED=1')
                problems, _ = self.check(record, unix=True)
                self.assertTrue(any(said in problem for problem in problems), problems)

    def test_reads_a_windows_record_its_runner_writes_in_utf8(self):
        """The Windows runner writes machine.json and summary.json in UTF-8 with a mark since the forty-ninth review: in a double-byte
        ANSI code page a character's second byte can be a backslash (表 is 0x95 0x5C in cp932), and such a record did not
        read as JSON at all."""
        mark = bytes((0xEF, 0xBB, 0xBF))
        record = windows_json({"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro 10.0.26100.0",
                               "Defender": {"Exclusions": ["C:\\" + chr(0x8868) + chr(0x793A)]},
                               "Env": 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + checker.GATE})
        contents = files()
        contents['machine.json'] = mark + record.encode('utf-8')
        contents['summary.json'] = mark + contents['summary.json'].encode('utf-8')
        problems, facts = self.check(contents)
        self.assertEqual([], problems)
        self.assertEqual('windows', facts['system'])

    def test_reads_a_mark_only_where_a_member_begins(self):
        """A byte-order mark is read where a member begins: a log holding U+FEFF further along is UTF-8 from its first
        byte, and a reason it opens with is read whole."""
        log = (STEP_ASIDE_WORDS[0] + ' here, then ' + chr(0xFEFF) + '\n').encode('utf-8')
        contents = files(log=log, system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        problems, _ = self.check(contents, unix=True)
        self.assertTrue(any('stepped aside' in problem for problem in problems), problems)

    def test_accepts_a_zip_that_lists_a_folder_twice(self):
        """A zip's folder entries hold nothing a rule reads and are left out of its members, so a folder listed twice is
        not a member named twice."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-folders-evidence.zip'
            with warnings.catch_warnings():
                warnings.simplefilter('ignore')  # zipfile warns of the name it is given twice
                with zipfile.ZipFile(path, 'w') as held:
                    for name, text in with_child_cmd(files()).items():
                        held.writestr(name.replace('/', '\\'), sealed(name, text, False))
                    held.writestr('results/', b'')
                    held.writestr('results/', b'')
            with zipfile.ZipFile(path) as held:
                self.assertEqual(2, sum(item.is_dir() for item in held.infolist()))
            self.assertEqual([], checker.check(path)[0])

    def test_refuses_a_tar_whose_earlier_member_claims_data_past_the_stream(self):
        """The floor is the furthest any member's data reaches, not the last member's: a leading folder header claiming
        8 MiB - which tarfile does not skip for a folder - reaches past what was compressed (the fiftieth review; no
        runner writes such a header)."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-floor-evidence.tar.gz'
            with tarfile.open(path, 'w:gz') as tar:
                top = tarfile.TarInfo('edit-test')
                top.type = tarfile.DIRTYPE
                top.mode = 0o755
                top.size = 8 << 20
                tar.addfile(top)
                for leaf, text in files(system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux').items():
                    blob = sealed(leaf, text, True)
                    item = tarfile.TarInfo('edit-test/' + leaf)
                    item.size = len(blob)
                    tar.addfile(item, io.BytesIO(blob))
            problems, _ = checker.check(path)
            self.assertTrue(any('past what was compressed' in problem for problem in problems), problems)

    def test_refuses_bytes_at_the_start_of_a_later_block_past_the_tar(self):
        """Past the tar's own end every byte of each later block read is read, not only that block's last few: a gzip
        whose header is padded so the first 1 MiB read ends where a second deflate block begins holds a run, 2 MiB of
        zeros, then 512 bytes of something else (the fiftieth review; no runner writes such a stream)."""
        raw = io.BytesIO()
        with tarfile.open(fileobj=raw, mode='w') as tar:
            for leaf, text in files(system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux').items():
                blob = sealed(leaf, text, True)
                item = tarfile.TarInfo('edit-test/' + leaf)
                item.size = len(blob)
                tar.addfile(item, io.BytesIO(blob))
        first = raw.getvalue() + bytes(2 << 20)
        second = b'JUNK' * 128 + bytes(3 << 20)
        packer = zlib.compressobj(6, zlib.DEFLATED, -15)
        head = packer.compress(first) + packer.flush(zlib.Z_FULL_FLUSH)
        rest = packer.compress(second) + packer.flush()
        comment = b'c' * ((1 << 20) - 10 - 1 - len(head))
        header = bytes((0x1F, 0x8B, 8, 0x10)) + bytes(4) + bytes((0, 255)) + comment + bytes(1)
        self.assertEqual(1 << 20, len(header) + len(head))
        plain = first + second
        trailer = struct.pack('<II', zlib.crc32(plain), len(plain) & 0xFFFFFFFF)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-tail-evidence.tar.gz'
            path.write_bytes(header + head + rest + trailer)
            problems, _ = checker.check(path)
            self.assertTrue(any('what it holds past its members is not padding but 512 bytes of something' in problem
                                for problem in problems), problems)

    def test_reads_a_member_s_descriptor_by_its_own_header_s_flag(self):
        """Whether a descriptor follows a member's data is its own local header's to say: a zip written to a stream,
        whose directory entries were then rewritten to clear that flag, is still read whole (the fiftieth review)."""
        class Stream(io.RawIOBase):
            def __init__(self):
                self.data = bytearray()

            def writable(self):
                return True

            def write(self, block):
                self.data += block
                return len(block)

        sink = Stream()
        with zipfile.ZipFile(sink, 'w', zipfile.ZIP_DEFLATED) as held:
            for leaf, text in with_child_cmd(files()).items():
                held.writestr(leaf.replace('/', '\\'), sealed(leaf, text, False))
        data = bytearray(sink.data)
        at = data.find(b'PK\x01\x02')
        while at >= 0:
            flag, = struct.unpack('<H', data[at + 8:at + 10])
            data[at + 8:at + 10] = struct.pack('<H', flag & ~8)
            at = data.find(b'PK\x01\x02', at + 4)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-flags-evidence.zip'
            path.write_bytes(bytes(data))
            with zipfile.ZipFile(path) as held:
                self.assertFalse(any(item.flag_bits & 8 for item in held.infolist()))
            self.assertEqual([], checker.check(path)[0])

    def test_counts_the_processes_every_test_list_names(self):
        """Every tests.txt in the archive is read: a second list, in a folder of its own, names a process no log was
        written for, and is refused as a line too many in the first would be (the fiftieth review)."""
        contents = two_process_run(False)
        contents['notes/tests.txt'] = TEST_LINE + '\r\n'
        problems, _ = self.check(contents)
        self.assertTrue(any('its runner listed 3 test processes where the archive holds the logs of 2' in problem
                            for problem in problems), problems)

    def test_refuses_a_unix_name_outside_filecat_in_lower_case(self):
        """A Unix name is matched with case, as the Unix environment keeps it: `filecat_archive_txf_native` is not a
        FILECAT_ name, and the test platform may read a name outside them (the fiftieth review; the Unix runner refuses
        one before a run)."""
        record = self.unix_record('env=filecat_archive_txf_native=1', 'env=FILECAT_EDIT_NATIVE_REQUIRED=1')
        problems, _ = self.check(record, unix=True)
        self.assertTrue(any("sets 'filecat_archive_txf_native', outside FILECAT_" in problem for problem in problems),
                        problems)

    def test_reads_a_deflated_zip64_member_s_compressed_size_from_its_own_field(self):
        """A zip64 field states the plain size first and the compressed size second, and a deflated member's two differ:
        the compressed one is what its span is measured by (the fifty-first review - every zip64 member here had been
        stored, its two sizes alike)."""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-z64-deflated-evidence.zip'
            with zipfile.ZipFile(path, 'w') as held:
                for name, text in with_child_cmd(files()).items():
                    held.writestr(name.replace('/', '\\'), sealed(name, text, False))
                packed = zipfile.ZipInfo('zz-packed.bin')
                packed.compress_type = zipfile.ZIP_DEFLATED
                with held.open(packed, 'w', force_zip64=True) as member:
                    member.write(b'p' * 5000)
            with zipfile.ZipFile(path) as held:
                item = held.getinfo('zz-packed.bin')
                self.assertLess(item.compress_size, item.file_size)
                self.assertIsNone(held.testzip())
            self.assertEqual([], checker.check(path)[0])

    def test_reads_a_windows_run_as_the_r35_lanes_sealed_it(self):
        """The runner that sealed the r35 lanes wrote machine.json and summary.json in the ANSI code page with no mark;
        it writes them in UTF-8 with a mark now, as the fixtures seal them, and an archive of either is read."""
        contents = with_child_cmd(files())
        contents['machine.json'] = contents['machine.json'].encode('cp1252')
        contents['summary.json'] = contents['summary.json'].encode('cp1252')
        problems, facts = self.check(contents)
        self.assertEqual([], problems)
        self.assertEqual('windows', facts['system'])

    def test_reads_a_tar_whose_stream_runs_past_one_block(self):
        """A gzipped tar is read a block of 1 MiB of its stream at a time, and how far it reached is the sum of every
        block's output: r35's full Unix lanes run to several such blocks, where every tar the suite accepted had fitted
        in one (the fifty-second review)."""
        contents = files(system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        contents['zz-ballast.bin'] = random.Random(52).randbytes(1536 * 1024)
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-large-evidence.tar.gz', contents, unix=True)
            self.assertGreater(path.stat().st_size, 1 << 20)
            self.assertEqual([], checker.check(path)[0])

    def test_accepts_a_windows_edit_lane_shaped_as_r35_s(self):
        """The Windows edit lane as r35 holds it, in the shapes the rules read: the launcher's own projects - a publish
        entry, and a shard of two classes joined by `+` - each file deflated and each name split with a backslash as
        Compress-Archive writes them, the results rows as the logger writes them across three results files, the Unix
        class's dual-root row stepped aside with its reason before the Windows class's passed, the records in the ANSI
        code page of the runner that sealed it. The fifty-third review found five rules the sealed lanes rest on held by
        no case, the fixtures' members shaped otherwise."""
        pins = checker.PINS
        app = logger_trx([logger_row('FileCat.App.Tests.EditStoreCompositionTests.' + pins[0], 'Passed', number=1),
                          logger_row('FileCat.App.Tests.EditTestStores.A_theory(scenario: &quot;capture-gate&quot;)',
                                     'Passed', number=2)])
        core = logger_trx([logger_row('FileCat.Core.Tests.NativeEditStorageTests.' + pins[1], 'Passed', number=3),
                           logger_row('FileCat.Core.Tests.UnixEditStorageTests.' + pins[2], 'NotExecuted',
                                      'Run the Windows native storage lane.', number=4)])
        platform = logger_trx([logger_row('FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pins[2], 'Passed',
                                          number=5)])
        names = ('FileCat.App.Tests-Edit', 'FileCat.Core.Tests-Edit-NativeEditStorage', 'FileCat.Platform.Windows.Tests-Edit')
        suites = {name: {"total": 2, "passed": 2, "skipped": 0, "failed": 0, "counters_total": 2} for name in names}
        summary = windows_json({"ok": True, "exit_codes": {name: 0 for name in names}, "failed": [], "suites": suites})
        record = windows_json({"RunId": "edit-r35-win", "Os": "Microsoft Windows 11 Pro Insider Preview 10.0.26340.0",
                               "Env": R35_ENV, "User": "Admin"})
        tests = ''.join('test %s: %s\r\n' % (name, shard) for name, shard in (
            (names[1], '(FullyQualifiedName~Edit|FullyQualifiedName~NativeEditStorage)'),
            (names[0], '(FullyQualifiedName~Edit)'), (names[2], '(FullyQualifiedName~Edit)')))
        utf16 = lambda text: bytes((0xFF, 0xFE)) + text.encode('utf-16-le')
        marked = lambda text: bytes((0xEF, 0xBB, 0xBF)) + text.encode('utf-8')
        launched = launcher(R35_ENV, run='C:\\FileCatValidation\\aw-claude\\edit-r35-win', projects=R35_PROJECTS,
                            hang='25')
        members = [('results/039f1593-6184-4683-a741-373b45165ae0/', b''),
                   ('results/24b7231a-dcaa-471d-b059-1f1e70c99361/', b''),
                   ('results/f90c1e6f-3835-4cfa-829e-bd8eca26fae2/', b''),
                   ('results/' + names[0] + '.trx', marked(app)), ('results/' + names[1] + '.trx', marked(core)),
                   ('results/' + names[2] + '.trx', marked(platform)),
                   ('account.json', windows_json({"Name": "FILECAT-VM\\Admin", "ElevationType": 3}).encode('cp1252'))]
        members += [('build-%s.log' % name.split('-')[0], utf16('  Determining projects to restore...\r\n'))
                    for name in names]
        members += [('child.cmd', launched.encode('ascii')), ('child.out', b''),
                    ('child.ps1', b'param([string]$Run)\r\n'), ('launch.json', b'{\r\n    "Finished":  "2026"\r\n}\r\n'),
                    ('machine.json', record.encode('cp1252')),
                    ('publish-FileCat.App.log', utf16('  FileCat.App -> publish\\FileCat.App\\\r\n')),
                    ('status.txt', b'PASS\r\n'), ('summary.json', summary.encode('cp1252')),
                    ('task.log', utf16('SUCCESS: The scheduled task "FcRun-edit-r35-win" has successfully been created.\r\n'))]
        members += [('test-%s.log' % name, utf16('[xUnit.net] everything ran\r\n')) for name in names]
        members += [('tests.txt', tests.encode('ascii'))]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'edit-r35-win-evidence.zip'
            zip_with_backslashes(path, members)
            with zipfile.ZipFile(path) as held:
                self.assertIn('results\\' + names[0] + '.trx', [item.orig_filename for item in held.infolist()])
                self.assertIsNone(held.testzip())
            problems, facts = checker.check(path)
            self.assertEqual([], problems)
            self.assertEqual('windows', facts['system'])

    def test_accepts_a_unix_full_lane_shaped_as_r35_s(self):
        """A full Unix lane as r35 holds one, in the shapes the rules read, with the entry lines its runner writes now:
        a build entry and a publish entry, a shard and the negated shard beside it - whose test line holds `!`, which
        dotnet passes on as written - the results rows as the logger writes them (the fifty-third review)."""
        pins = checker.PINS
        system = ('Linux filecat-ubuntu2404 7.0.0-43-generic #43~24.04.1-Ubuntu SMP PREEMPT_DYNAMIC Sat Oct 31 06:52:47 '
                  'UTC 2 x86_64 x86_64 x86_64 GNU/Linux')
        names = ('FileCat.Core.Tests-UnixArchiveStorageTests', 'FileCat.Core.Tests-not-UnixArchiveStorageTests',
                 'FileCat.App.Tests')
        lines = unix_head(system) + [
            'packet=546d8caf28f54066cbb58e5d222a9e5aab464899ab35568b4108e01fc8c982d4', 'filter=*',
            'entry=publish:src/FileCat.App/FileCat.App.csproj',
            'entry=build:tests/FileCat.CoreCrashHarness/FileCat.CoreCrashHarness.csproj',
            'entry=tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj#UnixArchiveStorageTests',
            'entry=tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj#!UnixArchiveStorageTests',
            'entry=tests/FileCat.App.Tests/FileCat.App.Tests.csproj', 'entry=hang:60', 'entry=parallel:on',
            'entry=env:FILECAT_EDIT_NATIVE_REQUIRED=1', 'hang=60m', 'parallel=on', 'env=FILECAT_EDIT_NATIVE_REQUIRED=1',
            'test %s: (FullyQualifiedName~UnixArchiveStorageTests)' % names[0],
            'test %s: (FullyQualifiedName!~UnixArchiveStorageTests)' % names[1], 'test %s: *' % names[2]]
        rows = {names[0]: [logger_row('FileCat.Core.Tests.UnixArchiveStorageTests.A_case', 'Passed', number=1)],
                names[1]: [logger_row('FileCat.Core.Tests.NativeEditStorageTests.' + pins[1], 'Passed', number=2),
                           logger_row('FileCat.Core.Tests.UnixEditStorageTests.' + pins[2], 'Passed', number=3)],
                names[2]: [logger_row('FileCat.App.Tests.EditStoreCompositionTests.' + pins[0], 'Passed', number=4)]}
        # The App suite's other rows, a thousand of them (a full lane's results hold thousands): a stream of that size
        # refers back further than 16 KiB - to a class's name a few dozen rows back - as every accepted r35 Unix lane's
        # does, which a reader with less than gzip's own window refuses (the fifty-fourth review found no accepted
        # case's stream reaching back further than 512 bytes).
        rows[names[2]] += [logger_row('FileCat.App.Tests.Some%dTests.A_case_%d' % (number % 37, number), 'Passed',
                                      number=number) for number in range(5, 1005)]
        suites = {name: {"total": len(rows[name]), "passed": len(rows[name]), "skipped": 0, "failed": 0,
                         "counters_total": len(rows[name])} for name in names}
        contents = {'build-FileCat.App.Tests.log': '  Determining projects to restore...\n',
                    'build-FileCat.CoreCrashHarness.log': '  Determining projects to restore...\n',
                    'started.txt': '2026-11-14T08:48:57Z\n'}
        contents.update({'results/%s.trx' % name: logger_trx(rows[name]) for name in names})
        contents.update({'publish-FileCat.App.log': '  FileCat.App -> publish/FileCat.App/\n',
                         'machine.txt': '\n'.join(lines) + '\n', 'finished.txt': '2026-11-14T09:58:02Z\n',
                         'summary.json': json.dumps({"suites": suites, "failed": [], "ok": True,
                                                     "exit_codes": {name: 0 for name in names}}, indent=2),
                         'status.txt': 'PASS\n'})
        contents.update({'exit-%s.txt' % name: '0\n' for name in names})
        contents.update({'test-%s.log' % name: '[xUnit.net] everything ran\n' for name in names})
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-run-evidence.tar.gz', contents, unix=True)
            # The fixture's own shape, asserted: read with a 16 KiB window, its stream does not read.
            with self.assertRaises(zlib.error):
                zlib.decompressobj(30).decompress(path.read_bytes())
            problems, facts = checker.check(path)
        self.assertEqual([], problems)
        self.assertEqual('linux', facts['system'])

    def test_refuses_more_test_logs_than_its_runner_listed_processes(self):
        """A log of a process no line lists is no more its runner's than a line with no log: the count is compared
        both ways (the fifty-third review found the other way held by no case)."""
        contents = files()
        contents['test-FileCat.Unlisted.Tests.log'] = '[xUnit.net] everything ran\n'
        problems, _ = self.check(contents)
        self.assertTrue(any('its runner listed 1 test processes where the archive holds the logs of 2' in problem
                            for problem in problems), problems)

    def test_refuses_a_suite_that_exited_below_zero_or_counted_failures_below_zero(self):
        """An exit code below zero is a failure - Windows' -1 for a job that returned nothing, a crash's status code -
        and a failure counter below zero is no clean count: each is refused, as one above zero is (the fifty-third
        review found the lower side of each held by no case)."""
        name = 'FileCat.App.Tests-Edit'
        exited = dict(SUMMARY, exit_codes={name: -1073741819})
        self.assertIn('suite %s exited -1073741819' % name, self.check(files(summary=exited))[0])
        counters = dict(SUMMARY['suites'][name], failed=-1)
        counted = dict(SUMMARY, suites={name: counters})
        self.assertIn('suite %s has -1 failures' % name, self.check(files(summary=counted))[0])

    def test_accepts_a_tar_whose_last_member_ends_one_byte_into_a_block(self):
        """A member's data fills its last block only in part, and the floor rounds it up to that block's end: a last
        member of 513 bytes - 512 and one, as any log can be - ends one byte into its second block, and a floor rounded
        short of it read that byte as something past the members (the fifty-fourth review found this side of the
        rounding held by no case; a block more is boundary 1)."""
        contents = files(system='Linux host 6.0.0 x86_64 GNU/Linux')
        log = ''.join('  Passed FileCat.App.Tests.EditTests.A_case_%d [1 ms]\n' % number for number in range(20))
        del contents['test-FileCat.App.Tests-Edit.log']
        contents['test-FileCat.App.Tests-Edit.log'] = log[:512] + '\n'
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-run-evidence.tar.gz', contents, unix=True)
            with tarfile.open(path) as held:
                last = max(held.getmembers(), key=lambda item: item.offset_data + item.size)
                self.assertEqual(('edit-test/test-FileCat.App.Tests-Edit.log', 513), (last.name, last.size))
            problems, facts = checker.check(path)
        self.assertEqual([], problems)
        self.assertEqual('linux', facts['system'])

    def test_accepts_every_character_the_windows_runner_admits(self):
        """A filter holding every character the Windows runner's -Filter admits, an Env value holding every character
        its -Env admits, and hang minutes of every digit, one to three of them: the launcher, the record and the test
        list as the runner writes them from inputs it accepts, and the check accepts each (the fifty-fifth review found
        a narrower reading of each noticed by no case)."""
        # And an empty value, which the runner admits and the child applies - clearing the variable (the fifty-seventh
        # review found an empty value held by no case).
        env = 'FILECAT_ARCHIVE_TXF_NATIVE=1;FILECAT_EMPTY=;%s;FILECAT_NOTES=%s' % (checker.GATE, WINDOWS_ENV_VALUE)
        record = {"RunId": "edit-test", "Os": 'Microsoft Windows 11 Pro 10.0.26220.0', "Env": env}
        for hang in ('9', '78', '120', '345', '600'):
            with self.subTest(hang=hang):
                contents = files()
                contents['machine.json'] = windows_json(record)
                contents['child.cmd'] = launcher(env, filter=WINDOWS_FILTER, hang=hang)
                contents['tests.txt'] = 'test FileCat.App.Tests-Edit: (%s)&(FullyQualifiedName~Edit)\r\n' % WINDOWS_FILTER
                problems, facts = self.check(contents)
                self.assertEqual(([], 'windows'), (problems, facts.get('system')))

    def test_accepts_every_character_the_unix_runner_admits(self):
        """A filter holding every character the Unix runner's filter admits, as the runner writes it into the test line
        beside a shard, and an env: entry whose value holds every character such a value admits: the record as the
        runner writes it from inputs it accepts, and the check accepts it (the fifty-fifth review found a test line's
        `&` - a filter beside a shard, a negated shard of two classes - and five digits in no accepted record's lines)."""
        system = 'Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux'
        notes = 'FILECAT_NOTES=' + UNIX_ENV_VALUE
        # A filter by display name holds `test ` and `packet=` inside its line, which the record's own rules read only
        # where a line begins; and an env: entry may give an empty value, which the runner exports as such (the
        # fifty-seventh review found each held by no case).
        expression = 'DisplayName~a test case|DisplayName~packet=1|' + UNIX_ENTRY
        lines = unix_head(system) + [
            'packet=546d8caf28f54066cbb58e5d222a9e5aab464899ab35568b4108e01fc8c982d4', 'filter=' + expression,
            'entry=env:FILECAT_ARCHIVE_TXF_NATIVE=1', 'entry=env:' + checker.GATE, 'entry=env:' + notes,
            'entry=env:FILECAT_EMPTY=', 'entry=hang:600', 'entry=tests/FileCat.App.Tests/FileCat.App.Tests.csproj#Edit',
            'env=FILECAT_ARCHIVE_TXF_NATIVE=1', 'env=' + checker.GATE, 'env=' + notes, 'env=FILECAT_EMPTY=', 'hang=600m',
            'test FileCat.App.Tests-Edit: (%s)&(FullyQualifiedName~Edit)' % expression]
        contents = files(system=system)
        contents['machine.txt'] = '\n'.join(lines) + '\n'
        problems, facts = self.check(contents, unix=True)
        self.assertEqual(([], 'linux'), (problems, facts.get('system')))

    def test_refuses_a_directory_that_understates_a_member_another_entry_covers(self):
        """A directory that understates a member's size ends that member's span short of its own bytes - and where those
        bytes are a member of their own, listed as well, nothing is left uncovered and nothing overlaps: the size the
        file states for the member is all that tells the short entry from it, plain or zip64 (the fifty-sixth review
        built it, against two notes that called the size rule redundant there)."""
        name = b'inner.txt'
        data = b'bytes no rule reads, inside another member. ' * 4
        crc = zlib.crc32(data)
        inner = struct.pack('<4sHHHHHIIIHH', b'PK' + bytes((3, 4)), 20, 0, 0, 0, 0, crc, len(data), len(data), len(name),
                            0) + name + data
        for zip64 in (False, True):
            with self.subTest(zip64=zip64), tempfile.TemporaryDirectory() as directory:
                path = archive(directory, 'edit-understated-evidence.zip', files())
                with zipfile.ZipFile(path, 'a') as zipped:
                    with zipped.open('zz-outer.bin', 'w', force_zip64=zip64) as handle:
                        handle.write(inner)
                entries = []
                with zipfile.ZipFile(path) as held, open(path, 'rb') as raw:
                    start = held.start_dir
                    for item in held.infolist():
                        if item.filename != 'zz-outer.bin':
                            entries.append((item, item.compress_size))
                            continue
                        raw.seek(item.header_offset + 26)
                        length, extra = struct.unpack('<HH', raw.read(4))
                        entries.append((item, 0))  # the outer member listed as holding nothing
                        inside = zipfile.ZipInfo('inner.txt')
                        inside.header_offset = item.header_offset + 30 + length + extra
                        inside.CRC, inside.file_size = crc, len(data)
                        entries.append((inside, len(data)))
                self.rewritten_directory(path, entries, at=start)
                problems, _ = checker.check(path)
                self.assertTrue(any("the entry 'zz-outer.bin' is 0 bytes long where the file says" in problem
                                    for problem in problems), problems)

    def test_refuses_a_summary_whose_ok_is_not_json_s_true(self):
        """Both runners write a summary's `ok` as JSON's true or false: one with no `ok`, a null one, or one saying 1
        or "true" was not written by them, and is not read as ok (the fifty-sixth review found only `false` in any
        case)."""
        missing = {key: value for key, value in SUMMARY.items() if key != 'ok'}
        for summary in (missing, dict(SUMMARY, ok=None), dict(SUMMARY, ok=1), dict(SUMMARY, ok='true')):
            with self.subTest(ok=summary.get('ok', 'missing')):
                self.assertIn('the runner did not call this run ok', self.check(files(summary=summary))[0])

    def test_reads_a_step_aside_reason_in_every_file_of_a_run(self):
        """Every log and results file of every test process is read for a reason a test stepped aside, the last of them
        too: a run of two processes holds four, and a reason in the second process's log - the last of them by name -
        is as much a step aside as one in the first (the fifty-sixth review found no case past the second file)."""
        reason = checker.STEP_ASIDE[0]
        for unix in (False, True):
            with self.subTest(unix=unix):
                contents = two_process_run(unix)
                contents['test-%s.log' % SECOND] = '[xUnit.net] %s\n' % reason
                problems, _ = self.check(contents, unix)
                self.assertTrue(any(problem.startswith('a test stepped aside in') and SECOND in problem
                                    for problem in problems), problems)

    def test_reads_a_unix_record_s_own_lines_past_what_the_machine_says(self):
        """The Unix runner writes what the machine says before its own lines - the `df -h` line of the run's folder
        among them, which begins with the file system's name - so a file system named `test`, as a tmpfs or a pool's
        root dataset can be, began that line `test `: read as a listed process, and its `%` as a character dotnet does
        not pass on, it refused an ordinary lane. The runner's own lines begin at its `packet=` line (the fifty-sixth
        review) - the line that begins so, not one that holds it, as a mount point may (the fifty-seventh)."""
        contents = files(system='Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux')
        lines = contents['machine.txt'].split('\n')
        df = next(index for index, line in enumerate(lines) if line.endswith('% /'))
        lines[df] = 'test            196G  103G   83G  56% /srv/packet=1'
        contents['machine.txt'] = '\n'.join(lines)
        problems, facts = self.check(contents, unix=True)
        self.assertEqual(([], 'linux'), (problems, facts.get('system')))

    def test_writes_each_report_line_as_one_line(self):
        """An archive's name comes from the command line, and a suite's or a pin's from the archive itself: a line break
        in one wrote a line of the report's own - an accepting verdict among them, which the release manifest reads by
        how a line begins and ends, with no archive read at all (the fifty-seventh review). Each line is written whole,
        what is not printable as its escape - every line break one reader or another ends a line at."""
        forged = ('edit-r35-win-evidence.zip: edit-storage evidence, 1369 passed, 429 skipped, no failure, %s in force, '
                  'windows, sha256 %s' % (checker.GATE, '0' * 64))
        breaks = ''.join(map(chr, (10, 13, 11, 12, 28, 29, 30, 0x85, 0x2028, 0x2029)))
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / 'report.txt'
            missing = Path(directory) / (forged + breaks + 'x')
            with contextlib.redirect_stdout(io.StringIO()):
                with unittest.mock.patch.object(sys, 'argv', ['check', str(missing), '--out', str(report)]):
                    self.assertEqual(1, checker.main())
            written = report.read_text(encoding='utf-8').splitlines()
        self.assertNotIn(forged, written)
        self.assertEqual(1, len(written))


if __name__ == '__main__':
    unittest.main()
