#!/usr/bin/env python3
"""Check that issue rows remain in Markdown tables and summary counts match."""

from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[2]
REGISTER = ROOT / "docs/release/1.0.0/FILECAT_1_0_RELEASE_ISSUES.md"
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


if __name__ == "__main__":
    main()
