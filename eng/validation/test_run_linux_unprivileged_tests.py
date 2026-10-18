"""Portable unit checks for the isolated Linux validation runner's boundaries.

Run: python3 -m unittest discover -s eng/validation -p test_run_linux_unprivileged_tests.py -v
The actual UID/capability/permission checks are exercised by every integration run.
"""

import importlib.util
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET


SPEC = importlib.util.spec_from_file_location("runner", Path(__file__).with_name("run-linux-unprivileged-tests.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


def ordinary():
    return dict.fromkeys(runner.CAPABILITY_FIELDS, 0) | {
        "Uid": [65534] * 4, "Gid": [65534] * 4, "Groups": [], "NoNewPrivs": 1}


class IdentityTests(unittest.TestCase):
    def test_parses_kernel_status_without_inventing_missing_fields(self):
        text = "\n".join(key + ":\t0000000000000000" for key in runner.CAPABILITY_FIELDS)
        text += "\nUid:\t65534\t65534\t65534\t65534\nGid:\t65534\t65534\t65534\t65534\nGroups:\t\nNoNewPrivs:\t1\n"
        self.assertEqual(ordinary(), runner.parse_status(text))
        for field in ordinary():
            with self.subTest(field=field), self.assertRaises(KeyError):
                runner.parse_status("\n".join(line for line in text.splitlines() if not line.startswith(field + ":")))

    def test_accepts_only_the_fully_dropped_identity(self):
        runner.require_ordinary_identity(ordinary(), 65534, 65534)

    def test_rejects_root_requested_identity(self):
        for uid, gid in ((0, 65534), (65534, 0), (-1, 65534)):
            with self.subTest(uid=uid, gid=gid), self.assertRaises(ValueError):
                runner.require_ordinary_identity(ordinary(), uid, gid)

    def test_checks_real_effective_saved_and_filesystem_ids(self):
        for field in ("Uid", "Gid"):
            for index in range(4):
                snapshot = ordinary()
                snapshot[field][index] = 0
                with self.subTest(field=field, index=index), self.assertRaises(ValueError):
                    runner.require_ordinary_identity(snapshot, 65534, 65534)

    def test_wrong_or_truncated_ordinary_ids_also_fail_closed(self):
        for ids in ([1234] * 4, [65534] * 3, [65534] * 5):
            snapshot = ordinary() | {"Uid": ids}
            with self.subTest(ids=ids), self.assertRaises(ValueError):
                runner.require_ordinary_identity(snapshot, 65534, 65534)

    def test_every_capability_set_including_bounding_must_be_empty(self):
        for field in runner.CAPABILITY_FIELDS:
            with self.subTest(field=field), self.assertRaises(ValueError):
                runner.require_ordinary_identity(ordinary() | {field: 1}, 65534, 65534)

    def test_no_supplementary_group_is_retained(self):
        for groups in ([0], [65534], [100, 200]):
            with self.subTest(groups=groups), self.assertRaises(ValueError):
                runner.require_ordinary_identity(ordinary() | {"Groups": groups}, 65534, 65534)

    def test_no_new_privileges_is_mandatory(self):
        with self.assertRaises(ValueError):
            runner.require_ordinary_identity(ordinary() | {"NoNewPrivs": 0}, 65534, 65534)

    def test_permission_probe_requires_a_real_denial(self):
        def denied():
            raise PermissionError("denied by kernel")
        self.assertEqual("probe", runner.require_permission_denied(denied, "probe"))
        with self.assertRaises(RuntimeError):
            runner.require_permission_denied(lambda: None, "probe")

    def test_missing_file_is_not_misreported_as_a_permission_denial(self):
        def absent():
            raise FileNotFoundError("fixture missing")
        with self.assertRaises(FileNotFoundError):
            runner.require_permission_denied(absent, "probe")

    def test_environment_does_not_inherit_privilege_claims_or_live_device_opt_ins(self):
        hostile = {"SUDO_UID": "1000", "SUDO_GID": "1000", "SUDO_USER": "somebody",
                   "LD_PRELOAD": "/injected.so", "DOTNET_STARTUP_HOOKS": "/injected.dll",
                   "FILECAT_V09_UNIX_DEVICE": "/dev/sda", "FILECAT_REQUIRE_WEBKIT": "1"}
        with patch.dict(os.environ, hostile):
            environment = runner.child_environment(Path("/tmp/private-stage"))
        self.assertTrue(set(environment).isdisjoint(hostile))
        self.assertNotIn("HOME", environment)
        self.assertEqual("/tmp/private-stage/dotnet", environment["DOTNET_ROOT"])
        self.assertEqual("/tmp/private-stage/state/tmp", environment["TMPDIR"])
        self.assertEqual("/tmp/private-stage/state/runtime", environment["XDG_RUNTIME_DIR"])


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="filecat-runner-unit-")
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def report(self, outcomes, *, summary="Completed", execution_ids=None, **overrides):
        root = ET.Element("TestRun", xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
        results = ET.SubElement(root, "Results")
        for index, outcome in enumerate(outcomes):
            attributes = {"outcome": outcome}
            execution_id = str(index) if execution_ids is None else execution_ids[index]
            if execution_id is not None:
                attributes["executionId"] = execution_id
            ET.SubElement(results, "UnitTestResult", attributes)
        counts = {"total": len(outcomes), "executed": len(outcomes) - outcomes.count("NotExecuted"),
                  "passed": outcomes.count("Passed"), "failed": outcomes.count("Failed"),
                  "notExecuted": outcomes.count("NotExecuted")} | overrides
        ET.SubElement(ET.SubElement(root, "ResultSummary", outcome=summary), "Counters",
                      {key: str(value) for key, value in counts.items()})
        source = self.root / "results.trx"
        ET.ElementTree(root).write(source, encoding="utf-8", xml_declaration=True)
        return source

    def test_copies_complete_regular_evidence(self):
        source = self.root / "source.trx"
        source.write_bytes(b"test evidence\n" * 10000)
        destination = self.root / "retained.trx"
        runner.copy_evidence(source, destination)
        self.assertEqual(source.read_bytes(), destination.read_bytes())

    def test_does_not_overwrite_previous_evidence(self):
        source = self.root / "source.trx"
        destination = self.root / "retained.trx"
        source.write_bytes(b"new")
        destination.write_bytes(b"old")
        with self.assertRaises(FileExistsError):
            runner.copy_evidence(source, destination)
        self.assertEqual(b"old", destination.read_bytes())

    def test_refuses_a_result_link_without_reading_its_target(self):
        target = self.root / "private"
        target.write_bytes(b"not evidence")
        source = self.root / "source.trx"
        source.symlink_to(target)
        destination = self.root / "retained.trx"
        with self.assertRaises(OSError):
            runner.copy_evidence(source, destination)
        self.assertFalse(destination.exists())

    def test_refuses_a_fifo_without_waiting_for_a_writer(self):
        source = self.root / "source.trx"
        os.mkfifo(source)
        with self.assertRaises(ValueError):
            runner.copy_evidence(source, self.root / "retained.trx")

    def test_refuses_oversized_evidence_before_copying(self):
        source = self.root / "source.trx"
        source.write_bytes(b"12345")
        destination = self.root / "retained.trx"
        with patch.object(runner, "MAX_EVIDENCE_BYTES", 4), self.assertRaises(ValueError):
            runner.copy_evidence(source, destination)
        self.assertFalse(destination.exists())

    def test_accepts_exact_evidence_limit(self):
        source = self.root / "source.trx"
        source.write_bytes(b"1234")
        destination = self.root / "retained.trx"
        with patch.object(runner, "MAX_EVIDENCE_BYTES", 4):
            runner.copy_evidence(source, destination)
        self.assertEqual(b"1234", destination.read_bytes())

    def test_records_pass_fail_and_native_skip_counters(self):
        source = self.report(("Passed", "Passed", "Failed", "NotExecuted"), summary="Failed")
        result = runner.result_counters(source)
        self.assertEqual("Failed", result["outcome"])
        self.assertEqual({"total": 4, "executed": 3, "passed": 2, "failed": 1, "notExecuted": 1}, result["counters"])

    def test_xunit_omitted_aggregate_skips_are_counted_from_complete_distinct_records(self):
        source = self.report(("Passed", "NotExecuted"), notExecuted=0)
        result = runner.result_counters(source)
        self.assertEqual({"total": 2, "executed": 1, "passed": 1, "failed": 0, "notExecuted": 1}, result["counters"])
        self.assertEqual(0, result["reported_counters"]["notExecuted"])

    def test_completed_failing_xunit_run_retains_its_failure_and_omitted_skips(self):
        source = self.report(("Passed", "Failed", "NotExecuted"), summary="Failed", notExecuted=0)
        result = runner.result_counters(source)
        self.assertEqual("Failed", result["outcome"])
        self.assertEqual({"total": 3, "executed": 2, "passed": 1, "failed": 1, "notExecuted": 1}, result["counters"])

    def test_all_native_skips_are_accounted_without_claiming_executions(self):
        source = self.report(("NotExecuted", "NotExecuted"), notExecuted=0)
        result = runner.result_counters(source)
        self.assertEqual({"total": 2, "executed": 0, "passed": 0, "failed": 0, "notExecuted": 2}, result["counters"])

    def test_counter_mismatches_cannot_be_hidden_by_skip_records(self):
        for overrides in ({"total": 3}, {"passed": 2}, {"executed": 2}, {"notExecuted": 2}, {"failed": -1}):
            source = self.report(("Passed", "NotExecuted"), **overrides)
            with self.subTest(overrides=overrides), self.assertRaises(ValueError):
                runner.result_counters(source)

    def test_duplicate_or_missing_execution_ids_cannot_fabricate_completion(self):
        for identities in (("same", "same"), ("one", None), ("", "two")):
            source = self.report(("Passed", "Passed"), execution_ids=identities)
            with self.subTest(identities=identities), self.assertRaises(ValueError):
                runner.result_counters(source)

    def test_unfinished_or_unknown_record_outcomes_are_not_complete(self):
        for outcome in ("Pending", "InProgress", "Aborted", "Unknown"):
            source = self.report((outcome,))
            with self.subTest(outcome=outcome), self.assertRaises(ValueError):
                runner.result_counters(source)

    def test_failed_run_summary_is_preserved_even_if_every_retained_case_passed(self):
        source = self.report(("Passed", "Passed"), summary="Failed")
        self.assertEqual("Failed", runner.result_counters(source)["outcome"])

    def test_aggregate_success_without_individual_records_is_not_complete(self):
        source = self.report((), total=1, executed=1, passed=1)
        with self.assertRaises(ValueError):
            runner.result_counters(source)

    def test_empty_selection_or_incomplete_results_are_not_green(self):
        source = self.root / "results.trx"
        for total, executed in ((0, 0), (5, 3)):
            source.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                              '<ResultSummary outcome="Completed"><Counters total="' + str(total) + '" '
                              'executed="' + str(executed) + '" passed="0" failed="0" notExecuted="0" />'
                              '</ResultSummary></TestRun>')
            with self.subTest(total=total, executed=executed), self.assertRaises(ValueError):
                runner.result_counters(source)

    def test_missing_result_summary_is_not_green(self):
        source = self.root / "results.trx"
        source.write_text("<TestRun />")
        with self.assertRaises(ValueError):
            runner.result_counters(source)

    def test_staging_is_a_private_copy_with_no_set_id_bits(self):
        source = self.root / "original"
        source.mkdir()
        (source / "host").write_bytes(b"executable")
        (source / "data").write_bytes(b"original")
        (source / "host").chmod(0o6755)
        copied = self.root / "copied"
        with patch.object(os, "chown"):
            runner.copy_tree(source, copied, 65534, 65534, writable=True)
        self.assertEqual(0o700, stat.S_IMODE((copied / "host").stat().st_mode))
        self.assertEqual(0o600, stat.S_IMODE((copied / "data").stat().st_mode))
        (copied / "data").write_bytes(b"changed by test")
        self.assertEqual(b"original", (source / "data").read_bytes())
        self.assertNotEqual((source / "data").stat().st_ino, (copied / "data").stat().st_ino)

    def test_staging_refuses_links_back_to_the_original_or_outside_tree(self):
        source = self.root / "original"
        source.mkdir()
        (source / "data").write_bytes(b"original")
        (source / "link").symlink_to(source / "data")
        with patch.object(os, "chown"), self.assertRaises(ValueError):
            runner.copy_tree(source, self.root / "copied", 65534, 65534, writable=True)
        self.assertEqual(b"original", (source / "data").read_bytes())

    def test_staging_keeps_contained_relative_links_inside_the_copy(self):
        source = self.root / "original"
        source.mkdir()
        (source / "data").write_bytes(b"original")
        (source / "link").symlink_to("data")
        copied = self.root / "copied"
        with patch.object(os, "chown"):
            runner.copy_tree(source, copied, 65534, 65534, writable=True)
        self.assertEqual(copied / "data", (copied / "link").resolve())


if __name__ == "__main__":
    unittest.main()
