"""Inventory and retention advice only. This program has no cleanup mode.

Run with --help. Outputs are create-new under the configured report directory.
Unknown ownership, incomplete runs, references and preservation holds always
prevent expiry advice. Advice never authorizes deletion or source changes.
"""
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timedelta, timezone
import gzip
import hashlib
import json
import os
from pathlib import Path, PureWindowsPath
import re
import sys
import uuid

from evidence_retention_native import WindowsMetadataIO


def utcnow():
    return datetime.now(timezone.utc)


def normalized(path):
    return str(PureWindowsPath(str(path))).rstrip("\\").casefold()


def within(path, root):
    a, b = normalized(path), normalized(root)
    return a == b or a.startswith(b + "\\")


class Prefixes:
    def __init__(self):
        self.tree = {}

    def add(self, path, value):
        node = self.tree
        for part in PureWindowsPath(normalized(path)).parts:
            node = node.setdefault(part, {})
        node[None] = value

    def get(self, path):
        node, result = self.tree, None
        for part in PureWindowsPath(normalized(path)).parts:
            if part not in node:
                break
            node = node[part]
            result = node.get(None, result)
        return result

    def covers_subtree(self, path):
        if self.get(path):
            return True
        node = self.tree
        for part in PureWindowsPath(normalized(path)).parts:
            if part not in node:
                return False
            node = node[part]
        return bool(node)


def timestamp(value):
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None:
        raise ValueError("A completion timestamp must include its timezone")
    return result.astimezone(timezone.utc)


def retention(run, now, limits=None):
    """Conservative review advice, never a deletion decision."""
    held = dict(review_due=False, deletion_authorized=False, action="hold")
    if not run:
        return dict(held, reason="unclassified_legacy")
    if run.get("preservation_hold") is not False:
        return dict(held, reason="preservation_hold_or_unspecified")
    if run.get("state") != "completed":
        return dict(held, reason="active_incomplete_or_untriaged")
    kind = run.get("retention_class")
    days = (limits or {"staging": 2, "routine_success": 90, "closed_failure": 180}).get(kind)
    if days is None:
        return dict(held, reason="pinned_or_unknown_class")
    if kind in ("staging", "routine_success") and run.get("outcome") != "success":
        return dict(held, reason="successful_outcome_not_declared")
    if kind == "closed_failure" and (run.get("outcome") != "failure" or run.get("triaged") is not True):
        return dict(held, reason="failure_not_triaged")
    try:
        completed = timestamp(run["completed_utc"])
        if kind == "closed_failure":
            completed = timestamp(run["issue_closed_utc"])
        if completed > now:
            raise ValueError("Future completion time")
    except (ValueError, TypeError, KeyError):
        return dict(held, reason="invalid_or_missing_completion")
    age = (now - completed).total_seconds() / 86400
    if kind == "staging" and run.get("recent_materialization_hold") is not False:
        return dict(held, reason="recent_materialization_hold_or_unspecified", age_days=age)
    # Even a complete declaration is not an independent validation of archive,
    # backup, reference graph, restore, current hashes or active use.
    due = age >= days
    return dict(review_due=due, deletion_authorized=False,
                action="review" if due else "retain",
                reason="age_review_requires_fresh_safety_checks" if due else "within_retention",
                age_days=round(age, 3), retention_days=days,
                archive_and_backup_claimed=bool(run.get("archive_receipt") and run.get("backup_receipt")),
                archive_backup_and_restore_validated=False)


def validate_policy(policy):
    if policy.get("schema_version") != 1 or policy.get("mode") != "report_only":
        raise ValueError("Only policy schema 1 in report_only mode is supported")
    if policy.get("automatic_cleanup") is not False:
        raise ValueError("Automatic cleanup must be explicitly false")
    roots = policy.get("roots", [])
    if not roots or len({normalized(r["path"]) for r in roots}) != len(roots):
        raise ValueError("Inventory roots must be explicit and unique")
    for root in roots:
        path = PureWindowsPath(root["path"])
        if not path.is_absolute() or ".." in path.parts:
            raise ValueError("Inventory roots must be absolute, without traversal")
    for i, root in enumerate(roots):
        for other in roots[i + 1:]:
            if within(root["path"], other["path"]) or within(other["path"], root["path"]):
                raise ValueError("Overlapping roots would double count evidence")
    if not PureWindowsPath(policy["report_parent"]).is_absolute():
        raise ValueError("Report parent must be absolute")
    for kind, days in policy.get("retention_days", {}).items():
        if kind not in ("staging", "routine_success", "closed_failure") or type(days) is not int or days < 1:
            raise ValueError("Retention days must be positive integers for known routine classes")


def validate_output(output, policy):
    if not within(output, policy["report_parent"]) or normalized(output) == normalized(policy["report_parent"]):
        raise ValueError("Output must be a new child of report_parent")
    for path in policy.get("protected_prefixes", []):
        if within(output, path):
            raise ValueError("Output may not enter protected evidence")
    for package in policy.get("archive_packages", []):
        if within(output, package["root"]):
            raise ValueError("Output may not enter sealed archives")
    if output.exists():
        raise ValueError("Output already exists; reports never overwrite files")
    # Resolve the existing parent, detecting junctions/symlinks before creating
    # a new report. The default parent is deliberately under workspace .tmp.
    parent = output.parent
    current = parent
    while not current.exists():
        current = current.parent
    if normalized(current.resolve(strict=True)) != normalized(current):
        raise ValueError("Output ancestry traverses a reparse point")


def read_json(io, path, maximum=64 * 1024 * 1024):
    return json.loads(io.read_control(path, maximum).decode("utf-8-sig"))


class Inventory:
    def __init__(self, policy, output, io, now=None, hash_budget=0):
        validate_policy(policy)
        validate_output(output, policy)
        self.policy, self.output, self.io = policy, output, io
        self.now = now or utcnow()
        self.hash_budget = hash_budget
        self.protected, self.references = Prefixes(), {}
        self.core, self.keepers, self.package_names = {}, set(), {}
        self.archived_paths, self.confirmed_retired = {}, set()
        self.issues, self.excluded, self.run_manifests = [], [], []
        self.totals = dict(files=0, directories=0, namespace_logical_bytes=0,
                           unique_physical_files=0, allocated_file_data_bytes=0,
                           alias_file_entries=0)
        self.by_root, self.by_bucket = defaultdict(Counter), defaultdict(Counter)
        self.seen, self.size_groups = {}, {}
        self.observed_prefixes = set()
        self.source_content_hashes = {}
        self.protected_checks = []
        self.progress_at = 0
        self.age_bands = Counter()
        self.quota_bytes = Counter()
        self.scan_started = None
        self.legacy_state_reads = 0
        for path in policy.get("protected_prefixes", []):
            self.protected.add(path, "explicit_preservation_hold")

    def issue(self, kind, path, error, critical=False):
        self.issues.append(dict(kind=kind, path=str(path), detail=str(error), critical=critical))

    def load_protection(self):
        for package in self.policy.get("archive_packages", []):
            root = Path(package["root"])
            self.protected.add(root, "sealed_archive_package")
            raw = self.io.read_control(root / "FINAL-PACKAGE-SHA256.txt")
            if hashlib.sha256(raw).hexdigest() != package["manifest_sha256"]:
                raise ValueError("Sealed package manifest mismatch: " + str(root))
            names = {}
            for line in raw.decode("utf-8").splitlines():
                digest, name = line.split("  ", 1)
                rel = PureWindowsPath(name)
                if rel.is_absolute() or ".." in rel.parts or name in names or not re.fullmatch("[a-f0-9]{64}", digest):
                    raise ValueError("Invalid package manifest member")
                names[name] = digest
            if len(names) != package["sealed_content_files"]:
                raise ValueError("Sealed package membership count mismatch")
            names["FINAL-PACKAGE-SHA256.txt"] = package["manifest_sha256"]
            self.package_names[package["id"]] = names
            for name, digest in names.items():
                self.core[str(root / name)] = digest
            if package["id"] == self.policy.get("keeper_package_id"):
                data = self.io.read_control(root / "ARCHIVE-RECEIPT.json")
                if hashlib.sha256(data).hexdigest() != names["ARCHIVE-RECEIPT.json"]:
                    raise ValueError("Keeper receipt hash mismatch")
                receipt = json.loads(data)
                groups = receipt["group_results"]
                if receipt["status"] != "PASS" or len(groups) != self.policy["expected_keepers"]:
                    raise ValueError("Keeper receipt is not the verified group set")
                for group in groups:
                    path = group["canonical_original_retained"]
                    if not any(within(path, r["path"]) for r in self.policy["roots"]) or \
                       ".." in PureWindowsPath(path).parts or not PureWindowsPath(path).is_absolute():
                        raise ValueError("Keeper lies outside explicit inventory roots")
                    self.keepers.add(normalized(path))
                    self.protected.add(path, "explicit_keeper")
                    self.core[path] = group["sha256"]
                raw_map = self.io.read_control(root / "catalog" / "archive-path-map.jsonl")
                if hashlib.sha256(raw_map).hexdigest() != names["catalog/archive-path-map.jsonl"]:
                    raise ValueError("Archive path catalog mismatch")
                for line in raw_map.decode().splitlines():
                    row = json.loads(line)
                    self.archived_paths[normalized(row["original_path"])] = dict(
                        archive_filename=row["archive_filename"], archive_entry=row["archive_path"],
                        content_sha256=row["blob_sha256"], primary_package=str(root))
        if len(self.keepers) != self.policy.get("expected_keepers", 0):
            raise ValueError("Expected keeper set is incomplete")
        retired = self.policy.get("retirement_packet")
        if retired:
            root = Path(retired["root"])
            self.protected.add(root, "retirement_custody_receipt")
            raw = self.io.read_control(root / "RECEIPT-SHA256.txt")
            if hashlib.sha256(raw).hexdigest() != retired["manifest_sha256"]:
                raise ValueError("Retirement receipt manifest mismatch")
            pins = dict(line.split("  ", 1)[::-1] for line in raw.decode().splitlines())
            receipt_bytes = self.io.read_control(root / "RETIREMENT-RECEIPT.json")
            paths_bytes = self.io.read_control(root / "retirement-paths.jsonl")
            if hashlib.sha256(receipt_bytes).hexdigest() != pins["RETIREMENT-RECEIPT.json"] or \
               hashlib.sha256(paths_bytes).hexdigest() != pins["retirement-paths.jsonl"]:
                raise ValueError("Retirement custody records do not match their seal")
            receipt = json.loads(receipt_bytes)
            rows = [json.loads(s) for s in paths_bytes.decode().splitlines()]
            if receipt["status"] != "PASS" or receipt["retired_files"] != len(rows):
                raise ValueError("Retirement scope does not reconcile")
            self.confirmed_retired = {normalized(r["original_path"]) for r in rows}
            if self.confirmed_retired & self.keepers:
                raise ValueError("Retired set overlaps protected keepers")

    def verify_core(self, phase):
        observations = {}
        for i, (name, expected) in enumerate(self.core.items(), 1):
            path = Path(name)
            before = self.io.stat(path)
            digest = self.io.digest(path)
            after = self.io.stat(path)
            if digest != expected or before != after:
                raise ValueError("Protected content/metadata mismatch: " + name)
            observations[name] = after
            if i % 500 == 0:
                print(json.dumps(dict(event="protected_check", phase=phase, files=i, total=len(self.core))), flush=True)
        self.protected_checks.append(dict(phase=phase, files=len(observations), hashes_match=True,
                                          observed_basic_metadata_unchanged=True))
        return observations

    def discover_references(self):
        base = Path(self.policy["repository_root"])
        artifact = next((r["path"] for r in self.policy["roots"] if r["id"] == "artifacts"), None)
        if not artifact:
            return
        aliases = self.policy.get("aliases", [])
        for relative in self.policy.get("reference_directories", []):
            directory = base / relative
            if not directory.exists():
                continue
            documents, stack = [], [directory]
            while stack:
                current = stack.pop()
                try:
                    entries = list(self.io.entries(current))
                except (OSError, ValueError, RuntimeError) as error:
                    self.issue("reference_directory_unreadable", current, error)
                    continue
                for item in entries:
                    if item["reparse"]:
                        continue
                    path = current / item["name"]
                    if item["directory"]:
                        stack.append(path)
                    elif path.suffix.lower() == ".md":
                        documents.append(path)
            for document in documents:
                try:
                    text = self.io.read_control(document).decode("utf-8-sig")
                except (OSError, ValueError, UnicodeError, RuntimeError) as error:
                    self.issue("reference_document_unreadable", document, error)
                    continue
                for token in re.findall(r"(?<!\x60)\x60([^\x60\r\n]+)\x60(?!\x60)", text):
                    candidate = token.replace("/", "\\").strip()
                    if candidate.startswith("artifacts\\"):
                        resolution = "repository_relative_assumption"
                        candidate = str(PureWindowsPath(artifact) / candidate[len("artifacts\\"):])
                    else:
                        resolution = "absolute_physical"
                        for alias in aliases:
                            if within(candidate, alias["path"]):
                                resolution = "compatibility_alias"
                                suffix = str(PureWindowsPath(candidate).relative_to(PureWindowsPath(alias["path"])))
                                candidate = str(PureWindowsPath(alias["target"]) / suffix)
                                break
                    if not within(candidate, artifact):
                        continue
                    rel = PureWindowsPath(candidate).relative_to(PureWindowsPath(artifact))
                    if not rel.parts or str(rel) in ("release-evidence", "retained-archives", "."):
                        continue  # A custody container is not a specific evidence pin.
                    if ".." in rel.parts or any(x in candidate for x in ("*", "?", "{", "}", "...", "…", "<", ">")):
                        self.issue("reference_not_resolvable_as_literal", document, token)
                        continue
                    key = normalized(candidate)
                    reference = self.references.setdefault(key, dict(path=candidate, documents=[], origins=[]))
                    reference["documents"].append(str(document.relative_to(base)))
                    reference["origins"].append(dict(literal=token, resolution=resolution))
                    self.protected.add(candidate, "repository_evidence_reference")

    def load_run(self, directory, entries):
        if not any(e["name"] == "evidence-run.json" and not e["reparse"] and not e["directory"] for e in entries):
            return None
        path = directory / "evidence-run.json"
        if self.protected.get(directory):
            # A preservation hold does not need producer-controlled completion
            # data. In particular, do not evaluate retained negative fixtures.
            return dict(state="pinned", preservation_hold=True)
        try:
            run = read_json(self.io, path, maximum=1024 * 1024)
            if not isinstance(run, dict):
                raise ValueError("A run manifest must be a JSON object")
            if run.get("schema_version") != 1 or run.get("run_id") is None:
                raise ValueError("Unsupported run manifest")
            if "scope_root" in run and normalized(run["scope_root"]) != normalized(directory):
                raise ValueError("A run manifest cannot claim another directory")
            advice = retention(run, self.now, self.policy.get("retention_days"))
            if self.protected.covers_subtree(directory):
                advice = dict(action="hold", reason="run_contains_pinned_or_referenced_evidence",
                              review_due=False, deletion_authorized=False)
                run = dict(run, preservation_hold=True)
            self.run_manifests.append(dict(path=str(path), run_id=run["run_id"], advice=advice))
            return run
        except (OSError, ValueError, TypeError, RuntimeError) as error:
            self.issue("invalid_run_manifest", path, error)
            # Invalid nearer ownership overrides any less-specific completion.
            return dict(state="invalid", preservation_hold=True)

    def classify(self, path, root, run):
        if normalized(path) in self.keepers:
            return "explicit_keeper", dict(action="hold", reason="explicit_keeper",
                                           review_due=False, deletion_authorized=False)
        pin = self.protected.get(path)
        if pin:
            return pin, dict(action="hold", reason=pin, review_due=False, deletion_authorized=False)
        if root["role"] == "backup":
            return "backup_hold", dict(action="hold", reason="backup_not_cleanup_input",
                                       review_due=False, deletion_authorized=False)
        advice = retention(run, self.now, self.policy.get("retention_days"))
        kind = run.get("retention_class") if run else None
        bucket = kind if kind in ("staging", "routine_success", "closed_failure", "release",
                                  "pinned", "incident", "acceptance") else "unclassified_legacy"
        return bucket, advice

    def count_file(self, root, path, entry, bucket, advice):
        self.totals["files"] += 1
        self.totals["namespace_logical_bytes"] += entry["size"]
        key = (entry["volume_serial"], entry["file_id"])
        if key[1] == "0" * 32:
            raise RuntimeError("Zero native file identity cannot establish unique allocation")
        counts = self.by_root[root["id"]]
        counts["files"] += 1
        counts["namespace_logical_bytes"] += entry["size"]
        self.by_bucket[bucket]["files"] += 1
        self.by_bucket[bucket]["namespace_logical_bytes"] += entry["size"]
        try:
            written = datetime(1601, 1, 1, tzinfo=timezone.utc) + timedelta(microseconds=entry["basic"]["write"] // 10)
            age = (self.now - written).total_seconds() / 86400
            band = "future_date" if age < 0 else ("0_to_2_days" if age < 2 else
                    "2_to_14_days" if age < 14 else "14_to_90_days" if age < 90 else
                    "90_to_365_days" if age < 365 else "over_365_days")
        except (ValueError, OverflowError):
            band = "invalid_date"
        self.age_bands[band] += 1
        alias = key in self.seen
        if not alias:
            charges = []
            if bucket == "staging":
                charges.append("expanded_staging_bytes")
            if root["role"] == "evidence" and bucket in ("routine_success", "closed_failure"):
                charges.append("expanded_evidence_bytes")
            archive_root = self.policy.get("primary_archive_root")
            if archive_root and within(path, archive_root):
                charges.append("primary_archive_library_bytes")
            if root["role"] == "backup":
                charges.append("independent_backup_bytes")
            self.seen[key] = (entry["allocation"], root["id"], bucket, tuple(charges))
            self.totals["unique_physical_files"] += 1
            self.totals["allocated_file_data_bytes"] += entry["allocation"]
            counts["unique_physical_files"] += 1
            counts["allocated_file_data_bytes"] += entry["allocation"]
            self.by_bucket[bucket]["allocated_file_data_bytes_first_observation"] += entry["allocation"]
            for charge in charges:
                self.quota_bytes[charge] += entry["allocation"]
        else:
            self.totals["alias_file_entries"] += 1
            first_allocation, first_root, first_bucket, charges = self.seen[key]
            if first_allocation != entry["allocation"]:
                # NTFS directory entries for different hardlink names can
                # retain different cached sizes. Reconcile the physical object
                # through FileStandardInfo instead of treating both as data.
                actual = self.io.stat(path)
                if (actual["volume_serial"], actual["file_id"]) != key:
                    raise RuntimeError("Alias identity changed during observation")
                delta = actual["allocation"] - first_allocation
                self.totals["allocated_file_data_bytes"] += delta
                self.by_root[first_root]["allocated_file_data_bytes"] += delta
                self.by_bucket[first_bucket]["allocated_file_data_bytes_first_observation"] += delta
                for charge in charges:
                    self.quota_bytes[charge] += delta
                self.seen[key] = (actual["allocation"], first_root, first_bucket, charges)
                self.issue("alias_cached_allocation_reconciled", path,
                           dict(first=first_allocation, alias_entry=entry["allocation"],
                                authoritative=actual["allocation"]))
        if advice["review_due"]:
            self.by_bucket[bucket]["files_due_for_review"] += 1
        if not alias and entry["size"] >= self.policy["duplicate_minimum_bytes"]:
            group = self.size_groups.setdefault(entry["size"], dict(files=0, allocation=0, samples=[]))
            group["files"] += 1
            group["allocation"] += entry["allocation"]
            if len(group["samples"]) < 5:
                group["samples"].append(str(path))
        return alias

    def walk(self, stream):
        calibrated, fallback = set(), set()
        for root in self.policy["roots"]:
            folder = Path(root["path"])
            try:
                stat = self.io.stat(folder)
                if not stat["directory"]:
                    raise ValueError("Configured root is not a directory")
                stack = [(folder, None)]
                self.observed_prefixes.add(normalized(folder))
            except (OSError, ValueError, RuntimeError) as error:
                self.issue("root_unreadable", folder, error, critical=True)
                continue
            while stack:
                directory, inherited = stack.pop()
                try:
                    entries = list(self.io.entries(directory))
                except (OSError, ValueError, RuntimeError) as error:
                    self.issue("directory_unreadable_or_atime_guard_unavailable", directory, error)
                    continue
                self.totals["directories"] += 1
                self.by_root[root["id"]]["directories"] += 1
                self.observed_prefixes.add(normalized(directory))
                own_run = self.load_run(directory, entries)
                run = own_run if own_run is not None else inherited
                for entry in entries:
                    path = directory / entry["name"]
                    if within(path, self.output):
                        self.excluded.append(dict(path=str(path), reason="current_report_output"))
                        continue
                    if entry["reparse"]:
                        self.excluded.append(dict(path=str(path), reason="reparse_not_followed",
                                                  tag=entry["reparse_tag"]))
                        continue
                    if entry["directory"]:
                        if entry["name"] in self.policy.get("excluded_directory_names", []):
                            self.excluded.append(dict(path=str(path), reason="configured_non_evidence_directory"))
                        else:
                            stack.append((path, run))
                        continue
                    try:
                        # Native directory allocation is calibrated against
                        # FileStandardInfo for each volume/flag/size class.
                        band = 0 if entry["size"] <= 65536 else (1 if entry["size"] < 2097152 else 2)
                        key = (root["id"], entry["basic"]["attributes"] & (0x800 | 0x200), band)
                        if key not in calibrated or key in fallback or entry["file_id"] == "0" * 32:
                            actual = self.io.stat(path)
                            if actual["directory"] or actual["delete_pending"]:
                                raise ValueError("File changed kind or is pending removal")
                            if key not in calibrated:
                                calibrated.add(key)
                                self.io.enumeration_allocation_comparisons += 1
                                if entry["allocation"] != actual["allocation"]:
                                    fallback.add(key)
                            entry = dict(entry, **actual)
                        bucket, advice = self.classify(path, root, run)
                        alias = self.count_file(root, path, entry, bucket, advice)
                        if normalized(path) in self.references or normalized(path) in self.keepers:
                            self.observed_prefixes.add(normalized(path))
                        record = dict(root=root["id"], relative_path=str(path.relative_to(folder)),
                                      size=entry["size"], allocated_bytes=entry["allocation"],
                                      volume_serial=entry["volume_serial"], file_id=entry["file_id"],
                                      basic=entry["basic"], physical_alias=alias, bucket=bucket,
                                      retention=advice, fresh_content_hash=None)
                        stream.write(json.dumps(record, ensure_ascii=True) + "\n")
                        if self.totals["files"] - self.progress_at >= 50000:
                            self.progress_at = self.totals["files"]
                            print(json.dumps(dict(event="inventory_progress", root=root["id"],
                                                  files=self.totals["files"],
                                                  allocated_bytes=self.totals["allocated_file_data_bytes"])),
                                  flush=True)
                    except (OSError, ValueError, RuntimeError) as error:
                        self.issue("file_changed_or_metadata_unreadable", path, error)
        return dict(calibrated_classes=len(calibrated), native_stat_fallback_classes=len(fallback))

    def duplicate_report(self):
        groups = []
        for size, data in self.size_groups.items():
            if data["files"] > 1:
                groups.append(dict(logical_size=size, physical_objects=data["files"],
                                   allocated_bytes=data["allocation"], sample_paths=data["samples"],
                                   confidence="same_size_only_not_verified_duplicates",
                                   reclaimable_bytes_verified=0))
        groups.sort(key=lambda x: x["allocated_bytes"], reverse=True)
        hashed, used = [], 0
        if self.hash_budget:
            repeated_sizes = {r["logical_size"] for r in groups}
            digests = defaultdict(list)
            with gzip.open(self.output / "inventory.jsonl.gz", "rt", encoding="utf-8") as inventory:
                for line in inventory:
                    row = json.loads(line)
                    if row["size"] not in repeated_sizes or row["physical_alias"]:
                        continue
                    root = next(r for r in self.policy["roots"] if r["id"] == row["root"])
                    path = Path(root["path"]) / row["relative_path"]
                    # Hashing is opt-in and limited to declared, closed routine
                    # data. Unknown legacy, pins, backups and active work stay
                    # metadata-only even when a hash budget was supplied.
                    if row["bucket"] not in ("staging", "routine_success", "closed_failure") or \
                       row["retention"]["action"] == "hold" or self.protected.get(path):
                        continue
                    if used + row["size"] > self.hash_budget:
                        continue
                    try:
                        before = self.io.stat(path)
                        if before["file_id"] != row["file_id"] or before["basic"] != row["basic"] or before["size"] != row["size"]:
                            raise RuntimeError("File changed since inventory")
                        digest = self.io.digest(path)
                        used += row["size"]
                        digests[digest].append(dict(path=str(path), size=row["size"],
                                                    allocation=before["allocation"],
                                                    volume_serial=before["volume_serial"], file_id=before["file_id"]))
                    except (OSError, ValueError, RuntimeError) as error:
                        self.issue("optional_hash_unstable_or_unreadable", path, error)
            for digest, files in digests.items():
                if len(files) > 1:
                    hashed.append(dict(sha256=digest, files=files, confidence="fresh_hashes_in_this_observation",
                                       deletion_authorized=False,
                                       requires_new_hash_and_active_use_check_before_any_retirement=True))
        return dict(size_only_groups=groups[:200], total_size_only_groups=len(groups),
                    size_group_list_truncated=len(groups) > 200, hash_budget_bytes=self.hash_budget,
                    optional_content_bytes_hashed=used, fresh_duplicate_groups=hashed,
                    verified_reclaimable_bytes=0,
                    note="Same size is not content equality. No current report authorizes deletion.")

    def reference_report(self):
        results = []
        for key, ref in sorted(self.references.items()):
            if key in self.observed_prefixes:
                state = "present_in_inventory"
            elif key in self.confirmed_retired and key in self.archived_paths:
                state = "archived_only_confirmed_retirement"
            else:
                try:
                    self.io.stat(Path(ref["path"]))
                    state = "present_metadata_only"
                except (OSError, ValueError, RuntimeError) as error:
                    relative_only = ref.get("origins") and all(
                        x["resolution"] == "repository_relative_assumption" for x in ref["origins"])
                    state = "relative_reference_requires_context" if relative_only else "unresolved_or_missing_review_required"
                    self.issue("reference_context_needed" if relative_only else "referenced_evidence_not_resolved",
                               ref["path"], error)
            item = dict(ref, state=state)
            if key in self.archived_paths:
                item["archive_locator"] = self.archived_paths[key]
            results.append(item)
        return results

    def usage(self):
        volumes = {}
        for root in self.policy["roots"]:
            drive = PureWindowsPath(root["path"]).anchor
            if drive in volumes:
                continue
            try:
                space = self.io.disk_space(drive)
                limits = self.policy["volume_limits"].get(drive.rstrip("\\"), {})
                free = space["available_bytes"]
                space["proposed_status"] = (
                    "below_admission_floor" if free < limits.get("floor_bytes", 0) else
                    "warning" if free < limits.get("warning_bytes", 0) else "within_proposed_limits")
                space["proposed_limits"] = limits
                space["enforced"] = False
                volumes[drive] = space
            except OSError as error:
                self.issue("volume_space_unreadable", drive, error)
        return volumes

    def execute(self):
        self.output.mkdir(parents=True, exist_ok=False)
        self.scan_started = utcnow()
        before = {}
        critical_failure = False
        try:
            self.load_protection()
            before = self.verify_core("before")
        except (OSError, ValueError, TypeError, KeyError, RuntimeError) as error:
            self.issue("critical_protection_import_or_integrity_failure", "", error, critical=True)
            critical_failure = True
        allocation = {}
        if not critical_failure:
            self.discover_references()
            with gzip.open(self.output / "inventory.jsonl.gz", "xt", encoding="utf-8", compresslevel=6) as stream:
                allocation = self.walk(stream)
        else:
            with gzip.open(self.output / "inventory.jsonl.gz", "xt", encoding="utf-8") as stream:
                pass
        duplicates = self.duplicate_report()
        references = self.reference_report()
        if before:
            try:
                after = self.verify_core("after")
                if after != before:
                    raise RuntimeError("Protected metadata changed across the live inventory")
            except (OSError, ValueError, RuntimeError) as error:
                self.issue("critical_protected_postcheck_failure", "", error, critical=True)
        volume = self.usage()
        ci_path = Path(self.policy["repository_root"]) / ".github" / "workflows" / "ci.yml"
        ci = {}
        if ci_path.exists():
            text = self.io.read_control(ci_path).decode("utf-8-sig")
            ci = dict(upload_steps=text.count("actions/upload-artifact@"),
                      explicit_retention_lines=[s.strip() for s in text.splitlines() if "retention-days:" in s],
                      modified=False)
        critical = any(i["critical"] for i in self.issues)
        summary = dict(schema_version=1, mode="report_only", automatic_cleanup=False,
                       deletion_authorized=False, files_deleted=0, files_moved=0,
                       source_content_writes=0, persistent_source_timestamp_assignments=0,
                       source_permission_changes=0, scheduled_tasks_created=0,
                       status="CRITICAL_REVIEW_REQUIRED" if critical else
                              ("REVIEW_REQUIRED" if self.issues else "COMPLETE_FOR_CONFIGURED_SCOPE"),
                       started_utc=self.scan_started.isoformat(), completed_utc=utcnow().isoformat(),
                       totals=self.totals, by_root=dict(self.by_root), by_bucket=dict(self.by_bucket),
                       observed_write_age_file_counts=dict(self.age_bands),
                       protected_checks=self.protected_checks, expected_keepers=self.policy.get("expected_keepers", 0),
                       known_keepers=len(self.keepers), archive_packages=len(self.package_names),
                       confirmed_retired_path_catalog_entries=len(self.confirmed_retired),
                       discovered_specific_evidence_references=len(self.references),
                       reference_states=dict(Counter(x["state"] for x in references)),
                       declared_run_manifests=len(self.run_manifests),
                       runs_due_for_review=sum(r["advice"]["review_due"] for r in self.run_manifests),
                       issue_counts=dict(Counter(i["kind"] for i in self.issues)),
                       exclusions=dict(Counter(x["reason"] for x in self.excluded)),
                       concurrent_directory_observations=len(self.io.directory_changes),
                       allocation_measurement=dict(method="native NTFS directory AllocationSize calibrated against FileStandardInfo",
                                                   stream_scope="unnamed data stream; ADS and directory/MFT/journal overhead are not inventoried",
                                                   **allocation),
                       volumes=volume, ci_retention=ci,
                       proposed_budget_observations={
                           name: dict(limit_bytes=limit,
                                      classified_observed_allocated_bytes=self.quota_bytes[name],
                                      classification_complete=name in ("primary_archive_library_bytes", "independent_backup_bytes"),
                                      observed_over_limit=self.quota_bytes[name] > limit, enforced=False)
                           for name, limit in self.policy.get("proposed_limits_only", {}).items()
                           if name != "archive_review_at_bytes"},
                       source_content_reads=self.io.content_reads,
                       source_content_bytes_read=self.io.content_bytes_read,
                       duplicate_assessment=dict(size_only_groups=duplicates["total_size_only_groups"],
                                                 fresh_duplicate_groups=len(duplicates["fresh_duplicate_groups"]),
                                                 verified_reclaimable_bytes=0),
                       coverage_notes=[
                           "Live observation, not an atomic filesystem snapshot; producers may change unprotected data.",
                           "Windows namespace lookups can advance directory access times; no timestamps are backdated or assigned. Explicit read/enumeration handles suppress their automatic atime updates.",
                           "Reparse points and configured non-evidence directories are not followed; their contents are excluded from totals.",
                           "Byte totals cover unnamed data streams. Alternate data streams and directory/MFT/journal overhead are not included; no whole-volume allocation reconciliation is claimed.",
                           "Unique physical allocation is counted by native volume serial plus 128-bit file ID, not directory-entry stat inode zeroes.",
                           "Metadata inventory does not establish content equality. Unknown legacy evidence is held.",
                           "No full owner/group/DACL/SACL audit is claimed; protected data hashes and observed basic metadata are checked before and after.",
                           "Quota figures are proposed observations, not enforced settings.",
                           "Per-bucket allocation is charged to the first physical observation; alias names are not extra reclaimable bytes.",
                           "Historical archive manifests describe creation-time state; the verified retirement ledger resolves archived-only paths."
                       ])
        summary["tool_provenance"] = dict(
            python_version=sys.version, executable=sys.executable,
            files={name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                   for name in ("evidence_retention.py", "evidence_retention_native.py")})
        for name, value in [("summary.json", summary), ("issues.json", self.issues),
                            ("excluded.json", self.excluded), ("runs.json", self.run_manifests),
                            ("references.json", references), ("duplicates.json", duplicates),
                            ("concurrent-directory-observations.json", self.io.directory_changes),
                            ("policy-snapshot.json", self.policy)]:
            with (self.output / name).open("x", encoding="utf-8") as stream:
                json.dump(value, stream, indent=2, ensure_ascii=True)
                stream.write("\n")
        with (self.output / "REPORT.txt").open("x", encoding="utf-8") as report:
            report.write("EVIDENCE RETENTION INVENTORY — REPORT ONLY\n")
            report.write("No deletion, movement, archival, permission changes or scheduling.\n\n")
            report.write("Status: " + summary["status"] + "\n")
            report.write("Observed file names: " + str(self.totals["files"]) + "\n")
            report.write("Unique physical file objects: " + str(self.totals["unique_physical_files"]) + "\n")
            report.write("Observed unnamed-stream allocation: " + str(self.totals["allocated_file_data_bytes"]) + " bytes\n")
            report.write("Namespace logical size: " + str(self.totals["namespace_logical_bytes"]) + " bytes\n")
            report.write("Known keepers: " + str(len(self.keepers)) + "\n")
            report.write("Protected archive packages: " + str(len(self.package_names)) + "\n")
            report.write("Declared runs due for review: " + str(summary["runs_due_for_review"]) + "\n")
            report.write("Verified reclaimable bytes: 0 (no deletion qualification performed)\n\n")
            for root, values in self.by_root.items():
                report.write(root + ": " + str(values["files"]) + " names, " +
                             str(values["allocated_file_data_bytes"]) + " allocated bytes\n")
            for drive, values in volume.items():
                report.write(drive + ": " + str(values["available_bytes"]) + " free bytes; proposed " +
                             values["proposed_status"] + " (not enforced)\n")
            report.write("\nSee summary.json for coverage limits; issues.json and references.json require review.\n")
        # Only new output files are hashed here; source files are never opened
        # through normal Python reads that could update evidence access times.
        with (self.output / "REPORT-SHA256.txt").open("x", encoding="utf-8") as manifest:
            for path in sorted(self.output.iterdir()):
                if not path.is_file() or path.name == "REPORT-SHA256.txt":
                    continue
                digest = hashlib.sha256()
                with path.open("rb") as stream:
                    for data in iter(lambda: stream.read(4 * 1024 * 1024), b""):
                        digest.update(data)
                manifest.write(digest.hexdigest() + "  " + path.name + "\n")
        return summary


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--policy", type=Path, default=Path(__file__).with_name("evidence-retention.policy.json"))
    parser.add_argument("--output", type=Path, help="New directory beneath the policy report_parent")
    parser.add_argument("--hash-budget-bytes", type=int, default=0,
                        help="Opt-in hash budget for declared closed routine data; default 0")
    arguments = parser.parse_args(argv)
    if arguments.hash_budget_bytes < 0:
        parser.error("Hash budget cannot be negative")
    policy = json.loads(arguments.policy.read_text(encoding="utf-8-sig"))
    validate_policy(policy)
    output = arguments.output or Path(policy["report_parent"]) / (
        utcnow().strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
    summary = Inventory(policy, output, WindowsMetadataIO(),
                        hash_budget=arguments.hash_budget_bytes).execute()
    print(json.dumps(dict(status=summary["status"], report=str(output), totals=summary["totals"],
                          known_keepers=summary["known_keepers"],
                          issue_counts=summary["issue_counts"]), indent=2), flush=True)
    return 2 if summary["status"] == "CRITICAL_REVIEW_REQUIRED" else 0


if __name__ == "__main__":
    raise SystemExit(main())
