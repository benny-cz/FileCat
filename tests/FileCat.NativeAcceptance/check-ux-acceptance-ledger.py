#!/usr/bin/env python3
"""Check that the current UX acceptance ledger is finite and cross-linked.

Usage: python tests/FileCat.NativeAcceptance/check-ux-acceptance-ledger.py
This checks tracking structure, not the application's native behavior. Open and accepted rows
are both read, so a ledger with nothing left open passes too.
"""

from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2] / "docs" / "validation"
AUDIT = (ROOT / "FileCat_UX_UI_Audit.md").read_text(encoding="utf-8")
LEDGER = (ROOT / "UX_UI_ACCEPTANCE_LEDGER.md").read_text(encoding="utf-8")
REMAINING = (ROOT / "UX_UI_REMAINING.md").read_text(encoding="utf-8")
WORKFLOW = (ROOT / "UX_UI_SESSION_WORKFLOW.md").read_text(encoding="utf-8")
STATUS = (ROOT / "UX_UI_IMPLEMENTATION_STATUS.md").read_text(encoding="utf-8")


def finding_ids(text: str, prefix: str) -> set[str]:
    result: set[str] = set()
    for match in re.finditer(rf"\b{prefix}-([0-9]{{3}}(?:/[0-9]{{3}})*)", text):
        result.update(f"{prefix}-{part}" for part in match.group(1).split("/"))
    return result


audit_ids = set(re.findall(r'(?m)^<a id="(fcux-[0-9]{3})"', AUDIT))
audit_ids = {item.upper() for item in audit_ids}
assert len(audit_ids) == 76, f"Expected 76 stable audit IDs, got {len(audit_ids)}"

accepted_section = LEDGER.split("## Accepted cases with durable scope", 1)[1].split(
    "## S2 — shared desktop boundaries", 1)[0].strip().splitlines()
accepted_table = accepted_section[next(index for index, line in enumerate(accepted_section)
    if line.startswith("| Stable case |")):]
assert len(accepted_table) > 2, "The accepted-case table is empty"
assert all(line.startswith("|") and line.count("|") == 7 for line in accepted_table), (
    "The accepted-case table has a blank/broken row or a different number of fields"
)

# Open rows sit in the scenario tables, which give the finding before the status. A closed row is
# bold; the accepted-case table gives its status before the accepted scope.
anchors: set[str] = set()
scenario_ids: set[str] = set()
open_rows: list[str] = []
accepted_rows: list[str] = []
ledger_finding_ids: set[str] = set()
table = ""
for line in LEDGER.splitlines():
    if line.startswith("| Stable "):
        table = line.split("|")[1].strip()
        continue
    if not re.match(r"^\| (\*\*)?<a id=", line):
        continue
    cells = [cell.strip() for cell in line.split("|")[1:-1]]
    assert len(cells) == 6, f"Ledger row needs six fields: {line[:100]}"
    anchor_match = re.fullmatch(r'(\*\*)?<a id="([a-z0-9-]+)"></a>([A-Z0-9-]+)(\*\*)?', cells[0])
    assert anchor_match and anchor_match.group(1) == anchor_match.group(4), (
        f"Stable scenario/anchor missing: {line[:100]}"
    )
    closed, anchor, scenario = bool(anchor_match.group(1)), anchor_match.group(2), anchor_match.group(3)
    assert anchor not in anchors and scenario not in scenario_ids, f"Duplicate {scenario}"
    anchors.add(anchor)
    scenario_ids.add(scenario)
    assert all(cells[1:]), f"Incomplete {scenario}"
    assert "[" in cells[4] and "](" in cells[4], f"Evidence link missing: {scenario}"
    status = cells[1] if table == "Stable case" else cells[2]
    if closed:
        assert status.startswith("Accepted"), f"Bold row {scenario} is not accepted"
        accepted_rows.append(scenario)
    else:
        assert table == "Stable scenario", f"Open row {scenario} outside a scenario table"
        assert status.startswith(("Open", "Failed", "Blocked")), f"Unclassified {scenario}"
        open_rows.append(scenario)
        ledger_finding_ids.update(finding_ids(cells[1], "FCUX"))
assert open_rows or accepted_rows, "The current ledger lost its finite scenario rows"

accepted_match = re.search(
    r"(?s)<!-- ACCEPTED-FINDINGS-BEGIN -->(.*?)<!-- ACCEPTED-FINDINGS-END -->",
    LEDGER,
)
assert accepted_match, "Explicit accepted-finding inventory missing"
accepted_ids = finding_ids(accepted_match.group(1), "FCUX")
assert ledger_finding_ids.isdisjoint(accepted_ids), (
    "An accepted finding still has an open scenario; record accepted subcase instead"
)
assert audit_ids == ledger_finding_ids | accepted_ids, (
    f"Audit IDs with no current disposition: {sorted(audit_ids - ledger_finding_ids - accepted_ids)}"
)

for target in re.findall(r"UX_UI_ACCEPTANCE_LEDGER\.md#([a-z0-9-]+)", REMAINING):
    assert target in anchors or target in {
        "s2--shared-desktop-boundaries",
        "s3--focused-usability-routes",
    }, f"Remaining-work link misses the ledger: {target}"
if not open_rows:
    # With nothing open, the remaining-work index lists nothing either.
    for section in re.split(r"(?m)^## ", REMAINING)[1:]:
        heading, _, body = section.partition("\n")
        assert body.strip().startswith("None"), f"Nothing is open, yet remaining work lists {heading}"

assert "GATE-CI-FINAL" in scenario_ids
assert "Mac 1× remains open" not in STATUS
assert "documentation-only" in WORKFLOW and "repeat" in WORKFLOW.lower()
print(
    f"UX acceptance ledger: {len(open_rows)} open/failed and {len(accepted_rows)} accepted scenarios, "
    f"{len(accepted_ids)} fully accepted findings, "
    f"{len(ledger_finding_ids)} findings with remaining scenarios"
)
