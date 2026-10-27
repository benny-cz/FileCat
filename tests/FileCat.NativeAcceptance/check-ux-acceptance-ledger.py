#!/usr/bin/env python3
"""Check that the current UX acceptance ledger is finite and cross-linked.

Usage: python tests/FileCat.NativeAcceptance/check-ux-acceptance-ledger.py
This checks tracking structure, not the application's native behavior.
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

lines = [line for line in LEDGER.splitlines() if re.match(r"^\| <a id=", line)]
assert lines, "The current ledger lost its finite scenario rows"
anchors: set[str] = set()
scenario_ids: set[str] = set()
ledger_finding_ids: set[str] = set()
for line in lines:
    cells = [cell.strip() for cell in line.split("|")[1:-1]]
    assert len(cells) == 6, f"Ledger row needs six fields: {line[:100]}"
    anchor_match = re.fullmatch(r'<a id="([a-z0-9-]+)"></a>([A-Z0-9-]+)', cells[0])
    assert anchor_match, f"Stable scenario/anchor missing: {line[:100]}"
    anchor, scenario = anchor_match.groups()
    assert anchor not in anchors and scenario not in scenario_ids, f"Duplicate {scenario}"
    anchors.add(anchor)
    scenario_ids.add(scenario)
    assert cells[2].startswith(("Open", "Failed", "Blocked")), f"Unclassified {scenario}"
    assert all(cells[index] for index in (1, 3, 4, 5)), f"Incomplete {scenario}"
    assert "[" in cells[4] and "](" in cells[4], f"Evidence link missing: {scenario}"
    ledger_finding_ids.update(finding_ids(cells[1], "FCUX"))

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

assert "GATE-CI-FINAL" in scenario_ids
assert "Mac 1× remains open" not in STATUS
assert "documentation-only" in WORKFLOW and "repeat" in WORKFLOW.lower()
print(
    f"UX acceptance ledger: {len(lines)} unique open/failed scenarios, "
    f"{len(accepted_ids)} fully accepted findings, "
    f"{len(ledger_finding_ids)} findings with remaining scenarios"
)
