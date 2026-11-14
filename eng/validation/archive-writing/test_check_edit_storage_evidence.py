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
        # release manifest binds a route by - with each archive's own digest written as such, since a fixture's bytes
        # differ from run to run.
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
    "The fixture's filesystem cannot be established here.",
    'is not a listed native edit storage route for',
)
PASSING_TRX = ''.join(
    '<UnitTestResult executionId="%d" testName="FileCat.Tests.%s" outcome="Passed" />' % (index, pin)
    for index, pin in enumerate(PIN_NAMES))
SUMMARY = {"ok": True, "failed": [], "exit_codes": {"FileCat.App.Tests-Edit": 0},
           "suites": {"FileCat.App.Tests-Edit": {"total": 7, "passed": 7, "skipped": 0, "failed": 0,
                                                 "counters_total": 7}}}


def files(gate=True, trx=PASSING_TRX, summary=None, log='[xUnit.net] everything ran\n', status='PASS\r\n',
          gate_value=None, system='Microsoft Windows 11 Pro 10.0.26220.0'):
    """The four files the runners seal, as a name-to-text map; each argument stands for one way a run can differ."""
    if gate_value is not None:
        gate_text = checker.GATE_NAME + '=' + gate_value
    else:
        gate_text = checker.GATE if gate else 'FILECAT_EDIT_NATIVE_ROOT=/tmp/x'
    # The dual-root pin is one method in two real classes, and the check reads the class that passed it: a fixture
    # naming neither is not a run of these suites, so it carries the class this fixture's own system implies.
    platform = ('FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' if system.startswith(('Microsoft Windows', 'Windows'))
                else 'FileCat.Core.Tests.UnixEditStorageTests.')
    trx = trx.replace('FileCat.Tests.' + PIN_NAMES[2], platform + PIN_NAMES[2])
    return {
        'machine.json': json.dumps({"RunId": "edit-test", "Os": system,
                                    "Env": 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate_text}),
        'summary.json': json.dumps(SUMMARY if summary is None else summary),
        'status.txt': status,
        'results/FileCat.App.Tests-Edit.trx': '<?xml version="1.0"?><TestRun><Results>' + trx + '</Results></TestRun>',
        'test-FileCat.App.Tests-Edit.log': log,
    }


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
    try:
        env = json.loads(contents['machine.json']).get('Env')
    except (ValueError, AttributeError):
        return contents
    if not isinstance(env, str):
        return contents
    return dict(contents, **{'child.cmd': launcher(env)})


def sealed(leaf, text, unix):
    """A member's bytes as its runner writes them: the Unix runner's status ends in a newline; the Windows runner's in
    a carriage return and a newline, and its console logs - Windows PowerShell 5.1's `*>` - are UTF-16 with a mark."""
    if leaf == 'status.txt' and unix:
        text = text.replace('\r\n', '\n')
    if leaf.split('/')[-1].startswith('test-') and not unix:
        return bytes((0xFF, 0xFE)) + text.encode('utf-16-le')
    return text.encode('utf-8')


def archive(directory, name, contents, unix=False):
    contents = with_child_cmd(contents)
    path = Path(directory) / name
    if unix:
        with tarfile.open(path, 'w:gz') as tar:
            for leaf, text in contents.items():
                blob = sealed(leaf, text, unix)
                item = tarfile.TarInfo('edit-test/' + leaf)
                item.size = len(blob)
                tar.addfile(item, io.BytesIO(blob))
    else:
        with zipfile.ZipFile(path, 'w') as zipped:
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
        class says "Run the Windows native storage lane." while the Windows platform suite's own class passes."""
        pin = checker.PINS[2]
        trx = (PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin) +
               '<UnitTestResult testName="FileCat.Core.Tests.UnixEditStorageTests.%s" outcome="NotExecuted" />' % pin)
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
        del contents['machine.json']
        contents['machine.txt'] = ('Linux filecat-ubuntu2404 7.0.0-43-generic #43 SMP x86_64 GNU/Linux\n'
                                   'packet=abc\nfilter=*\nenv=FILECAT_ARCHIVE_TXF_NATIVE=1\nenv=%s\n' % checker.GATE)
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
        """The step-aside scan reads those logs: without them it is not a check that passed."""
        contents = files()
        del contents['test-FileCat.App.Tests-Edit.log']
        problems, _ = self.check(contents)
        self.assertEqual(['it carries no test output to read'], problems)

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
            del contents['machine.json']
            contents['machine.txt'] = first + '\nenv=%s\n' % checker.GATE
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
        the run's results, so the run says the family too, and the two have to agree."""
        pin = checker.PINS[2]
        windows = (PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin) +
                   '<UnitTestResult testName="FileCat.Core.Tests.UnixEditStorageTests.%s" outcome="NotExecuted" />' % pin)
        unix = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Core.Tests.UnixEditStorageTests.' + pin)
        self.assertEqual([], self.check(files(trx=windows))[0])  # its record says Windows, as that run's does
        problems, _ = self.check(files(trx=windows, system='Linux host 6.0.0 x86_64 GNU/Linux'))
        self.assertIn('its machine record says linux while its own dual-root pin passed in the Windows platform '
                      "suite's class, so the record and the run disagree about which system ran it", problems)
        problems, _ = self.check(files(trx=unix, system='Darwin host 27.0.0 arm64'))
        self.assertEqual([], problems)  # a Unix run and a Unix record agree, whichever Unix it is
        problems, _ = self.check(files(trx=unix))
        self.assertIn("its machine record says windows while its own dual-root pin passed in the Core suite's Unix "
                      'class, so the record and the run disagree about which system ran it', problems)

    def test_reads_the_family_past_a_third_class_that_also_passed_the_pin(self):
        """Read as a set of labels, a third class passing the dual-root pin silenced the agreement rule altogether:
        one added row had this check accept the real Windows archive as Linux evidence. Each class is asked for on its
        own now, so a third one is ignored and the two known ones are read as they were."""
        pin = checker.PINS[2]
        windows = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin)
        shared = '<UnitTestResult testName="FileCat.Core.Tests.EditStorageSharedTests.%s" outcome="Passed" />' % pin
        self.assertEqual([], self.check(files(trx=windows + shared))[0])
        problems, _ = self.check(files(trx=windows + shared, system='Linux host 6.0.0 x86_64 GNU/Linux'))
        self.assertIn('its machine record says linux while its own dual-root pin passed in the Windows platform '
                      "suite's class, so the record and the run disagree about which system ran it", problems)

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
        with tempfile.TemporaryDirectory() as directory:
            path, order, start = self.laid_out(directory, contents)
            renamed = {index: 'notes.bin' for index, (item, _) in enumerate(order)
                       if item.filename.endswith('Core.Tests-Edit.log')}
            self.rewritten_directory(path, [(item, item.compress_size) for item, _ in order], at=start, names=renamed)
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
        record went unchallenged."""
        pin = checker.PINS[2]
        trx = PASSING_TRX.replace('FileCat.Tests.' + pin, 'FileCat.Core.Tests.UnixEditStorageTestsV2.' + pin)
        problems, _ = self.check(files(trx=trx, system='Linux host 6.0.0 x86_64 GNU/Linux'))
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
        del contents['machine.json']
        contents['machine.txt'] = ('Darwin host 27.0.0 Darwin Kernel Version 27.0.0 arm64\n'
                                   'Linux-compat layer: off\nenv=%s\n' % checker.GATE)
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
        del contents['machine.json']
        contents['machine.txt'] = 'Linux host 7.0.0-43-generic #43 SMP x86_64 GNU/Linux\n' + '\n'.join(lines) + '\n'
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
            found = self.tar_called('bsdtar')
            if found is not None:
                tree = Path(directory) / 'run'
                tree.mkdir()
                (tree / 'empty.txt').write_bytes(b'')
                (tree / 'a.txt').write_text('x', encoding='utf-8')
                done = subprocess.run([found, '--format', 'zip', '--options', 'zip:zip64', '-cf', 'wide.zip', 'run'],
                                      cwd=directory, capture_output=True)
                if done.returncode == 0:
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
        a line break - and machine.json is written in the ANSI code page with best-fit, so a fullwidth semicolon in a
        value reads there as a real one while the child was given one variable. The record is trusted only as plain
        printable ASCII without %, ! or ^, and only where the sealed child.cmd - the runner's own launcher, read whole -
        passes exactly it."""
        gate = checker.GATE_NAME
        for env, said in (('FILECAT_ARCHIVE_TXF_NATIVE=100%;' + gate + '=1', "holds '%'"),
                          ('FILECAT_ARCHIVE_TXF_NATIVE=1\r\nX;' + gate + '=1', "holds '\\r'")):
            with self.subTest(env):
                problems, _ = self.check(self.windows_record(env))
                self.assertTrue(any(said in problem for problem in problems), problems)
        recorded = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate + '=1'
        for script, said in ((launcher(recorded.replace(';', chr(0xFF1B))), 'is not the launcher its runner writes'),
                             (launcher('FILECAT_ARCHIVE_TXF_NATIVE=1'),
                              "its child.cmd passed 'FILECAT_ARCHIVE_TXF_NATIVE=1' where its machine record holds Env"),
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
        problems, facts = self.check(contents)
        self.assertEqual([], problems)
        self.assertEqual('macos', facts['system'])

    def test_reads_the_gate_s_name_whole_and_not_as_a_prefix_of_another(self):
        """`FILECAT_EDIT_NATIVE_REQUIRED_LEGACY=1` is not this gate, and a name matched as a part would take it."""
        contents = files()
        contents['machine.json'] = json.dumps({"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro",
                                               "Env": checker.GATE_NAME + '_LEGACY=1'})
        self.assertIn('its machine record does not name ' + checker.GATE_NAME, self.check(contents)[0])

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
                    path = archive(directory, 'edit-system-evidence.zip', files(system=system))
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


if __name__ == '__main__':
    unittest.main()
