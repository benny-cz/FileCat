#!/usr/bin/env python3
"""Check the release register, evidence index and acceptance ledger."""

from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
REGISTER = ROOT / "docs/release/1.0.0/FILECAT_1_0_RELEASE_ISSUES.md"
EVIDENCE = ROOT / "docs/release/1.0.0/FILECAT_1_0_RELEASE_EVIDENCE_INDEX.md"
DASHBOARD = ROOT / "docs/release/1.0.0/FILECAT_1_0_RELEASE_EXECUTION_REPORT.md"
BLOCKERS = ROOT / "docs/release/1.0.0/FILECAT_1_0_RELEASE_BLOCKERS.md"
ISSUE = re.compile(r"^\| I(\d+) \|")
SUMMARY = re.compile(
    r"\*\*(\d+) Remediated preliminarily, (two|\d+) Closed for preliminary scope, "
    r"(\d+) unresolved statuses; (\d+) total\.\*\*"
)


def main() -> None:
    lines = REGISTER.read_text(encoding="utf-8-sig").splitlines()
    summary = next((SUMMARY.search(line) for line in lines if SUMMARY.search(line)), None)
    if summary is None:
        raise ValueError("Issue-register summary is missing or malformed")

    sections = {"Unresolved issues": [], "Closed for preliminary scope": [], "Remediated preliminarily": []}
    section = None
    for index, line in enumerate(lines):
        if line.startswith("## "):
            heading = line[3:]
            section = heading if heading in sections else None
        match = ISSUE.match(line)
        if match is None:
            continue
        if section is None:
            raise ValueError(f"Issue row outside a known section at line {index + 1}")
        previous = lines[index - 1] if index else ""
        if ISSUE.match(previous) is None and not previous.startswith("|---|"):
            raise ValueError(f"I{int(match.group(1)):02d} is outside its Markdown table at line {index + 1}")
        sections[section].append(int(match.group(1)))

    ids = [number for numbers in sections.values() for number in numbers]
    if len(ids) != len(set(ids)):
        raise ValueError("Issue IDs appear more than once in the current register")

    remediated, closed, unresolved, total = summary.groups()
    expected = [int(remediated), 2 if closed == "two" else int(closed), int(unresolved), int(total)]
    # Older preliminary remediations live in the frozen full register, so the
    # current table intentionally contains fewer than all summary issue rows.
    if sum(expected[:3]) != expected[3] or max(ids) != expected[3]:
        raise ValueError(f"Issue summary {expected} disagrees with highest current ID I{max(ids)}")
    if len(sections["Unresolved issues"]) != expected[2] or len(sections["Closed for preliminary scope"]) != expected[1]:
        raise ValueError("Open/closed issue rows disagree with the summary")
    if len(sections["Remediated preliminarily"]) > expected[0]:
        raise ValueError("Current remediated rows exceed the historical summary")
    print(f"Release issue register: {expected[3]} IDs, {expected[0]} remediated, {expected[1]} closed, {expected[2]} unresolved; current tables intact")

    evidence_lines = EVIDENCE.read_text(encoding="utf-8-sig").splitlines()
    evidence_ids = [line.split(" | ", 1)[0][2:] for line in evidence_lines if line.startswith("| E-")]
    if len(evidence_ids) != len(set(evidence_ids)):
        raise ValueError("Evidence index contains duplicate IDs")
    evidence_header = next((re.search(r"\*\*(\d+) evidence entries;", line) for line in evidence_lines if " evidence entries;" in line), None)
    if evidence_header is None or int(evidence_header.group(1)) != len(evidence_ids):
        raise ValueError(f"Evidence header disagrees with {len(evidence_ids)} index rows")
    dashboard = DASHBOARD.read_text(encoding="utf-8-sig")
    dashboard_issues = re.search(
        r"^\| Issue register \| (\d+) IDs: (\d+) Remediated preliminarily, "
        r"(two|\d+) Closed for preliminary scope, (\d+) unresolved statuses\.",
        dashboard,
        re.MULTILINE,
    )
    if dashboard_issues is None:
        raise ValueError("Issue dashboard summary is missing")
    dashboard_total, dashboard_remediated, dashboard_closed, dashboard_unresolved = dashboard_issues.groups()
    dashboard_expected = [
        int(dashboard_remediated),
        2 if dashboard_closed == "two" else int(dashboard_closed),
        int(dashboard_unresolved),
        int(dashboard_total),
    ]
    if dashboard_expected != expected:
        raise ValueError("Issue dashboard disagrees with the issue register")
    dashboard_count = re.search(r"^\| Evidence catalogue \| (\d+) entries;", dashboard, re.MULTILINE)
    if dashboard_count is None or int(dashboard_count.group(1)) != len(evidence_ids):
        raise ValueError("Evidence dashboard disagrees with the index")
    print(f"Release evidence index: {len(evidence_ids)} unique entries; dashboard agrees")

    start = "<!-- RELEASE-LEDGER-BEGIN -->"
    end = "<!-- RELEASE-LEDGER-END -->"
    if dashboard.count(start) != 1 or dashboard.count(end) != 1:
        raise ValueError("Release ledger markers are missing or duplicated")
    ledger = dashboard.split(start, 1)[1].split(end, 1)[0]
    rows = []
    for line in ledger.splitlines():
        if not line.startswith("|") or line.startswith("|---") or line.startswith("| Stable case ID"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) != 8:
            raise ValueError(f"Release ledger row has {len(cells)} columns instead of 8: {line[:100]}")
        rows.append(cells)
    if not rows:
        raise ValueError("Release acceptance ledger is empty")

    case_ids = [row[0] for row in rows]
    if len(case_ids) != len(set(case_ids)):
        raise ValueError("Release acceptance ledger contains duplicate case IDs")
    allowed_statuses = {"Open", "Failed", "Blocked", "Accepted", "Optional"}
    for case_id, _campaign, requirement, priority, status, tested, evidence, gap in rows:
        if status not in allowed_statuses:
            raise ValueError(f"{case_id} has unsupported status {status!r}")
        if priority not in {"P0", "P1", "P2", "P3"}:
            raise ValueError(f"{case_id} has unsupported priority {priority!r}")
        if not requirement or not tested or not evidence or not gap:
            raise ValueError(f"{case_id} is missing required acceptance-ledger detail")

    required_preserved = {
        "GATE-CI-FINAL", "REL-UX-AUDIT", "FCUX-GIT-001",
        "FCSEC-AC-01", "FCSEC-AC-02", "FCSEC-AC-03", "FCSEC-AC-04",
        "V09-I495", "I542-LINE-BUDGET", "I543-MD-BUDGET", "I524-ARM64-ACK",
    }
    missing = sorted(required_preserved.difference(case_ids))
    if missing:
        raise ValueError(f"Release ledger dropped accepted stable cases: {missing}")
    status_by_case = {row[0]: row[4] for row in rows}
    if status_by_case.get("I524-ARM64-ACK") != "Accepted":
        raise ValueError("I524-ARM64-ACK must retain the accepted exact ARM64 pass and recurrence")

    dependency_ids = set(re.findall(r"\b(?:DEC|EXT|ENV|PPL)-\d+\b", BLOCKERS.read_text(encoding="utf-8-sig")))
    for row in rows:
        if row[4] != "Blocked":
            continue
        references = set(re.findall(r"\b(?:DEC|EXT|ENV|PPL)-\d+\b", " ".join(row)))
        unknown = references.difference(dependency_ids)
        if unknown:
            raise ValueError(f"{row[0]} cites unknown blocker dependencies: {sorted(unknown)}")
        if not references and "REL-" not in " ".join(row):
            raise ValueError(f"{row[0]} is Blocked without a named dependency")

    retired_headings = {
        "## Progress", "## Remaining issue work — 23 entries",
        "## Remaining campaign tests", "## Next executable slices",
    }
    present = sorted(heading for heading in retired_headings if heading in dashboard)
    if present:
        raise ValueError(f"Dashboard retains superseded parallel backlog sections: {present}")
    counts = {status: sum(row[4] == status for row in rows) for status in sorted(allowed_statuses)}
    print(f"Release acceptance ledger: {len(rows)} unique cases; statuses {counts}; named blockers verified")


if __name__ == "__main__":
    main()
