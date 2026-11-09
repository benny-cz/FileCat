#!/usr/bin/env python3
"""Compare raw connected test inventories without treating skips as passes."""

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def read(path: Path, casefold_substring: str):
    data = path.read_bytes()
    tree = ET.fromstring(data)
    rows = tree.findall('.//t:UnitTestResult', NS)
    counts = tree.find('.//t:Counters', NS).attrib
    if len(rows) != int(counts['total']):
        raise ValueError(f'Raw case count differs from declared total: {path}')
    if any(row.attrib['outcome'] not in ('Passed', 'NotExecuted', 'Failed') for row in rows):
        raise ValueError(f'Unexpected raw case outcome: {path}')
    passed = sum(row.attrib['outcome'] == 'Passed' for row in rows)
    failed = sum(row.attrib['outcome'] == 'Failed' for row in rows)
    if passed != int(counts['passed']) or failed != int(counts['failed']):
        raise ValueError(f'Raw outcomes differ from counters: {path}')

    def name(row):
        value = row.attrib['testName']
        return value.casefold() if casefold_substring and casefold_substring in value else value

    return (Counter(name(row) for row in rows),
            Counter(name(row) for row in rows if row.attrib['outcome'] == 'Passed'),
            dict(total=len(rows), passed=passed, failed=failed,
                 skipped=sum(row.attrib['outcome'] == 'NotExecuted' for row in rows),
                 sha256=hashlib.sha256(data).hexdigest()))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('previous', type=Path)
    parser.add_argument('current', type=Path)
    parser.add_argument('--casefold-substring', default='')
    parser.add_argument('--replacement', action='append', default=[], metavar='OLD=NEW',
                        help='Explicitly map one renamed prior case to one current case.')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    before, prior_pass, before_counts = read(args.previous, args.casefold_substring)
    after, current_pass, after_counts = read(args.current, args.casefold_substring)
    replacements = {}
    replacement_targets = set()
    for pair in args.replacement:
        if '=' not in pair:
            parser.error(f'Invalid replacement: {pair}')
        old, new = pair.split('=', 1)
        if not old or not new or old in replacements or new in replacement_targets:
            parser.error(f'Duplicate or empty replacement: {pair}')
        if before[old] != 1 or after[old] or after[new] != 1 or before[new]:
            parser.error(f'Replacement must bind one removed prior case to one added current case: {pair}')
        replacements[old] = new
        replacement_targets.add(new)
    for old, new in replacements.items():
        before[new] += before.pop(old)
        if prior_pass[old]:
            prior_pass[new] += prior_pass.pop(old)
    missing = list((before - after).elements())
    lost = list((prior_pass - current_pass).elements())
    report = dict(previous=before_counts, current=after_counts,
                  replacements=replacements,
                  added_cases=sum((after - before).values()),
                  missing_prior_cases=missing, lost_prior_passes=lost)
    if args.output:
        args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
    if missing or lost or after_counts['failed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
