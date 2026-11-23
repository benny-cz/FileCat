"""Safety and semantics checks; fixtures are retained, never deleted."""
from datetime import datetime, timedelta, timezone
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
import uuid

ENG = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ENG))
import evidence_retention as report
from evidence_retention_native import WindowsMetadataIO

NOW = datetime(2026, 11, 16, tzinfo=timezone.utc)


def run_manifest(**changes):
    data = dict(schema_version=1, run_id="test-run", state="completed",
                preservation_hold=False, outcome="success", retention_class="routine_success",
                completed_utc=(NOW - timedelta(days=100)).isoformat())
    data.update(changes)
    return data


def policy(source, output):
    return dict(schema_version=1, mode="report_only", automatic_cleanup=False,
                repository_root=str(source), report_parent=str(output),
                roots=[dict(id="fixture", path=str(source), role="evidence")],
                archive_packages=[], expected_keepers=0, protected_prefixes=[],
                reference_directories=[], duplicate_minimum_bytes=1024,
                retention_days=dict(staging=2, routine_success=90, closed_failure=180),
                volume_limits={}, proposed_limits_only={})


class Semantics(unittest.TestCase):
    def test_unknown_and_incomplete_stay_held(self):
        for run in (None, {}, run_manifest(state="running"), run_manifest(preservation_hold=True)):
            self.assertEqual(report.retention(run, NOW)["action"], "hold")

    def test_failed_run_cannot_masquerade_as_success(self):
        self.assertEqual(report.retention(run_manifest(outcome="failure"), NOW)["action"], "hold")

    def test_pin_overrides_old_age(self):
        self.assertFalse(report.retention(run_manifest(preservation_hold=True), NOW)["review_due"])

    def test_untriaged_failure_is_held(self):
        data = run_manifest(retention_class="closed_failure", outcome="failure",
                            issue_closed_utc=(NOW-timedelta(days=200)).isoformat())
        self.assertEqual(report.retention(data, NOW)["action"], "hold")
        self.assertTrue(report.retention(dict(data, triaged=True), NOW)["review_due"])

    def test_staging_requires_explicit_recent_hold_release(self):
        data = run_manifest(retention_class="staging")
        self.assertEqual(report.retention(data, NOW)["action"], "hold")
        self.assertTrue(report.retention(dict(data, recent_materialization_hold=False), NOW)["review_due"])

    def test_missing_timezone_or_future_completion_is_held(self):
        for value in ("2020-01-01", (NOW+timedelta(days=1)).isoformat(), "invalid"):
            self.assertEqual(report.retention(run_manifest(completed_utc=value), NOW)["action"], "hold")

    def test_report_never_authorizes_deletion(self):
        for kind in ("staging", "routine_success", "closed_failure", "release", "pinned"):
            self.assertIs(report.retention(run_manifest(retention_class=kind), NOW)["deletion_authorized"], False)

    def test_configured_retention_is_used(self):
        self.assertFalse(report.retention(run_manifest(), NOW, {"routine_success": 365})["review_due"])

    def test_prefixes_do_not_match_neighbor_names(self):
        p = report.Prefixes()
        p.add(r"V:\evidence\run", "hold")
        self.assertEqual(p.get(r"v:\EVIDENCE\run\proof"), "hold")
        self.assertIsNone(p.get(r"V:\evidence\runnable\proof"))
        self.assertTrue(p.covers_subtree(r"V:\evidence"))
        self.assertFalse(p.covers_subtree(r"V:\elsewhere"))

    def test_apply_mode_is_rejected(self):
        p = policy(Path(r"E:\source"), Path(r"E:\reports"))
        with self.assertRaises(ValueError):
            report.validate_policy(dict(p, mode="cleanup"))
        with self.assertRaises(ValueError):
            report.validate_policy(dict(p, automatic_cleanup=True))

    def test_overlapping_roots_are_rejected(self):
        p = policy(Path(r"E:\source"), Path(r"E:\reports"))
        p["roots"].append(dict(id="alias", path=r"E:\source\child", role="evidence"))
        with self.assertRaises(ValueError):
            report.validate_policy(p)

    def test_destructive_cli_flag_is_not_available(self):
        result = subprocess.run([sys.executable, "-B", str(ENG/"evidence_retention.py"), "--delete"],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("unrecognized arguments", result.stderr)


@unittest.skipUnless(os.name == "nt", "Native scanner is Windows-only")
class NativeSafety(unittest.TestCase):
    def setUp(self):
        base = ENG.parent/".tmp"/"evidence-retention-tool-tests"/uuid.uuid4().hex
        base.mkdir(parents=True)
        self.base = base
        self.source, self.outputs = base/"source", base/"reports"
        self.source.mkdir()
        self.io = WindowsMetadataIO()

    def test_directory_enumeration_and_hash_preserve_metadata(self):
        target = self.source/"data.bin"
        target.write_bytes(b"evidence"*8192)
        before_file = self.io.stat(target)
        before_dir = self.io.stat(self.source)
        rows = list(self.io.entries(self.source))
        self.assertEqual(self.io.stat(self.source), before_dir)
        self.assertEqual(rows[0]["file_id"], before_file["file_id"])
        self.assertEqual(rows[0]["allocation"], before_file["allocation"])
        self.assertEqual(self.io.digest(target), hashlib.sha256(b"evidence"*8192).hexdigest())
        # Windows path lookups may advance parent-directory access times;
        # test the enumeration handle and file-read handle separately.
        self.assertEqual(self.io.stat(target), before_file)

    def test_output_cannot_enter_protected_evidence(self):
        config = policy(self.source, self.outputs)
        config["protected_prefixes"] = [str(self.outputs)]
        with self.assertRaises(ValueError):
            report.validate_output(self.outputs/"new", config)
        self.assertFalse((self.outputs/"new").exists())

    def test_complete_inventory_keeps_sources_and_pins_old_run(self):
        config = policy(self.source, self.outputs)
        config["protected_prefixes"] = [str(self.source)]
        (self.source/"evidence-run.json").write_text(json.dumps(run_manifest()), encoding="utf-8")
        data = self.source/"same-size.bin"
        data.write_bytes(b"x"*8192)
        before_dir, before_file = self.io.stat(self.source), self.io.stat(data)
        tool = report.Inventory(config, self.outputs/"run", self.io, now=NOW)
        summary = tool.execute()
        self.assertEqual(summary["totals"]["files"], 2)
        self.assertEqual(summary["runs_due_for_review"], 0)
        self.assertEqual(summary["files_deleted"], 0)
        self.assertEqual(self.io.stat(data), before_file)
        self.assertEqual(self.io.stat(self.source), before_dir)
        with gzip.open(self.outputs/"run"/"inventory.jsonl.gz", "rt") as f:
            rows = [json.loads(line) for line in f]
        self.assertTrue(all(row["retention"]["action"]=="hold" for row in rows))

    def test_existing_report_is_not_overwritten(self):
        output = self.outputs/"existing"
        output.mkdir(parents=True)
        marker = output/"owner.txt"
        marker.write_text("keep")
        with self.assertRaises(ValueError):
            report.Inventory(policy(self.source, self.outputs), output, self.io)
        self.assertEqual(marker.read_text(), "keep")

    def test_junction_is_not_followed(self):
        outside = self.base/"outside"
        outside.mkdir()
        (outside/"private.bin").write_bytes(b"untouched")
        junction = self.source/"junction"
        created = subprocess.run(["cmd.exe", "/d", "/c", "mklink", "/J", str(junction), str(outside)],
                                 capture_output=True, text=True)
        self.assertEqual(created.returncode, 0, created.stderr)
        result = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io).execute()
        self.assertEqual(result["totals"]["files"], 0)
        self.assertEqual(result["exclusions"]["reparse_not_followed"], 1)

    def test_hardlink_aliases_are_not_extra_physical_bytes(self):
        first, second = self.source/"first.bin", self.source/"second.bin"
        first.write_bytes(b"x"*8192)
        os.link(first, second)
        allocation = self.io.stat(first)["allocation"]
        result = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io).execute()
        self.assertEqual(result["totals"]["files"], 2)
        self.assertEqual(result["totals"]["unique_physical_files"], 1)
        self.assertEqual(result["totals"]["allocated_file_data_bytes"], allocation)
        self.assertEqual(result["totals"]["alias_file_entries"], 1)

    def test_incomplete_run_remains_held_and_optional_hashes_skip_it(self):
        (self.source/"evidence-run.json").write_text(json.dumps(run_manifest(state="running")), encoding="utf-8")
        for name in ("a.bin", "b.bin"):
            (self.source/name).write_bytes(b"x"*8192)
        tool = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io,
                                now=NOW, hash_budget=100000)
        result = tool.execute()
        self.assertEqual(result["duplicate_assessment"]["fresh_duplicate_groups"], 0)
        self.assertEqual(result["runs_due_for_review"], 0)

    def test_declared_closed_duplicates_are_hashed_only_with_budget(self):
        (self.source/"evidence-run.json").write_text(json.dumps(run_manifest()), encoding="utf-8")
        for name in ("a.bin", "b.bin"):
            (self.source/name).write_bytes(b"x"*8192)
        tool = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io,
                                now=NOW, hash_budget=20000)
        result = tool.execute()
        self.assertEqual(result["duplicate_assessment"]["fresh_duplicate_groups"], 1)
        self.assertEqual(result["duplicate_assessment"]["verified_reclaimable_bytes"], 0)
        self.assertTrue((self.source/"a.bin").exists())
        self.assertTrue((self.source/"b.bin").exists())

    def test_manifest_cannot_claim_another_scope(self):
        manifest = run_manifest(scope_root=str(self.base/"outside"))
        (self.source/"evidence-run.json").write_text(json.dumps(manifest), encoding="utf-8")
        (self.source/"keep.bin").write_bytes(b"keep")
        result = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io, now=NOW).execute()
        self.assertEqual(result["runs_due_for_review"], 0)
        self.assertIn("invalid_run_manifest", result["issue_counts"])

    def test_nonobject_manifest_is_held(self):
        (self.source/"evidence-run.json").write_text("[]", encoding="utf-8")
        result = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io, now=NOW).execute()
        self.assertEqual(result["runs_due_for_review"], 0)
        self.assertIn("invalid_run_manifest", result["issue_counts"])

    def test_present_reference_overrides_historical_retirement(self):
        target = self.source/"proof.bin"
        target.write_bytes(b"present")
        tool = report.Inventory(policy(self.source, self.outputs), self.outputs/"scan", self.io, now=NOW)
        key = report.normalized(target)
        tool.references[key] = dict(path=str(target), documents=["example.md"])
        tool.observed_prefixes.add(key)
        tool.confirmed_retired.add(key)
        tool.archived_paths[key] = dict(archive_entry="/proof.bin")
        self.assertEqual(tool.reference_report()[0]["state"], "present_in_inventory")


if __name__ == "__main__":
    unittest.main()
