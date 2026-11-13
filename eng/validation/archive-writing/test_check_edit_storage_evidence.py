"""Unit checks for the native-edit-storage evidence check: what it accepts, and what it must refuse.

Run: python3 -m unittest discover -s eng/validation/archive-writing -p test_check_edit_storage_evidence.py -v
The real archives are produced by the three machines' edit lanes; these are the smallest archives of each shape.
"""

import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location("checker", Path(__file__).with_name("check_edit_storage_evidence.py"))
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)

PASSING_TRX = ''.join(
    '<UnitTestResult executionId="%d" testName="FileCat.Tests.%s" outcome="Passed" />' % (index, pin)
    for index, pin in enumerate(checker.PINS))
SUMMARY = {"ok": True, "failed": [], "suites": {"FileCat.App.Tests-Edit": {
    "total": 7, "passed": 7, "skipped": 0, "failed": 0, "counters_total": 7}}}


def files(gate=True, trx=PASSING_TRX, summary=None, log='[xUnit.net] everything ran\n', status='PASS'):
    """The four files the runners seal, as a name-to-text map; each argument stands for one way a run can differ."""
    env = 'FILECAT_ARCHIVE_TXF_NATIVE=1;' + (checker.GATE if gate else 'FILECAT_EDIT_NATIVE_ROOT=/tmp/x')
    return {
        'machine.json': json.dumps({"RunId": "edit-test", "Env": env}),
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

    def test_refuses_a_run_whose_machine_record_does_not_name_the_gate(self):
        problems, _ = self.check(files(gate=False))
        self.assertEqual(['its machine record does not name ' + checker.GATE], problems)

    def test_refuses_a_run_that_is_missing_a_pin(self):
        trx = ''.join(part for part in PASSING_TRX.split('/>') if checker.PINS[0] not in part)
        problems, _ = self.check(files(trx=trx))
        self.assertIn('it does not hold the pin ' + checker.PINS[0], problems)

    def test_refuses_a_run_whose_pin_did_not_pass(self):
        problems, _ = self.check(files(trx=PASSING_TRX.replace('outcome="Passed"', 'outcome="NotExecuted"', 1)))
        self.assertTrue(any(problem.startswith('the pin ') and 'not Passed' in problem for problem in problems), problems)

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
        summary = {"ok": False, "failed": ["FileCat.Tests.Something"], "suites": {"FileCat.App.Tests-Edit": {
            "total": 7, "passed": 6, "skipped": 0, "failed": 1, "counters_total": 7}}}
        problems, _ = self.check(files(summary=summary))
        self.assertIn('it failed: FileCat.Tests.Something', problems)
        self.assertIn('suite FileCat.App.Tests-Edit has 1 failures', problems)

    def test_refuses_a_run_whose_status_is_not_pass(self):
        problems, _ = self.check(files(status='FAIL'))
        self.assertIn('its status is not PASS', problems)

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


if __name__ == '__main__':
    unittest.main()
