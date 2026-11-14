"""Unit checks for the native-edit-storage evidence check: what it accepts, and what it must refuse.

Run: python3 -m unittest discover -s eng/validation/archive-writing -p test_check_edit_storage_evidence.py -v
The real archives are produced by the three machines' edit lanes; these are the smallest archives of each shape.
"""

import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
import unittest.mock
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
          gate_value=None):
    """The four files the runners seal, as a name-to-text map; each argument stands for one way a run can differ."""
    if gate_value is not None:
        gate_text = checker.GATE_NAME + '=' + gate_value
    else:
        gate_text = checker.GATE if gate else 'FILECAT_EDIT_NATIVE_ROOT=/tmp/x'
    return {
        'machine.json': json.dumps({"RunId": "edit-test", "Env": 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + gate_text}),
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
        self.assertIn('no case of the pin %s ran: %s' % (pin, ['FileCat.Tests.' + pin]), problems)

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
        contents = files()
        del contents['machine.json']
        contents['machine.txt'] = 'packet=abc\nfilter=*\nenv=FILECAT_ARCHIVE_TXF_NATIVE=1\nenv=%s\n' % checker.GATE
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
        contents['other/summary.json'] = json.dumps(dict(SUMMARY, failed=['FileCat.Tests.Other'], ok=False))
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
