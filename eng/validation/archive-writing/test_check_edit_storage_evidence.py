"""Unit checks for the native-edit-storage evidence check: what it accepts, and what it must refuse.

Run: python3 -m unittest discover -s eng/validation/archive-writing -p test_check_edit_storage_evidence.py -v
The real archives are produced by the three machines' edit lanes; these are the smallest archives of each shape.
"""

import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import struct
import tarfile
import tempfile
import unittest
import unittest.mock
import warnings
import zipfile

SPEC = importlib.util.spec_from_file_location("checker", Path(__file__).with_name("check_edit_storage_evidence.py"))
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)

PASSING_TRX = ''.join(
    '<UnitTestResult executionId="%d" testName="FileCat.Tests.%s" outcome="Passed" />' % (index, pin)
    for index, pin in enumerate(checker.PINS))
SUMMARY = {"ok": True, "failed": [], "exit_codes": {"FileCat.App.Tests-Edit": 0},
           "suites": {"FileCat.App.Tests-Edit": {"total": 7, "passed": 7, "skipped": 0, "failed": 0,
                                                 "counters_total": 7}}}


def files(gate=True, trx=PASSING_TRX, summary=None, log='[xUnit.net] everything ran\n', status='PASS',
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
    trx = trx.replace('FileCat.Tests.' + checker.PINS[2], platform + checker.PINS[2])
    return {
        'machine.json': json.dumps({"RunId": "edit-test", "Os": system,
                                    "Env": 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate_text}),
        'summary.json': json.dumps(SUMMARY if summary is None else summary),
        'status.txt': status,
        'results/FileCat.App.Tests-Edit.trx': '<?xml version="1.0"?><TestRun><Results>' + trx + '</Results></TestRun>',
        'test-FileCat.App.Tests-Edit.log': log,
    }


def archive(directory, name, contents, unix=False):
    path = Path(directory) / name
    if unix:
        with tarfile.open(path, 'w:gz') as tar:
            for leaf, text in contents.items():
                blob = text.encode('utf-8')
                item = tarfile.TarInfo('edit-test/' + leaf)
                item.size = len(blob)
                tar.addfile(item, io.BytesIO(blob))
    else:
        with zipfile.ZipFile(path, 'w') as zipped:
            for leaf, text in contents.items():
                zipped.writestr(leaf.replace('/', '\\'), text)
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
        pin = checker.PINS[2]
        trx = PASSING_TRX.replace('testName="FileCat.Tests.%s" outcome="Passed"' % pin,
                                  'testName="FileCat.Tests.%s" outcome="NotExecuted"' % pin)
        problems, _ = self.check(files(trx=trx))
        self.assertIn('no case of the pin %s ran: %s'
                      % (pin, ['FileCat.Platform.Windows.Tests.WindowsEditStorageTests.' + pin]), problems)

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
        summary = {"ok": False, "failed": ["FileCat.Tests.Something"], "exit_codes": {"FileCat.App.Tests-Edit": 1},
                   "suites": {"FileCat.App.Tests-Edit": {"total": 7, "passed": 6, "skipped": 0, "failed": 1,
                                                         "counters_total": 7}}}
        problems, _ = self.check(files(summary=summary))
        self.assertIn('it failed: FileCat.Tests.Something', problems)
        self.assertIn('suite FileCat.App.Tests-Edit has 1 failures', problems)
        self.assertIn('the runner did not call this run ok', problems)
        self.assertIn('suite FileCat.App.Tests-Edit exited 1', problems)

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
        for reason in checker.STEP_ASIDE:
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

    def rewritten_directory(self, path, entries):
        """The archive at `path` with a directory of its own listing only `entries`, as (ZipInfo, compressed size)."""
        blob = path.read_bytes()
        body = bytearray()
        for item, size in entries:
            name = item.filename.encode('utf-8')
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
        appended directory covered a whole sealed archive on paper. The local header states the size too."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, start = self.laid_out(directory)
            first = order[0][0]
            stretched = start - first.header_offset - (order[0][1] - first.compress_size)
            self.rewritten_directory(path, [(first, stretched)])
            problems, _ = checker.check(path)
            self.assertTrue(any('where the file says' in problem for problem in problems), problems)

    def test_refuses_a_listing_that_stops_short_of_its_directory(self):
        """Once the sizes have to agree, a forged directory can still leave an entry out - and those bytes are then in
        the file and in no member of it."""
        with tempfile.TemporaryDirectory() as directory:
            path, order, _ = self.laid_out(directory)
            self.rewritten_directory(path, [(item, item.compress_size) for item, _ in order[:-1]])
            problems, _ = checker.check(path)
            self.assertTrue(any('in no member of it' in problem for problem in problems), problems)

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
            for path in (wide, folders, streamed):
                with self.subTest(path.name):
                    with checker.Archive(path) as held:
                        self.assertIsNone(held.listing_gap())

    def test_refuses_a_tar_with_another_archive_before_it(self):
        """gzip reads joined members as one stream while tarfile stops at the first tar's end-of-archive blocks: an
        append is not read, but a sealed archive put after a forged one is passed over in favour of the forgery."""
        with tempfile.TemporaryDirectory() as directory:
            sealed = archive(directory, 'edit-sealed-evidence.tar.gz', files(), unix=True)
            forged = archive(directory, 'edit-forged-evidence.tar.gz', files(status='FAIL'), unix=True)
            joined = Path(directory) / 'edit-joined-evidence.tar.gz'
            joined.write_bytes(forged.read_bytes() + sealed.read_bytes())
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
        nothing saw it. It emptied a log that said a test had stepped aside, and the lane was accepted."""
        step = next(iter(checker.STEP_ASIDE))
        contents = files(log='[xUnit.net] ' + step + '\n')
        with tempfile.TemporaryDirectory() as directory:
            path = archive(directory, 'edit-shadowed-evidence.tar.gz', contents, unix=True)
            self.assertTrue(any(step in problem for problem in checker.check(path)[0]))
            name = next(n for n in checker.Archive(path).names() if n.split('/')[-1].startswith('test-'))
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
        Windows and this check Linux."""
        contents = files()
        contents['machine.json'] = ('{"RunId": "edit-test", "Os": "Microsoft Windows 11 Pro", "Env": "%s", '
                                    '"Os": "Linux h 6.0 x86_64 GNU/Linux"}' % checker.GATE)
        problems, _ = self.check(contents)
        self.assertIn('its machine record holds Os more than once, so what it says depends on who reads it', problems)

    def test_refuses_a_summary_that_names_no_suite(self):
        """Counters are what the report's numbers come from; a summary with none is not a run that passed."""
        problems, _ = self.check(files(summary=dict(SUMMARY, suites={})))
        self.assertIn('summary.json names no suite', problems)
        self.assertIn('its summary names no suite', problems)

    def test_refuses_an_archive_that_holds_a_name_more_than_once(self):
        """A reader gets the last member of a repeated name while every loop here lists each occurrence, so appending
        a clean copy - with no original byte rewritten - made a run that failed read as one that passed."""
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
